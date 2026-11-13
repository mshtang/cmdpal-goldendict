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
    if (!Inputs(form).ContainsKey("uiLanguage")) throw new Exception("Installed settings page is missing.");
    Console.WriteLine("PASS installed production CLSID activation and native settings (read only)");
    return 0;
}
if (args.Length is < 2 or > 3) { Console.Error.WriteLine("Usage: smoke <extension.exe> <config.json> [auto|en|zh-Hans]"); return 2; }
var temporary = Path.Combine(Path.GetTempPath(), "PaletteDictionary.Smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
var testConfigPath = Path.Combine(temporary, "config.json");
var initial = PluginConfiguration.Load(args[1]);
initial.UiLanguage = args.Length == 3 ? args[2] : "auto";
UiText.SetLanguage(initial.UiLanguage);
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
            TestLanguages(provider, testConfigPath);
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
    var inputs = Inputs(first);
    var original = PluginConfiguration.Load(file);
    var path = original.Groups[0].Dictionaries[0].Path;
    inputs["goldenDictPath"] = "\"" + original.GoldenDictPath.Replace('\\', '/') + "\"";
    inputs["g0_d0_path"] = "“" + path.Replace('\\', '/') + "”";
    inputs["g0_d0_name"] = "Oxford’s [中文] & #1";
    inputs["g0_name"] = "原生设置测试";
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
static void TestLanguages(ICommandProvider provider, string file)
{
    var settings = provider.Settings!.SettingsPage;
    var names = PluginConfiguration.Load(file).Groups.Select(g => g.Name).ToArray();
    foreach (var language in new[] { "en", "zh-Hans", "auto", "en" })
    {
        var form = settings.GetContent().Single().As<IFormContent>();
        var values = Inputs(form); values["uiLanguage"] = language; values["fuzzyMatching"] = "true";
        form.SubmitForm(values.ToJsonString(), "{\"operation\":\"save\"}");
        UiText.SetLanguage(language);
        var expectedTitle = UiText.Culture.Name == "zh" ? "本地词典设置" : "Local Dictionary Settings";
        if (provider.TopLevelCommands()[1].Title != expectedTitle || settings.Title != expectedTitle)
            throw new Exception("Page/command titles did not update after a language change.");
        var saved = PluginConfiguration.Load(file);
        if (saved.UiLanguage != language || !saved.Groups.Select(g => g.Name).SequenceEqual(names))
            throw new Exception("Language selection was not persisted or changed user-defined names.");
        form = settings.GetContent().Single().As<IFormContent>();
        var nodes = Walk(JsonNode.Parse(form.TemplateJson)!).ToArray();
        var selector = nodes.Single(n => n["id"]?.ToString() == "uiLanguage");
        if (selector["label"]?.ToString() != UiText.Get("Language")) throw new Exception("Language label did not switch.");
        if (UiText.Culture.Name == "en")
            foreach (var node in nodes)
            {
                var type = node["type"]?.ToString() ?? "";
                var keys = type.StartsWith("Input.", StringComparison.Ordinal) ? new[] { "label", "title", "placeholder" } :
                    type is "TextBlock" or "Action.Submit" ? new[] { "text", "title" } : [];
                foreach (var key in keys)
                    if (node[key]?.ToString().Any(c => c is >= '\u4e00' and <= '\u9fff') == true)
                        throw new Exception("Untranslated English UI text: " + node[key]);
            }
        var page = provider.TopLevelCommands()[0].Command.As<IDynamicListPage>();
        page.SearchText = "aple";
        var expectedCorrection = UiText.Correction(1);
        for (var wait = 0; wait < 100 && page.GetItems().All(i => i.Title != "apple" || !i.Subtitle.StartsWith(expectedCorrection, StringComparison.Ordinal)); wait++) Thread.Sleep(100);
        if (page.GetItems().All(i => i.Title != "apple" || !i.Subtitle.StartsWith(expectedCorrection, StringComparison.Ordinal)))
            throw new Exception("Suggestion language did not change without restarting.");
        Console.WriteLine($"PASS language switch: {language}; titles, form, candidates and user names");
    }
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
