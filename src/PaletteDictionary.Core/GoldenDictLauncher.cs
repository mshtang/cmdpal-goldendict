using System.Diagnostics;

namespace PaletteDictionary.Core;

public static class GoldenDictLauncher
{
    public static ProcessStartInfo CreateStartInfo(string executable, string word, string? group = null)
    {
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            throw new FileNotFoundException(UiText.Get("InvalidProgramPath"), executable);
        if (string.IsNullOrWhiteSpace(word) || word.Length > 512 || word.Contains('\0'))
            throw new ArgumentException(UiText.Get("InvalidQuery"), nameof(word));
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(executable))! };
        info.ArgumentList.Add("--main-window");
        if (!string.IsNullOrWhiteSpace(group)) info.ArgumentList.Add("--group-name=" + group);
        // Older GoldenDict-ng versions do not all handle '--' consistently.
        // Passing a URI as one positional argument keeps option-like words and
        // whitespace safe without requiring the OS URL scheme to be registered.
        info.ArgumentList.Add("goldendict://" + Uri.EscapeDataString(word.Trim()));
        return info;
    }

    public static void Open(string executable, string word, string? group = null)
    {
        using var process = Process.Start(CreateStartInfo(executable, word, group));
        if (process is null) throw new IOException(UiText.Get("LaunchFailed"));
    }
}
