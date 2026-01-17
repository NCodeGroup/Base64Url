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
using System.Security.Cryptography;
using Nerdbank.Streams;

namespace NCode.Encoders.Tests;

public class Base64UrlTests
{
    private static string ToBase64UrlSlow(ReadOnlySpan<byte> inputBytes) =>
        Convert
            .ToBase64String(inputBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static void AssertBase64Url(ReadOnlySpan<byte> inputBytes, ReadOnlySpan<char> actual) =>
        Assert.Equal(ToBase64UrlSlow(inputBytes), actual.ToString());

    #region GetCharCountForEncode Tests

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 6)]
    [InlineData(5, 7)]
    [InlineData(6, 8)]
    [InlineData(7, 10)]
    [InlineData(100, 134)]
    [InlineData(1000, 1334)]
    public void GetCharCountForEncode_GivenByteCount_ReturnsExpectedCharCount(int byteCount, int expectedCharCount)
    {
        var actualCharCount = Base64Url.GetCharCountForEncode(byteCount);

        Assert.Equal(expectedCharCount, actualCharCount);
    }

    #endregion

    #region Encode Tests

    [Fact]
    public void Encode_GivenEmptySpan_ReturnsEmptyString()
    {
        var result = Base64Url.Encode(ReadOnlySpan<byte>.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Encode_GivenEmptySequence_ReturnsEmptyString()
    {
        var result = Base64Url.Encode(ReadOnlySequence<byte>.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("f", "Zg")]
    [InlineData("fo", "Zm8")]
    [InlineData("foo", "Zm9v")]
    [InlineData("foob", "Zm9vYg")]
    [InlineData("fooba", "Zm9vYmE")]
    [InlineData("foobar", "Zm9vYmFy")]
    public void Encode_GivenKnownValues_ReturnsExpectedResult(string input, string expected)
    {
        var inputBytes = System.Text.Encoding.UTF8.GetBytes(input);

        var actual = Base64Url.Encode(inputBytes);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Encode_GivenBytesProducingPlusInBase64_UsesMinus()
    {
        // 0xFB produces '+' in standard base64
        byte[] inputBytes = [0xFB];

        var result = Base64Url.Encode(inputBytes);

        Assert.DoesNotContain("+", result);
        Assert.Contains("-", result);
    }

    [Fact]
    public void Encode_GivenBytesProducingSlashInBase64_UsesUnderscore()
    {
        // 0xFF produces '/' in standard base64
        byte[] inputBytes = [0xFF];

        var result = Base64Url.Encode(inputBytes);

        Assert.DoesNotContain("/", result);
        Assert.Contains("_", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Encode_WhenUsingArray_ReturnsValidBase64Url(int byteCount)
    {
        Span<byte> inputBytes = new byte[byteCount];
        RandomNumberGenerator.Fill(inputBytes);

        var actual = Base64Url.Encode(inputBytes);

        AssertBase64Url(inputBytes, actual);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void Encode_WhenUsingBufferWriter_ReturnsValidBase64Url(int byteCount)
    {
        Span<byte> inputBytes = new byte[byteCount];
        RandomNumberGenerator.Fill(inputBytes);
        using var charBuffer = new Sequence<char>(ArrayPool<char>.Shared);

        var charsWritten = Base64Url.Encode(inputBytes, charBuffer);

        Assert.Equal(charBuffer.Length, charsWritten);
        var actual = charBuffer.AsReadOnlySequence.ToString();
        AssertBase64Url(inputBytes, actual);
    }

    [Theory]
    [InlineData(3, 3, 3)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 4, 5)]
    [InlineData(6, 7, 8)]
    public void Encode_WhenUsingSequence_ReturnsValidBase64Url(int byteCount1, int byteCount2, int byteCount3)
    {
        var inputBytes1 = new byte[byteCount1];
        RandomNumberGenerator.Fill(inputBytes1);
        var segment1 = new MemorySegment<byte>(inputBytes1);

        var inputBytes2 = new byte[byteCount2];
        RandomNumberGenerator.Fill(inputBytes2);
        var segment2 = segment1.Append(inputBytes2);

        var inputBytes3 = new byte[byteCount3];
        RandomNumberGenerator.Fill(inputBytes3);
        var segment3 = segment2.Append(inputBytes3);

        var inBuffer = new ReadOnlySequence<byte>(segment1, 0, segment3, inputBytes3.Length);
        var inSpan = inBuffer.ToArray().AsSpan();
        var expected = ToBase64UrlSlow(inSpan);
        using var outBuffer = new Sequence<char>(ArrayPool<char>.Shared);

        var charsWritten = Base64Url.Encode(inBuffer, outBuffer);

        Assert.Equal(expected.Length, charsWritten);
        Assert.Equal(expected.Length, outBuffer.Length);
        Assert.Equal(expected, outBuffer.AsReadOnlySequence.ToString());
    }

    [Fact]
    public void Encode_WhenUsingSingleSegmentSequence_ReturnsValidBase64Url()
    {
        byte[] inputBytes = "Hello, World!"u8.ToArray();
        var sequence = new ReadOnlySequence<byte>(inputBytes);
        var expected = ToBase64UrlSlow(inputBytes);

        var actual = Base64Url.Encode(sequence);

        Assert.Equal(expected, actual);
    }

    #endregion

    #region TryEncode Tests

    [Fact]
    public void TryEncode_GivenEmptySpan_ReturnsTrue()
    {
        var result = Base64Url.TryEncode(Span<byte>.Empty, Span<char>.Empty, out var charsWritten);

        Assert.True(result);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryEncode_GivenEmptySequence_ReturnsTrue()
    {
        var result = Base64Url.TryEncode(ReadOnlySequence<byte>.Empty, Span<char>.Empty, out var charsWritten);

        Assert.True(result);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryEncode_GivenDestinationTooSmall_ReturnsFalse()
    {
        const int expectedByteCount = 32;
        Span<byte> inputBytes = new byte[expectedByteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var charCount = Base64Url.GetCharCountForEncode(expectedByteCount) - 1;
        Span<char> charBuffer = stackalloc char[charCount];

        var result = Base64Url.TryEncode(inputBytes, charBuffer, out var charsWritten);

        Assert.False(result);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryEncode_GivenSequenceDestinationTooSmall_ReturnsFalse()
    {
        byte[] inputBytes = "Hello, World!"u8.ToArray();
        var sequence = new ReadOnlySequence<byte>(inputBytes);
        var charCount = Base64Url.GetCharCountForEncode(inputBytes.Length) - 1;
        Span<char> charBuffer = stackalloc char[charCount];

        var result = Base64Url.TryEncode(sequence, charBuffer, out var charsWritten);

        Assert.False(result);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryEncode_GivenExactSizeDestination_ReturnsTrue()
    {
        const int expectedByteCount = 32;
        Span<byte> inputBytes = new byte[expectedByteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var charCount = Base64Url.GetCharCountForEncode(expectedByteCount);
        Span<char> charBuffer = new char[charCount];

        var result = Base64Url.TryEncode(inputBytes, charBuffer, out var charsWritten);

        Assert.True(result);
        Assert.Equal(charCount, charsWritten);
        AssertBase64Url(inputBytes, charBuffer);
    }

    [Fact]
    public void TryEncode_GivenOversizedDestination_ReturnsTrue()
    {
        const int expectedByteCount = 32;
        Span<byte> inputBytes = new byte[expectedByteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var charCount = Base64Url.GetCharCountForEncode(expectedByteCount) + 1;
        Span<char> charBuffer = new char[charCount];

        var result = Base64Url.TryEncode(inputBytes, charBuffer, out var charsWritten);

        Assert.True(result);
        Assert.Equal(charCount - 1, charsWritten);
        AssertBase64Url(inputBytes, charBuffer[..charsWritten]);
    }

    [Theory]
    [InlineData(3, 3, 3)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 4, 5)]
    [InlineData(6, 7, 8)]
    public void TryEncode_WhenUsingSequence_ReturnsTrue(int byteCount1, int byteCount2, int byteCount3)
    {
        var inputBytes1 = new byte[byteCount1];
        RandomNumberGenerator.Fill(inputBytes1);
        var segment1 = new MemorySegment<byte>(inputBytes1);

        var inputBytes2 = new byte[byteCount2];
        RandomNumberGenerator.Fill(inputBytes2);
        var segment2 = segment1.Append(inputBytes2);

        var inputBytes3 = new byte[byteCount3];
        RandomNumberGenerator.Fill(inputBytes3);
        var segment3 = segment2.Append(inputBytes3);

        var inBuffer = new ReadOnlySequence<byte>(segment1, 0, segment3, inputBytes3.Length);
        var inSpan = inBuffer.ToArray().AsSpan();
        var expected = ToBase64UrlSlow(inSpan);
        Span<char> outChars = new char[expected.Length + 1];

        var result = Base64Url.TryEncode(inBuffer, outChars, out var charsWritten);

        Assert.True(result);
        Assert.Equal(expected.Length, charsWritten);
        Assert.Equal(expected, outChars[..charsWritten].ToString());
    }

    #endregion

    #region GetByteCountForDecode Tests

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(6, 4)]
    [InlineData(100, 75)]
    [InlineData(1000, 750)]
    public void GetByteCountForDecode_GivenValidCharCount_ReturnsExpectedByteCount(int charCount, int expectedByteCount)
    {
        var actualByteCount = Base64Url.GetByteCountForDecode(charCount);

        Assert.Equal(expectedByteCount, actualByteCount);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(9)]
    [InlineData(13)]
    public void GetByteCountForDecode_GivenInvalidCharCount_ThrowsFormatException(int charCount)
    {
        var exception = Assert.Throws<FormatException>(() => Base64Url.GetByteCountForDecode(charCount));

        Assert.Equal("Invalid length for a Base64Url char array or string.", exception.Message);
    }

    #endregion

    #region Decode Tests

    [Fact]
    public void Decode_GivenEmptySpan_ReturnsEmptyArray()
    {
        var result = Base64Url.Decode(ReadOnlySpan<char>.Empty);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Zg", "f")]
    [InlineData("Zm8", "fo")]
    [InlineData("Zm9v", "foo")]
    [InlineData("Zm9vYg", "foob")]
    [InlineData("Zm9vYmE", "fooba")]
    [InlineData("Zm9vYmFy", "foobar")]
    public void Decode_GivenKnownValues_ReturnsExpectedResult(string input, string expected)
    {
        var result = Base64Url.Decode(input);

        var actual = System.Text.Encoding.UTF8.GetString(result);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decode_GivenMinusCharacter_DecodesCorrectly()
    {
        // "-w" decodes to 0xFB in base64url
        const string input = "-w";

        var result = Base64Url.Decode(input);

        Assert.Equal([0xFB], result);
    }

    [Fact]
    public void Decode_GivenUnderscoreCharacter_DecodesCorrectly()
    {
        // "_w" decodes to 0xFF in base64url
        const string input = "_w";

        var result = Base64Url.Decode(input);

        Assert.Equal([0xFF], result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Decode_WhenUsingArray_ReturnsOriginalBytes(int byteCount)
    {
        Span<byte> inputBytes = new byte[byteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var inputChars = ToBase64UrlSlow(inputBytes);

        var actual = Base64Url.Decode(inputChars);

        Assert.True(inputBytes.SequenceEqual(actual));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Decode_WhenUsingBufferWriter_ReturnsOriginalBytes(int byteCount)
    {
        Span<byte> inputBytes = new byte[byteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var inputChars = ToBase64UrlSlow(inputBytes);
        using var byteBuffer = new Sequence<byte>();

        var bytesWritten = Base64Url.Decode(inputChars, byteBuffer);

        Assert.Equal(byteBuffer.Length, bytesWritten);
        Assert.True(inputBytes.SequenceEqual(byteBuffer.AsReadOnlySequence.ToArray()));
    }

    [Theory]
    [InlineData(3, 3, 4)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 4, 5)]
    public void Decode_WhenUsingSequence_ReturnsOriginalBytes(int charCount1, int charCount2, int charCount3)
    {
        var totalCharCount = charCount1 + charCount2 + charCount3;
        var totalByteCount = Base64Url.GetByteCountForDecode(totalCharCount);

        Span<byte> totalByteBuffer = new byte[totalByteCount];
        RandomNumberGenerator.Fill(totalByteBuffer);
        var totalCharBuffer = ToBase64UrlSlow(totalByteBuffer).AsMemory();

        var inputChars1 = totalCharBuffer[..charCount1];
        var inputChars2 = totalCharBuffer.Slice(charCount1, charCount2);
        var inputChars3 = totalCharBuffer.Slice(charCount1 + charCount2, charCount3);

        var segment1 = new MemorySegment<char>(inputChars1);
        var segment2 = segment1.Append(inputChars2);
        var segment3 = segment2.Append(inputChars3);

        var inBuffer = new ReadOnlySequence<char>(segment1, 0, segment3, charCount3);
        using var outBuffer = new Sequence<byte>(ArrayPool<byte>.Shared);

        var bytesWritten = Base64Url.Decode(inBuffer, outBuffer);

        Assert.Equal(totalByteCount, bytesWritten);
        Assert.Equal(totalByteCount, outBuffer.Length);
        Assert.Equal(totalByteBuffer.ToArray(), outBuffer.AsReadOnlySequence.ToArray());
    }

    [Fact]
    public void Decode_GivenInvalidCharacter_ThrowsFormatException()
    {
        const string input = "SGVs!G8"; // '!' is invalid

        var exception = Assert.Throws<FormatException>(() => Base64Url.Decode(input));

        Assert.Equal("The input is not a valid Base64Url string as it contains an illegal character.", exception.Message);
    }

    [Fact]
    public void Decode_GivenStandardBase64PlusCharacter_ThrowsFormatException()
    {
        const string input = "+w"; // '+' is not valid in base64url

        Assert.Throws<FormatException>(() => Base64Url.Decode(input));
    }

    [Fact]
    public void Decode_GivenStandardBase64SlashCharacter_ThrowsFormatException()
    {
        const string input = "/w"; // '/' is not valid in base64url

        Assert.Throws<FormatException>(() => Base64Url.Decode(input));
    }

    #endregion

    #region TryDecode Tests

    [Fact]
    public void TryDecode_GivenEmptySpan_ReturnsTrue()
    {
        var result = Base64Url.TryDecode(Span<char>.Empty, Span<byte>.Empty, out var bytesWritten);

        Assert.True(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecode_GivenEmptySequence_ReturnsTrue()
    {
        var result = Base64Url.TryDecode(ReadOnlySequence<char>.Empty, Span<byte>.Empty, out var bytesWritten);

        Assert.True(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecode_GivenDestinationTooSmall_ReturnsFalse()
    {
        const int expectedByteCount = 32;
        Span<byte> inputBytes = new byte[expectedByteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var inputChars = ToBase64UrlSlow(inputBytes);
        var charCount = inputChars.Length;
        var bufferSize = Base64Url.GetByteCountForDecode(charCount) - 1;
        Span<byte> byteBuffer = stackalloc byte[bufferSize];

        var result = Base64Url.TryDecode(inputChars, byteBuffer, out var bytesWritten);

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecode_GivenSequenceDestinationTooSmall_ReturnsFalse()
    {
        const string inputChars = "SGVsbG8gV29ybGQ";
        var sequence = new ReadOnlySequence<char>(inputChars.AsMemory());
        var bufferSize = Base64Url.GetByteCountForDecode(inputChars.Length) - 1;
        Span<byte> byteBuffer = stackalloc byte[bufferSize];

        var result = Base64Url.TryDecode(sequence, byteBuffer, out var bytesWritten);

        Assert.False(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecode_GivenExactSizeDestination_ReturnsTrue()
    {
        const int expectedByteCount = 32;
        Span<byte> inputBytes = new byte[expectedByteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var inputChars = ToBase64UrlSlow(inputBytes);
        var charCount = inputChars.Length;
        var bufferSize = Base64Url.GetByteCountForDecode(charCount);
        Span<byte> byteBuffer = stackalloc byte[bufferSize];

        var result = Base64Url.TryDecode(inputChars, byteBuffer, out var bytesWritten);

        Assert.True(result);
        Assert.Equal(expectedByteCount, bytesWritten);
        Assert.True(inputBytes.SequenceEqual(byteBuffer));
    }

    [Fact]
    public void TryDecode_GivenOversizedDestination_ReturnsTrue()
    {
        const int expectedByteCount = 32;
        Span<byte> inputBytes = new byte[expectedByteCount];
        RandomNumberGenerator.Fill(inputBytes);
        var inputChars = ToBase64UrlSlow(inputBytes);
        var charCount = inputChars.Length;
        var bufferSize = Base64Url.GetByteCountForDecode(charCount) + 1;
        Span<byte> byteBuffer = stackalloc byte[bufferSize];

        var result = Base64Url.TryDecode(inputChars, byteBuffer, out var bytesWritten);

        Assert.True(result);
        Assert.Equal(expectedByteCount, bytesWritten);
        Assert.True(inputBytes.SequenceEqual(byteBuffer[..bytesWritten]));
    }

    [Theory]
    [InlineData(3, 3, 4)]
    [InlineData(2, 4, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(0, 1, 2)]
    [InlineData(1, 1, 0)]
    [InlineData(2, 2, 2)]
    [InlineData(3, 4, 5)]
    public void TryDecode_WhenUsingSequence_ReturnsTrue(int charCount1, int charCount2, int charCount3)
    {
        var totalCharCount = charCount1 + charCount2 + charCount3;
        var totalByteCount = Base64Url.GetByteCountForDecode(totalCharCount);

        Span<byte> totalByteBuffer = new byte[totalByteCount];
        RandomNumberGenerator.Fill(totalByteBuffer);
        var totalCharBuffer = ToBase64UrlSlow(totalByteBuffer).AsMemory();

        var inputChars1 = totalCharBuffer[..charCount1];
        var inputChars2 = totalCharBuffer.Slice(charCount1, charCount2);
        var inputChars3 = totalCharBuffer.Slice(charCount1 + charCount2, charCount3);

        var segment1 = new MemorySegment<char>(inputChars1);
        var segment2 = segment1.Append(inputChars2);
        var segment3 = segment2.Append(inputChars3);

        var inBuffer = new ReadOnlySequence<char>(segment1, 0, segment3, charCount3);
        Span<byte> outBytes = new byte[totalByteCount + 1];

        var result = Base64Url.TryDecode(inBuffer, outBytes, out var bytesWritten);

        Assert.True(result);
        Assert.Equal(totalByteCount, bytesWritten);
        Assert.Equal(totalByteBuffer.ToArray(), outBytes[..bytesWritten].ToArray());
    }

    #endregion

    #region Round-Trip Tests

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(100)]
    [InlineData(1000)]
    public void RoundTrip_EncodeAndDecode_ReturnsOriginalBytes(int byteCount)
    {
        Span<byte> original = new byte[byteCount];
        RandomNumberGenerator.Fill(original);

        var encoded = Base64Url.Encode(original);
        var decoded = Base64Url.Decode(encoded);

        Assert.True(original.SequenceEqual(decoded));
    }

    [Fact]
    public void RoundTrip_AllByteValues_EncodesAndDecodesCorrectly()
    {
        byte[] original = new byte[256];
        for (var i = 0; i < 256; i++)
        {
            original[i] = (byte)i;
        }

        var encoded = Base64Url.Encode(original);
        var decoded = Base64Url.Decode(encoded);

        Assert.Equal(original, decoded);
    }

    [Theory]
    [InlineData("Hello, World!")]
    [InlineData("The quick brown fox jumps over the lazy dog")]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("AB")]
    [InlineData("ABC")]
    [InlineData("ABCD")]
    public void RoundTrip_Utf8Strings_EncodesAndDecodesCorrectly(string original)
    {
        var originalBytes = System.Text.Encoding.UTF8.GetBytes(original);

        var encoded = Base64Url.Encode(originalBytes);
        var decoded = Base64Url.Decode(encoded);

        var result = System.Text.Encoding.UTF8.GetString(decoded);
        Assert.Equal(original, result);
    }

    [Theory]
    [InlineData(3, 3, 3)]
    [InlineData(1, 2, 3)]
    [InlineData(10, 20, 30)]
    public void RoundTrip_UsingSequences_ReturnsOriginalBytes(int byteCount1, int byteCount2, int byteCount3)
    {
        var inputBytes1 = new byte[byteCount1];
        RandomNumberGenerator.Fill(inputBytes1);
        var segment1 = new MemorySegment<byte>(inputBytes1);

        var inputBytes2 = new byte[byteCount2];
        RandomNumberGenerator.Fill(inputBytes2);
        var segment2 = segment1.Append(inputBytes2);

        var inputBytes3 = new byte[byteCount3];
        RandomNumberGenerator.Fill(inputBytes3);
        var segment3 = segment2.Append(inputBytes3);

        var originalSequence = new ReadOnlySequence<byte>(segment1, 0, segment3, inputBytes3.Length);
        var originalArray = originalSequence.ToArray();

        var encoded = Base64Url.Encode(originalSequence);
        var decoded = Base64Url.Decode(encoded);

        Assert.Equal(originalArray, decoded);
    }

    #endregion
}
