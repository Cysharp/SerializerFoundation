namespace SerializerFoundation;

// non ref-struct version for netstandard
public unsafe struct FixedPointerWriteBuffer : IWriteBuffer
{
    PointerSpan buffer;
    int written;

    public long BytesWritten => written;

    public FixedPointerWriteBuffer(byte* buffer, int length)
    {
        this.buffer = new PointerSpan(buffer, length);
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
        return ref buffer.GetReference();
    }

    public void Advance(int bytesWritten)
    {
        buffer.Advance(bytesWritten);
        written += bytesWritten;
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
    }
}
