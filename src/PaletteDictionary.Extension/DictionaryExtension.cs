using System.Runtime.InteropServices;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteDictionary.Core;

namespace PaletteDictionary.Extension;

[Guid("6B671D4F-3922-4B36-8AD4-8194F84C385D")]
public sealed partial class DictionaryExtension(ManualResetEvent stopped) : IExtension, IDisposable
{
    private readonly DictionaryCommandsProvider _provider = new();
    public object? GetProvider(ProviderType providerType) => providerType == ProviderType.Commands ? _provider : null;
    public void Dispose() { _provider.Dispose(); stopped.Set(); }
}

// A distinct, unregistered CLSID lets the smoke client exercise the exact provider
// in a fresh process without COM selecting a Palette-owned running class factory.
[Guid("0AC75259-B561-49DE-802D-FAE24D77DA26")]
public sealed partial class SmokeExtension(ManualResetEvent stopped) : IExtension, IDisposable
{
    private readonly DictionaryExtension _inner = new(stopped);
    public object? GetProvider(ProviderType providerType) => _inner.GetProvider(providerType);
    public void Dispose() => _inner.Dispose();
}

public sealed partial class DictionaryCommandsProvider : CommandProvider
{
    private readonly DictionaryService _service;
    private readonly DictionarySearchPage _page;
    private readonly ConfigurationPage _configure;
    private readonly CommandItem[] _commands;
    private int _disposed;
    public DictionaryCommandsProvider()
    {
        var overridePath = Environment.GetEnvironmentVariable("PALETTEDICTIONARY_CONFIG");
        var path = string.IsNullOrWhiteSpace(overridePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaletteDictionary", "config.json")
            : Path.GetFullPath(overridePath);
        var root = Path.GetDirectoryName(path)!;
        // Older configurations may contain a manual language override. The UI follows Windows.
        UiText.SetLanguage("auto");
        _service = new DictionaryService(path, Path.Combine(root, "cache"));
        _configure = new ConfigurationPage(_service, path);
        _page = new DictionarySearchPage(_service, path, _configure);
        DisplayName = UiText.Get("AppName");
        Frozen = false;
        Id = "PaletteDictionary";
        Icon = new IconInfo("\uE82D");
        Settings = new NativeSettings(_configure);
        _commands = [new CommandItem(_page)
        {
            Title = UiText.Get("AppName"), Subtitle = UiText.Get("ExtensionSubtitle"),
            MoreCommands = _page.ContextCommands()
        }, new CommandItem(_configure) { Title = UiText.Get("SettingsTitle"), Subtitle = UiText.Get("SettingsSubtitle") }];
        UiText.LanguageChanged += UpdateLanguage;
        _service.Start();
    }
    private void UpdateLanguage()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        DisplayName = UiText.Get("AppName");
        _commands[0].Title = UiText.Get("AppName"); _commands[0].Subtitle = UiText.Get("ExtensionSubtitle");
        _commands[1].Title = UiText.Get("SettingsTitle"); _commands[1].Subtitle = UiText.Get("SettingsSubtitle");
        _page.UpdateLanguage(); _configure.UpdateLanguage();
        RaiseItemsChanged();
    }
    public override ICommandItem[] TopLevelCommands() => _commands;
    public override ICommand? GetCommand(string id) => id == _page.Id ? _page : id == _configure.Id ? _configure : null;
    public override ICommandItem? GetCommandItem(string id) => id == _page.Id ? _commands[0] : id == _configure.Id ? _commands[1] : null;
    public override void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        UiText.LanguageChanged -= UpdateLanguage;
        _page.Dispose(); _service.Dispose();
    }
}
