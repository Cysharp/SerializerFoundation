using System;

namespace SerializerFoundation;

#if NET9_0_OR_GREATER

public ref struct BufferWriterWriteBuffer<TBufferWriter> : IWriteBuffer
    where TBufferWriter : IBufferWriter<byte>
{
    ref TBufferWriter bufferWriter; // allow mutable struct buffer writers(only .NET 9 ore greater)
    Span<byte> buffer;
    int writtenInBuffer;
    long totalWritten;

    public long BytesWritten => totalWritten;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BufferWriterWriteBuffer(ref TBufferWriter bufferWriter)
    {
        this.bufferWriter = ref bufferWriter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetReference(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return ref MemoryMarshal.GetReference(buffer);
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
        buffer = bufferWriter.GetSpan(sizeHint);

        // validate IBufferWriter contract
        if (buffer.Length == 0 || buffer.Length < sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }
    }

    public void Dispose()
    {
        Flush();
    }
}

#endif

public struct NonRefBufferWriterWriteBuffer<TBufferWriter> : IWriteBuffer
    where TBufferWriter : class, IBufferWriter<byte>
{
    TBufferWriter bufferWriter;
    PointerSpan buffer;
    MemoryHandle bufferHandle;
    int writtenInBuffer;
    long totalWritten;

    public long BytesWritten => totalWritten;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NonRefBufferWriterWriteBuffer(TBufferWriter bufferWriter) // copy so only allows class
    {
        this.bufferWriter = bufferWriter;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetReference(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return ref buffer.GetReference();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        buffer.Advance(bytesWritten);
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
        var memory = bufferWriter.GetMemory(sizeHint);
        unsafe
        {
            bufferHandle.Dispose();

            var handle = memory.Pin(); // keep pinned until next Flush
            this.bufferHandle = handle;
            this.buffer = new PointerSpan((byte*)handle.Pointer, memory.Length);
        }

        // validate IBufferWriter contract
        if (buffer.Length == 0 || buffer.Length < sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }
    }

    public void Dispose()
    {
        try
        {
            Flush();
        }
        finally
        {
            buffer = default;
            bufferHandle.Dispose();
        }
    }
}
