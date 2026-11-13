using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using PaletteDictionary.Core.ThirdParty;

namespace PaletteDictionary.Core;

/// <summary>Reads MDX keyword sections only. Never reads record/HTML/MDD sections.</summary>
public static class MdxHeadwordReader
{
    private const int MaxBlockBytes = 128 * 1024 * 1024;
    private const int MaxHeaderBytes = 4 * 1024 * 1024;
    public const int MaxHeadwordLength = 256;
    static MdxHeadwordReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string[] Read(string path, CancellationToken cancellationToken = default)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65536, FileOptions.SequentialScan);
        var headerLength = CheckedSize(ReadU32(stream), MaxHeaderBytes);
        var headerBytes = ReadBytes(stream, headerLength);
        if (headerLength < 4 || (headerLength & 1) != 0) throw new InvalidDataException(UiText.Get("MdxInvalidHeaderLength"));
        var checksum = ReadU32LittleEndian(stream);
        if (Adler32(headerBytes) != checksum) throw new InvalidDataException(UiText.Get("MdxHeaderChecksum"));
        var headerText = Encoding.Unicode.GetString(headerBytes).TrimEnd('\0');
        using var xmlReader = XmlReader.Create(new StringReader(headerText), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        var header = XElement.Load(xmlReader);
        string Attribute(string name, string fallback = "") => header.Attribute(name)?.Value ?? fallback;
        var version = double.Parse(Attribute("GeneratedByEngineVersion", "2.0"), CultureInfo.InvariantCulture);
        if (version < 1 || version >= 3) throw new NotSupportedException(UiText.Format("MdxUnsupportedVersion", version));
        var encryptedValue = Attribute("Encrypted", "0");
        var encrypted = encryptedValue.Equals("No", StringComparison.OrdinalIgnoreCase) ? 0 :
            encryptedValue.Equals("Yes", StringComparison.OrdinalIgnoreCase) ? 1 : int.Parse(encryptedValue, CultureInfo.InvariantCulture);
        if ((encrypted & ~2) != 0) throw new NotSupportedException(UiText.Get("MdxEncrypted"));
        var encodingName = Attribute("Encoding", "UTF-8").ToUpperInvariant();
        var encoding = encodingName switch
        {
            "UTF8" or "UTF-8" => new UTF8Encoding(false, true),
            "UTF16" or "UTF-16" or "UTF-16LE" => new UnicodeEncoding(false, false, true),
            "GBK" or "GB2312" or "GB18030" => Encoding.GetEncoding("GB18030", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            "BIG5" => Encoding.GetEncoding(950, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            _ => Encoding.GetEncoding(encodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
        };
        var unit = encoding.CodePage == Encoding.Unicode.CodePage ? 2 : 1;
        var modern = version >= 2;
        var numberSize = modern ? 8 : 4;
        var keywordHeader = ReadBytes(stream, modern ? 40 : 16);
        var h = 0;
        var numberBlocks = TakeNumber(keywordHeader, ref h, numberSize);
        var numberEntries = TakeNumber(keywordHeader, ref h, numberSize);
        var infoDecompressedLength = modern ? TakeNumber(keywordHeader, ref h, numberSize) : 0;
        var infoLength = TakeNumber(keywordHeader, ref h, numberSize);
        var totalBlockLength = TakeNumber(keywordHeader, ref h, numberSize);
        if (numberBlocks > 2_000_000 || numberEntries > 50_000_000) throw new InvalidDataException(UiText.Get("MdxHeadwordLimit"));
        if (modern && ReadU32(stream) != Adler32(keywordHeader)) throw new InvalidDataException(UiText.Get("MdxKeywordChecksum"));
        var infoData = ReadBytes(stream, CheckedSize(infoLength, MaxBlockBytes));
        if (modern)
        {
            if ((encrypted & 2) != 0) DecryptInfo(infoData);
            infoData = DecodeBlock(infoData, CheckedSize(infoDecompressedLength, MaxBlockBytes));
        }
        var position = 0;
        var words = new HashSet<string>(StringComparer.Ordinal);
        ulong entriesSeen = 0, blocksLengthSeen = 0;
        for (ulong block = 0; block < numberBlocks; block++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entryCount = TakeNumber(infoData, ref position, numberSize);
            SkipBoundary(infoData, ref position, modern, unit);
            SkipBoundary(infoData, ref position, modern, unit);
            var compressedLength = TakeNumber(infoData, ref position, numberSize);
            var decompressedLength = TakeNumber(infoData, ref position, numberSize);
            var data = DecodeBlock(ReadBytes(stream, CheckedSize(compressedLength, MaxBlockBytes)), CheckedSize(decompressedLength, MaxBlockBytes));
            var p = 0;
            for (ulong entry = 0; entry < entryCount; entry++)
            {
                if ((entry & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                _ = TakeNumber(data, ref p, numberSize); // Record offset is deliberately not followed.
                var start = p;
                while (p + unit <= data.Length && (data[p] != 0 || (unit == 2 && data[p + 1] != 0))) p += unit;
                if (p + unit > data.Length) throw new InvalidDataException(UiText.Get("MdxMissingTerminator"));
                var word = encoding.GetString(data, start, p - start).Trim();
                p += unit;
                if (word.Length is > 0 and <= MaxHeadwordLength && !word.StartsWith(":", StringComparison.Ordinal) && !word.Any(char.IsControl))
                    words.Add(word.Normalize(NormalizationForm.FormC));
            }
            if (p != data.Length) throw new InvalidDataException(UiText.Get("MdxEntryCount"));
            entriesSeen += entryCount;
            blocksLengthSeen += compressedLength;
        }
        if (position != infoData.Length || entriesSeen != numberEntries || blocksLengthSeen != totalBlockLength)
            throw new InvalidDataException(UiText.Get("MdxIndexStructure"));
        return words.Order(StringComparer.Ordinal).ToArray();
    }

    private static void SkipBoundary(byte[] data, ref int position, bool modern, int unit)
    {
        var length = TakeNumber(data, ref position, modern ? 2 : 1);
        var bytes = checked((int)(length + (modern ? 1UL : 0UL)) * unit);
        if (position > data.Length - bytes) throw new InvalidDataException(UiText.Get("MdxBoundary"));
        position += bytes;
    }

    internal static byte[] DecodeBlock(byte[] data, int expectedLength)
    {
        if (data.Length < 8) throw new InvalidDataException(UiText.Get("MdxShortBlock"));
        var type = BinaryPrimitives.ReadUInt32LittleEndian(data);
        byte[] output;
        switch (type)
        {
            case 0: output = data[8..]; break;
            case 1: output = LzoDecoder.Decode(data.AsSpan(8), expectedLength); break;
            case 2:
                output = new byte[expectedLength];
                using (var compressed = new MemoryStream(data, 8, data.Length - 8, false))
                using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
                {
                    zlib.ReadExactly(output);
                    if (zlib.ReadByte() != -1) throw new InvalidDataException(UiText.Get("MdxDecompressionLength"));
                }
                break;
            default: throw new NotSupportedException(UiText.Format("MdxCompressionType", type));
        }
        if (output.Length != expectedLength || Adler32(output) != BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)))
            throw new InvalidDataException(UiText.Get("MdxBlockChecksum"));
        return output;
    }

    private static void DecryptInfo(byte[] data)
    {
        if (data.Length < 8) throw new InvalidDataException(UiText.Get("MdxShortEncryptedIndex"));
        var seed = new byte[8];
        data.AsSpan(4, 4).CopyTo(seed);
        BinaryPrimitives.WriteUInt32LittleEndian(seed.AsSpan(4), 0x3695);
        var key = Ripemd128.ComputeHash(seed);
        byte previous = 0x36;
        for (var i = 8; i < data.Length; i++)
        {
            var original = data[i];
            data[i] = (byte)(((original >> 4) | (original << 4)) ^ previous ^ ((i - 8) & 255) ^ key[(i - 8) % key.Length]);
            previous = original;
        }
    }

    internal static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (var value in data) { a = (a + value) % 65521; b = (b + a) % 65521; }
        return (b << 16) | a;
    }

    private static ulong TakeNumber(byte[] data, ref int position, int size)
    {
        if (position < 0 || position > data.Length - size) throw new InvalidDataException(UiText.Get("MdxIndexBounds"));
        var span = data.AsSpan(position, size);
        position += size;
        return size switch { 8 => BinaryPrimitives.ReadUInt64BigEndian(span), 4 => BinaryPrimitives.ReadUInt32BigEndian(span),
            2 => BinaryPrimitives.ReadUInt16BigEndian(span), 1 => span[0], _ => throw new InvalidDataException() };
    }
    private static int CheckedSize(ulong size, int maximum) => size <= (ulong)maximum ? (int)size : throw new InvalidDataException(UiText.Get("MdxBlockTooLarge"));
    private static byte[] ReadBytes(Stream stream, int size)
    {
        if (size > stream.Length - stream.Position) throw new InvalidDataException(UiText.Get("MdxIncompleteFile"));
        var data = new byte[size]; stream.ReadExactly(data); return data;
    }
    private static uint ReadU32(Stream stream) { Span<byte> b = stackalloc byte[4]; stream.ReadExactly(b); return BinaryPrimitives.ReadUInt32BigEndian(b); }
    private static uint ReadU32LittleEndian(Stream stream) { Span<byte> b = stackalloc byte[4]; stream.ReadExactly(b); return BinaryPrimitives.ReadUInt32LittleEndian(b); }
}
