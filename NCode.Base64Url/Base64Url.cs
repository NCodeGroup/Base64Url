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
using JetBrains.Annotations;

namespace NCode.Encoders;

/// <summary>
/// Provides methods for encoding binary data to base64url and decoding base64url text back to binary data,
/// as defined in <see href="https://datatracker.ietf.org/doc/html/rfc4648#section-5">RFC 4648 Section 5</see>.
/// </summary>
/// <remarks>
/// <para>
/// Base64url encoding is a URL-safe variant of standard Base64 encoding. It uses '-' (minus) and '_' (underscore)
/// instead of '+' and '/', making it suitable for use in URLs, filenames, and other contexts where these characters
/// may cause issues.
/// </para>
/// <para>
/// This implementation omits padding characters ('=') in the encoded output, as permitted by RFC 4648.
/// The decoder can handle input both with and without padding.
/// </para>
/// <para>
/// All encoding methods produce unpadded base64url strings, and all decoding methods accept both
/// padded and unpadded input.
/// </para>
/// </remarks>
/// <seealso href="https://datatracker.ietf.org/doc/html/rfc4648">RFC 4648 - The Base16, Base32, and Base64 Data Encodings</seealso>
[PublicAPI]
public static class Base64Url
{
    /// <summary>
    /// The maximum number of padding characters that can be omitted (or present) in base64url encoding.
    /// </summary>
    private const int MaxPadCount = 2;

    /// <summary>
    /// The number of bytes in a single encoding block. Every 3 bytes of input produce 4 characters of output.
    /// </summary>
    private const int ByteBlockSize = 3;

    /// <summary>
    /// The number of characters in a single encoding block. Every 4 characters of input decode to 3 bytes of output.
    /// </summary>
    private const int CharBlockSize = 4;

    /// <summary>
    /// The base64url alphabet used for encoding: A-Z, a-z, 0-9, '-' (minus), and '_' (underscore).
    /// </summary>
    private static ReadOnlySpan<byte> EncodingMap =>
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"u8;

    /// <summary>
    /// A 256-byte lookup table that maps ASCII character values to their corresponding 6-bit base64url values.
    /// Invalid characters are mapped to -1.
    /// </summary>
    private static ReadOnlySpan<sbyte> DecodingMap
    {
        get
        {
            const sbyte __ = -1;

            return
            [
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
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __,
                __, __, __, __, __, __, __, __, __, __, __, __, __, __, __, __
            ];
        }
    }

    /// <summary>
    /// Calculates the number of characters produced by encoding the specified number of bytes.
    /// </summary>
    /// <param name="byteCount">The number of bytes to encode. Must be non-negative.</param>
    /// <returns>The number of characters produced by encoding the specified number of bytes, without padding.</returns>
    /// <remarks>
    /// <para>
    /// The formula used is: <c>(byteCount * 4 + 2) / 3</c>, which accounts for the base64 expansion ratio
    /// (3 bytes become 4 characters) rounded up for partial blocks, minus any padding that would be omitted.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// int charCount = Base64Url.GetCharCountForEncode(10); // Returns 14
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetCharCountForEncode(int byteCount)
    {
        return (byteCount * CharBlockSize + MaxPadCount) / ByteBlockSize;
    }

    /// <summary>
    /// Encodes the span of binary data into a base64url encoded string.
    /// </summary>
    /// <param name="bytes">The input span containing the binary data to encode.</param>
    /// <returns>
    /// A string containing the base64url representation of the input data, or <see cref="string.Empty"/> if the input is empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method allocates a new string to hold the encoded result. For scenarios where memory allocation
    /// should be minimized, consider using <see cref="TryEncode(ReadOnlySpan{byte}, Span{char}, out int)"/> instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// byte[] data = { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello" in ASCII
    /// string encoded = Base64Url.Encode(data); // Returns "SGVsbG8"
    /// </code>
    /// </example>
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
    /// Encodes the sequence of binary data into a base64url encoded string.
    /// </summary>
    /// <param name="sequence">The input sequence containing the binary data to encode.</param>
    /// <returns>
    /// A string containing the base64url representation of the input data, or <see cref="string.Empty"/> if the input is empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This overload is optimized for <see cref="ReadOnlySequence{T}"/> inputs, which may consist of multiple
    /// non-contiguous memory segments. For single-segment sequences, this method delegates to the span-based overload.
    /// </para>
    /// </remarks>
    public static string Encode(ReadOnlySequence<byte> sequence)
    {
        if (sequence.Length == 0)
            return string.Empty;

        if (sequence.IsSingleSegment)
            return Encode(sequence.FirstSpan);

        var byteCount = (int)sequence.Length;
        var charCount = GetCharCountForEncode(byteCount);

        return string.Create(charCount, sequence, static (chars, seq) =>
        {
            var result = TryEncode(seq, chars, out var charsWritten);
            Debug.Assert(result && charsWritten == chars.Length);
        });
    }

    /// <summary>
    /// Encodes the span of binary data into base64url text and writes it to the specified buffer writer.
    /// </summary>
    /// <param name="bytes">The input span containing the binary data to encode.</param>
    /// <param name="writer">The buffer writer to which the encoded base64url text will be written.</param>
    /// <returns>The number of characters written to <paramref name="writer"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method is useful for high-performance scenarios where output should be written directly to a buffer
    /// without intermediate string allocations.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
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
    /// Encodes the sequence of binary data into base64url text and writes it to the specified buffer writer.
    /// </summary>
    /// <param name="sequence">The input sequence containing the binary data to encode.</param>
    /// <param name="writer">The buffer writer to which the encoded base64url text will be written.</param>
    /// <returns>The number of characters written to <paramref name="writer"/>.</returns>
    /// <remarks>
    /// <para>
    /// This method handles multi-segment sequences by processing each segment and accumulating partial blocks
    /// in a scratch buffer to ensure correct encoding across segment boundaries.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public static int Encode(ReadOnlySequence<byte> sequence, IBufferWriter<char> writer)
    {
        if (sequence.Length == 0)
            return 0;

        if (sequence.IsSingleSegment)
            return Encode(sequence.FirstSpan, writer);

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

            var (spanQuotient, spanRemainder) = Math.DivRem(spanLen, ByteBlockSize);
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
    /// Attempts to encode the sequence of binary data into base64url text.
    /// </summary>
    /// <param name="sequence">The input sequence containing the binary data to encode.</param>
    /// <param name="chars">The destination span for the encoded base64url text.</param>
    /// <param name="charsWritten">When this method returns, contains the number of characters written to <paramref name="chars"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the encoding operation succeeded; <see langword="false"/> if <paramref name="chars"/> is too small to contain the encoded output.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Use <see cref="GetCharCountForEncode"/> to determine the required size of the destination buffer.
    /// </para>
    /// <para>
    /// This method handles multi-segment sequences by processing each segment and accumulating partial blocks
    /// in a scratch buffer to ensure correct encoding across segment boundaries.
    /// </para>
    /// </remarks>
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

        if (sequence.IsSingleSegment)
            return TryEncode(sequence.FirstSpan, chars, out charsWritten);

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

            var (spanQuotient, spanRemainder) = Math.DivRem(spanLen, ByteBlockSize);
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
    /// Attempts to encode the span of binary data into base64url text.
    /// </summary>
    /// <param name="bytes">The input span containing the binary data to encode.</param>
    /// <param name="chars">The destination span for the encoded base64url text.</param>
    /// <param name="charsWritten">When this method returns, contains the number of characters written to <paramref name="chars"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the encoding operation succeeded; <see langword="false"/> if <paramref name="chars"/> is too small to contain the encoded output.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Use <see cref="GetCharCountForEncode"/> to determine the required size of the destination buffer.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// byte[] data = { 0x48, 0x65, 0x6C, 0x6C, 0x6F };
    /// Span&lt;char&gt; buffer = stackalloc char[Base64Url.GetCharCountForEncode(data.Length)];
    /// if (Base64Url.TryEncode(data, buffer, out int written))
    /// {
    ///     // buffer[..written] contains "SGVsbG8"
    /// }
    /// </code>
    /// </example>
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

    /// <summary>
    /// Core encoding implementation that converts binary data to base64url characters using unsafe pointers.
    /// </summary>
    /// <param name="byteCount">The number of bytes to encode.</param>
    /// <param name="bytes">Pointer to the source binary data.</param>
    /// <param name="chars">Pointer to the destination character buffer.</param>
    /// <returns>The number of characters written to the destination.</returns>
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
    /// Calculates the number of bytes produced by decoding the specified number of base64url characters.
    /// </summary>
    /// <param name="charCount">The number of base64url characters to decode. Must be non-negative.</param>
    /// <returns>The number of bytes produced by decoding the specified number of characters.</returns>
    /// <exception cref="FormatException"><paramref name="charCount"/> is not a valid base64url length (i.e., <c>charCount % 4 == 1</c>).</exception>
    /// <remarks>
    /// <para>
    /// The formula used is: <c>(charCount * 3) / 4</c>, which accounts for the base64 compression ratio
    /// (4 characters become 3 bytes) with adjustment for partial blocks.
    /// </para>
    /// <para>
    /// A valid base64url string length modulo 4 can be 0, 2, or 3, but never 1.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// int byteCount = Base64Url.GetByteCountForDecode(14); // Returns 10
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetByteCountForDecode(int charCount)
    {
        return GetByteCountForDecode(charCount, out _);
    }

    /// <summary>
    /// Calculates the number of bytes produced by decoding the specified number of characters,
    /// also returning the remainder for internal decoding logic.
    /// </summary>
    /// <param name="charCount">The number of characters to decode.</param>
    /// <param name="remainder">When this method returns, contains the remainder bytes that need special handling during decoding.</param>
    /// <returns>The number of bytes produced by decoding the specified number of characters.</returns>
    /// <exception cref="FormatException"><paramref name="charCount"/> is not a valid base64url length.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetByteCountForDecode(int charCount, out int remainder)
    {
        if (charCount == 0)
        {
            remainder = 0;
            return 0;
        }

        var (byteCount, tempRemainder) = Math.DivRem(charCount * ByteBlockSize, CharBlockSize);
        if (tempRemainder > MaxPadCount)
            throw new FormatException("Invalid length for a Base64Url char array or string.");

        remainder = tempRemainder;
        return byteCount;
    }

    /// <summary>
    /// Decodes the base64url encoded text into binary data.
    /// </summary>
    /// <param name="chars">The input span containing the base64url encoded text to decode.</param>
    /// <returns>
    /// A byte array containing the decoded binary data, or an empty array if the input is empty.
    /// </returns>
    /// <exception cref="FormatException">
    /// <paramref name="chars"/> has an invalid length, or contains an invalid base64url character.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method allocates a new byte array to hold the decoded result. For scenarios where memory allocation
    /// should be minimized, consider using <see cref="TryDecode(ReadOnlySpan{char}, Span{byte}, out int)"/> instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// string encoded = "SGVsbG8";
    /// byte[] decoded = Base64Url.Decode(encoded); // Returns { 0x48, 0x65, 0x6C, 0x6C, 0x6F }
    /// </code>
    /// </example>
    public static byte[] Decode(ReadOnlySpan<char> chars)
    {
        if (chars.Length == 0)
            return [];

        var minDestLength = GetByteCountForDecode(chars.Length, out var remainder);

        var bytes = new byte[minDestLength];
        var result = TryDecode(chars, bytes, minDestLength, remainder, out var bytesWritten);
        Debug.Assert(result && bytesWritten == minDestLength);

        return bytes;
    }

    /// <summary>
    /// Decodes the base64url encoded text and writes the binary data to the specified buffer writer.
    /// </summary>
    /// <param name="chars">The input span containing the base64url encoded text to decode.</param>
    /// <param name="writer">The buffer writer to which the decoded binary data will be written.</param>
    /// <returns>The number of bytes written to <paramref name="writer"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="chars"/> has an invalid length, or contains an invalid base64url character.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method is useful for high-performance scenarios where output should be written directly to a buffer
    /// without intermediate array allocations.
    /// </para>
    /// </remarks>
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
    /// Decodes the sequence of base64url encoded text and writes the binary data to the specified buffer writer.
    /// </summary>
    /// <param name="sequence">The input sequence containing the base64url encoded text to decode.</param>
    /// <param name="writer">The buffer writer to which the decoded binary data will be written.</param>
    /// <returns>The number of bytes written to <paramref name="writer"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">
    /// <paramref name="sequence"/> has an invalid length, or contains an invalid base64url character.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This method handles multi-segment sequences by processing each segment and accumulating partial blocks
    /// in a scratch buffer to ensure correct decoding across segment boundaries.
    /// </para>
    /// </remarks>
    public static int Decode(ReadOnlySequence<char> sequence, IBufferWriter<byte> writer)
    {
        if (sequence.Length == 0)
            return 0;

        if (sequence.IsSingleSegment)
            return Decode(sequence.FirstSpan, writer);

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

            var (spanQuotient, spanRemainder) = Math.DivRem(spanLen, CharBlockSize);
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
    /// Attempts to decode the sequence of base64url encoded text into binary data.
    /// </summary>
    /// <param name="sequence">The input sequence containing the base64url encoded text to decode.</param>
    /// <param name="bytes">The destination span for the decoded binary data.</param>
    /// <param name="bytesWritten">When this method returns, contains the number of bytes written to <paramref name="bytes"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the decoding operation succeeded; <see langword="false"/> if <paramref name="bytes"/> is too small to contain the decoded output.
    /// </returns>
    /// <exception cref="FormatException">
    /// <paramref name="sequence"/> has an invalid length, or contains an invalid base64url character.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Use <see cref="GetByteCountForDecode(int)"/> to determine the required size of the destination buffer.
    /// </para>
    /// <para>
    /// This method handles multi-segment sequences by processing each segment and accumulating partial blocks
    /// in a scratch buffer to ensure correct decoding across segment boundaries.
    /// </para>
    /// </remarks>
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

        if (sequence.IsSingleSegment)
            return TryDecode(sequence.FirstSpan, bytes, out bytesWritten);

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

            var (spanQuotient, spanRemainder) = Math.DivRem(spanLen, CharBlockSize);
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
    /// Attempts to decode the base64url encoded text into binary data.
    /// </summary>
    /// <param name="chars">The input span containing the base64url encoded text to decode.</param>
    /// <param name="bytes">The destination span for the decoded binary data.</param>
    /// <param name="bytesWritten">When this method returns, contains the number of bytes written to <paramref name="bytes"/>.</param>
    /// <returns>
    /// <see langword="true"/> if the decoding operation succeeded; <see langword="false"/> if <paramref name="bytes"/> is too small to contain the decoded output.
    /// </returns>
    /// <exception cref="FormatException">
    /// <paramref name="chars"/> has an invalid length, or contains an invalid base64url character.
    /// </exception>
    /// <remarks>
    /// <para>
    /// Use <see cref="GetByteCountForDecode(int)"/> to determine the required size of the destination buffer.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// string encoded = "SGVsbG8";
    /// Span&lt;byte&gt; buffer = stackalloc byte[Base64Url.GetByteCountForDecode(encoded.Length)];
    /// if (Base64Url.TryDecode(encoded, buffer, out int written))
    /// {
    ///     // buffer[..written] contains the decoded bytes
    /// }
    /// </code>
    /// </example>
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

    /// <summary>
    /// Core decoding implementation that processes base64url characters into bytes.
    /// </summary>
    /// <param name="chars">The input span containing the base64url encoded text.</param>
    /// <param name="bytes">The destination span for the decoded binary data.</param>
    /// <param name="minDestLength">The minimum required destination length.</param>
    /// <param name="remainder">The number of remainder characters that need special handling.</param>
    /// <param name="bytesWritten">When this method returns, contains the number of bytes written to <paramref name="bytes"/>.</param>
    /// <returns>
    /// <see langword="true"/> if successful; <see langword="false"/> if the destination is too small.
    /// </returns>
    /// <exception cref="FormatException">The input contains an invalid base64url character.</exception>
    private static bool TryDecode(
        ReadOnlySpan<char> chars,
        Span<byte> bytes,
        int minDestLength,
        int remainder,
        out int bytesWritten
    )
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

    /// <summary>
    /// Decodes four base64url characters into a 24-bit value representing three bytes.
    /// </summary>
    /// <param name="src">Reference to the source character buffer.</param>
    /// <param name="srcIndex">Reference to the current source index, which is advanced by 4.</param>
    /// <param name="map">Reference to the decoding map.</param>
    /// <returns>A 24-bit integer containing the decoded bytes, or a negative value if invalid characters are encountered.</returns>
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

    /// <summary>
    /// Decodes three base64url characters (representing two bytes with one padding character omitted) into a 24-bit value.
    /// </summary>
    /// <param name="src">Reference to the source character buffer.</param>
    /// <param name="srcIndex">Reference to the current source index, which is advanced by 3.</param>
    /// <param name="map">Reference to the decoding map.</param>
    /// <returns>A 24-bit integer containing the decoded bytes (two valid bytes in high-order positions), or a negative value if invalid characters are encountered.</returns>
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

    /// <summary>
    /// Decodes two base64url characters (representing one byte with two padding characters omitted) into a 24-bit value.
    /// </summary>
    /// <param name="src">Reference to the source character buffer.</param>
    /// <param name="srcIndex">Reference to the current source index, which is advanced by 2.</param>
    /// <param name="map">Reference to the decoding map.</param>
    /// <returns>A 24-bit integer containing the decoded byte (one valid byte in high-order position), or a negative value if invalid characters are encountered.</returns>
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

    /// <summary>
    /// Writes the three low-order bytes from a 24-bit decoded value to the destination buffer.
    /// </summary>
    /// <param name="dest">Reference to the destination byte buffer.</param>
    /// <param name="destIndex">Reference to the current destination index, which is advanced by 3.</param>
    /// <param name="value">The 24-bit value containing three bytes to write.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteThreeLowOrderBytes(ref byte dest, ref int destIndex, int value)
    {
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 16);
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 8);
        Unsafe.Add(ref dest, destIndex++) = (byte)value;
    }

    /// <summary>
    /// Writes the two high-order bytes from a 24-bit decoded value to the destination buffer.
    /// </summary>
    /// <param name="dest">Reference to the destination byte buffer.</param>
    /// <param name="destIndex">Reference to the current destination index, which is advanced by 2.</param>
    /// <param name="value">The 24-bit value containing two bytes to write (in bits 16-23 and 8-15).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteTwoLowOrderBytes(ref byte dest, ref int destIndex, int value)
    {
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 16);
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 8);
    }

    /// <summary>
    /// Writes the single high-order byte from a 24-bit decoded value to the destination buffer.
    /// </summary>
    /// <param name="dest">Reference to the destination byte buffer.</param>
    /// <param name="destIndex">Reference to the current destination index, which is advanced by 1.</param>
    /// <param name="value">The 24-bit value containing one byte to write (in bits 16-23).</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteOneLowOrderByte(ref byte dest, ref int destIndex, int value)
    {
        Unsafe.Add(ref dest, destIndex++) = (byte)(value >> 16);
    }
}
