using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PaletteDictionary.Core;

/// <summary>An editable configuration with atomic saves, backup recovery and conflict detection.</summary>
public sealed class ConfigurationDocument
{
    public string FilePath { get; }
    public PluginConfiguration Configuration { get; }
    public string? RecoveryNotice { get; }
    private string? _fingerprint;
    private bool _originalWasValid;

    private ConfigurationDocument(string path, PluginConfiguration configuration, string? fingerprint, bool valid, string? notice)
    { FilePath = path; Configuration = configuration; _fingerprint = fingerprint; _originalWasValid = valid; RecoveryNotice = notice; }

    public static ConfigurationDocument Open(string path)
    {
        path = Path.GetFullPath(path);
        if (!File.Exists(path)) return new(path, PluginConfiguration.CreateDefault(), null, false, null);
        var text = File.ReadAllText(path);
        var fingerprint = Fingerprint(text);
        try { return new(path, PluginConfiguration.Parse(text, path), fingerprint, true, null); }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or FormatException)
        {
            try
            {
                if (File.Exists(path + ".bak"))
                    return new(path, PluginConfiguration.Load(path + ".bak"), fingerprint, false,
                        UiText.Get("BackupRecovered"));
            }
            catch (Exception backupError) when (backupError is JsonException or InvalidDataException or ArgumentException or IOException or FormatException) { }
            return new(path, PluginConfiguration.CreateDefault(), fingerprint, false,
                UiText.Get("InvalidConfigurationRecovered"));
        }
    }

    public void Save()
    {
        Configuration.NormalizeAndValidate(FilePath);
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, ".config-" + Guid.NewGuid().ToString("N") + ".tmp");
        // Cooperating writers use the same sidecar lock; external edits are detected below.
        using var writeLock = new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        try
        {
            AssertUnchanged();
            var text = Configuration.ToJson() + Environment.NewLine;
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(text);
                stream.Write(bytes); stream.Flush(true);
            }
            AssertUnchanged();
            if (File.Exists(FilePath))
            {
                if (_originalWasValid) File.Replace(temporary, FilePath, FilePath + ".bak");
                else
                {
                    var invalidBackup = FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + ".json";
                    File.Copy(FilePath, invalidBackup);
                    File.Move(temporary, FilePath, true);
                }
            }
            else File.Move(temporary, FilePath);
            _fingerprint = Fingerprint(text); _originalWasValid = true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private void AssertUnchanged()
    {
        var current = File.Exists(FilePath) ? Fingerprint(File.ReadAllText(FilePath)) : null;
        if (current != _fingerprint) throw new ConfigurationConflictException();
    }
    private static string Fingerprint(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}

public sealed class ConfigurationConflictException() : IOException(UiText.Get("ConfigurationConflict"));

public sealed class ConfigurationWorkspace(PluginConfiguration configuration)
{
    public PluginConfiguration Configuration { get; } = configuration;
    public DictionaryGroup AddGroup()
    {
        var group = new DictionaryGroup { Name = UniqueName(UiText.Get("NewGroup")) };
        Configuration.Groups.Add(group); return group;
    }
    public DictionaryGroup DuplicateGroup(DictionaryGroup original)
    {
        var group = new DictionaryGroup
        {
            Name = UniqueName(UiText.Format("CopyGroup", original.Name)), GoldenDictGroup = original.GoldenDictGroup,
            Extra = original.Extra?.ToDictionary(p => p.Key, p => p.Value.Clone()),
            Dictionaries = original.Dictionaries.Select(s => new DictionarySource
            { Name = s.Name, Path = s.Path, Enabled = s.Enabled, Extra = s.Extra?.ToDictionary(p => p.Key, p => p.Value.Clone()) }).ToList()
        };
        Configuration.Groups.Add(group); return group;
    }
    public void RenameGroup(DictionaryGroup group, string name)
    {
        var wasDefault = Configuration.DefaultGroup == group.Name;
        group.Name = name.Trim();
        if (wasDefault) Configuration.DefaultGroup = group.Name;
    }
    public void RemoveGroup(DictionaryGroup group)
    {
        if (Configuration.Groups.Count <= 1) throw new InvalidOperationException(UiText.Get("KeepOneGroup"));
        var wasDefault = Configuration.DefaultGroup == group.Name;
        Configuration.Groups.Remove(group);
        if (wasDefault) Configuration.DefaultGroup = Configuration.Groups[0].Name;
    }
    public int AddFiles(DictionaryGroup group, IEnumerable<string> files)
    {
        var paths = group.Dictionaries.Select(d => Path.GetFullPath(d.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var additions = new List<DictionarySource>();
        foreach (var file in files)
        {
            var full = Path.GetFullPath(file);
            if (!full.EndsWith(".mdx", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException(UiText.Get("SelectMdx"));
            if (paths.Add(full)) additions.Add(new() { Path = full, Name = Path.GetFileNameWithoutExtension(full) });
        }
        group.Dictionaries.AddRange(additions); return additions.Count;
    }
    private string UniqueName(string stem)
    {
        var name = stem; var n = 2;
        while (Configuration.Groups.Any(g => g.Name == name)) name = stem + " " + n++;
        return name;
    }
}
