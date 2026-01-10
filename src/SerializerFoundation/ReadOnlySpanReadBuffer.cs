namespace SerializerFoundation;

public ref struct ReadOnlySpanReadBuffer : IReadBuffer
{
    ReadOnlySpan<byte> buffer;
    int consumed = 0;

    public long BytesConsumed => consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpanReadBuffer(ReadOnlySpan<byte> buffer)
    {
        this.buffer = buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }

        return buffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesConsumed)
    {
        buffer = buffer.Slice(bytesConsumed);
        consumed += bytesConsumed;
    }
}
