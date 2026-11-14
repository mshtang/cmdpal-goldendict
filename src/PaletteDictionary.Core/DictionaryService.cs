namespace PaletteDictionary.Core;

public sealed record GroupIndex(DictionaryGroup Configuration, HeadwordIndex Index);
public sealed record ServiceSnapshot(PluginConfiguration Configuration, IReadOnlyDictionary<string, GroupIndex> Groups,
    IReadOnlyList<string> Warnings, DateTime UpdatedUtc);

/// <summary>Publishes complete immutable snapshots. Never mutates an index queried by the UI.</summary>
public sealed class DictionaryService : IDisposable
{
    private readonly string _configurationPath;
    private readonly HeadwordCache _cache;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Timer _timer;
    private Dictionary<string, SourceStamp> _stamps = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _configTime;
    private long _lastSourceCheck;
    private ServiceSnapshot? _snapshot;
    private string? _lastError;
    private volatile bool _loading;
    public ServiceSnapshot? Snapshot => Volatile.Read(ref _snapshot);
    public string? LastError => Volatile.Read(ref _lastError);
    public bool IsLoading => _loading;
    public event Action? Changed;
    public DictionaryService(string configurationPath, string cacheDirectory)
    {
        _configurationPath = configurationPath; _cache = new(cacheDirectory);
        _timer = new Timer(_ => _ = PollAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }
    public void Start()
    {
        _ = ReloadAsync();
        _timer.Change(TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3));
    }
    public async Task ReloadAsync(bool force = false)
    {
        if (!await _gate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            _loading = true; Changed?.Invoke();
            await Task.Run(() => Build(force, _lifetime.Token), _lifetime.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Volatile.Write(ref _lastError, ex.Message); }
        finally { _loading = false; _gate.Release(); Changed?.Invoke(); }
    }
    private void Build(bool force, CancellationToken token)
    {
        PluginConfiguration.EnsureExists(_configurationPath);
        var config = PluginConfiguration.Load(_configurationPath);
        var configTime = File.GetLastWriteTimeUtc(_configurationPath);
        var warnings = new List<string>();
        var wordsByPath = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var stamps = new Dictionary<string, SourceStamp>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in config.Groups.SelectMany(g => g.Dictionaries).Where(s => s.Enabled))
        {
            token.ThrowIfCancellationRequested();
            if (wordsByPath.ContainsKey(source.Path)) continue;
            try
            {
                wordsByPath[source.Path] = _cache.LoadOrBuild(source.Path, token, force);
                stamps[source.Path] = SourceStamp.Read(source.Path);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                warnings.Add($"{source.Name}：{ex.Message}"); wordsByPath[source.Path] = [];
            }
        }
        var groups = new Dictionary<string, GroupIndex>(StringComparer.Ordinal);
        foreach (var group in config.Groups)
        {
            var words = group.Dictionaries.Where(s => s.Enabled).SelectMany(s => wordsByPath[s.Path].Select(w => new WordSource(w, s.Name)));
            groups.Add(group.Name, new(group, new HeadwordIndex(words, config.IgnoreDiacritics, token)));
        }
        var allSources = config.Groups.SelectMany(g => g.Dictionaries).Where(s => s.Enabled).ToList();
        var allIndex = config.Groups.Count == 1 ? groups[config.Groups[0].Name].Index :
            new HeadwordIndex(allSources.SelectMany(s => wordsByPath[s.Path].Select(w => new WordSource(w, s.Name))), config.IgnoreDiacritics, token);
        groups.Add(PluginConfiguration.AllGroupsId, new(new DictionaryGroup
        {
            Name = UiText.Get("AllGroups"), Dictionaries = allSources
        }, allIndex));
        token.ThrowIfCancellationRequested();
        if (File.GetLastWriteTimeUtc(_configurationPath) != configTime) throw new IOException(UiText.Get("ConfigurationChangedDuringBuild"));
        _stamps = stamps; _configTime = configTime;
        Volatile.Write(ref _snapshot, new(config, groups, warnings, DateTime.UtcNow));
        Volatile.Write(ref _lastError, null);
    }
    private async Task PollAsync()
    {
        if (_loading || _lifetime.IsCancellationRequested) return;
        try
        {
            if (File.GetLastWriteTimeUtc(_configurationPath) != _configTime) { await ReloadAsync().ConfigureAwait(false); return; }
            var now = Environment.TickCount64;
            if (now - _lastSourceCheck < 30_000) return;
            _lastSourceCheck = now;
            var snapshot = Snapshot;
            if (snapshot is null) return;
            foreach (var source in snapshot.Configuration.Groups.SelectMany(g => g.Dictionaries).Where(s => s.Enabled))
            {
                var stamp = File.Exists(source.Path) ? SourceStamp.Read(source.Path) : default;
                if (!_stamps.TryGetValue(source.Path, out var previous) || stamp != previous)
                { await ReloadAsync().ConfigureAwait(false); return; }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Volatile.Write(ref _lastError, ex.Message); Changed?.Invoke(); }
    }
    public void Dispose() { _timer.Dispose(); _lifetime.Cancel(); }
}
