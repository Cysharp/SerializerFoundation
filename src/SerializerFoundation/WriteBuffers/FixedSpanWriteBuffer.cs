namespace SerializerFoundation;

public ref struct FixedSpanWriteBuffer : IWriteBuffer
{
    Span<byte> buffer;
    int written;

    public long BytesWritten => written;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public FixedSpanWriteBuffer(Span<byte> buffer)
    {
        this.buffer = buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }
        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetReference(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }

        return ref MemoryMarshal.GetReference(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        buffer = buffer.Slice(bytesWritten);
        written += bytesWritten;
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
    }
}
