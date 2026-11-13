using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteDictionary.Core;

namespace PaletteDictionary.Extension;

internal sealed partial class NativeSettings(ConfigurationPage page) : ICommandSettings
{
    public IContentPage SettingsPage => page;
}

/// <summary>Native Palette settings card: text paths, toggles and editable nested groups.</summary>
internal sealed partial class ConfigurationPage : ContentPage
{
    private readonly DictionaryService _service;
    private readonly string _path;
    private readonly object _sync = new();
    private ConfigurationDocument? _document;
    private string _messageKey = "PathHint";
    private long _revision;

    public ConfigurationPage(DictionaryService service, string path)
    {
        _service = service; _path = path;
        Id = "PaletteDictionary.Settings"; Title = UiText.Get("SettingsTitle"); Name = UiText.Get("OpenSettings"); Icon = new IconInfo("\uE713");
    }

    public void UpdateLanguage()
    {
        Title = UiText.Get("SettingsTitle"); Name = UiText.Get("OpenSettings");
        RaiseItemsChanged();
    }

    public override IContent[] GetContent()
    {
        lock (_sync)
        {
            try
            {
                _document ??= ConfigurationDocument.Open(_path);
                return [new ConfigurationForm(BuildCard(_document.Configuration), _revision, Submit)];
            }
            catch (Exception ex) { return [new MarkdownContent(UiText.Get("SettingsLoadFailed") + ex.Message)]; }
        }
    }

    private string BuildCard(PluginConfiguration config)
    {
        var body = new JsonArray
        {
            Note(_document!.RecoveryNotice ?? UiText.Get(_messageKey)),
            Actions(Action(UiText.Get("Save"), "save"), Action(UiText.Get("AddGroup"), "addGroup"), Action(UiText.Get("Reload"), "reload")),
            Row(Column(Heading(UiText.Get("BasicSettings"))),
                Column(Choice("uiLanguage", UiText.Get("Language"), config.UiLanguage,
                    [(UiText.Get("SystemLanguage"), "auto"), ("English", "en"), ("简体中文", "zh-Hans")]))),
            TextInput("goldenDictPath", UiText.Get("ProgramPath"), config.GoldenDictPath, UiText.Get("ProgramPathPlaceholder")),
            Row(
                Column(Choice("defaultGroup", UiText.Get("DefaultGroup"), Math.Max(0, config.Groups.FindIndex(g => g.Name == config.DefaultGroup)).ToString(CultureInfo.InvariantCulture),
                    config.Groups.Select((g, i) => (g.Name, i.ToString(CultureInfo.InvariantCulture))))),
                Column(NumberInput("maxResults", UiText.Get("MaxResults"), config.MaxResults, 1, 100)),
                Column(NumberInput("debounceMilliseconds", UiText.Get("Debounce"), config.DebounceMilliseconds, 0, 1000))),
            Row(
                Column(Toggle("fuzzyMatching", UiText.Get("FuzzyMatching"), config.FuzzyMatching)),
                Column(Choice("maxEditDistance", UiText.Get("MaxEditDistance"), config.MaxEditDistance.ToString(CultureInfo.InvariantCulture), [("0", "0"), ("1", "1"), ("2", "2")])),
                Column(Toggle("ignoreDiacritics", UiText.Get("IgnoreDiacritics"), config.IgnoreDiacritics)))
        };
        for (var g = 0; g < config.Groups.Count; g++)
        {
            var group = config.Groups[g]; var items = new JsonArray
            {
                Row(Column(Heading(UiText.Format("GroupNumber", g + 1))),
                    AutoColumn(Actions(Action(UiText.Get("AddDictionary"), "addDictionary", g), Action(UiText.Get("RemoveGroup"), "removeGroup", g)))),
                Row(Column(TextInput($"g{g}_name", UiText.Get("GroupName"), group.Name)),
                    Column(TextInput($"g{g}_target", UiText.Get("GoldenDictGroup"), group.GoldenDictGroup ?? "")))
            };
            for (var d = 0; d < group.Dictionaries.Count; d++)
            {
                var source = group.Dictionaries[d];
                items.Add(new JsonObject
                {
                    ["type"] = "Container", ["separator"] = true, ["spacing"] = "Small",
                    ["items"] = new JsonArray
                    {
                        Row(Column(new JsonObject { ["type"] = "TextBlock", ["text"] = UiText.Format("DictionaryNumber", d + 1), ["weight"] = "Bolder", ["spacing"] = "None" }),
                            AutoColumn(Toggle($"g{g}_d{d}_enabled", UiText.Get("Enabled"), source.Enabled)),
                            AutoColumn(Actions(Action(UiText.Get("RemoveDictionary"), "removeDictionary", g, d)))),
                        Row(Column(TextInput($"g{g}_d{d}_name", UiText.Get("DictionaryName"), source.Name)),
                            Column(TextInput($"g{g}_d{d}_path", UiText.Get("MdxPath"), source.Path), 3))
                    }
                });
            }
            body.Add(new JsonObject { ["type"] = "Container", ["separator"] = true, ["spacing"] = "Medium", ["items"] = items });
        }
        body.Add(Note(UiText.Get("SaveHint")));
        return new JsonObject
        {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json", ["type"] = "AdaptiveCard", ["version"] = "1.5",
            ["body"] = body, ["actions"] = new JsonArray { Action(UiText.Get("Save"), "save") }
        }.ToJsonString();
    }

    private ICommandResult Submit(long revision, string inputs, string data)
    {
        string? language = null;
        var saved = false;
        lock (_sync)
        {
            try
            {
                if (revision != _revision) return CommandResult.ShowToast(UiText.Get("StaleSettings"));
                var values = JsonNode.Parse(string.IsNullOrWhiteSpace(inputs) ? "{}" : inputs)?.AsObject() ?? new();
                var action = JsonNode.Parse(string.IsNullOrWhiteSpace(data) ? "{}" : data)?.AsObject() ?? new();
                var operation = Read(action, "operation", Read(values, "operation", "save"));
                if (operation == "reload")
                { _document = ConfigurationDocument.Open(_path); _messageKey = "Reloaded"; language = _document.Configuration.UiLanguage; }
                else
                {
                    _document ??= ConfigurationDocument.Open(_path);
                    var config = _document.Configuration;
                    ApplyInputs(config, values);
                    var groupIndex = ReadInt(action, "group", ReadInt(values, "group", -1));
                    var dictionaryIndex = ReadInt(action, "dictionary", ReadInt(values, "dictionary", -1));
                    switch (operation)
                    {
                        case "save":
                            _document.Save(); _document = ConfigurationDocument.Open(_path);
                            _messageKey = "SavedHint"; language = config.UiLanguage; saved = true;
                            break;
                        case "addGroup": new ConfigurationWorkspace(config).AddGroup(); _messageKey = "GroupAdded"; break;
                        case "addDictionary": config.Groups[groupIndex].Dictionaries.Add(new()); _messageKey = "DictionaryAdded"; break;
                        case "removeDictionary": config.Groups[groupIndex].Dictionaries.RemoveAt(dictionaryIndex); _messageKey = "DictionaryRemoved"; break;
                        case "removeGroup": new ConfigurationWorkspace(config).RemoveGroup(config.Groups[groupIndex]); _messageKey = "GroupRemoved"; break;
                        default: throw new InvalidDataException(UiText.Get("UnknownSettingsAction"));
                    }
                }
                _revision++;
            }
            catch (Exception ex) { return CommandResult.ShowToast(ex.Message); }
        }
        // Language notifications and COM callbacks must stay outside the form lock.
        if (language is not null) UiText.SetLanguage(language);
        if (saved) _ = _service.ReloadAsync();
        RaiseItemsChanged();
        return saved ? CommandResult.ShowToast(UiText.Get("SettingsSaved")) : CommandResult.KeepOpen();
    }

    private static void ApplyInputs(PluginConfiguration config, JsonObject values)
    {
        var selected = ReadInt(values, "defaultGroup", Math.Max(0, config.Groups.FindIndex(g => g.Name == config.DefaultGroup)));
        config.GoldenDictPath = Read(values, "goldenDictPath", config.GoldenDictPath);
        config.UiLanguage = Read(values, "uiLanguage", config.UiLanguage);
        config.MaxResults = ReadInt(values, "maxResults", config.MaxResults);
        config.DebounceMilliseconds = ReadInt(values, "debounceMilliseconds", config.DebounceMilliseconds);
        config.MaxEditDistance = ReadInt(values, "maxEditDistance", config.MaxEditDistance);
        config.FuzzyMatching = ReadBool(values, "fuzzyMatching", config.FuzzyMatching);
        config.IgnoreDiacritics = ReadBool(values, "ignoreDiacritics", config.IgnoreDiacritics);
        for (var g = 0; g < config.Groups.Count; g++)
        {
            var group = config.Groups[g]; group.Name = Read(values, $"g{g}_name", group.Name).Trim();
            var target = Read(values, $"g{g}_target", group.GoldenDictGroup ?? "").Trim();
            group.GoldenDictGroup = target.Length == 0 ? null : target;
            for (var d = 0; d < group.Dictionaries.Count; d++)
            {
                var source = group.Dictionaries[d]; source.Name = Read(values, $"g{g}_d{d}_name", source.Name).Trim();
                source.Path = Read(values, $"g{g}_d{d}_path", source.Path);
                source.Enabled = ReadBool(values, $"g{g}_d{d}_enabled", source.Enabled);
            }
        }
        if (selected < 0 || selected >= config.Groups.Count) throw new InvalidDataException(UiText.Get("SelectDefaultGroup"));
        config.DefaultGroup = config.Groups[selected].Name;
    }

    private static string Read(JsonObject values, string key, string fallback) => values.TryGetPropertyValue(key, out var value) && value is not null ? value.ToString() : fallback;
    private static int ReadInt(JsonObject values, string key, int fallback) => !values.ContainsKey(key) ? fallback :
        int.TryParse(Read(values, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : throw new InvalidDataException(UiText.Get("InvalidInteger"));
    private static bool ReadBool(JsonObject values, string key, bool fallback) => !values.ContainsKey(key) ? fallback :
        bool.TryParse(Read(values, key, ""), out var value) ? value : throw new InvalidDataException(UiText.Get("InvalidToggle"));
    private static JsonObject Heading(string text) => new() { ["type"] = "TextBlock", ["text"] = text, ["weight"] = "Bolder", ["size"] = "Medium", ["wrap"] = true, ["spacing"] = "Small" };
    private static JsonObject Note(string text) => new() { ["type"] = "TextBlock", ["text"] = text, ["wrap"] = true, ["isSubtle"] = true };
    private static JsonObject TextInput(string id, string label, string value, string placeholder = "") => new()
    { ["type"] = "Input.Text", ["id"] = id, ["label"] = label, ["value"] = value, ["placeholder"] = placeholder, ["spacing"] = "None" };
    private static JsonObject NumberInput(string id, string label, int value, int min, int max) => new()
    { ["type"] = "Input.Number", ["id"] = id, ["label"] = label, ["value"] = value, ["min"] = min, ["max"] = max, ["spacing"] = "None" };
    private static JsonObject Toggle(string id, string label, bool value) => new()
    { ["type"] = "Input.Toggle", ["id"] = id, ["title"] = label, ["value"] = value ? "true" : "false", ["valueOn"] = "true", ["valueOff"] = "false", ["wrap"] = true, ["spacing"] = "None" };
    private static JsonObject Choice(string id, string label, string value, IEnumerable<(string Title, string Value)> choices) => new()
    {
        ["type"] = "Input.ChoiceSet", ["id"] = id, ["label"] = label, ["value"] = value, ["spacing"] = "None",
        ["choices"] = new JsonArray(choices.Select(c => (JsonNode)new JsonObject { ["title"] = c.Title, ["value"] = c.Value }).ToArray())
    };
    private static JsonObject Action(string title, string operation, int group = -1, int dictionary = -1) => new()
    { ["type"] = "Action.Submit", ["title"] = title, ["data"] = new JsonObject { ["operation"] = operation, ["group"] = group, ["dictionary"] = dictionary } };
    private static JsonObject Actions(params JsonObject[] actions) => new()
    { ["type"] = "ActionSet", ["spacing"] = "None", ["actions"] = new JsonArray(actions.Select(a => (JsonNode)a).ToArray()) };
    private static JsonObject Row(params JsonObject[] columns) => new()
    { ["type"] = "ColumnSet", ["spacing"] = "Small", ["columns"] = new JsonArray(columns.Select(c => (JsonNode)c).ToArray()) };
    private static JsonObject Column(JsonObject item, int width = 1) => new()
    { ["type"] = "Column", ["width"] = width, ["spacing"] = "Small", ["verticalContentAlignment"] = "Bottom", ["items"] = new JsonArray(item) };
    private static JsonObject AutoColumn(JsonObject item)
    {
        var column = Column(item); column["width"] = "auto"; return column;
    }
}

internal sealed partial class ConfigurationForm : FormContent
{
    private readonly long _revision;
    private readonly Func<long, string, string, ICommandResult> _submit;
    public ConfigurationForm(string template, long revision, Func<long, string, string, ICommandResult> submit)
    { TemplateJson = template; DataJson = "{}"; StateJson = "{}"; _revision = revision; _submit = submit; }
    public override ICommandResult SubmitForm(string inputs, string data) => _submit(_revision, inputs, data);
    public override ICommandResult SubmitForm(string inputs) => _submit(_revision, inputs, "{}");
}
