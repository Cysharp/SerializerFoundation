using BenchmarkDotNet.Attributes;
using SerializerFoundation;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Benchmark.MicroBenchmarks;

public class MemorySliceVsPinPointer
{
    ArrayBufferWriter<byte> bufferWriter;

    public MemorySliceVsPinPointer()
    {
        bufferWriter = new ArrayBufferWriter<byte>(100 * 1000);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        bufferWriter.ResetWrittenCount();
    }

    [Benchmark]
    public void PinPointer()
    {
        // PointerSpan + MemoryHandle
        using var buffer = new NonRefBufferWriterWriteBuffer(bufferWriter);
        for (int i = 0; i < 1000; i++)
        {
            ref var foo = ref buffer.GetReference(100);
            buffer.Advance(100);
        }
    }

    [Benchmark]
    public void PinPointerGetSpan()
    {
        // PointerSpan + MemoryHandle
        using var buffer = new NonRefBufferWriterWriteBuffer(bufferWriter);
        for (int i = 0; i < 1000; i++)
        {
            var foo = buffer.GetSpan(100);
            buffer.Advance(100);
        }
    }

    [Benchmark]
    public void MemorySlice()
    {
        var buffer = new MemorySliceWriteBuffer(bufferWriter);
        for (int i = 0; i < 1000; i++)
        {
            ref var foo = ref buffer.GetReference(100);
            buffer.Advance(100);
        }
    }

    [Benchmark]
    public void SpanVirtual()
    {
        IBufferWriter<byte> bf = bufferWriter;
        var buffer = new BufferWriterWriteBuffer<IBufferWriter<byte>>(ref bf);
        for (int i = 0; i < 1000; i++)
        {
            ref var foo = ref buffer.GetReference(100);
            buffer.Advance(100);
        }
    }

    [Benchmark]
    public void Span()
    {
        ArrayBufferWriter<byte> bf = bufferWriter;
        var buffer = new BufferWriterWriteBuffer<ArrayBufferWriter<byte>>(ref bf);
        for (int i = 0; i < 1000; i++)
        {
            ref var foo = ref buffer.GetReference(100);
            buffer.Advance(100);
        }
    }
}

public struct MemorySliceWriteBuffer : IWriteBuffer
{
    IBufferWriter<byte> bufferWriter;
    Memory<byte> buffer;
    int writtenInBuffer;
    long totalWritten;

    public long BytesWritten => totalWritten;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MemorySliceWriteBuffer(IBufferWriter<byte> bufferWriter)
    {
        this.bufferWriter = bufferWriter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if ((uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return buffer.Span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetReference(int sizeHint = 0)
    {
        if ((uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return ref MemoryMarshal.GetReference(buffer.Span);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        buffer = buffer.Slice(bytesWritten);
        writtenInBuffer += bytesWritten;
        totalWritten += bytesWritten;
    }

    public void Flush()
    {
        if (writtenInBuffer > 0)
        {
            bufferWriter.Advance(checked((int)writtenInBuffer));
            writtenInBuffer = 0;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void EnsureNewBuffer(int sizeHint)
    {
        Flush();
        buffer = bufferWriter.GetMemory(sizeHint);

        // validate IBufferWriter contract
        if (buffer.Length < sizeHint)
        {
            // Throws.InsufficientSpaceInBuffer();
        }
    }

    public void Dispose()
    {
        Flush();
    }
}
