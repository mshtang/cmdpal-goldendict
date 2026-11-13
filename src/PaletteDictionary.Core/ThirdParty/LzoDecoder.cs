// LZO1X decoding adapted from minilzo-js / MDict.Csharp's Lzo1x implementation.
// Original minilzo: Markus F. X. J. Oberhumer; JS port: Alistair Braidwood.
// SPDX-License-Identifier: GPL-2.0-or-later
// This adaptation removes compression and uses bounded, bytewise buffers.
namespace PaletteDictionary.Core.ThirdParty;

internal static class LzoDecoder
{
    public static byte[] Decode(ReadOnlySpan<byte> input, int outputLength)
    {
        var output = new byte[outputLength];
        var ip = 0; var op = 0; var count = 0; var match = 0;
        if (input.Length < 3) throw new InvalidDataException(UiText.Get("LzoShortInput"));
        var state = 0; // 0: literal run; 1: first literal match; 2: match; 3: trailing literals
        if (input[0] > 17)
        {
            count = input[ip++] - 17;
            CopyLiteral(input, output, ref ip, ref op, count);
            state = count < 4 ? 2 : 1;
        }
        while (true)
        {
            if (state == 0)
            {
                count = Read(input, ref ip);
                if (count >= 16) { state = 2; }
                else
                {
                    if (count == 0) count = Extended(input, ref ip, 15);
                    CopyLiteral(input, output, ref ip, ref op, checked(count + 3));
                    state = 1;
                }
            }
            if (state == 1)
            {
                count = Read(input, ref ip);
                if (count < 16)
                {
                    match = op - 0x801 - (count >> 2) - (Read(input, ref ip) << 2);
                    CopyMatch(output, ref op, match, 3);
                    state = 3;
                }
                else state = 2;
            }
            if (state == 2)
            {
                if (count >= 64)
                {
                    match = op - 1 - ((count >> 2) & 7) - (Read(input, ref ip) << 3);
                    count = (count >> 5) + 1;
                }
                else if (count >= 32)
                {
                    count &= 31;
                    if (count == 0) count = Extended(input, ref ip, 31);
                    var low = Read(input, ref ip); var high = Read(input, ref ip);
                    match = op - 1 - (low >> 2) - (high << 6);
                    count = checked(count + 2);
                }
                else if (count >= 16)
                {
                    match = op - ((count & 8) << 11);
                    count &= 7;
                    if (count == 0) count = Extended(input, ref ip, 7);
                    var low = Read(input, ref ip); var high = Read(input, ref ip);
                    match -= (low >> 2) + (high << 6);
                    if (match == op)
                    {
                        if (op != outputLength || ip != input.Length) throw new InvalidDataException(UiText.Get("LzoLength"));
                        return output;
                    }
                    match -= 0x4000;
                    count = checked(count + 2);
                }
                else
                {
                    match = op - 1 - (count >> 2) - (Read(input, ref ip) << 2);
                    count = 2;
                }
                CopyMatch(output, ref op, match, count);
                state = 3;
            }
            if (state == 3)
            {
                count = input[ip - 2] & 3;
                if (count == 0) { state = 0; continue; }
                CopyLiteral(input, output, ref ip, ref op, count);
                count = Read(input, ref ip);
                state = 2;
            }
        }
    }
    private static int Read(ReadOnlySpan<byte> input, ref int p) => p < input.Length ? input[p++] : throw new InvalidDataException(UiText.Get("LzoInputBounds"));
    private static int Extended(ReadOnlySpan<byte> input, ref int p, int basis)
    {
        var count = 0; int value;
        while ((value = Read(input, ref p)) == 0) count = checked(count + 255);
        return checked(count + basis + value);
    }
    private static void CopyLiteral(ReadOnlySpan<byte> input, Span<byte> output, ref int ip, ref int op, int length)
    {
        if (length < 0 || ip > input.Length - length || op > output.Length - length) throw new InvalidDataException(UiText.Get("LzoLiteralBounds"));
        input.Slice(ip, length).CopyTo(output.Slice(op)); ip += length; op += length;
    }
    private static void CopyMatch(Span<byte> output, ref int op, int match, int length)
    {
        if (length < 0 || match < 0 || match >= op || op > output.Length - length) throw new InvalidDataException(UiText.Get("LzoReferenceBounds"));
        for (var i = 0; i < length; i++) output[op++] = output[match++];
    }
}
