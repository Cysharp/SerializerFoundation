SerializerFoundation
===
[![CI](https://github.com/Cysharp/SerializerFoundation/actions/workflows/build-debug.yaml/badge.svg)](https://github.com/Cysharp/SerializerFoundation/actions/workflows/build-debug.yaml)
[![NuGet](https://img.shields.io/nuget/v/SerializerFoundation)](https://www.nuget.org/packages/SerializerFoundation)

High performance serialization infrastructure for C#: buffer interfaces, implementations for spans, pooled arrays and segmented data, and analyzers that catch common contract violations. The package targets netstandard2.0, netstandard2.1 and net10.0.

SerializerFoundation is extracted from the [MessagePack-CSharp v4](https://github.com/MessagePack-CSharp/MessagePack-CSharp/tree/v4) rewrite, drawing on experience from [MessagePack-CSharp](https://github.com/MessagePack-CSharp/MessagePack-CSharp/) and [MemoryPack](https://github.com/Cysharp/MemoryPack). A serializer needs a forward-only buffer, and it needs to support several input and output types such as `Span<byte>`, `IBufferWriter<byte>` and `ReadOnlySequence<byte>`. SerializerFoundation abstracts these behind `IWriteBuffer` and `IReadBuffer`, and provides a buffer layer optimized so that the JIT can specialize code per buffer type.

```bash
dotnet add package SerializerFoundation
```

* **Struct buffers, passed by ref**: buffers flow through `ref TBuffer`, allowing the JIT to specialize generic code per buffer type and devirtualize and inline the hot path
* **Caller-provided scratch**: `ArrayPoolListWriteBuffer` uses `stackalloc` scratch before renting from `ArrayPool<byte>`, and `ReadOnlySequenceReadBuffer` can assemble windows across segment seams in scratch. `ToArray()` allocates the final output array and copies the written bytes into it
* **`allows ref struct`**: on net10.0 the `ref struct` buffers flow straight through generic formatters; `Compatible*` variants keep the same code shape on netstandard2.0/2.1
* **Zero-copy segment access**: `BufferSegments` exposes a written message as a borrowed view of its segments, so processors that accept segmented input can consume it without first flattening it
* **Compile-time checks**: bundled analyzers reject buffer classes (SF001), common accidental buffer copies (SF002) and missing overrides of `[RequireOverride]` methods (SF003)

The design comes from ten years of maintaining MessagePack-CSharp and MemoryPack. A serializer using `IBufferWriter<byte>` directly requests and advances a window for each primitive, or wraps it to cache a window between calls. SerializerFoundation makes that buffer state part of the formatter's generic contract. Its `BufferWriterWriteBuffer` adapter caches the current window, leaving calls to the underlying `IBufferWriter<byte>` for window refills and flushes.

Core Interface
---
Serializers write into an `IWriteBuffer` and read from an `IReadBuffer`. Both are deliberately small.

```csharp
public interface IWriteBuffer : IDisposable
{
    long BytesWritten { get; }

    // a writable span of at least sizeHint bytes (sizeHint == 0: some non-empty span)
    Span<byte> GetSpan(int sizeHint = 0);

    // commits bytes written into the span from GetSpan
    void Advance(int bytesWritten);

    // pushes buffered bytes to the underlying destination, when there is one
    void Flush();
}

public interface IReadBuffer : IDisposable
{
    long BytesConsumed { get; }
    long BytesRemaining { get; }

    // the unread part of the current contiguous window, never throws, empty only at end of data
    ReadOnlySpan<byte> GetUnreadSpan();

    // a contiguous window of at least sizeHint bytes, or false if fewer bytes remain
    // copies across segment seams as needed; does not consume bytes
    bool TryGetSpan(int sizeHint, out ReadOnlySpan<byte> span);

    // consumes bytes
    void Advance(int bytesConsumed);

    // copies the next destination.Length bytes without consuming them
    void CopyTo(Span<byte> destination);
}
```

The write side is the familiar `GetSpan` / `Advance` pair from `IBufferWriter<byte>`. The read side has the same shape, reading from a span and then advancing past it, but for optimization it offers several ways to read: `GetUnreadSpan` for whatever is contiguous right now, `TryGetSpan` for a window of a required size across segment seams, and `CopyTo` for copying out without building a contiguous window.

The basic pattern is to build your serializer's definitions on these interfaces, like the following.

```csharp
public interface IFormatter<TWriteBuffer, TReadBuffer, T>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    void Serialize(ref TWriteBuffer buffer, T value);
    T Deserialize(ref TReadBuffer buffer);
}
```

```csharp
using System.IO;
using System.Buffers.Binary;
using SerializerFoundation;

public static class BufferExtensions
{
    extension<TWriteBuffer>(ref TWriteBuffer buffer)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    {
        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.GetSpan(4), value);
            buffer.Advance(4);
        }
    }

    extension<TReadBuffer>(ref TReadBuffer buffer)
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        public int ReadInt32()
        {
            var span = buffer.GetUnreadSpan();
            if (span.Length < 4 && !buffer.TryGetSpan(4, out span))
            {
                throw new EndOfStreamException();
            }

            var value = BinaryPrimitives.ReadInt32LittleEndian(span);
            buffer.Advance(4);
            return value;
        }
    }
}
```

Span lifetime is the one rule to keep in mind. A span from `GetSpan`, `GetUnreadSpan` or `TryGetSpan` stays valid until the next call that hands out a span (or `Flush` / `Dispose`), because that call may hand the window to the destination, return it to a pool, or replace it with a larger one. `Advance` and `CopyTo` do not invalidate a held span. Anything that may touch the buffer, such as a nested formatter, can request a window, so re-request the span after such calls instead of holding on to it.

`SerializerFoundation.WriteBufferExtensions.GetReference` is a by-reference shortcut for `GetSpan` with the same contract. For example, an encoder can write a little-endian integer through `Unsafe.WriteUnaligned`:

```csharp
ref byte destination = ref writer.GetReference(sizeof(int));
System.Runtime.CompilerServices.Unsafe.WriteUnaligned(
    ref destination,
    BitConverter.IsLittleEndian ? value : BinaryPrimitives.ReverseEndianness(value));
writer.Advance(sizeof(int));
```

Keep one owner for each buffer. A serializer entry point normally constructs it, passes it by `ref` to formatters, and disposes it in `finally`. A `using` local cannot be passed by `ref` (compiler error CS1657), so the entry points below use `try` / `finally`. Caller-provided scratch, input memory and underlying writers are borrowed; disposing a buffer releases its own rented storage or commits its staged writes, without disposing those external resources.

Write Buffers
---
| Type | Destination | Notes |
| --- | --- | --- |
| `ArrayPoolListWriteBuffer` | caller scratch, then a chain of `ArrayPool<byte>` arrays | The general-purpose `byte[]`-producing buffer. `ToArray()`, `WriteTo(Span<byte>)`, `GetWrittenSegments()`. Dispose returns the rented arrays. |
| `BufferWriterWriteBuffer` | any `IBufferWriter<byte>` (`PipeWriter`, `ArrayBufferWriter<byte>`) | Writes are staged in the current span and committed on a window refill, `Flush` or `Dispose`. |
| `SpanWriteBuffer` | a fixed caller-provided span, such as `stackalloc` memory | Never grows; running out of space throws. For messages with a known maximum size. |

`ArrayPoolListWriteBuffer` supports `Serialize<T>(T value) : byte[]` style APIs. It starts with the scratch span you provide, typically `stackalloc` memory. When the next requested window no longer fits, it rents another segment from `ArrayPool<byte>` without copying previously written bytes. Minimum segment sizes grow exponentially, with larger size hints accommodated as needed. `ToArray()` makes the final contiguous copy; use `GetWrittenSegments()` when the next stage can consume segments directly.

```csharp
Span<byte> scratch = stackalloc byte[512];
var buffer = new ArrayPoolListWriteBuffer(scratch);
try
{
    formatter.Serialize(ref buffer, value);
    return buffer.ToArray();
}
finally
{
    buffer.Dispose();
}
```

When the message has to be post-processed before it reaches its destination, `GetWrittenSegments()` returns a `BufferSegments`: a borrowed, zero-copy view of the written message, valid until the next write or Dispose. Each segment is a `ReadOnlySpan<byte>`, so a compressor or framing layer that accepts segmented input can consume it directly. Iterate with `TryGetNext`; `Reset()` restarts from the first segment.

```csharp
var segments = buffer.GetWrittenSegments();
while (segments.TryGetNext(out var segment))
{
    stream.Write(segment);
}
```

`BufferWriterWriteBuffer` connects to the existing `IBufferWriter<byte>` ecosystem, including `PipeWriter` and `ArrayBufferWriter<byte>`. Call `Flush()` when the serializer is done to commit the staged bytes through `IBufferWriter<byte>.Advance`. With a `PipeWriter`, the caller must also call `FlushAsync()` to publish those bytes and observe backpressure.

```csharp
var buffer = new BufferWriterWriteBuffer(pipeWriter);
try
{
    formatter.Serialize(ref buffer, value);
    buffer.Flush();
}
finally
{
    buffer.Dispose();
}
await pipeWriter.FlushAsync();
```

Read Buffers
---
| Type | Source | Notes |
| --- | --- | --- |
| `ReadOnlySpanReadBuffer` | a single contiguous block, `byte[]` or `ReadOnlySpan<byte>` | The standard entry point. `TryGetSpan` never copies. |
| `ReadOnlySequenceReadBuffer` | `ReadOnlySequence<byte>`, such as `PipeReader` output | Reads each contiguous segment in place; bytes that straddle a seam are copied into caller scratch, then into a rented temp only when they do not fit. Dispose returns the temp. |

`ReadOnlySequenceReadBuffer` takes an optional scratch span. Small windows that cross a segment seam, which is the common case for a multi-byte token, are assembled in that scratch instead of renting from the pool.

```csharp
Span<byte> scratch = stackalloc byte[512];
var buffer = new ReadOnlySequenceReadBuffer(sequence, scratch);
try
{
    var value = formatter.Deserialize(ref buffer);
    long consumed = buffer.BytesConsumed;
    return value;
}
finally
{
    buffer.Dispose();
}
```

`CopyTo(Span<byte>)` copies the next bytes without consuming them, and a multi-segment implementation copies straight out of its segments without building a contiguous window first. It is the right call for fixed-size payloads such as a `byte[]` body or a blittable struct, where a contiguous window would only be an intermediate copy.

Keep the input memory valid for the buffer's lifetime. When reading a `PipeReader` result, `buffer.Advance()` only updates the buffer's position; the caller still uses `BytesConsumed` to determine where to call `PipeReader.AdvanceTo()` after decoding.

Building a Serializer
---
A formatter can be generic over both buffer types and receive them by `ref`. The following `Point`, `IFormatter`, formatter and primitive extension methods are examples you implement in your serializer; they are not types supplied by this package.

```csharp
public readonly struct Point(int x, int y)
{
    public int X { get; } = x;
    public int Y { get; } = y;
}

public interface IFormatter<TWriteBuffer, TReadBuffer, T>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    void Serialize(ref TWriteBuffer buffer, T value);
    T Deserialize(ref TReadBuffer buffer);
}

public sealed class PointFormatter<TWriteBuffer, TReadBuffer> : IFormatter<TWriteBuffer, TReadBuffer, Point>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    public void Serialize(ref TWriteBuffer buffer, Point value)
    {
        buffer.WriteInt32(value.X);
        buffer.WriteInt32(value.Y);
    }

    public Point Deserialize(ref TReadBuffer buffer)
    {
        var x = buffer.ReadInt32();
        var y = buffer.ReadInt32();
        return new Point(x, y);
    }
}
```

Different value-type buffer arguments allow separate native-code specializations of `PointFormatter`. This lets the JIT devirtualize and inline calls such as `GetSpan` and `Advance` on the hot path. The exact optimization depends on the runtime and calling code; the `BufferWriterWriteBuffer` adapter still calls the underlying writer interface when refilling or flushing.

Primitive encoders are best expressed as C# 14 extension blocks over the buffer type, so that `buffer.WriteInt32(x)` reads naturally inside a formatter while still being fully generic.

```csharp
public static class PrimitiveWriteExtensions
{
    extension<TWriteBuffer>(ref TWriteBuffer buffer)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    {
        public void WriteInt32(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer.GetSpan(4), value);
            buffer.Advance(4);
        }
    }
}
```

The top-level entry points then choose the buffer for the job and hand it to the formatter. In these method excerpts, `GetFormatter<TWriteBuffer, TReadBuffer, T>()` stands for your serializer's resolver, returning an `IFormatter<TWriteBuffer, TReadBuffer, T>`.

```csharp
public static byte[] Serialize<T>(T value)
{
    Span<byte> scratch = stackalloc byte[256];
    var buffer = new ArrayPoolListWriteBuffer(scratch);
    try
    {
        GetFormatter<ArrayPoolListWriteBuffer, ReadOnlySpanReadBuffer, T>().Serialize(ref buffer, value);
        return buffer.ToArray();
    }
    finally
    {
        buffer.Dispose();
    }
}

public static void Serialize<T>(IBufferWriter<byte> output, T value)
{
    var buffer = new BufferWriterWriteBuffer(output);
    try
    {
        GetFormatter<BufferWriterWriteBuffer, ReadOnlySpanReadBuffer, T>().Serialize(ref buffer, value);
        buffer.Flush();
    }
    finally
    {
        buffer.Dispose();
    }
}

public static T Deserialize<T>(ReadOnlySpan<byte> source)
{
    var buffer = new ReadOnlySpanReadBuffer(source);
    try
    {
        return GetFormatter<ArrayPoolListWriteBuffer, ReadOnlySpanReadBuffer, T>().Deserialize(ref buffer);
    }
    finally
    {
        buffer.Dispose();
    }
}

public static T Deserialize<T>(in ReadOnlySequence<byte> source)
{
    Span<byte> scratch = stackalloc byte[256];
    var buffer = new ReadOnlySequenceReadBuffer(source, scratch);
    try
    {
        return GetFormatter<ArrayPoolListWriteBuffer, ReadOnlySequenceReadBuffer, T>().Deserialize(ref buffer);
    }
    finally
    {
        buffer.Dispose();
    }
}
```

Target Frameworks and the Compatible Tier
---
The package ships a net10.0 asset with `allows ref struct` support and netstandard2.0/2.1 assets for older targets. The language feature itself requires C# 13 and .NET 9 or later, but **this package's generic helper APIs enable it starting with the net10.0 asset**. Use `NET10_0_OR_GREATER` when selecting between the two tiers.

| Package asset | Buffer tier for generic serializer code |
| --- | --- |
| net10.0 | `ArrayPoolListWriteBuffer`, `BufferWriterWriteBuffer`, `SpanWriteBuffer`, `ReadOnlySpanReadBuffer`, `ReadOnlySequenceReadBuffer` (all `ref struct`) and the `Compatible*` variants |
| netstandard2.0, netstandard2.1 | `CompatibleArrayPoolListWriteBuffer`, `CompatibleBufferWriterWriteBuffer`, `CompatibleSpanWriteBuffer`, `CompatibleReadOnlySpanReadBuffer`, `CompatibleReadOnlySequenceReadBuffer` (plain `struct`) |

The `ref struct` buffers exist on every target and can be used directly. Generic serializer code targeting netstandard uses the `Compatible*` tier. .NET 9 applications also select a netstandard asset: their own generic code can allow ref structs, but the package's `GetReference` helper cannot accept them on that asset. Selecting the `Compatible*` tier keeps those helpers usable across older targets.

The `Compatible*` structs implement the same interfaces with the same semantics. `CompatibleArrayPoolListWriteBuffer` starts with pooled storage and takes no scratch span; `CompatibleReadOnlySequenceReadBuffer` uses a rented temporary array when stitching is necessary. The span-based variants (`CompatibleSpanWriteBuffer`, `CompatibleReadOnlySpanReadBuffer`) take a `byte*` and length instead of a span. Keep that memory valid for the buffer's lifetime, and pin it when it belongs to a managed object.

The netstandard assets cover compatible .NET Framework and engine runtimes such as Unity and Godot. Runtime compatibility and compiler support are separate: older compilers can use the `Compatible*` structs, while defining the extension-block syntax in this README requires C# 14. The equivalent traditional syntax is `public static void WriteInt32<TBuffer>(this ref TBuffer buffer, int value) where TBuffer : struct, IWriteBuffer`.

A formatter that multi-targets writes its constraints once with a conditional `allows ref struct`.

```csharp
public sealed class PointFormatter<TWriteBuffer, TReadBuffer> : IFormatter<TWriteBuffer, TReadBuffer, Point>
    where TWriteBuffer : struct, IWriteBuffer
#if NET10_0_OR_GREATER
    , allows ref struct
#endif
    where TReadBuffer : struct, IReadBuffer
#if NET10_0_OR_GREATER
    , allows ref struct
#endif
{
    // the body is identical on every target
}
```

Use the same conditional constraints on `IFormatter` and on generic helper methods. The serializer's entry points then pick the tier per target. A serializer that loads formatter assemblies built for different targets also needs a resolver strategy for selecting a compatible formatter; that policy belongs to the serializer.

```csharp
public static byte[] Serialize<T>(T value)
{
#if NET10_0_OR_GREATER
    Span<byte> scratch = stackalloc byte[256];
    var buffer = new ArrayPoolListWriteBuffer(scratch);
    try
    {
        GetFormatter<ArrayPoolListWriteBuffer, ReadOnlySpanReadBuffer, T>().Serialize(ref buffer, value);
        return buffer.ToArray();
    }
    finally
    {
        buffer.Dispose();
    }
#else
    var buffer = new CompatibleArrayPoolListWriteBuffer();
    try
    {
        GetFormatter<CompatibleArrayPoolListWriteBuffer, CompatibleReadOnlySpanReadBuffer, T>().Serialize(ref buffer, value);
        return buffer.ToArray();
    }
    finally
    {
        buffer.Dispose();
    }
#endif
}

public static unsafe T Deserialize<T>(ReadOnlySpan<byte> source)
{
#if NET10_0_OR_GREATER
    var buffer = new ReadOnlySpanReadBuffer(source);
    try
    {
        return GetFormatter<ArrayPoolListWriteBuffer, ReadOnlySpanReadBuffer, T>().Deserialize(ref buffer);
    }
    finally
    {
        buffer.Dispose();
    }
#else
    fixed (byte* pointer = source)
    {
        var buffer = new CompatibleReadOnlySpanReadBuffer(pointer, source.Length);
        try
        {
            return GetFormatter<CompatibleArrayPoolListWriteBuffer, CompatibleReadOnlySpanReadBuffer, T>().Deserialize(ref buffer);
        }
        finally
        {
            buffer.Dispose();
        }
    }
#endif
}
```

`BufferSegments` is the same type on every target, so message processors (compressors, encryptors, framers) that consume it multi-target without any `#if`.

RequireOverride
---
When a multi-targeted base class adds a generic buffer method only on its net10.0 target, making that method `abstract` would break subclasses compiled against its netstandard target: they have no implementation of the new abstract method. A `virtual` method with a bridge body keeps those subclasses usable, while newly compiled subclasses can override it with a direct implementation.

`[RequireOverride]` marks such a virtual method as conceptually abstract. The bundled SF003 analyzer requires every non-abstract derived type that can access the method to override it, directly or through a base class. Downlevel compilations never see the method, so they have no override requirement. The following `MessageProcessor` is an example base class for your serializer.

```csharp
using System.Buffers;
using SerializerFoundation;
using SerializerFoundation.CodeAnalysis;

public abstract class MessageProcessor
{
    // every target: the interface-shaped entry
    public abstract bool TryEncode(ref BufferSegments message, IBufferWriter<byte> output);

#if NET10_0_OR_GREATER
    // net10.0 only: writes straight into the target buffer.
    // virtual so netstandard subclasses keep loading; [RequireOverride] so modern subclasses cannot forget it.
    [RequireOverride]
    public virtual bool TryEncode<TWriteBuffer>(ref BufferSegments message, ref TWriteBuffer output)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    {
        // bridge for netstandard subclasses: encode through the interface overload, then copy (one extra copy)
        var staging = new ArrayBufferWriter<byte>();
        if (!TryEncode(ref message, staging)) return false;
        if (staging.WrittenCount > 0)
        {
            staging.WrittenSpan.CopyTo(output.GetSpan(staging.WrittenCount));
            output.Advance(staging.WrittenCount);
        }
        return true;
    }
#endif
}
```

The attribute is not inherited. An override satisfies the requirement for everything below it, and an intermediate override that should itself be overridden again applies the attribute anew.

Analyzers
---
The package bundles Roslyn analyzers that check the buffer contract at compile time. All three diagnostics have error severity by default: SF001 catches buffer implementations that cannot satisfy a serializer's struct constraint, SF002 catches common ownership mistakes, and SF003 enforces explicitly required overrides.

| ID | Description |
| --- | --- |
| SF001 | `IWriteBuffer` / `IReadBuffer` implementations must be structs. Every consuming API constrains buffers to `struct`, so a class implementation compiles at its declaration and is unusable at every call site. |
| SF002 | Buffer structs are single-owner and must not be copied. A copy diverges silently (two write indexes over one window) and double-returns pooled arrays at Dispose. |
| SF003 | Virtual methods marked `[RequireOverride]` must be overridden by concrete derived types that can access them. |

SF002 is the one you will meet. Implementing a buffer interface is the non-copyable marker, no attribute needed. The analyzer catches common copy mistakes in serializer code: by-value parameters and extension receivers, `in` / `ref readonly` parameters (the callee mutates a defensive copy), local and field initialization from another buffer, boxing, capture in a tuple or array initializer, `foreach`, pattern matching, `with`, by-value properties, yielding an existing buffer, and mutation through a `readonly` reference. It is a guard against common mistakes, not a complete ownership checker: callers still need to keep buffers single-owner and pass them by `ref`.

```csharp
void ByValue(ArrayPoolListWriteBuffer buffer) { }    // SF002
void ByReadonlyRef(in ArrayPoolListWriteBuffer buffer) { } // SF002
void ByRef(ref ArrayPoolListWriteBuffer buffer) { }  // OK

var buffer = new ArrayPoolListWriteBuffer(scratch); // OK: constructed in place
var copy = buffer;                                 // SF002: copied by assignment
```

The analyzers ship inside the SerializerFoundation package under `analyzers/dotnet/cs` and require a host compatible with Roslyn 4.3.1 or later. Standard NuGet `PackageReference` integration loads them automatically, including through transitive package references unless analyzer assets are excluded. When importing DLLs manually or through an engine-specific workflow, configure that host to load the analyzer DLL as well.

License
---
This library is under the MIT License.
