namespace SerializerFoundation;

public ref struct ReadOnlySequenceReadBuffer : IReadBuffer, IDisposable
{
    // Slicing a ReadOnlySequence is slow
    // so avoid it as much as possible and use Span instead.
    ReadOnlySequence<byte> sequence;
    ReadOnlySpan<byte> currentSpan;
    byte[]? tempBuffer;
    long consumed;

    public long BytesConsumed => consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySequenceReadBuffer(in ReadOnlySequence<byte> sequence)
    {
        this.sequence = sequence;
        this.currentSpan = sequence.FirstSpan;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> GetSpan(int sizeHint = 0)
    {
        if (currentSpan.Length == 0 || (uint)currentSpan.Length < (uint)sizeHint)
        {
            return GetSpanSlow(sizeHint);
        }

        return currentSpan;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    ReadOnlySpan<byte> GetSpanSlow(int sizeHint)
    {
        ReturnTempBuffer();

        // move to next segment
        if (currentSpan.Length == 0)
        {
            if (sequence.Length == 0)
            {
                Throws.InsufficientSpaceInBuffer();
            }
            currentSpan = sequence.FirstSpan;
        }

        // if still not enough, copy to temp buffer
        if ((uint)currentSpan.Length < (uint)sizeHint)
        {
            if ((uint)sequence.Length < (uint)sizeHint)
            {
                Throws.InsufficientSpaceInBuffer();
            }

            tempBuffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            sequence.Slice(0, sizeHint).CopyTo(tempBuffer);
            currentSpan = tempBuffer.AsSpan(0, sizeHint);
        }

        return currentSpan;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesConsumed)
    {
        if (tempBuffer == null && bytesConsumed < currentSpan.Length)
        {
            currentSpan = currentSpan.Slice(bytesConsumed);
        }
        else
        {
            AdvanceSlow(bytesConsumed);
        }
        consumed += bytesConsumed;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    void AdvanceSlow(int bytesConsumed)
    {
        ReturnTempBuffer();
        sequence = sequence.Slice(bytesConsumed);
        currentSpan = sequence.FirstSpan;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ReturnTempBuffer()
    {
        if (tempBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
            tempBuffer = null;
        }
    }

    public void Dispose()
    {
        ReturnTempBuffer();
    }
}
