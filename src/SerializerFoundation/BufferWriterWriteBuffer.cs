namespace SerializerFoundation;

public ref struct BufferWriterWriteBuffer<TBufferWriter> : IWriteBuffer
    where TBufferWriter : IBufferWriter<byte>
{
    ref TBufferWriter bufferWriter; // allow mutable struct buffer writers
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
        if ((uint)buffer.Length < (uint)sizeHint)
        {
            EnsureNewBuffer(sizeHint);
        }

        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        buffer = buffer.Slice(bytesWritten);
        writtenInBuffer += bytesWritten;
        totalWritten += bytesWritten;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
        if (buffer.Length < sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }
    }
}
