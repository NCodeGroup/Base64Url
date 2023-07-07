![example workflow](https://github.com/NCodeGroup/Base64Url/actions/workflows/dotnet.yml/badge.svg)

# Overview

This library provides methods for encoding and decoding `base64url` ([RFC 4648]) data using the new memory
efficient [Span] and [Buffer] APIs.

[RFC 4648]: https://datatracker.ietf.org/doc/html/rfc4648

[Span]: https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/

[Buffer]: https://learn.microsoft.com/en-us/dotnet/standard/io/buffers

```csharp
namespace NCode.Encoders;

public static class Base64Url
{
    // Encode...

    public static int GetCharCountForEncode(
        int byteCount);
    
    public static string Encode(
        ReadOnlySpan<byte> bytes);
    
    public static int Encode(
        ReadOnlySpan<byte> bytes,
        IBufferWriter<char> writer);
    
    public static bool TryEncode(
        ReadOnlySpan<byte> bytes,
        Span<char> chars,
        out int charsWritten);
    
    // Decode...
    
    public static int GetByteCountForDecode(
        int charCount);
    
    public static byte[] Decode(
        ReadOnlySpan<char> chars);
    
    public static int Decode(
        ReadOnlySpan<char> chars,
        IBufferWriter<byte> writer);
    
    public static bool TryDecode(
        ReadOnlySpan<char> chars,
        Span<byte> bytes,
        out int bytesWritten);
}
```
