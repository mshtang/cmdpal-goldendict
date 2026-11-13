using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using PaletteDictionary.Core;

namespace PaletteDictionary.Extension;

internal sealed partial class LookupCommand(string executable, string word, string? group) : InvokableCommand
{
    public override string Name => UiText.Get("OpenGoldenDict");
    public override ICommandResult Invoke()
    {
        try { GoldenDictLauncher.Open(executable, word, group); return CommandResult.Dismiss(); }
        catch (Exception ex) { return CommandResult.ShowToast(ex.Message); }
    }
}

internal sealed partial class RefreshCommand(DictionaryService service, bool rebuild = false) : InvokableCommand
{
    public override string Name => rebuild ? UiText.Get("RebuildCache") : UiText.Get("ReloadDictionaries");
    public override ICommandResult Invoke()
    {
        _ = service.ReloadAsync(rebuild);
        return CommandResult.ShowToast(rebuild ? UiText.Get("RebuildingCache") : UiText.Get("Reloading"));
    }
}
