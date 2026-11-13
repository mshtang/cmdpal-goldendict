using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaletteDictionary.Core;

public sealed class DictionarySource
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public bool Enabled { get; set; } = true;
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class DictionaryGroup
{
    public string Name { get; set; } = "默认";
    public string? GoldenDictGroup { get; set; }
    public List<DictionarySource> Dictionaries { get; set; } = [];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class PluginConfiguration
{
    public string UiLanguage { get; set; } = "auto";
    public string GoldenDictPath { get; set; } = "";
    public string DefaultGroup { get; set; } = "默认";
    public int MaxResults { get; set; } = 30;
    public int DebounceMilliseconds { get; set; } = 60;
    public bool FuzzyMatching { get; set; } = true;
    public int MaxEditDistance { get; set; } = 2;
    public bool IgnoreDiacritics { get; set; } = true;
    public List<DictionaryGroup> Groups { get; set; } = [new()];
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static PluginConfiguration Load(string file)
    {
        return Parse(File.ReadAllText(file), file);
    }

    public static PluginConfiguration Parse(string text, string file)
    {
        var config = JsonSerializer.Deserialize(text, ConfigurationJsonContext.Default.PluginConfiguration)
            ?? throw new InvalidDataException(UiText.Get("EmptyConfiguration"));
        config.NormalizeAndValidate(file);
        return config;
    }

    public void NormalizeAndValidate(string file)
    {
        var config = this;
        if (config.UiLanguage is not ("auto" or "en" or "zh-Hans")) throw new InvalidDataException(UiText.Get("InvalidLanguage"));
        if (config.Groups is null || config.Groups.Count == 0) throw new InvalidDataException(UiText.Get("RequireGroup"));
        if (config.Groups.Any(g => g is null || string.IsNullOrWhiteSpace(g.Name) || g.Dictionaries is null))
            throw new InvalidDataException(UiText.Get("InvalidGroup"));
        if (config.Groups.Select(g => g.Name).Distinct(StringComparer.Ordinal).Count() != config.Groups.Count)
            throw new InvalidDataException(UiText.Get("DuplicateGroup"));
        if (!config.Groups.Any(g => g.Name == config.DefaultGroup))
            throw new InvalidDataException(UiText.Get("InvalidDefaultGroup"));
        if (config.MaxResults is < 1 or > 100 || config.DebounceMilliseconds is < 0 or > 1000 || config.MaxEditDistance is < 0 or > 2)
            throw new InvalidDataException(UiText.Get("InvalidSearchOptions"));
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file))!;
        config.GoldenDictPath = Expand(config.GoldenDictPath, directory);
        foreach (var source in config.Groups.SelectMany(g => g.Dictionaries))
        {
            if (source is null || string.IsNullOrWhiteSpace(source.Path)) throw new InvalidDataException(UiText.Get("EmptyDictionaryPath"));
            source.Path = Expand(source.Path, directory);
            if (!source.Path.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(UiText.Format("MdxOnly", source.Path));
            if (string.IsNullOrWhiteSpace(source.Name)) source.Name = System.IO.Path.GetFileNameWithoutExtension(source.Path);
        }
        foreach (var group in config.Groups)
        {
            if (group.Dictionaries.Select(s => s.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != group.Dictionaries.Count)
                throw new InvalidDataException(UiText.Format("DuplicateDictionary", group.Name));
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, ConfigurationJsonContext.Default.PluginConfiguration);

    public static PluginConfiguration CreateDefault()
    {
        var name = UiText.Get("DefaultGroupName");
        return new() { DefaultGroup = name, Groups = [new() { Name = name }] };
    }

    private static string Expand(string? path, string directory) => ConfigurationPath.Normalize(path, directory);

    public static void EnsureExists(string file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        if (!File.Exists(file))
        {
            using var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write);
            JsonSerializer.Serialize(stream, CreateDefault(), ConfigurationJsonContext.Default.PluginConfiguration);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(PluginConfiguration))]
internal partial class ConfigurationJsonContext : JsonSerializerContext;
