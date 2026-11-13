using System.Security.Cryptography;
using System.Text;

namespace PaletteDictionary.Core;

public readonly record struct SourceStamp(long Length, long LastWriteUtcTicks)
{
    public static SourceStamp Read(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) throw new FileNotFoundException(UiText.Get("DictionaryNotFound"), path);
        return new(file.Length, file.LastWriteTimeUtc.Ticks);
    }
}

public sealed class HeadwordCache(string directory)
{
    private const int FormatVersion = 1;
    public string GetPath(string source)
    {
        var identity = Path.GetFullPath(source);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        return Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))) + ".pdhw");
    }

    public string[] LoadOrBuild(string source, CancellationToken cancellationToken = default, bool force = false)
    {
        var stamp = SourceStamp.Read(source);
        var path = GetPath(source);
        if (!force && TryRead(path, stamp, out var words)) return words;
        words = MdxHeadwordReader.Read(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (SourceStamp.Read(source) != stamp) throw new IOException(UiText.Get("DictionaryChanged"));
        Directory.CreateDirectory(directory);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    writer.Write("PDHW"); writer.Write(FormatVersion); writer.Write(stamp.Length); writer.Write(stamp.LastWriteUtcTicks);
                    writer.Write(words.Length);
                    foreach (var word in words) { cancellationToken.ThrowIfCancellationRequested(); writer.Write(word); }
                    writer.Flush();
                }
                stream.Position = 0;
                var hash = SHA256.HashData(stream);
                stream.Position = stream.Length;
                stream.Write(hash);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return words;
    }

    private static bool TryRead(string file, SourceStamp stamp, out string[] words)
    {
        words = [];
        try
        {
            if (!File.Exists(file)) return false;
            var info = new FileInfo(file);
            if (info.Length < 61 || info.Length > 2L * 1024 * 1024 * 1024) return false;
            var bytes = File.ReadAllBytes(file);
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, bytes.Length - 32)), bytes.AsSpan(bytes.Length - 32))) return false;
            using var stream = new MemoryStream(bytes, 0, bytes.Length - 32, false);
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            if (reader.ReadString() != "PDHW" || reader.ReadInt32() != FormatVersion || reader.ReadInt64() != stamp.Length || reader.ReadInt64() != stamp.LastWriteUtcTicks) return false;
            var count = reader.ReadInt32();
            if (count < 0 || count > 50_000_000 || count > bytes.Length / 2) return false;
            var result = new string[count];
            for (var i = 0; i < count; i++)
            {
                var word = reader.ReadString();
                if (word.Length is < 1 or > MdxHeadwordReader.MaxHeadwordLength || (i > 0 && StringComparer.Ordinal.Compare(result[i - 1], word) >= 0)) return false;
                result[i] = word;
            }
            if (stream.Position != stream.Length) return false;
            words = result;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException) { return false; }
    }
}
