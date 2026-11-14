using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteDictionary.Core;

namespace PaletteDictionary.Extension;

internal sealed partial class DictionarySearchPage : DynamicListPage, IDisposable
{
    private readonly DictionaryService _service;
    private readonly string _configurationPath;
    private readonly ConfigurationPage _settingsPage;
    private readonly object _queryLock = new();
    private CancellationTokenSource? _queryCancellation;
    private IListItem[] _items = [];
    private string _query = "";
    private string _groupId = "";
    private string _filterSignature = "";
    private string? _defaultGroup;
    private long _generation;
    private bool _disposed;

    public DictionarySearchPage(DictionaryService service, string configurationPath, ConfigurationPage settingsPage)
    {
        _service = service; _configurationPath = configurationPath; _settingsPage = settingsPage;
        Id = "PaletteDictionary.Search";
        Title = UiText.Get("AppName"); Name = UiText.Get("Lookup"); Icon = new IconInfo("\uE82D");
        PlaceholderText = UiText.Get("SearchPlaceholder");
        _service.Changed += OnServiceChanged;
        OnServiceChanged();
    }
    public void UpdateLanguage()
    {
        Title = UiText.Get("AppName"); Name = UiText.Get("Lookup");
        PlaceholderText = UiText.Get("SearchPlaceholder");
        Schedule();
    }
    public IContextItem[] ContextCommands() =>
    [
        new CommandContextItem(_settingsPage),
        new CommandContextItem(new RefreshCommand(_service)),
        new CommandContextItem(new RefreshCommand(_service, true))
    ];
    public override IListItem[] GetItems() => Volatile.Read(ref _items);
    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        lock (_queryLock) _query = newSearch;
        Schedule();
    }
    private void OnServiceChanged()
    {
        lock (_queryLock)
        {
            if (_disposed) return;
            var snapshot = _service.Snapshot;
            if (snapshot is not null)
            {
                var defaultChanged = _defaultGroup != snapshot.Configuration.DefaultGroup;
                if (defaultChanged || !snapshot.Groups.ContainsKey(_groupId)) _groupId = snapshot.Configuration.DefaultGroup;
                _defaultGroup = snapshot.Configuration.DefaultGroup;
                var signature = string.Join('\0', snapshot.Groups.Keys);
                if (signature != _filterSignature || defaultChanged)
                {
                    var filters = new GroupFilters(snapshot.Groups.Keys.ToArray(), _groupId);
                    filters.PropChanged += (_, args) =>
                    {
                        if (args.PropertyName == nameof(GroupFilters.CurrentFilterId))
                        {
                            lock (_queryLock) _groupId = filters.CurrentFilterId;
                            Schedule();
                        }
                    };
                    Filters = filters; _filterSignature = signature;
                }
            }
        }
        Schedule();
    }
    private void Schedule()
    {
        CancellationToken token; string query; string groupId; long generation;
        lock (_queryLock)
        {
            if (_disposed) return;
            _queryCancellation?.Cancel(); _queryCancellation?.Dispose();
            _queryCancellation = new CancellationTokenSource(); token = _queryCancellation.Token;
            query = _query.Trim(); groupId = _groupId; generation = ++_generation;
        }
        var snapshot = _service.Snapshot;
        var group = snapshot is not null && snapshot.Groups.TryGetValue(groupId, out var selected) ? selected : null;
        // Clear stale selectable words immediately: Enter always belongs to the current input.
        Publish(query.Length == 0 ? IdleItems(snapshot, group) : DirectItems(snapshot, group, query), generation);
        if (query.Length == 0 || group is null || query.Length > MdxHeadwordReader.MaxHeadwordLength) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(snapshot!.Configuration.DebounceMilliseconds, token).ConfigureAwait(false);
                var config = snapshot.Configuration;
                var hits = group.Index.Search(query, config.MaxResults, config.FuzzyMatching, config.MaxEditDistance, token);
                var items = hits.Select(hit => (IListItem)new ListItem(new LookupCommand(config.GoldenDictPath, hit.Word, group.Configuration.GoldenDictGroup))
                {
                    Title = hit.Word,
                    Subtitle = (hit.Kind == MatchKind.Correction ? UiText.Correction(hit.EditDistance) : "") + string.Join(" · ", hit.Dictionaries),
                    Icon = new IconInfo(hit.Kind == MatchKind.Correction ? "\uE70F" : "\uE82D"),
                    TextToSuggest = hit.Word,
                    MoreCommands = ContextCommands()
                }).ToList();
                if (!hits.Any(h => h.Word == query)) items.Add(DirectItem(config, group, query));
                AddStatus(items, snapshot);
                token.ThrowIfCancellationRequested(); Publish(items.ToArray(), generation);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                var items = DirectItems(snapshot, group, query).ToList();
                items.Add(Status(UiText.Get("SuggestionsFailed"), ex.Message)); Publish(items.ToArray(), generation);
            }
        }, token);
    }
    private void Publish(IListItem[] items, long generation)
    {
        lock (_queryLock)
        {
            if (_disposed || generation != _generation) return;
            Volatile.Write(ref _items, items);
        }
        RaiseItemsChanged();
    }
    private IListItem[] IdleItems(ServiceSnapshot? snapshot, GroupIndex? group)
    {
        var items = new List<IListItem>();
        if (group is not null) items.Add(Status(UiText.Format("HeadwordCount", group.Index.Count, group.Configuration.Name), UiText.Get("LookupHint")));
        if (group is null || group.Index.Count == 0)
            items.Add(new ListItem(_settingsPage) { Title = UiText.Get("ConfigureDictionaries"), Subtitle = UiText.Get("ConfigureHint"), Icon = new IconInfo("\uE713") });
        items.Add(new ListItem(_settingsPage) { Title = UiText.Get("OpenSettings"), Subtitle = UiText.Get("ManageSettings"), Icon = new IconInfo("\uE713") });
        items.Add(new ListItem(new RefreshCommand(_service)) { Title = UiText.Get("ReloadDictionaries"), Icon = new IconInfo("\uE72C") });
        AddStatus(items, snapshot); return items.ToArray();
    }
    private IListItem[] DirectItems(ServiceSnapshot? snapshot, GroupIndex? group, string query)
    {
        var items = new List<IListItem>();
        if (snapshot is not null && group is not null) items.Add(DirectItem(snapshot.Configuration, group, query));
        else items.Add(new ListItem(_settingsPage) { Title = UiText.Get("ConfigureFirst"), Subtitle = UiText.Get("ConfigureFirstHint"), Icon = new IconInfo("\uE713") });
        if (query.Length > MdxHeadwordReader.MaxHeadwordLength) items.Add(Status(UiText.Get("QueryTooLong"), UiText.Get("QueryLimitHint")));
        AddStatus(items, snapshot); return items.ToArray();
    }
    private ListItem DirectItem(PluginConfiguration config, GroupIndex group, string query) => new(new LookupCommand(config.GoldenDictPath, query, group.Configuration.GoldenDictGroup))
    { Title = UiText.Format("DirectLookup", query), Subtitle = UiText.Format("OpenInGroup", group.Configuration.Name), Icon = new IconInfo("\uE721"), MoreCommands = ContextCommands() };
    private void AddStatus(List<IListItem> items, ServiceSnapshot? snapshot)
    {
        if (_service.IsLoading) items.Add(Status(UiText.Get("UpdatingIndex"), snapshot is null ? UiText.Get("InitialIndexHint") : UiText.Get("ExistingIndexHint")));
        if (_service.LastError is { } error) items.Add(Status(UiText.Get("UpdateFailed"), error));
        if (snapshot is not null)
        {
            if (string.IsNullOrWhiteSpace(snapshot.Configuration.GoldenDictPath) || !File.Exists(snapshot.Configuration.GoldenDictPath))
                items.Add(new ListItem(_settingsPage) { Title = UiText.Get("SetProgramPath"), Subtitle = UiText.Get("SetProgramPathHint"), Icon = new IconInfo("\uE7BA") });
            foreach (var warning in snapshot.Warnings) items.Add(Status(UiText.Get("DictionariesUnavailable"), warning));
        }
    }
    private ListItem Status(string title, string subtitle) => new(new NoOpCommand())
    { Title = title, Subtitle = subtitle, Icon = new IconInfo("\uE946"), MoreCommands = ContextCommands() };
    public void Dispose()
    {
        lock (_queryLock)
        {
            _disposed = true; _queryCancellation?.Cancel(); _queryCancellation?.Dispose();
            _service.Changed -= OnServiceChanged;
        }
    }
}

internal sealed partial class GroupFilters : Filters
{
    private readonly IFilterItem[] _filters;
    public GroupFilters(string[] names, string selected)
    {
        _filters = names.OrderBy(name => name != PluginConfiguration.AllGroupsId)
            .Select(name => (IFilterItem)new Filter { Id = name, Name = name == PluginConfiguration.AllGroupsId ? UiText.Get("AllGroups") : name }).ToArray();
        CurrentFilterId = selected;
    }
    public override IFilterItem[] GetFilters() => _filters;
}
