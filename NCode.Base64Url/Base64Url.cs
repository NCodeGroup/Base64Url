#region Copyright Preamble

//
//    Copyright @ 2023 NCode Group
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

#endregion

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace NCode.Encoders;

/// <summary>
/// Converts between binary data and UTF-8 encoded text that is represented in base64url (RFC 4648).
/// </summary>
public static class Base64Url
{
    private const int MaxPadCount = 2;
    private const int ByteBlockSize = 3;
    private const int CharBlockSize = 4;

    private static ReadOnlySpan<byte> EncodingMap =>
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"u8;

    private static ReadOnlySpan<sbyte> DecodingMap
    {
        get
        {
            const sbyte __ = -1;

            return new sbyte[]
            {
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, 62, __, __,
                52, 53, 54, 55, 56, 57, 58, 59, 60, 61, __, __, __, __, __, __,
                __, 00, 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 11, 12, 13, 14,
                15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, __, __, __, __, 63,
                __, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
                41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int Quotient, int Remainder) MathDivRem(int left, int right)
    {
#if NET6_0
        return Math.DivRem(left, right);
#else
        var quotient = left / right;
        return (quotient, left - (quotient * right));
#endif
    }

    /// <summary>
    /// Calculates the number of characters produced by encoding the specified number of bytes.
    /// </summary>
    /// <param name="byteCount">The number of bytes to encode.</param>
    /// <returns>The number of characters produced by encoding the specified number of bytes.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCharCountForEncode(int byteCount)
    {
        return (byteCount * CharBlockSize + MaxPadCount) / ByteBlockSize;
    }

    /// <summary>
    /// Encodes the span of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="bytes">The input span that contains the binary data that needs to be encoded.</param>
    /// <returns>The string representation of the binary data in base64url format.</returns>
    public static unsafe string Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        var byteCount = bytes.Length;
        var charCount = GetCharCountForEncode(byteCount);

        fixed (byte* bytesPtr = &MemoryMarshal.GetReference(bytes))
        {
            return string.Create(charCount, (byteCount, bytesPtr: (IntPtr)bytesPtr), static (chars, state) =>
            {
                fixed (char* charsPtr = &MemoryMarshal.GetReference(chars))
                {
                    var charsWritten = Encode(state.byteCount, (byte*)state.bytesPtr, charsPtr);
                    Debug.Assert(charsWritten == chars.Length);
                }
            });
        }
    }

    /// <summary>
    /// Encodes the span of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="bytes">The input span that contains the binary data that needs to be encoded.</param>
    /// <param name="writer">The buffer to which the encoded base64url text will be written to.</param>
    /// <returns>The number of characters written to <paramref name="writer"/>.</returns>
    public static int Encode(ReadOnlySpan<byte> bytes, IBufferWriter<char> writer)
    {
        var byteCount = bytes.Length;
        if (byteCount == 0)
            return 0;

        var charCount = GetCharCountForEncode(byteCount);
        var chars = writer.GetSpan(charCount);
        TryEncode(bytes, chars, out var charsWritten);
        writer.Advance(charsWritten);

        return charsWritten;
    }

    /// <summary>
    /// Encodes the sequence of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="sequence">The input sequence that contains the binary data that needs to be encoded.</param>
    /// <param name="writer">The buffer to which the encoded base64url text will be written to.</param>
    /// <returns>The number of characters written to <paramref name="writer"/>.</returns>
    public static int Encode(ReadOnlySequence<byte> sequence, IBufferWriter<char> writer)
    {
        if (sequence.Length == 0)
            return 0;

        Span<byte> scratch = stackalloc byte[ByteBlockSize];
        var scratchPos = 0;

        var totalCharsWritten = 0;
        foreach (var segment in sequence)
        {
            var span = segment.Span;
            var spanLen = span.Length;
            if (scratchPos > 0)
            {
                var need = ByteBlockSize - scratchPos;
                if (need <= spanLen)
                {
                    span[..need].CopyTo(scratch[scratchPos..]);
                    totalCharsWritten += Encode(scratch, writer);
                    scratchPos = 0;

                    span = span[need..];
                    spanLen = span.Length;
                }
                else
                {
                    span.CopyTo(scratch[scratchPos..]);
                    scratchPos += spanLen;

                    span = Span<byte>.Empty;
                    spanLen = 0;
                }
            }

            if (spanLen == 0)
                continue;

            var (spanQuotient, spanRemainder) = MathDivRem(spanLen, ByteBlockSize);
            if (spanRemainder == 0)
            {
                totalCharsWritten += Encode(span, writer);
            }
            else
            {
                var wholeLen = spanQuotient * ByteBlockSize;
                totalCharsWritten += Encode(span[..wholeLen], writer);

                span[wholeLen..].CopyTo(scratch[scratchPos..]);
                scratchPos += spanRemainder;
            }
        }

        if (scratchPos > 0)
        {
            totalCharsWritten += Encode(scratch[..scratchPos], writer);
        }

        return totalCharsWritten;
    }

    /// <summary>
    /// Encodes the sequence of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="sequence">The input sequence that contains the binary data that needs to be encoded.</param>
    /// <param name="chars">Destination for the encoded base64url text.</param>
    /// <param name="charsWritten">When this method returns, contains a value that indicates the number of characters written to <paramref name="chars"/>.</param>
    /// <returns><c>true</c> if the operation succeeded; otherwise, <c>false</c> if the destination is too small.</returns>
    public static bool TryEncode(ReadOnlySequence<byte> sequence, Span<char> chars, out int charsWritten)
    {
        var byteCount = (int)sequence.Length;
        if (byteCount == 0)
        {
            charsWritten = 0;
            return true;
        }

        var minCharCount = GetCharCountForEncode(byteCount);
        if (chars.Length < minCharCount)
        {
            charsWritten = 0;
            return false;
        }

        Span<byte> scratch = stackalloc byte[ByteBlockSize];
        var scratchPos = 0;

        var totalCharsWritten = 0;
        foreach (var segment in sequence)
        {
            var span = segment.Span;
            var spanLen = span.Length;
            if (scratchPos > 0)
            {
                var need = ByteBlockSize - scratchPos;
                if (need <= spanLen)
                {
                    span[..need].CopyTo(scratch[scratchPos..]);
                    var result = TryEncode(scratch, chars, out var localCharsWritten);
                    Debug.Assert(result && localCharsWritten == CharBlockSize);

                    chars = chars[localCharsWritten..];
                    totalCharsWritten += localCharsWritten;

                    span = span[need..];
                    spanLen = span.Length;

                    scratchPos = 0;
                }
                else
                {
                    span.CopyTo(scratch[scratchPos..]);
                    scratchPos += spanLen;

                    span = Span<byte>.Empty;
                    spanLen = 0;
                }
            }

            if (spanLen == 0)
                continue;

            var (spanQuotient, spanRemainder) = MathDivRem(spanLen, ByteBlockSize);
            if (spanRemainder == 0)
            {
                var result = TryEncode(span, chars, out var localCharsWritten);
                Debug.Assert(result);

                chars = chars[localCharsWritten..];
                totalCharsWritten += localCharsWritten;
            }
            else
            {
                var wholeLen = spanQuotient * ByteBlockSize;
                var result = TryEncode(span[..wholeLen], chars, out var localCharsWritten);
                Debug.Assert(result);

                chars = chars[localCharsWritten..];
                totalCharsWritten += localCharsWritten;

                span[wholeLen..].CopyTo(scratch[scratchPos..]);
                scratchPos += spanRemainder;
            }
        }

        if (scratchPos > 0)
        {
            var result = TryEncode(scratch[..scratchPos], chars, out var localCharsWritten);
            Debug.Assert(result);

            totalCharsWritten += localCharsWritten;
        }

        charsWritten = totalCharsWritten;
        return true;
    }

    /// <summary>
    /// Encodes the span of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="bytes">The input span that contains the binary data that needs to be encoded.</param>
    /// <param name="chars">Destination for the encoded base64url text.</param>
    /// <param name="charsWritten">When this method returns, contains a value that indicates the number of characters written to <paramref name="chars"/>.</param>
    /// <returns><c>true</c> if the operation succeeded; otherwise, <c>false</c> if the destination is too small.</returns>
    public static unsafe bool TryEncode(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten)
    {
        var byteCount = bytes.Length;
        if (byteCount == 0)
        {
            charsWritten = 0;
            return true;
        }

        var minCharCount = GetCharCountForEncode(byteCount);
        if (chars.Length < minCharCount)
        {
            charsWritten = 0;
            return false;
        }

        fixed (byte* bytesPtr = &MemoryMarshal.GetReference(bytes))
        fixed (char* charsPtr = &MemoryMarshal.GetReference(chars))
        {
            charsWritten = Encode(byteCount, bytesPtr, charsPtr);
            return true;
        }
    }

    private static unsafe int Encode(int byteCount, byte* bytes, char* chars)
    {
        var remainderBytes = byteCount % ByteBlockSize;
        var wholeBlockBytes = byteCount - remainderBytes;
        var charPos = 0;

        fixed (byte* map = EncodingMap)
        {
            int bytePos;

            for (bytePos = 0; bytePos < wholeBlockBytes; bytePos += ByteBlockSize)
            {
                chars[charPos] = (char)map[(bytes[bytePos] & 0xFC) >> 2];
                chars[charPos + 1] = (char)map[((bytes[bytePos] & 0x03) << 4) | ((bytes[bytePos + 1] & 0xF0) >> 4)];
                chars[charPos + 2] = (char)map[((bytes[bytePos + 1] & 0x0F) << 2) | ((bytes[bytePos + 2] & 0xC0) >> 6)];
                chars[charPos + 3] = (char)map[bytes[bytePos + 2] & 0x3F];
                charPos += CharBlockSize;
            }

            switch (remainderBytes)
            {
                case 1: // two character padding omitted
                    chars[charPos] = (char)map[(bytes[bytePos] & 0xFC) >> 2];
                    chars[charPos + 1] = (char)map[(bytes[bytePos] & 0x03) << 4];
                    charPos += 2;
                    break;

                case 2: // one character padding omitted
                    chars[charPos] = (char)map[(bytes[bytePos] & 0xFC) >> 2];
                    chars[charPos + 1] = (char)map[((bytes[bytePos] & 0x03) << 4) | ((bytes[bytePos + 1] & 0xF0) >> 4)];
                    chars[charPos + 2] = (char)map[(bytes[bytePos + 1] & 0x0F) << 2];
                    charPos += 3;
                    break;
            }
        }

        return charPos;
    }

    /// <summary>
    /// Calculates the number of bytes produced by decoding the specified number of characters.
    /// </summary>
    /// <param name="charCount">The number of characters to decode.</param>
    /// <returns>The number of bytes produced by decoding the specified number of characters.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetByteCountForDecode(int charCount)
    {
        return GetByteCountForDecode(charCount, out _);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetByteCountForDecode(int charCount, out int remainder)
    {
        if (charCount == 0)
        {
            remainder = 0;
            return 0;
        }

        var (byteCount, tempRemainder) = MathDivRem(charCount * ByteBlockSize, CharBlockSize);
        if (tempRemainder > MaxPadCount)
            throw new FormatException("Invalid length for a Base64Url char array or string.");

        remainder = tempRemainder;
        return byteCount;
    }

    /// <summary>
    /// Decodes the span of UTF-8 encoded text represented as base64url into binary data.
    /// </summary>
    /// <param name="chars">The input span that contains UTF-8 encoded text in base64url that needs to be decoded.</param>
    /// <returns>The decoded binary data.</returns>
    public static byte[] Decode(ReadOnlySpan<char> chars)
    {
        if (chars.Length == 0)
            return Array.Empty<byte>();

        var minDestLength = GetByteCountForDecode(chars.Length, out var remainder);

        var bytes = new byte[minDestLength];
        var result = TryDecode(chars, bytes, minDestLength, remainder, out var bytesWritten);
        Debug.Assert(result && bytesWritten == minDestLength);

        return bytes;
    }

    /// <summary>
    /// Decodes the span of UTF-8 encoded text represented as base64url into binary data.
    /// </summary>
    /// <param name="chars">The input span that contains UTF-8 encoded text in base64url that needs to be decoded.</param>
    /// <param name="writer">The buffer to which the decoded binary data will be written to.</param>
    /// <returns>The number of bytes written to <paramref name="writer"/>.</returns>
    public static int Decode(ReadOnlySpan<char> chars, IBufferWriter<byte> writer)
    {
        if (chars.Length == 0) return 0;

        var minDestLength = GetByteCountForDecode(chars.Length, out var remainder);
        var bytes = writer.GetSpan(minDestLength);

        TryDecode(chars, bytes, minDestLength, remainder, out var bytesWritten);
        writer.Advance(bytesWritten);

        return bytesWritten;
    }

    /// <summary>
    /// Decodes the sequence of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="sequence">The input sequence that contains UTF-8 encoded text in base64url that needs to be decoded.</param>
    /// <param name="writer">The buffer to which the decoded binary data will be written to.</param>
    /// <returns>The number of bytes written to <paramref name="writer"/>.</returns>
    public static int Decode(ReadOnlySequence<char> sequence, IBufferWriter<byte> writer)
    {
        if (sequence.Length == 0)
            return 0;

        Span<char> scratch = stackalloc char[CharBlockSize];
        var scratchPos = 0;

        var totalBytesWritten = 0;
        foreach (var segment in sequence)
        {
            var span = segment.Span;
            var spanLen = span.Length;
            if (scratchPos > 0)
            {
                var need = CharBlockSize - scratchPos;
                if (need <= spanLen)
                {
                    span[..need].CopyTo(scratch[scratchPos..]);
                    totalBytesWritten += Decode(scratch, writer);
                    scratchPos = 0;

                    span = span[need..];
                    spanLen = span.Length;
                }
                else
                {
                    span.CopyTo(scratch[scratchPos..]);
                    scratchPos += spanLen;

                    span = Span<char>.Empty;
                    spanLen = 0;
                }
            }

            if (spanLen == 0)
                continue;

            var (spanQuotient, spanRemainder) = MathDivRem(spanLen, CharBlockSize);
            if (spanRemainder == 0)
            {
                totalBytesWritten += Decode(span, writer);
            }
            else
            {
                var wholeLen = spanQuotient * CharBlockSize;
                totalBytesWritten += Decode(span[..wholeLen], writer);

                span[wholeLen..].CopyTo(scratch[scratchPos..]);
                scratchPos += spanRemainder;
            }
        }

        if (scratchPos > 0)
        {
            totalBytesWritten += Decode(scratch[..scratchPos], writer);
        }

        return totalBytesWritten;
    }


    /// <summary>
    /// Decodes the sequence of binary data into UTF-8 encoded text represented as base64url.
    /// </summary>
    /// <param name="sequence">The input sequence that contains UTF-8 encoded text in base64url that needs to be decoded.</param>
    /// <param name="bytes">Destination for the decoded base64url binary data.</param>
    /// <param name="bytesWritten">When this method returns, contains a value that indicates the number of bytes written to <paramref name="bytes"/>.</param>
    /// <returns><c>true</c> if the operation succeeded; otherwise, <c>false</c> if the destination is too small.</returns>
    public static bool TryDecode(ReadOnlySequence<char> sequence, Span<byte> bytes, out int bytesWritten)
    {
        var charCount = (int)sequence.Length;
        if (charCount == 0)
        {
            bytesWritten = 0;
            return true;
        }

        var minDestLength = GetByteCountForDecode(charCount, out _);
        if (bytes.Length < minDestLength)
        {
            bytesWritten = 0;
            return false;
        }

        Span<char> scratch = stackalloc char[CharBlockSize];
        var scratchPos = 0;

        var totalBytesWritten = 0;
        foreach (var segment in sequence)
        {
            var span = segment.Span;
            var spanLen = span.Length;
            if (scratchPos > 0)
            {
                var need = CharBlockSize - scratchPos;
                if (need <= spanLen)
                {
                    span[..need].CopyTo(scratch[scratchPos..]);
                    var result = TryDecode(scratch, bytes, ByteBlockSize, remainder: 0, out var localBytesWritten);
                    Debug.Assert(result && localBytesWritten == ByteBlockSize);

                    bytes = bytes[localBytesWritten..];
                    totalBytesWritten += localBytesWritten;

                    span = span[need..];
                    spanLen = span.Length;

                    scratchPos = 0;
                }
                else
                {
                    span.CopyTo(scratch[scratchPos..]);
                    scratchPos += spanLen;

                    span = Span<char>.Empty;
                    spanLen = 0;
                }
            }

            if (spanLen == 0)
                continue;

            var (spanQuotient, spanRemainder) = MathDivRem(spanLen, CharBlockSize);
            if (spanRemainder == 0)
            {
                var result = TryDecode(span, bytes, out var localBytesWritten);
                Debug.Assert(result);

                bytes = bytes[localBytesWritten..];
                totalBytesWritten += localBytesWritten;
            }
            else
            {
                var wholeLen = spanQuotient * CharBlockSize;
                var result = TryDecode(span[..wholeLen], bytes, out var localBytesWritten);
                Debug.Assert(result);

                bytes = bytes[localBytesWritten..];
                totalBytesWritten += localBytesWritten;

                span[wholeLen..].CopyTo(scratch[scratchPos..]);
                scratchPos += spanRemainder;
            }
        }

        if (scratchPos > 0)
        {
            var result = TryDecode(scratch[..scratchPos], bytes, out var localBytesWritten);
            Debug.Assert(result);

            totalBytesWritten += localBytesWritten;
        }

        bytesWritten = totalBytesWritten;
        return true;
    }

    /// <summary>
    /// Decodes the span of UTF-8 encoded text represented as base64url into binary data.
    /// </summary>
    /// <param name="chars">The input span that contains UTF-8 encoded text in base64url that needs to be decoded.</param>
    /// <param name="bytes">Destination for the decoded base64url binary data.</param>
    /// <param name="bytesWritten">When this method returns, contains a value that indicates the number of bytes written to <paramref name="bytes"/>.</param>
    /// <returns><c>true</c> if the operation succeeded; otherwise, <c>false</c> if the destination is too small.</returns>
    public static bool TryDecode(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten)
    {
        if (chars.Length == 0)
        {
            bytesWritten = 0;
            return true;
        }

        var minDestLength = GetByteCountForDecode(chars.Length, out var remainder);

        return TryDecode(chars, bytes, minDestLength, remainder, out bytesWritten);
    }

    private static bool TryDecode(
        ReadOnlySpan<char> chars,
        Span<byte> bytes,
        int minDestLength,
        int remainder,
        out int bytesWritten)
    {
        var destLength = bytes.Length;
        if (destLength < minDestLength)
        {
            bytesWritten = 0;
            return false;
        }

        var srcIndex = 0;
        var destIndex = 0;

        ref var src = ref MemoryMarshal.GetReference(chars);
        ref var dest = ref MemoryMarshal.GetReference(bytes);
        ref var map = ref MemoryMarshal.GetReference(DecodingMap);

        // only decode entire blocks
        var srcLengthBlocks = chars.Length & ~0x03;
        while (srcIndex < srcLengthBlocks)
        {
            var result = DecodeFour(ref src, ref srcIndex, ref map);
            if (result < 0)
                throw new FormatException(
                    "The input is not a valid Base64Url string as it contains an illegal character.");

            WriteThreeLowOrderBytes(ref dest, ref destIndex, result);
        }

        // ReSharper disable once ConvertIfStatementToSwitchStatement
        if (remainder == 1)
        {
            var result = DecodeThree(ref src, ref srcIndex, ref map);
            if (result < 0)
                throw new FormatException(
                    "The input is not a valid Base64Url string as it contains an illegal character.");

            WriteTwoLowOrderBytes(ref dest, ref destIndex, result);
        }
        else if (remainder == 2)
        {
            var result = DecodeTwo(ref src, ref srcIndex, ref map);
            if (result < 0)
                throw new FormatException(
                    "The input is not a valid Base64Url string as it contains an illegal character.");

            WriteOneLowOrderByte(ref dest, ref destIndex, result);
        }

        bytesWritten = destIndex;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeFour(ref char src, ref int srcIndex, ref sbyte map)
    {
        int i0 = Unsafe.Add(ref src, srcIndex++);
        int i1 = Unsafe.Add(ref src, srcIndex++);
        int i2 = Unsafe.Add(ref src, srcIndex++);
        int i3 = Unsafe.Add(ref src, srcIndex++);

        var isInvalid = ((i0 | i1 | i2 | i3) & ~0xFF) != 0;
        if (isInvalid) return -1;

        i0 = Unsafe.Add(ref map, i0);
        i1 = Unsafe.Add(ref map, i1);
        i2 = Unsafe.Add(ref map, i2);
        i3 = Unsafe.Add(ref map, i3);

        i0 <<= 18;
        i1 <<= 12;
        i2 <<= 6;

        i0 |= i3;
        i1 |= i2;

        i0 |= i1;

        return i0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeThree(ref char src, ref int srcIndex, ref sbyte map)
    {
        int i0 = Unsafe.Add(ref src, srcIndex++);
        int i1 = Unsafe.Add(ref src, srcIndex++);
        int i2 = Unsafe.Add(ref src, srcIndex++);

        var isInvalid = ((i0 | i1 | i2) & ~0xFF) != 0;
        if (isInvalid) return -1;

        i0 = Unsafe.Add(ref map, i0);
        i1 = Unsafe.Add(ref map, i1);
        i2 = Unsafe.Add(ref map, i2);

        i0 <<= 18;
        i1 <<= 12;
        i2 <<= 6;

        i0 |= i2;
        i0 |= i1;

        return i0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int DecodeTwo(ref char src, ref int srcIndex, ref sbyte map)
    {
        int i0 = Unsafe.Add(ref src, srcIndex++);
        int i1 = Unsafe.Add(ref src, srcIndex++);

        var isInvalid = ((i0 | i1) & ~0xFF) != 0;
        if (isInvalid) return -1;

        i0 = Unsafe.Add(ref map, i0);
        i1 = Unsafe.Add(ref map, i1);

        i0 <<= 18;
        i1 <<= 12;

        i0 |= i1;

        return i0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteThreeLowOrderBytes(ref byte dest, ref int destIndex, int value)
    {
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 16);
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 8);
        Unsafe.Add(ref dest, destIndex++) = (byte)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTwoLowOrderBytes(ref byte dest, ref int destIndex, int value)
    {
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 16);
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 8);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteOneLowOrderByte(ref byte dest, ref int destIndex, int value)
    {
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 16);
    }
}