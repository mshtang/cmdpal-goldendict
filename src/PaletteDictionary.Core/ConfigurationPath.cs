namespace PaletteDictionary.Core;

/// <summary>Normalizes pasted path text without checking or opening the target file.</summary>
public static class ConfigurationPath
{
    public static string Normalize(string? text, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var path = text.Trim();
        while (path.Length >= 2 && ((path[0], path[^1]) is ('"', '"') or ('\'', '\'') or ('“', '”') or ('‘', '’')))
            path = path[1..^1].Trim();
        if (path.Length == 0) return "";
        path = Environment.ExpandEnvironmentVariables(path);
        if (OperatingSystem.IsWindows()) path = path.Replace('/', '\\');
        return Path.GetFullPath(path, Path.GetFullPath(baseDirectory));
    }
}
