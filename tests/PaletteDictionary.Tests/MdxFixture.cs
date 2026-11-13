using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using PaletteDictionary.Core;
using PaletteDictionary.Core.ThirdParty;

namespace PaletteDictionary.Tests;

internal static class MdxFixture
{
    public static void Write(string path, string[] words, bool modern = true, string encodingName = "UTF-8", int compression = 2, bool encrypted = false)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(encodingName);
        var unit = encoding.CodePage == 1200 ? 2 : 1;
        var numberSize = modern ? 8 : 4;
        using var keys = new MemoryStream();
        for (var i = 0; i < words.Length; i++)
        {
            Number(keys, (ulong)i * 16, numberSize);
            keys.Write(encoding.GetBytes(words[i])); keys.Write(new byte[unit]);
        }
        var keyBlock = Block(keys.ToArray(), compression);
        using var info = new MemoryStream();
        Number(info, (ulong)words.Length, numberSize);
        foreach (var word in new[] { words[0], words[^1] })
        {
            var data = encoding.GetBytes(word);
            Number(info, (ulong)data.Length / (ulong)unit, modern ? 2 : 1); info.Write(data);
            if (modern) info.Write(new byte[unit]);
        }
        Number(info, (ulong)keyBlock.Length, numberSize); Number(info, (ulong)keys.Length, numberSize);
        var infoBlock = modern ? Block(info.ToArray(), 2) : info.ToArray();
        if (encrypted)
        {
            var seed = new byte[8]; infoBlock.AsSpan(4, 4).CopyTo(seed); BinaryPrimitives.WriteUInt32LittleEndian(seed.AsSpan(4), 0x3695);
            var secret = Ripemd128.ComputeHash(seed); byte previous = 0x36;
            for (var i = 8; i < infoBlock.Length; i++)
            {
                var plain = (byte)(infoBlock[i] ^ previous ^ ((i - 8) & 255) ^ secret[(i - 8) % 16]);
                var cipher = (byte)((plain >> 4) | (plain << 4)); infoBlock[i] = cipher; previous = cipher;
            }
        }
        var header = Encoding.Unicode.GetBytes($"<Dictionary GeneratedByEngineVersion=\"{(modern ? "2.0" : "1.2")}\" Encoding=\"{encodingName}\" Encrypted=\"{(encrypted ? 2 : 0)}\" Title=\"Fixture\"/>\0");
        using var file = File.Create(path); Number(file, (ulong)header.Length, 4); file.Write(header);
        Span<byte> little = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(little, MdxHeadwordReader.Adler32(header)); file.Write(little);
        using var keywordHeader = new MemoryStream();
        Number(keywordHeader, 1, numberSize); Number(keywordHeader, (ulong)words.Length, numberSize);
        if (modern) Number(keywordHeader, (ulong)info.Length, numberSize);
        Number(keywordHeader, (ulong)infoBlock.Length, numberSize); Number(keywordHeader, (ulong)keyBlock.Length, numberSize);
        file.Write(keywordHeader.ToArray());
        if (modern) Number(file, MdxHeadwordReader.Adler32(keywordHeader.ToArray()), 4);
        file.Write(infoBlock); file.Write(keyBlock);
        // A record section is unnecessary for keyword extraction; include sentinel bytes
        // to assert the reader stops before attempting to decompress HTML.
        file.Write(Encoding.UTF8.GetBytes("DO-NOT-READ-RECORDS"));
    }
    public static byte[] Block(byte[] data, int type)
    {
        using var block = new MemoryStream();
        Span<byte> marker = stackalloc byte[4]; BinaryPrimitives.WriteInt32LittleEndian(marker, type); block.Write(marker);
        Number(block, MdxHeadwordReader.Adler32(data), 4);
        if (type == 0) block.Write(data);
        else if (type == 1)
        {
            if (data.Length > 238 || data.Length < 4) throw new ArgumentException("Literal LZO fixture size.");
            block.WriteByte((byte)(data.Length + 17)); block.Write(data); block.Write([17, 0, 0]);
        }
        else { using var z = new ZLibStream(block, CompressionLevel.SmallestSize, true); z.Write(data); }
        return block.ToArray();
    }
    private static void Number(Stream stream, ulong value, int size)
    {
        Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); stream.Write(bytes[(8 - size)..]);
    }
}
