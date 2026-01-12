namespace SerializerFoundation;

// TODO: non ref version of ArrayPoolWriteBuffer
public ref struct ArrayPoolWriteBuffer : IWriteBuffer, IDisposable
{
    PooledArrays pooledArrays;
    CompletedLengths completedLengths; // [0] = scratch, [1..] = pooled
    int pooledCount;

    Span<byte> scratchBuffer;
    Span<byte> currentBuffer;
    int currentWritten;

    public long BytesWritten
    {
        get
        {
            long total = 0;
            for (int i = 0; i < pooledCount; i++)
            {
                total += completedLengths[i];
            }
            return total + currentWritten;
        }
    }

    [Obsolete("Use scratchBuffer ctor instead.", true)]
    public ArrayPoolWriteBuffer()
    {

    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArrayPoolWriteBuffer(Span<byte> scratchBuffer)
    {
        this.scratchBuffer = scratchBuffer;
        currentBuffer = scratchBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        var len = currentBuffer.Length - currentWritten;
        if (sizeHint > 0 && len >= sizeHint)
        {
            return MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetReference(currentBuffer), currentWritten),
                len);
        }

        return GetSpanSlow(sizeHint);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref byte GetReference(int sizeHint = 0)
    {
        var len = currentBuffer.Length - currentWritten;
        if (sizeHint > 0 && len >= sizeHint)
        {
            return ref Unsafe.Add(ref MemoryMarshal.GetReference(currentBuffer), currentWritten);
        }

        return ref MemoryMarshal.GetReference(GetSpanSlow(sizeHint));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    Span<byte> GetSpanSlow(int sizeHint)
    {
        if (sizeHint <= 0) sizeHint = 1;

        if (currentBuffer.Length - currentWritten < sizeHint)
        {
            // finish current segment
            completedLengths[pooledCount] = currentWritten;

            // allocate next segment
            var minSize = GetMinSegmentSize(pooledCount);
            var requiredSize = Math.Max(sizeHint, minSize);
            var newArray = ArrayPool<byte>.Shared.Rent(requiredSize);
            pooledArrays[pooledCount++] = newArray;

            currentBuffer = newArray;
            currentWritten = 0;
        }

        return currentBuffer.Slice(currentWritten);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        currentWritten += bytesWritten;
    }

    public byte[] ToArray()
    {
        var totalLength = checked((int)BytesWritten);
        if (totalLength == 0) return [];

        var result = GC.AllocateUninitializedArray<byte>(totalLength);
        WriteTo(result);
        return result;
    }

    public void WriteTo(Span<byte> destination)
    {
        // copy scratch buffer
        var scratchLen = pooledCount > 0 ? completedLengths[0] : currentWritten;
        if (scratchLen > 0)
        {
            scratchBuffer.Slice(0, scratchLen).CopyTo(destination);
            destination = destination.Slice(scratchLen);
        }

        // copy pooled buffers
        for (int i = 0; i < pooledCount; i++)
        {
            var len = i < pooledCount - 1 ? completedLengths[i + 1] : currentWritten;
            pooledArrays[i]!.AsSpan(0, len).CopyTo(destination);
            destination = destination.Slice(len);
        }
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
        for (int i = 0; i < pooledCount; i++)
        {
            var array = pooledArrays[i];
            if (array != null)
            {
                ArrayPool<byte>.Shared.Return(array);
                pooledArrays[i] = null;
            }
        }
        pooledCount = 0;
        currentWritten = 0;
        currentBuffer = scratchBuffer;
    }

    // Segment sizes grow exponentially from 64KB to 1GB.
    // Index 15 returns Array.MaxLength to handle edge cases where callers
    // request large buffers via sizeHint but only partially consume them via Advance().
    // This ensures we never exceed 16 pooled segments regardless of usage pattern.
    // Example: GetSpan(1GB) followed by Advance(1) repeated would exhaust segments
    // quickly if we continued doubling, but Array.MaxLength guarantees any sizeHint fits.
    static int GetMinSegmentSize(int index) => index switch
    {
        0 => 65_536,
        1 => 131_072,
        2 => 262_144,
        3 => 524_288,
        4 => 1_048_576,
        5 => 2_097_152,
        6 => 4_194_304,
        7 => 8_388_608,
        8 => 16_777_216,
        9 => 33_554_432,
        10 => 67_108_864,
        11 => 134_217_728,
        12 => 268_435_456,
        13 => 536_870_912,
        14 => 1_073_741_824,
        15 => Array.MaxLength,
        _ => Throws.InsufficientSpaceInBuffer<int>(),
    };

#if NET9_0_OR_GREATER

    [InlineArray(16)]
    internal struct PooledArrays
    {
        public byte[]? value;
    }

    [InlineArray(17)] // scratch(1) + pooled(16)
    internal struct CompletedLengths
    {
        public int value;
    }

#else

    [StructLayout(LayoutKind.Sequential)]
    internal struct PooledArrays
    {
        byte[]? _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15;

        public ref byte[]? this[int index]
        {
            [System.Diagnostics.CodeAnalysis.UnscopedRef]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Unsafe.Add(ref _0, index);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct CompletedLengths
    {
        int _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15, _16;

        public ref int this[int index]
        {
            [System.Diagnostics.CodeAnalysis.UnscopedRef]
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ref Unsafe.Add(ref _0, index);
        }
    }

#endif
}
