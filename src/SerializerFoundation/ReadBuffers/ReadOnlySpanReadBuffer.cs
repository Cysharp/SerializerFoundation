namespace SerializerFoundation;

// non ref-struct variation -> PointerReadBuffer

public ref struct ReadOnlySpanReadBuffer : IReadBuffer
{
    ReadOnlySpan<byte> buffer;
    int consumed = 0;
    readonly int length = 0;

    public long BytesConsumed => consumed;
    public long BytesRemaining => length - consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpanReadBuffer(ReadOnlySpan<byte> buffer)
    {
        this.buffer = buffer;
        this.length = buffer.Length;
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
    public ref readonly byte GetReference(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }

        return ref MemoryMarshal.GetReference(buffer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesConsumed)
    {
        buffer = buffer.Slice(bytesConsumed);
        consumed += bytesConsumed;
    }

    public void Dispose()
    {
    }
}

public unsafe struct PointerReadBuffer : IReadBuffer
{
    PointerSpan buffer;
    int consumed;
    readonly int length;

    public long BytesConsumed => consumed;
    public long BytesRemaining => length - consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PointerReadBuffer(byte* buffer, int length)
    {
        this.buffer = new(buffer, length);
        this.length = length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetSpan(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }

        return buffer.AsSpan();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly byte GetReference(int sizeHint = 0)
    {
        if (buffer.Length == 0 || (uint)buffer.Length < (uint)sizeHint)
        {
            Throws.InsufficientSpaceInBuffer();
        }

        return ref buffer.GetReference();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesConsumed)
    {
        buffer.Advance(bytesConsumed);
        consumed += bytesConsumed;
    }

    public void Dispose()
    {
    }
}

