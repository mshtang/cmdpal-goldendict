using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using PaletteDictionary.Core;
using WinRT;

if (args.Length == 1 && args[0] == "--installed")
{
    var clsid = new Guid("6B671D4F-3922-4B36-8AD4-8194F84C385D");
    var iid = GuidGenerator.GetIID(typeof(IExtension));
    var hr = Native.CoCreateInstance(ref clsid, 0, 4, ref iid, out var pointer);
    Marshal.ThrowExceptionForHR(hr);
    IExtension installed;
    try { installed = MarshalInspectable<IExtension>.FromAbi(pointer); }
    finally { Marshal.Release(pointer); }
    var provider = installed.GetProvider(ProviderType.Commands).As<ICommandProvider>();
    if (provider.TopLevelCommands().Length != 2 || provider.GetCommand("PaletteDictionary.Search") is null)
        throw new Exception("Installed extension commands are missing.");
    var form = provider.Settings!.SettingsPage.GetContent().Single().As<IFormContent>();
    if (!Inputs(form).ContainsKey("goldenDictPath")) throw new Exception("Installed settings page is missing.");
    Console.WriteLine("PASS installed production CLSID activation and native settings (read only)");
    return 0;
}
if (args.Length is < 2 or > 3) { Console.Error.WriteLine("Usage: smoke <extension.exe> <config.json> [auto|en|zh-Hans]"); return 2; }
var temporary = Path.Combine(Path.GetTempPath(), "PaletteDictionary.Smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var testConfigPath = Path.Combine(temporary, "config.json");
var initial = PluginConfiguration.Load(args[1]);
initial.UiLanguage = args.Length == 3 ? args[2] : "auto";
UiText.SetLanguage("auto");
File.WriteAllText(testConfigPath, initial.ToJson());
var info = new ProcessStartInfo(Path.GetFullPath(args[0])) { UseShellExecute = false, CreateNoWindow = true };
info.ArgumentList.Add("--smoke-server");
info.Environment["PALETTEDICTIONARY_CONFIG"] = testConfigPath;
using var server = Process.Start(info) ?? throw new Exception("Server failed to start.");
try
{
    IExtension? extension = null;
    var classId = new Guid("0AC75259-B561-49DE-802D-FAE24D77DA26");
    var interfaceId = GuidGenerator.GetIID(typeof(IExtension));
    var lastResult = 0;
    for (var attempt = 0; attempt < 100; attempt++)
    {
        lastResult = Native.CoCreateInstance(ref classId, 0, 4, ref interfaceId, out var raw);
        if (lastResult >= 0)
        {
            try { extension = MarshalInspectable<IExtension>.FromAbi(raw); }
            finally { Marshal.Release(raw); }
            break;
        }
        if (server.HasExited) throw new Exception($"Server exited with {server.ExitCode}.");
        Thread.Sleep(100);
    }
    if (extension is null) Marshal.ThrowExceptionForHR(lastResult);
    var provider = extension!.GetProvider(ProviderType.Commands).As<ICommandProvider>();
    var top = provider.TopLevelCommands();
    if (top.Length != 2 || top[0].Title != UiText.Get("AppName") || top[1].Title != UiText.Get("SettingsTitle")) throw new Exception("Unexpected top-level commands.");
    if (top[0].MoreCommands.Length < 2) throw new Exception("Context menu did not marshal.");
    var page = top[0].Command.As<IDynamicListPage>();
    for (var attempt = 0; attempt < 200; attempt++)
    {
        page.SearchText = "aple";
        Thread.Sleep(100);
        var items = page.GetItems();
        if (items.Any(i => i.Title == "apple"))
        {
            var apple = items.First(i => i.Title == "apple");
            if (!apple.Subtitle.StartsWith(UiText.Correction(1), StringComparison.Ordinal)) throw new Exception("Correction label missing.");
            if (apple.MoreCommands.Length < 2) throw new Exception("Candidate context menu missing.");
            Console.WriteLine($"PASS out-of-process COM: {items.Length} items; {apple.Title} / {apple.Subtitle}");
            page.SearchText = "appl";
            for (var wait = 0; wait < 50 && page.GetItems().All(i => i.Title != "apple"); wait++) Thread.Sleep(100);
            if (page.GetItems().All(i => i.Title != "apple")) throw new Exception("Prefix search did not update.");
            Console.WriteLine("PASS live dynamic page: typo → prefix");
            TestSettings(provider, testConfigPath);
            TestSystemLanguage(provider, testConfigPath);
            TestSettingsConflict(provider, testConfigPath);
            TestAllGroups(provider, testConfigPath);
            extension.As<IDisposable>().Dispose();
            if (!server.WaitForExit(5000)) throw new Exception("COM server did not shut down on Dispose.");
            Console.WriteLine("PASS isolated COM server shutdown");
            return 0;
        }
    }
    throw new Exception("Timed out waiting for apple candidate: " + string.Join(" | ", page.GetItems().Select(i => i.Title + ":" + i.Subtitle)));
}
finally { if (!server.HasExited) { server.Kill(); server.WaitForExit(); } Directory.Delete(temporary, true); }

static void TestSettings(ICommandProvider provider, string file)
{
    var settings = provider.Settings?.SettingsPage ?? throw new Exception("Native extension settings entry is missing.");
    var first = settings.GetContent().Single().As<IFormContent>();
    TestSettingsLayout(first);
    var inputs = Inputs(first);
    var original = PluginConfiguration.Load(file);
    var path = original.Groups[0].Dictionaries[0].Path;
    inputs["goldenDictPath"] = "\"" + original.GoldenDictPath.Replace('\\', '/') + "\"";
    inputs["g0_d0_path"] = "“" + path.Replace('\\', '/') + "”";
    inputs["g0_d0_name"] = "Oxford’s [中文] & #1";
    inputs["g0_name"] = "原生设置测试";
    inputs["defaultGroup"] = "0";
    inputs["maxResults"] = 23; inputs["fuzzyMatching"] = "false";
    var result = first.SubmitForm(inputs.ToJsonString(), "{\"operation\":\"save\"}");
    if (result.Kind != CommandResultKind.ShowToast) throw new Exception("Form save did not return a result.");
    var saved = PluginConfiguration.Load(file);
    if (saved.MaxResults != 23 || saved.FuzzyMatching || saved.DefaultGroup != "原生设置测试" || saved.Groups[0].Dictionaries[0].Path != path)
        throw new Exception("Native form did not round-trip paths, numbers, toggles or default group.");
    Console.WriteLine("PASS native settings: paths, numeric fields, toggles and default group");

    var form = settings.GetContent().Single().As<IFormContent>();
    var beforeAdd = File.ReadAllText(file);
    var originalCount = saved.Groups[0].Dictionaries.Count;
    result = form.SubmitForm(Inputs(form).ToJsonString(), "{\"operation\":\"addDictionary\",\"group\":0}");
    if (result.Kind != CommandResultKind.KeepOpen || File.ReadAllText(file) != beforeAdd) throw new Exception("Add must remain unsaved until Save.");
    var added = settings.GetContent().Single().As<IFormContent>();
    AssertExpanded(added, "group0_content");
    inputs = Inputs(added);
    var missingPath = @"C:/Missing folder/Oxford’s #1 & [test].mdx";
    inputs[$"g0_d{originalCount}_path"] = "\"" + missingPath + "\"";
    inputs[$"g0_d{originalCount}_name"] = "仅保存路径";
    added.SubmitForm(inputs.ToJsonString(), "{\"operation\":\"save\"}");
    saved = PluginConfiguration.Load(file);
    if (saved.Groups[0].Dictionaries.Count != originalCount + 1 || saved.Groups[0].Dictionaries[^1].Path != missingPath.Replace('/', '\\'))
        throw new Exception("Missing-file text path was not saved.");
    Console.WriteLine("PASS native settings: add dictionary without file existence checks");

    var snapshot = File.ReadAllText(file);
    first.SubmitForm(Inputs(first).ToJsonString(), "{\"operation\":\"save\"}");
    if (File.ReadAllText(file) != snapshot) throw new Exception("An outdated form overwrote newer settings.");
    form = settings.GetContent().Single().As<IFormContent>();
    form.SubmitForm(Inputs(form).ToJsonString(), $"{{\"operation\":\"removeDictionary\",\"group\":0,\"dictionary\":{originalCount}}}");
    form = settings.GetContent().Single().As<IFormContent>(); form.SubmitForm(Inputs(form).ToJsonString(), "{\"operation\":\"save\"}");
    if (PluginConfiguration.Load(file).Groups[0].Dictionaries.Count != originalCount) throw new Exception("Dictionary removal did not save.");

    form = settings.GetContent().Single().As<IFormContent>();
    form.SubmitForm(Inputs(form).ToJsonString(), "{\"operation\":\"addGroup\"}");
    form = settings.GetContent().Single().As<IFormContent>(); inputs = Inputs(form);
    var groupIndex = saved.Groups.Count;
    AssertExpanded(form, $"group{groupIndex}_content");
    inputs[$"g{groupIndex}_name"] = "第二分组"; inputs["defaultGroup"] = groupIndex.ToString();
    form.SubmitForm(inputs.ToJsonString(), "{\"operation\":\"save\"}");
    if (PluginConfiguration.Load(file).DefaultGroup != "第二分组") throw new Exception("Added group did not become the default.");
    Console.WriteLine("PASS native settings: add/remove and stale-form protection");
}

static JsonObject Inputs(IFormContent form)
{
    var result = new JsonObject();
    foreach (var node in Walk(JsonNode.Parse(form.TemplateJson)!))
        if (node["type"]?.ToString().StartsWith("Input.", StringComparison.Ordinal) == true && node["id"] is { } id)
            result[id.ToString()] = node["value"]?.DeepClone();
    return result;
}
static void TestSettingsLayout(IFormContent form)
{
    var card = JsonNode.Parse(form.TemplateJson)!;
    var nodes = Walk(card).ToArray();
    if (nodes.Count(n => n["data"]?["operation"]?.ToString() == "save") != 1)
        throw new Exception("Settings must contain only one Save button.");
    if (nodes.Any(n => n["data"]?["operation"]?.ToString() == "reload"))
        throw new Exception("Reload should only be offered for an external edit conflict.");
    if (!Walk(card["body"]!.AsArray().Last()!).Any(n => n["data"]?["operation"]?.ToString() == "addGroup"))
        throw new Exception("Add group must follow the last group.");
    var ids = nodes.Where(n => n["id"] is not null).Select(n => n["id"]!.ToString()).ToArray();
    if (ids.Length != ids.Distinct().Count()) throw new Exception("Settings element IDs must be unique.");
    if (ids.Contains("uiLanguage") || nodes.Any(n => n["text"]?.ToString() == UiText.Get("PathHint")))
        throw new Exception("Removed language selector or path hint is still visible.");
    var sections = card["body"]!.AsArray().OfType<JsonObject>().Where(n => n["items"]?[0]?["selectAction"] is not null).ToArray();
    if (sections.Length != Inputs(form).Count(p => p.Key.EndsWith("_target", StringComparison.Ordinal)) + 1)
        throw new Exception("General and dictionary groups must share the same section style.");
    foreach (var section in sections)
    {
        var action = section["items"]![0]!["selectAction"]!;
        if (action["type"]?.ToString() != "Action.ToggleVisibility") throw new Exception("Section header is not collapsible.");
        var targets = action["targetElements"]!.AsArray().Select(n => n!.ToString()).ToArray();
        if (targets.Length != 3 || targets.Any(id => !ids.Contains(id))) throw new Exception("Invalid section toggle targets.");
        var content = nodes.Single(n => n["id"]?.ToString() == targets[0]);
        var down = nodes.Single(n => n["id"]?.ToString() == targets[1]);
        var up = nodes.Single(n => n["id"]?.ToString() == targets[2]);
        var expanded = content["isVisible"]!.GetValue<bool>();
        if (expanded != (targets[0] == "general_content") || down["isVisible"]!.GetValue<bool>() == expanded || up["isVisible"]!.GetValue<bool>() != expanded)
            throw new Exception("Initial section visibility and chevrons disagree.");
    }
    Console.WriteLine("PASS native settings: consistent collapsible sections, valid targets and no language selector/path hint");
}
static void AssertExpanded(IFormContent form, string id)
{
    var content = Walk(JsonNode.Parse(form.TemplateJson)!).Single(n => n["id"]?.ToString() == id);
    if (content["isVisible"]?.GetValue<bool>() != true) throw new Exception("Edited group should stay expanded.");
}
static void TestSettingsConflict(ICommandProvider provider, string file)
{
    var settings = provider.Settings!.SettingsPage;
    var form = settings.GetContent().Single().As<IFormContent>();
    var external = PluginConfiguration.Load(file);
    external.MaxResults = 42;
    File.WriteAllText(file, external.ToJson());
    var snapshot = File.ReadAllText(file);
    var inputs = Inputs(form); inputs["maxResults"] = 17;
    var result = form.SubmitForm(inputs.ToJsonString(), "{\"operation\":\"save\"}");
    if (result.Kind != CommandResultKind.KeepOpen || File.ReadAllText(file) != snapshot)
        throw new Exception("Conflicting save must keep the page open and preserve external changes.");
    form = settings.GetContent().Single().As<IFormContent>();
    if (!Walk(JsonNode.Parse(form.TemplateJson)!).Any(n => n["data"]?["operation"]?.ToString() == "reload") || Inputs(form)["maxResults"]?.ToString() != "17")
        throw new Exception("Conflict must preserve the draft and offer Reload.");
    form.SubmitForm("{}", "{\"operation\":\"reload\"}");
    form = settings.GetContent().Single().As<IFormContent>();
    if (Inputs(form)["maxResults"]?.ToString() != "42" || Walk(JsonNode.Parse(form.TemplateJson)!).Any(n => n["data"]?["operation"]?.ToString() == "reload"))
        throw new Exception("Reload must restore external values and hide the recovery action.");
    Console.WriteLine("PASS native settings: conflict preserves file/draft and offers Reload only until recovery");
}
static void TestAllGroups(ICommandProvider provider, string file)
{
    var settings = provider.Settings!.SettingsPage;
    var form = settings.GetContent().Single().As<IFormContent>();
    var config = PluginConfiguration.Load(file);
    var second = config.Groups.Count - 1;
    form.SubmitForm(Inputs(form).ToJsonString(), $"{{\"operation\":\"addDictionary\",\"group\":{second}}}");
    form = settings.GetContent().Single().As<IFormContent>();
    var inputs = Inputs(form);
    var dictionary = config.Groups[second].Dictionaries.Count;
    inputs[$"g{second}_d{dictionary}_path"] = config.Groups[0].Dictionaries[0].Path;
    inputs[$"g{second}_d{dictionary}_name"] = "Secondary source";
    inputs["defaultGroup"] = "-1";
    form.SubmitForm(inputs.ToJsonString(), "{\"operation\":\"save\"}");
    if (PluginConfiguration.Load(file).DefaultGroup != PluginConfiguration.AllGroupsId || Inputs(settings.GetContent().Single().As<IFormContent>())["defaultGroup"]?.ToString() != "-1")
        throw new Exception("All must round-trip through the settings form and configuration.");
    var page = provider.TopLevelCommands()[0].Command.As<IDynamicListPage>();
    WaitFor(() => page.Filters?.CurrentFilterId == PluginConfiguration.AllGroupsId, "Saving All did not update the active search filter.");
    if (page.Filters!.GetFilters().Length != config.Groups.Count + 1) throw new Exception("All filter is missing.");
    page.SearchText = "apple";
    WaitFor(() => page.GetItems().Any(i => i.Title == "apple" && i.Subtitle.Contains("Secondary source") && i.Subtitle.Contains("Oxford’s")), "All did not merge dictionary sources across groups.");
    page.Filters.CurrentFilterId = config.Groups[second].Name;
    WaitFor(() => page.GetItems().Any(i => i.Title == "apple" && i.Subtitle.Contains("Secondary source") && !i.Subtitle.Contains("Oxford’s")), "Individual group filtering stopped working.");
    foreach (var selected in new[] { "0", "-1" })
    {
        form = settings.GetContent().Single().As<IFormContent>(); inputs = Inputs(form); inputs["defaultGroup"] = selected;
        form.SubmitForm(inputs.ToJsonString(), "{\"operation\":\"save\"}");
        var expected = selected == "-1" ? PluginConfiguration.AllGroupsId : config.Groups[0].Name;
        WaitFor(() => page.Filters?.CurrentFilterId == expected, "Changed default group was not applied to search.");
    }
    Console.WriteLine("PASS All groups: form persistence, combined search, individual filtering and live default changes");
}
static void WaitFor(Func<bool> condition, string failure)
{
    for (var attempt = 0; attempt < 100; attempt++)
    {
        if (condition()) return;
        Thread.Sleep(100);
    }
    throw new Exception(failure);
}
static void TestSystemLanguage(ICommandProvider provider, string file)
{
    var settings = provider.Settings!.SettingsPage;
    var names = PluginConfiguration.Load(file).Groups.Select(g => g.Name).ToArray();
    var expectedTitle = UiText.Get("SettingsTitle");
    foreach (var language in new[] { "en", "zh-Hans", "auto" })
    {
        var form = settings.GetContent().Single().As<IFormContent>();
        var values = Inputs(form); values["uiLanguage"] = language;
        form.SubmitForm(values.ToJsonString(), "{\"operation\":\"save\"}");
        if (provider.TopLevelCommands()[1].Title != expectedTitle || settings.Title != expectedTitle)
            throw new Exception("UI must keep following the system language.");
        var saved = PluginConfiguration.Load(file);
        if (saved.UiLanguage != "auto" || !saved.Groups.Select(g => g.Name).SequenceEqual(names))
            throw new Exception("Saving must reset legacy language overrides without changing user-defined names.");
        if (Inputs(settings.GetContent().Single().As<IFormContent>()).ContainsKey("uiLanguage"))
            throw new Exception("Language selector should be removed.");
    }
    Console.WriteLine("PASS system language: legacy overrides ignored, saves use auto and user names preserved");
}
static IEnumerable<JsonObject> Walk(JsonNode node)
{
    if (node is JsonObject obj) { yield return obj; foreach (var child in obj.Select(p => p.Value).OfType<JsonNode>()) foreach (var item in Walk(child)) yield return item; }
    else if (node is JsonArray array) foreach (var child in array.OfType<JsonNode>()) foreach (var item in Walk(child)) yield return item;
}

internal static class Native
{
    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint result);
}
