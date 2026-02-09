namespace SerializerFoundation;

public ref struct ReadOnlySequenceReadBuffer : IReadBuffer
{
    // Slicing a ReadOnlySequence is slow
    // so avoid it as much as possible and use Span instead.
    ReadOnlySequence<byte> sequence;
    ReadOnlySpan<byte> currentSpan;
    byte[]? tempBuffer;
    long consumed;
    readonly long length;

    public long BytesConsumed => consumed;
    public long BytesRemaining => length - consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySequenceReadBuffer(in ReadOnlySequence<byte> sequence)
    {
        this.sequence = sequence;
        this.currentSpan = sequence.FirstSpan;
        this.length = sequence.Length;
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly byte GetReference(int sizeHint = 0)
    {
        if (currentSpan.Length == 0 || (uint)currentSpan.Length < (uint)sizeHint)
        {
            return ref MemoryMarshal.GetReference(GetSpanSlow(sizeHint));
        }

        return ref MemoryMarshal.GetReference(currentSpan);
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

        if (sizeHint <= 0) sizeHint = 1; // minimum 1 byte

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
            currentSpan = default;
        }
    }

    public void Dispose()
    {
        ReturnTempBuffer();
    }
}

public unsafe struct NonRefReadOnlySequenceReadBuffer : IReadBuffer
{
    // Slicing a ReadOnlySequence is slow
    // so avoid it as much as possible and use Span instead.
    ReadOnlySequence<byte> sequence;
    PointerSpan currentSpan;
    MemoryHandle currentSpanHandle;
    byte[]? tempBuffer;
    long consumed;
    readonly long length;

    public long BytesConsumed => consumed;
    public long BytesRemaining => length - consumed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NonRefReadOnlySequenceReadBuffer(in ReadOnlySequence<byte> sequence)
    {
        this.sequence = sequence;
        var memory = sequence.First;
        SetSpan(memory);
        this.length = sequence.Length;
    }

    void SetSpan(ReadOnlyMemory<byte> memory)
    {
        this.currentSpanHandle.Dispose(); // unpin previous
        var handle = memory.Pin();
        this.currentSpanHandle = handle;
        this.currentSpan = new PointerSpan((byte*)handle.Pointer, memory.Length);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly byte GetReference(int sizeHint = 0)
    {
        if (currentSpan.Length == 0 || (uint)currentSpan.Length < (uint)sizeHint)
        {
            return ref MemoryMarshal.GetReference(GetSpanSlow(sizeHint));
        }

        return ref currentSpan.GetReference();
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

            SetSpan(sequence.First);
        }

        if (sizeHint <= 0) sizeHint = 1; // minimum 1 byte

        // if still not enough, copy to temp buffer
        if ((uint)currentSpan.Length < (uint)sizeHint)
        {
            if ((uint)sequence.Length < (uint)sizeHint)
            {
                Throws.InsufficientSpaceInBuffer();
            }

            tempBuffer = ArrayPool<byte>.Shared.Rent(sizeHint);
            sequence.Slice(0, sizeHint).CopyTo(tempBuffer);
            SetSpan(tempBuffer.AsMemory(0, sizeHint));
        }

        return currentSpan;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesConsumed)
    {
        if (tempBuffer == null && bytesConsumed < currentSpan.Length)
        {
            currentSpan.Advance(bytesConsumed);
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
        SetSpan(sequence.First);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ReturnTempBuffer()
    {
        if (tempBuffer != null)
        {
            ArrayPool<byte>.Shared.Return(tempBuffer);
            tempBuffer = null;
            currentSpan = default;
            currentSpanHandle.Dispose();
        }
    }

    public void Dispose()
    {
        ReturnTempBuffer();
        currentSpan = default;
        currentSpanHandle.Dispose();
    }
}
