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

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [InlineData(3, 4)]
    [InlineData(4, 6)]
    [InlineData(5, 7)]
    [InlineData(6, 8)]
    [InlineData(7, 10)]
    public void GetCharCountForEncode(int byteCount, int expectedCharCount)
    {
        var actualCharCount = Base64Url.GetCharCountForEncode(byteCount);
        Assert.Equal(expectedCharCount, actualCharCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Encode_WhenUsingArray(int byteCount)
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
    public void Encode_WhenUsingBufferWriter(int byteCount)
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
    public void Encode_WhenUsingSequence(int byteCount1, int byteCount2, int byteCount3)
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
    public void TryEncode_GivenEmpty_ThenZero()
    {
        var result = Base64Url.TryEncode(Span<byte>.Empty, Span<char>.Empty, out var charsWritten);
        Assert.True(result);
        Assert.Equal(0, charsWritten);
    }

    [Fact]
    public void TryEncode_GivenTooSmall_ThenFail()
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
    public void TryEncode_GivenExactSize_ThenValid()
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
    public void TryEncode_GivenLargerSize_ThenValid()
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
    [InlineData(0, 0, false)]
    [InlineData(1, 0, true)]
    [InlineData(2, 1, false)]
    [InlineData(3, 2, false)]
    [InlineData(4, 3, false)]
    [InlineData(5, 0, true)]
    [InlineData(6, 4, false)]
    public void GetByteCountForDecode(int charCount, int expectedByteCount, bool throws)
    {
        void ExecuteTestCase()
        {
            var actualByteCount = Base64Url.GetByteCountForDecode(charCount);
            Assert.Equal(expectedByteCount, actualByteCount);
        }

        if (throws)
        {
            var exception = Assert.Throws<FormatException>(ExecuteTestCase);
            Assert.Equal("Invalid length for a Base64Url char array or string.", exception.Message);
        }
        else
        {
            ExecuteTestCase();
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void Decode_WhenUsingArray(int byteCount)
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
    public void Decode_WhenUsingBufferWriter(int byteCount)
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
    public void Decode_WhenUsingSequence(int charCount1, int charCount2, int charCount3)
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
    public void TryDecode_GivenEmpty_ThenZero()
    {
        var result = Base64Url.TryDecode(Span<char>.Empty, Span<byte>.Empty, out var bytesWritten);
        Assert.True(result);
        Assert.Equal(0, bytesWritten);
    }

    [Fact]
    public void TryDecode_GivenTooSmall_ThenFail()
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
    public void TryDecode_GivenExactSize_ThenValid()
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
    public void TryDecode_GivenLargerSize_ThenValid()
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
}