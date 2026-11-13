using System.Buffers;
using System.Globalization;
using System.Text;

namespace PaletteDictionary.Core;

public enum MatchKind { Exact, NormalizedExact, Prefix, Correction }
public sealed record SearchHit(string Word, string[] Dictionaries, MatchKind Kind, int EditDistance);
public sealed record WordSource(string Word, string Dictionary);

/// <summary>Immutable sorted prefix index and compact array trie for bounded OSA edit search.</summary>
public sealed class HeadwordIndex
{
    private sealed record Entry(string Word, string Key, string[] Dictionaries);
    private struct Node
    {
        public char Character;
        public int FirstChild;
        public int NextSibling;
        public int EntryStart;
        public int EntryCount;
    }
    private readonly Entry[] _entries;
    private readonly Node[] _nodes;
    private readonly bool _ignoreDiacritics;
    public int Count => _entries.Length;
    public int TrieNodeCount => _nodes.Length;

    public HeadwordIndex(IEnumerable<WordSource> words, bool ignoreDiacritics = true, CancellationToken cancellationToken = default)
    {
        _ignoreDiacritics = ignoreDiacritics;
        var sources = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var sourceOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in words)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(item.Word) || item.Word.Length > MdxHeadwordReader.MaxHeadwordLength) continue;
            var word = item.Word.Normalize(NormalizationForm.FormC);
            if (!sources.TryGetValue(word, out var set)) sources[word] = set = new(StringComparer.Ordinal);
            set.Add(item.Dictionary);
            sourceOrder.TryAdd(item.Dictionary, sourceOrder.Count);
        }
        _entries = sources.Select(pair => new Entry(pair.Key, Normalize(pair.Key, ignoreDiacritics), pair.Value.OrderBy(s => sourceOrder[s]).ToArray()))
            .Where(e => e.Key.Length > 0).OrderBy(e => e.Key, StringComparer.Ordinal).ThenBy(e => e.Word, StringComparer.Ordinal).ToArray();
        var nodes = new List<Node> { new() { EntryStart = -1 } };
        var lastChildren = new List<int> { 0 };
        var path = new List<int> { 0 };
        var previous = "";
        for (var i = 0; i < _entries.Length; i++)
        {
            if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
            var key = _entries[i].Key;
            var common = 0;
            while (common < previous.Length && common < key.Length && previous[common] == key[common]) common++;
            path.RemoveRange(common + 1, path.Count - common - 1);
            for (var j = common; j < key.Length; j++)
            {
                var parentId = path[^1]; var nodeId = nodes.Count;
                nodes.Add(new() { Character = key[j], EntryStart = -1 }); lastChildren.Add(0);
                var parent = nodes[parentId];
                if (parent.FirstChild == 0) { parent.FirstChild = nodeId; nodes[parentId] = parent; }
                else { var sibling = nodes[lastChildren[parentId]]; sibling.NextSibling = nodeId; nodes[lastChildren[parentId]] = sibling; }
                lastChildren[parentId] = nodeId; path.Add(nodeId);
            }
            var terminalId = path[^1]; var terminal = nodes[terminalId];
            if (terminal.EntryStart < 0) terminal.EntryStart = i;
            terminal.EntryCount++; nodes[terminalId] = terminal;
            previous = key;
        }
        _nodes = nodes.ToArray();
    }

    public IReadOnlyList<SearchHit> Search(string query, int limit = 30, bool fuzzy = true, int maxDistance = 2, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        if (maxDistance is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(maxDistance));
        query = query.Trim();
        if (query.Length is 0 or > MdxHeadwordReader.MaxHeadwordLength) return [];
        var key = Normalize(query, _ignoreDiacritics);
        if (key.Length == 0) return [];
        var ranked = new Dictionary<int, (int Rank, int Distance)>();
        var start = LowerBound(key);
        // Exact key variants are all considered before limiting prefix results.
        var p = start;
        for (; p < _entries.Length && _entries[p].Key == key; p++)
            ranked[p] = (_entries[p].Word == query ? 0 : 1, 0);
        for (; p < _entries.Length && _entries[p].Key.StartsWith(key, StringComparison.Ordinal) && ranked.Count < limit; p++)
            ranked[p] = (2, 0);
        var distance = Math.Min(maxDistance, key.Length <= 4 ? 1 : 2);
        if (fuzzy && distance > 0 && key.Length >= 3 && ranked.Count < limit)
        {
            var matches = new PriorityQueue<(int Id, int Distance), (int NegativeDistance, int NegativeId)>();
            var row = ArrayPool<int>.Shared.Rent(key.Length + 1);
            try
            {
                for (var i = 0; i <= key.Length; i++) row[i] = i;
                for (var child = _nodes[0].FirstChild; child != 0; child = _nodes[child].NextSibling)
                    Visit(child, 1, '\0', row, null);
            }
            finally { ArrayPool<int>.Shared.Return(row); }
            while (matches.TryDequeue(out var match, out _)) ranked.TryAdd(match.Id, (3, match.Distance));

            void Visit(int nodeId, int depth, char previousCharacter, int[] previousRow, int[]? beforePreviousRow)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (depth > key.Length + distance) return;
                var node = _nodes[nodeId];
                var current = ArrayPool<int>.Shared.Rent(key.Length + 1);
                try
                {
                    current[0] = depth;
                    var minimum = depth;
                    for (var col = 1; col <= key.Length; col++)
                    {
                        var value = Math.Min(current[col - 1] + 1, Math.Min(previousRow[col] + 1,
                            previousRow[col - 1] + (key[col - 1] == node.Character ? 0 : 1)));
                        if (beforePreviousRow is not null && col > 1 && node.Character == key[col - 2] && previousCharacter == key[col - 1])
                            value = Math.Min(value, beforePreviousRow[col - 2] + 1);
                        current[col] = value; minimum = Math.Min(minimum, value);
                    }
                    var edits = current[key.Length];
                    if (node.EntryCount > 0 && edits <= distance)
                        for (var id = node.EntryStart; id < node.EntryStart + node.EntryCount; id++)
                            if (!ranked.ContainsKey(id))
                            {
                                matches.Enqueue((id, edits), (-edits, -id));
                                if (matches.Count > limit) matches.Dequeue();
                            }
                    if (minimum <= distance)
                        for (var child = node.FirstChild; child != 0; child = _nodes[child].NextSibling)
                            Visit(child, depth + 1, node.Character, current, previousRow);
                }
                finally { ArrayPool<int>.Shared.Return(current); }
            }
        }
        return ranked.OrderBy(x => x.Value.Rank).ThenBy(x => x.Value.Distance).ThenBy(x => x.Key).Take(limit)
            .Select(x => new SearchHit(_entries[x.Key].Word, _entries[x.Key].Dictionaries,
                (MatchKind)x.Value.Rank, x.Value.Distance)).ToArray();
    }

    private int LowerBound(string key)
    {
        var lo = 0; var hi = _entries.Length;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (StringComparer.Ordinal.Compare(_entries[mid].Key, key) < 0) lo = mid + 1; else hi = mid; }
        return lo;
    }

    public static string Normalize(string word, bool ignoreDiacritics = true)
    {
        var text = word.Normalize(NormalizationForm.FormKC).ToUpperInvariant();
        if (!ignoreDiacritics) return text;
        var result = new StringBuilder(text.Length);
        foreach (var rune in text.Normalize(NormalizationForm.FormD).EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) is not UnicodeCategory.NonSpacingMark and not UnicodeCategory.SpacingCombiningMark and not UnicodeCategory.EnclosingMark)
                result.Append(rune.ToString());
        return result.ToString().Normalize(NormalizationForm.FormC);
    }
}
