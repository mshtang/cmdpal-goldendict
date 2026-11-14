using System.Diagnostics;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Json;
using PaletteDictionary.Core;
using PaletteDictionary.Core.ThirdParty;
using PaletteDictionary.Tests;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length == 2 && args[0] is "--write-smoke-config" or "--write-smoke-matrix")
{
    var configPath = Path.GetFullPath(args[1]);
    var directory = Path.GetDirectoryName(configPath)!;
    Directory.CreateDirectory(directory);
    var dictionary = Path.Combine(directory, "synthetic.mdx");
    MdxFixture.Write(dictionary, ["apple", "application", "apply", "banana", "résumé"]);
    var config = new PluginConfiguration
    {
        GoldenDictPath = Path.Combine(directory, "goldendict.exe"), DefaultGroup = "Example",
        Groups = [new() { Name = "Example", Dictionaries = [new() { Name = "Synthetic dictionary", Path = dictionary }] }]
    };
    if (args[0] == "--write-smoke-matrix")
    {
        var sources = new List<DictionarySource>();
        foreach (var modern in new[] { true, false })
        foreach (var encoding in new[] { "UTF-8", "UTF-16", "GBK", "Big5" })
        foreach (var compression in new[] { 0, 1, 2 })
        {
            var marker = $"fixture-{(modern ? 2 : 1)}-{encoding}-{compression}";
            var path = Path.Combine(directory, marker + ".mdx");
            MdxFixture.Write(path, ["apple", "application", "中文", marker], modern, encoding, compression);
            sources.Add(new() { Name = marker, Path = path });
        }
        var encrypted = Path.Combine(directory, "fixture-encrypted.mdx");
        MdxFixture.Write(encrypted, ["apple", "中文", "fixture-encrypted"], encrypted: true);
        sources.Add(new() { Name = "fixture-encrypted", Path = encrypted });
        config.Groups[0].Dictionaries = sources;
    }
    File.WriteAllText(configPath, config.ToJson());
    Console.WriteLine("Synthetic smoke-test configuration created: " + configPath);
    return 0;
}
var root = Path.Combine(Path.GetTempPath(), "PaletteDictionary.Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var failed = 0; var passed = 0;
void Check(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
void Test(string name, Action run)
{
    try { run(); Console.WriteLine($"PASS {name}"); passed++; }
    catch (Exception ex) { Console.WriteLine($"FAIL {name}: {ex}"); failed++; }
}
void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or NotSupportedException) { return; } throw new Exception("Expected rejection."); }

try
{
    Test("Localization: system language and English fallback", () =>
    {
        foreach (var name in new[] { "en-US", "de-DE", "fr-FR", "ja-JP", "" })
            Check(UiText.ResolveCulture("auto", CultureInfo.GetCultureInfo(name)).Name == "en", name);
        foreach (var name in new[] { "zh-CN", "zh-TW", "zh-HK", "zh-Hans" })
            Check(UiText.ResolveCulture("auto", CultureInfo.GetCultureInfo(name)).Name == "zh", name);
        Check(UiText.ResolveCulture("en", CultureInfo.GetCultureInfo("zh-CN")).Name == "en");
        Check(UiText.ResolveCulture("zh-Hans", CultureInfo.GetCultureInfo("en-US")).Name == "zh");
    });
    Test("Localization: resource completeness and format arguments", () =>
    {
        var resources = new ResourceManager("PaletteDictionary.Core.Resources.Strings", typeof(UiText).Assembly);
        var english = resources.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true)!;
        var chinese = resources.GetResourceSet(CultureInfo.GetCultureInfo("zh"), true, false)!;
        var englishKeys = english.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        var chineseKeys = chinese.Cast<System.Collections.DictionaryEntry>().Select(e => (string)e.Key).ToHashSet();
        Check(englishKeys.SetEquals(chineseKeys), "Chinese resources silently fall back to English.");
        foreach (var key in englishKeys)
        {
            var en = english.GetString(key)!; var zh = chinese.GetString(key)!;
            Check(!string.IsNullOrWhiteSpace(en) && !string.IsNullOrWhiteSpace(zh), key);
            Check(!en.Any(c => c is >= '\u4e00' and <= '\u9fff'), key + " contains untranslated Chinese.");
            Check(CompositeFormat.Parse(en).MinimumArgumentCount == CompositeFormat.Parse(zh).MinimumArgumentCount, key);
        }
    });
    Test("Localization: language persistence, legacy defaults and validation", () =>
    {
        try
        {
            var path = Path.Combine(root, "language.json");
            Check(PluginConfiguration.Parse("{}", path).UiLanguage == "auto");
            var config = new PluginConfiguration { DefaultGroup = "我的分组", Groups = [new() { Name = "我的分组" }] };
            foreach (var language in new[] { "en", "zh-Hans", "auto" })
            {
                config.UiLanguage = language; UiText.SetLanguage(language);
                var roundtrip = PluginConfiguration.Parse(config.ToJson(), path);
                Check(roundtrip.UiLanguage == language && roundtrip.Groups[0].Name == "我的分组");
            }
            UiText.SetLanguage("en"); Check(UiText.Get("Save") == "Save");
            var englishDefault = PluginConfiguration.CreateDefault();
            Check(englishDefault.DefaultGroup == "Default" && englishDefault.Groups[0].Name == "Default");
            var group = new ConfigurationWorkspace(englishDefault).AddGroup(); Check(group.Name == "New group");
            config.MaxResults = 0;
            try { config.NormalizeAndValidate(path); throw new Exception("Invalid options accepted."); }
            catch (InvalidDataException ex) { Check(ex.Message.StartsWith("maxResults must be", StringComparison.Ordinal)); }
            UiText.SetLanguage("zh-Hans"); Check(UiText.Get("Save") == "保存");
            Check(PluginConfiguration.CreateDefault().DefaultGroup == "默认");
            config.MaxResults = 30; config.UiLanguage = "invalid";
            Reject(() => config.NormalizeAndValidate(path));
        }
        finally { UiText.SetLanguage("auto"); }
    });
    Test("RIPEMD-128 published vectors", () =>
    {
        Check(Convert.ToHexString(Ripemd128.ComputeHash([])).Equals("CDF26213A150DC3ECB610F18F6B38B46", StringComparison.Ordinal));
        Check(Convert.ToHexString(Ripemd128.ComputeHash(Encoding.ASCII.GetBytes("abc"))) == "C14A12199C66E4BA84636B0F69144C77");
    });
    foreach (var modern in new[] { true, false })
    foreach (var encoding in new[] { "UTF-8", "UTF-16", "GBK", "Big5" })
    foreach (var compression in new[] { 0, 1, 2 })
    {
        var version = modern; var enc = encoding; var type = compression;
        Test($"MDX {(version ? "2.0" : "1.2")} {enc} compression={type}", () =>
        {
            var path = Path.Combine(root, "fixture.mdx");
            string[] words = ["apple", "application", "中文", "apple"];
            MdxFixture.Write(path, words, version, enc, type);
            Check(MdxHeadwordReader.Read(path).SequenceEqual(new[] { "apple", "application", "中文" }), "Wrong decoded words.");
        });
    }
    Test("Encrypted=2 keyword metadata", () =>
    {
        var path = Path.Combine(root, "encrypted.mdx"); MdxFixture.Write(path, ["apple", "Straße", "中文"], encrypted: true);
        Check(MdxHeadwordReader.Read(path).SequenceEqual(new[] { "Straße", "apple", "中文" }));
    });
    Test("Reject truncated and corrupt MDX", () =>
    {
        var path = Path.Combine(root, "broken.mdx"); MdxFixture.Write(path, ["apple"]);
        var bytes = File.ReadAllBytes(path); bytes[6] ^= 1; File.WriteAllBytes(path, bytes); Reject(() => MdxHeadwordReader.Read(path));
        File.WriteAllBytes(path, bytes[..30]); Reject(() => MdxHeadwordReader.Read(path));
    });
    Test("Bounded LZO overlapping backreference and corrupt data", () =>
    {
        Check(Encoding.ASCII.GetString(LzoDecoder.Decode([21, 97, 98, 99, 100, 64, 0, 17, 0, 0], 7)) == "abcdddd");
        Reject(() => LzoDecoder.Decode([21, 97, 98, 99, 100, 64, 0, 17, 0, 0], 6));
        Reject(() => LzoDecoder.Decode([21, 97], 4));
    });
    Test("Persistent cache hit, invalidation, checksum recovery", () =>
    {
        var path = Path.Combine(root, "cache-source.mdx"); var cache = new HeadwordCache(Path.Combine(root, "cache"));
        MdxFixture.Write(path, ["apple"]); Check(cache.LoadOrBuild(path).Single() == "apple");
        var cachePath = cache.GetPath(path); var cacheTime = File.GetLastWriteTimeUtc(cachePath);
        Check(cache.LoadOrBuild(path).Single() == "apple" && File.GetLastWriteTimeUtc(cachePath) == cacheTime);
        MdxFixture.Write(path, ["application", "apply"]); Check(cache.LoadOrBuild(path).Length == 2);
        File.WriteAllBytes(cachePath, new byte[70]); Check(cache.LoadOrBuild(path).Length == 2);
    });
    Test("Source merge, exact/prefix ordering, accent and CJK", () =>
    {
        var index = new HeadwordIndex([new("apple", "A"), new("apple", "B"), new("application", "B"), new("résumé", "A"), new("中文词典", "C")]);
        var hits = index.Search("apple"); Check(hits[0].Kind == MatchKind.Exact && hits[0].Dictionaries.SequenceEqual(new[] { "A", "B" }));
        Check(index.Search("app")[0].Kind == MatchKind.Prefix);
        Check(index.Search("resume")[0].Word == "résumé");
        Check(index.Search("中文")[0].Word == "中文词典");
        Check(new HeadwordIndex([new("résumé", "A")], false).Search("resume", fuzzy: false).Count == 0);
    });
    Test("Missing/extra/wrong letters and adjacent transposition", () =>
    {
        var index = new HeadwordIndex([new("apple", "A"), new("banana", "B")]);
        foreach (var query in new[] { "aple", "appple", "apxle", "appel" })
            Check(index.Search(query).Any(h => h.Word == "apple" && h.Kind == MatchKind.Correction), query);
        Check(index.Search("aple", fuzzy: false).Count == 0);
        Check(index.Search("a").All(h => h.Kind != MatchKind.Correction));
    });
    Test("Fuzzy trie agrees with exhaustive independent OSA oracle", () =>
    {
        var random = new Random(73);
        string Word() => new(Enumerable.Range(0, random.Next(3, 10)).Select(_ => "abcde"[random.Next(5)]).ToArray());
        var words = Enumerable.Range(0, 90).Select(_ => Word()).Distinct().ToArray();
        var index = new HeadwordIndex(words.Select(w => new WordSource(w, "test")));
        for (var i = 0; i < 250; i++)
        {
            var query = Word(); var distance = query.Length <= 4 ? 1 : 2;
            var expected = words.Where(w => w.StartsWith(query, StringComparison.Ordinal) || Osa(w, query) <= distance).Order().ToArray();
            var actual = index.Search(query, 100).Select(h => h.Word).Order().ToArray();
            Check(actual.SequenceEqual(expected), $"Mismatch for {query}: {string.Join(',', expected)} vs {string.Join(',', actual)}");
        }
    });
    Test("Search cancellation", () =>
    {
        var index = new HeadwordIndex([new("banana", "A")]); using var cts = new CancellationTokenSource(); cts.Cancel();
        try { index.Search("banxna", cancellationToken: cts.Token); } catch (OperationCanceledException) { return; }
        throw new Exception("Search did not cancel.");
    });
    Test("Configuration relative paths, validation and comments", () =>
    {
        var path = Path.Combine(root, "config.json");
        File.WriteAllText(path, "{ // comment\n\"goldenDictPath\":\"gd.exe\",\"defaultGroup\":\"EN\",\"groups\":[{\"name\":\"EN\",\"dictionaries\":[{\"path\":\"foo.mdx\"}]}],}");
        var config = PluginConfiguration.Load(path); Check(config.Groups[0].Dictionaries[0].Path == Path.Combine(root, "foo.mdx"));
        Check(config.GoldenDictPath == Path.Combine(root, "gd.exe"));
        File.WriteAllText(path, "{\"groups\":[]}"); Reject(() => PluginConfiguration.Load(path));
    });
    Test("Launcher preserves spaces, quotes, symbols and option-like words", () =>
    {
        var executable = Path.Combine(root, "goldendict.exe"); File.WriteAllText(executable, "not-executed");
        var query = "--word \"中文\" & café";
        var info = GoldenDictLauncher.CreateStartInfo(executable, query, "English Dictionaries");
        Check(!info.UseShellExecute && info.ArgumentList.SequenceEqual(new[] { "--main-window", "--group-name=English Dictionaries", "goldendict://" + Uri.EscapeDataString(query) }));
    });
    Test("Service keeps last good snapshot after invalid configuration", () =>
    {
        var path = Path.Combine(root, "service.mdx"); MdxFixture.Write(path, ["apple", "apply"]);
        var configPath = Path.Combine(root, "service.json");
        File.WriteAllText(configPath, new PluginConfiguration
        {
            DefaultGroup = "EN", Groups = [new() { Name = "EN", Dictionaries = [new() { Name = "Fixture", Path = path }] }]
        }.ToJson());
        using var service = new DictionaryService(configPath, Path.Combine(root, "service-cache"));
        service.ReloadAsync().GetAwaiter().GetResult(); var first = service.Snapshot;
        Check(first?.Groups["EN"].Index.Count == 2, service.LastError ?? "No snapshot");
        File.WriteAllText(configPath, "broken-json"); service.ReloadAsync().GetAwaiter().GetResult();
        Check(ReferenceEquals(first, service.Snapshot) && service.LastError is not null);
    });
    ConfigurationDocument Editable(string name)
    {
        var folder = Path.Combine(root, name); Directory.CreateDirectory(folder);
        var executable = Path.Combine(folder, "goldendict.exe"); File.WriteAllText(executable, "not-executed");
        var mdx = Path.Combine(folder, "fixture.mdx"); MdxFixture.Write(mdx, ["apple", "apply"]);
        var document = ConfigurationDocument.Open(Path.Combine(folder, "config.json"));
        document.Configuration.GoldenDictPath = executable;
        document.Configuration.Groups[0].Dictionaries.Add(new() { Name = "Fixture", Path = mdx });
        return document;
    }
    Test("Visual settings: atomic save, backup and old JSON compatibility", () =>
    {
        var document = Editable("document"); document.Save();
        Check(PluginConfiguration.Load(document.FilePath).MaxResults == 30);
        document.Configuration.MaxResults = 17; document.Save();
        Check(PluginConfiguration.Load(document.FilePath).MaxResults == 17);
        Check(PluginConfiguration.Load(document.FilePath + ".bak").MaxResults == 30);
        Check(!Directory.EnumerateFiles(Path.GetDirectoryName(document.FilePath)!, "*.tmp").Any());
    });
    Test("Native settings: invalid structure never replaces saved configuration", () =>
    {
        var document = Editable("validation"); document.Save(); var before = File.ReadAllText(document.FilePath);
        document.Configuration.MaxResults = 0; Reject(document.Save);
        Check(File.ReadAllText(document.FilePath) == before);
        document.Configuration.MaxResults = 30; document.Configuration.GoldenDictPath = "missing.exe";
        document.Configuration.Groups[0].Dictionaries.Add(new() { Path = Path.Combine(root, "missing.mdx"), Enabled = true });
        document.Save(); // Save text paths without checking the target files.
        Check(PluginConfiguration.Load(document.FilePath).Groups[0].Dictionaries.Count == 2);
        Check(PluginConfiguration.Load(document.FilePath).GoldenDictPath.EndsWith("missing.exe", StringComparison.Ordinal));
    });
    Test("Visual settings: reject external edit conflict", () =>
    {
        var document = Editable("conflict"); document.Save();
        var second = ConfigurationDocument.Open(document.FilePath);
        document.Configuration.MaxResults = 19; document.Save(); second.Configuration.MaxResults = 42;
        try { second.Save(); } catch (ConfigurationConflictException)
        { Check(PluginConfiguration.Load(document.FilePath).MaxResults == 19); return; }
        throw new Exception("External change was overwritten.");
    });
    Test("Visual settings: recover backup without overwriting it with corrupt JSON", () =>
    {
        var document = Editable("recovery"); document.Save(); document.Configuration.MaxResults = 19; document.Save();
        File.WriteAllText(document.FilePath, "broken JSON");
        var recovered = ConfigurationDocument.Open(document.FilePath);
        Check(recovered.RecoveryNotice is not null && recovered.Configuration.MaxResults == 30);
        recovered.Save();
        Check(PluginConfiguration.Load(document.FilePath).MaxResults == 30);
        Check(PluginConfiguration.Load(document.FilePath + ".bak").MaxResults == 30);
        var corruptCopy = Directory.EnumerateFiles(Path.GetDirectoryName(document.FilePath)!, "*.invalid-*.json").Single();
        Check(File.ReadAllText(corruptCopy) == "broken JSON");
    });
    Test("Visual settings: preserve unknown options", () =>
    {
        var document = Editable("unknown"); document.Save();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(document.FilePath))!;
        json["futureOption"] = "keep me";
        json["groups"]![0]!["futureGroupOption"] = 7;
        json["groups"]![0]!["dictionaries"]![0]!["futureSourceOption"] = true;
        File.WriteAllText(document.FilePath, json.ToJsonString());
        var loaded = ConfigurationDocument.Open(document.FilePath); loaded.Configuration.MaxResults = 22; loaded.Save();
        var result = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(document.FilePath))!;
        Check((string?)result["futureOption"] == "keep me");
        Check((int?)result["groups"]![0]!["futureGroupOption"] == 7);
        Check((bool?)result["groups"]![0]!["dictionaries"]![0]!["futureSourceOption"] == true);
    });
    Test("Visual settings: group copy, rename/default, remove and file deduplication", () =>
    {
        var document = Editable("workspace"); var model = new ConfigurationWorkspace(document.Configuration);
        var original = document.Configuration.Groups[0]; var copy = model.DuplicateGroup(original);
        copy.Dictionaries[0].Name = "changed"; Check(original.Dictionaries[0].Name == "Fixture");
        model.RenameGroup(original, "English"); Check(document.Configuration.DefaultGroup == "English");
        Check(model.AddFiles(original, new[] { original.Dictionaries[0].Path, original.Dictionaries[0].Path }) == 0);
        var other = Path.Combine(Path.GetDirectoryName(document.FilePath)!, "other.mdx"); MdxFixture.Write(other, ["banana"]);
        Check(model.AddFiles(original, new[] { other, Path.Combine(root, "missing.mdx") }) == 2);
        Check(model.AddFiles(original, new[] { other }) == 0);
        model.RemoveGroup(original); Check(document.Configuration.DefaultGroup == copy.Name);
        try { model.RemoveGroup(copy); } catch (InvalidOperationException) { return; }
        throw new Exception("Last group was removed.");
    });
    Test("Native settings: pasted path quotes, separators, spaces and special characters", () =>
    {
        if (!OperatingSystem.IsWindows()) return;
        var expected = @"C:\A folder\Oxford’s #1 & [中文]\new-test.mdx";
        foreach (var pasted in new[] { expected, "  \"" + expected + "\"  ", "'" + expected + "'", "“" + expected + "”", expected.Replace('\\', '/') })
            Check(ConfigurationPath.Normalize(pasted, root) == expected, pasted);
        Check(ConfigurationPath.Normalize(@"C:\\A folder\\new-test.mdx", root) == @"C:\A folder\new-test.mdx");
        var unc = @"\\server\share\A folder\中文.mdx";
        Check(ConfigurationPath.Normalize(unc, root) == unc);
        Check(ConfigurationPath.Normalize("\"relative folder/new.mdx\"", root) == Path.Combine(root, "relative folder", "new.mdx"));
        var document = Editable("pasted");
        document.Configuration.GoldenDictPath = "\"C:/Some folder/goldendict.exe\"";
        document.Configuration.Groups[0].Dictionaries[0].Path = "“" + expected + "”";
        document.Save();
        Check(PluginConfiguration.Load(document.FilePath).Groups[0].Dictionaries[0].Path == expected);
        Check(!expected.Contains('\t') && !expected.Contains('\n'));
    });
    Test("Visual settings: saved changes reload into dictionary service", () =>
    {
        var document = Editable("apply"); document.Save();
        using var service = new DictionaryService(document.FilePath, Path.Combine(Path.GetDirectoryName(document.FilePath)!, "cache"));
        service.ReloadAsync().GetAwaiter().GetResult();
        document.Configuration.Groups[0].Dictionaries[0].Enabled = false; document.Save();
        service.ReloadAsync().GetAwaiter().GetResult();
        Check(service.Snapshot!.Groups[document.Configuration.DefaultGroup].Index.Count == 0);
    });
    Test("All groups: configuration persistence and group edits preserve the default", () =>
    {
        var document = Editable("all-default");
        var model = new ConfigurationWorkspace(document.Configuration);
        var first = document.Configuration.Groups[0]; model.DuplicateGroup(first);
        document.Configuration.DefaultGroup = PluginConfiguration.AllGroupsId;
        document.Save();
        Check(PluginConfiguration.Load(document.FilePath).DefaultGroup == PluginConfiguration.AllGroupsId);
        model.RenameGroup(first, "All"); // This user-defined name must not collide with the aggregate.
        document.Save();
        model.RemoveGroup(first); document.Save();
        Check(PluginConfiguration.Load(document.FilePath).DefaultGroup == PluginConfiguration.AllGroupsId);
        document.Configuration.DefaultGroup = "missing-group"; Reject(document.Save);
        document.Configuration.DefaultGroup = null!; Reject(document.Save);
    });
    Test("All groups: merge enabled dictionaries, deduplicate words and preserve individual searches", () =>
    {
        var folder = Path.Combine(root, "all-index"); Directory.CreateDirectory(folder);
        var a = Path.Combine(folder, "a.mdx"); var b = Path.Combine(folder, "b.mdx");
        MdxFixture.Write(a, ["apple", "apricot", "résumé"]); MdxFixture.Write(b, ["apple", "banana"]);
        var file = Path.Combine(folder, "config.json");
        var config = new PluginConfiguration
        {
            DefaultGroup = PluginConfiguration.AllGroupsId,
            Groups = [
                new() { Name = "All", GoldenDictGroup = "Target A", Dictionaries = [new() { Name = "A", Path = a }] },
                new() { Name = "B", Dictionaries = [new() { Name = "B", Path = b }, new() { Name = "Disabled", Path = "missing.mdx", Enabled = false }] },
                new() { Name = "Empty" }
            ]
        };
        File.WriteAllText(file, config.ToJson());
        using var service = new DictionaryService(file, Path.Combine(folder, "cache"));
        service.ReloadAsync().GetAwaiter().GetResult();
        var snapshot = service.Snapshot!; Check(snapshot is not null, service.LastError ?? "No snapshot");
        var all = snapshot!.Groups[PluginConfiguration.AllGroupsId];
        Check(all.Index.Count == 4 && all.Index.Search("apple").Single().Dictionaries.SequenceEqual(new[] { "A", "B" }));
        Check(all.Index.Search("banana", fuzzy: false).Single().Word == "banana");
        Check(all.Index.Search("resume", fuzzy: false).Single().Word == "résumé");
        Check(all.Index.Search("aple").Any(h => h.Word == "apple" && h.Kind == MatchKind.Correction));
        Check(snapshot.Groups["All"].Index.Search("banana", fuzzy: false).Count == 0);
        Check(snapshot.Groups["Empty"].Index.Count == 0 && snapshot.Warnings.Count == 0);
        Check(all.Configuration.GoldenDictGroup is null && all.Configuration.Dictionaries.Count == 2);
        config.Groups[1].Dictionaries[0].Enabled = false;
        config.IgnoreDiacritics = false; File.WriteAllText(file, config.ToJson());
        service.ReloadAsync().GetAwaiter().GetResult(); all = service.Snapshot!.Groups[PluginConfiguration.AllGroupsId];
        Check(all.Index.Search("banana", fuzzy: false).Count == 0 && all.Index.Search("resume", fuzzy: false).Count == 0);
    });
    foreach (var fixturePath in args)
        Test("Real MDX: " + Path.GetFileName(fixturePath), () =>
        {
            var watch = Stopwatch.StartNew(); var words = MdxHeadwordReader.Read(fixturePath);
            Check(words.Length > 100); Console.WriteLine($"  real fixture: {words.Length:N0} headwords in {watch.Elapsed.TotalMilliseconds:F1}ms");
            watch.Restart(); var index = new HeadwordIndex(words.Select(w => new WordSource(w, "real")));
            Console.WriteLine($"  index: {index.Count:N0} entries, {index.TrieNodeCount:N0} nodes in {watch.Elapsed.TotalMilliseconds:F1}ms");
            foreach (var query in new[] { "app", "apple", "aple", "appel", "serendipity", "zzzzzzzzz" })
            { watch.Restart(); var hits = index.Search(query); Console.WriteLine($"  {query}: {hits.Count} hits in {watch.Elapsed.TotalMilliseconds:F2}ms"); }
        });
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static int Osa(string a, string b)
{
    var d = new int[a.Length + 1, b.Length + 1];
    for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
    for (var j = 0; j <= b.Length; j++) d[0, j] = j;
    for (var i = 1; i <= a.Length; i++)
        for (var j = 1; j <= b.Length; j++)
        {
            d[i, j] = Math.Min(d[i - 1, j] + 1, Math.Min(d[i, j - 1] + 1, d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)));
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
    return d[a.Length, b.Length];
}
