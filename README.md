[![ci](https://github.com/NCodeGroup/Base64Url/actions/workflows/main.yml/badge.svg)](https://github.com/NCodeGroup/Base64Url/actions)
[![Nuget](https://img.shields.io/nuget/v/NCode.Base64Url.svg)](https://www.nuget.org/packages/NCode.Base64Url/)

# NCode.Base64Url

A high-performance .NET library for encoding and decoding **base64url** data as defined in [RFC 4648 Section 5](https://datatracker.ietf.org/doc/html/rfc4648#section-5). Built with modern memory-efficient APIs including [Span\<T\>](https://learn.microsoft.com/en-us/dotnet/standard/memory-and-spans/), [ReadOnlySequence\<T\>](https://learn.microsoft.com/en-us/dotnet/api/system.buffers.readonlysequence-1), and [IBufferWriter\<T\>](https://learn.microsoft.com/en-us/dotnet/standard/io/buffers).

## Features

- **URL-Safe Encoding** — Uses `-` and `_` instead of `+` and `/`, making encoded data safe for URLs, filenames, and identifiers
- **No Padding** — Omits trailing `=` padding characters (decoder accepts both padded and unpadded input)
- **Zero-Allocation Options** — `TryEncode`/`TryDecode` methods write directly to caller-provided buffers
- **Streaming Support** — Full support for `ReadOnlySequence<T>` for processing fragmented/pipelined data
- **Buffer Writer Integration** — Direct output to `IBufferWriter<T>` for high-throughput scenarios
- **High Performance** — Optimized with unsafe code, lookup tables, and aggressive inlining

## Installation

```shell
dotnet add package NCode.Base64Url
```

## Quick Start

```csharp
using NCode.Encoders;

// Encoding
byte[] data = { 0x48, 0x65, 0x6C, 0x6C, 0x6F }; // "Hello" in ASCII
string encoded = Base64Url.Encode(data);         // Returns "SGVsbG8"

// Decoding
byte[] decoded = Base64Url.Decode("SGVsbG8");    // Returns original bytes
```

## API Summary

### Encoding Methods

| Method | Description |
|--------|-------------|
| `GetCharCountForEncode(int)` | Calculates the output character count for a given byte count |
| `Encode(ReadOnlySpan<byte>)` | Encodes bytes to a base64url string |
| `Encode(ReadOnlySequence<byte>)` | Encodes a byte sequence to a base64url string |
| `Encode(ReadOnlySpan<byte>, IBufferWriter<char>)` | Encodes bytes directly to a buffer writer |
| `Encode(ReadOnlySequence<byte>, IBufferWriter<char>)` | Encodes a byte sequence directly to a buffer writer |
| `TryEncode(ReadOnlySpan<byte>, Span<char>, out int)` | Attempts to encode bytes to a provided character buffer |
| `TryEncode(ReadOnlySequence<byte>, Span<char>, out int)` | Attempts to encode a byte sequence to a provided character buffer |

### Decoding Methods

| Method | Description |
|--------|-------------|
| `GetByteCountForDecode(int)` | Calculates the output byte count for a given character count |
| `Decode(ReadOnlySpan<char>)` | Decodes a base64url string to a byte array |
| `Decode(ReadOnlySpan<char>, IBufferWriter<byte>)` | Decodes a base64url string directly to a buffer writer |
| `Decode(ReadOnlySequence<char>, IBufferWriter<byte>)` | Decodes a character sequence directly to a buffer writer |
| `TryDecode(ReadOnlySpan<char>, Span<byte>, out int)` | Attempts to decode a base64url string to a provided byte buffer |
| `TryDecode(ReadOnlySequence<char>, Span<byte>, out int)` | Attempts to decode a character sequence to a provided byte buffer |

## Advanced Usage

### Zero-Allocation Encoding

```csharp
byte[] data = GetData();
int charCount = Base64Url.GetCharCountForEncode(data.Length);
Span<char> buffer = stackalloc char[charCount];

if (Base64Url.TryEncode(data, buffer, out int charsWritten))
{
    // Use buffer[..charsWritten]
}
```

### Zero-Allocation Decoding

```csharp
ReadOnlySpan<char> encoded = "SGVsbG8";
int byteCount = Base64Url.GetByteCountForDecode(encoded.Length);
Span<byte> buffer = stackalloc byte[byteCount];

if (Base64Url.TryDecode(encoded, buffer, out int bytesWritten))
{
    // Use buffer[..bytesWritten]
}
```

### Using with IBufferWriter

```csharp
var writer = new ArrayBufferWriter<char>();
int charsWritten = Base64Url.Encode(data, writer);
```

## Target Frameworks

- .NET 8.0
- .NET 10.0

## Release Notes

| Version | Changes |
|---------|---------|
| v1.0.0 | Initial release |
| v1.1.0 | Added support for `ReadOnlySequence<T>` |
| v1.1.1 | Added sequence overloads without buffer writer |
| v1.1.2 | Optimized single-segment sequence handling |
| v2.0.0 | Target .NET 8.0+ only |
| v2.0.1 | Updates to xmldoc and readme |

## License

Licensed under the [Apache License, Version 2.0](LICENSE.txt).
