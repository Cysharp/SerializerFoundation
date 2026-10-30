// Uses a pooling strategy close to dotnet/runtime's SegmentedArrayBuilder.
// Unlike that internal type, however, this is exposed as public API with a long code path,
// which raises the risk of a double-Return caused by copying the struct.
// For that reason, we bundle an Analyzer(NonCopyableBufferAnalyzer) that reports an error when a struct implementing IReadBuffer/IWriteBuffer is copied, to prevent this.
// I hope C# will support ownership as a language feature in the future. https://github.com/dotnet/csharplang/pull/10296

namespace SerializerFoundation;

/// <summary>
/// An <see cref="IWriteBuffer"/> that stages the message in a caller-provided scratch span first,
/// then in a chain of arrays rented from <see cref="ArrayPool{T}"/>.
/// Dispose returns the rented arrays and must be called exactly once.
/// </summary>
public ref struct ArrayPoolListWriteBuffer : IWriteBuffer, IDisposable
{
    // The struct is kept small on purpose: a message that fits in the scratch span (the common case)
    // pays only for constructing these fields. The segment table is rented on the first spill.
    Span<byte> scratchBuffer;
    Span<byte> currentBuffer;
    int currentWritten;      // bytes written into currentBuffer
    int scratchLength;       // finished length of the scratch span once spilled (pooled != null)
    int pooledCount;         // rented arrays in pooled; the last one is currentBuffer
    long completedTotal;     // bytes in every finished segment (scratch and pooled), excludes currentWritten
    PooledSegment[]? pooled; // [i].Length = finished length of pooled segment i (i < pooledCount - 1)

    public long BytesWritten
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => completedTotal + currentWritten;
    }

    [Obsolete("Use scratchBuffer ctor instead.", true)]
    public ArrayPoolListWriteBuffer()
    {

    }

    /// <summary>
    /// Creates a write buffer that fills <paramref name="scratchBuffer"/> (typically stackalloc memory) before renting from the pool.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ArrayPoolListWriteBuffer(Span<byte> scratchBuffer)
    {
        this.scratchBuffer = scratchBuffer;
        currentBuffer = scratchBuffer;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        var remaining = currentBuffer.Length - currentWritten;
        if (remaining == 0 || (uint)remaining < (uint)sizeHint)
        {
            return GetSpanSlow(sizeHint);
        }
        else
        {
#if !NETSTANDARD2_0
            // The JIT does not always eliminate the range check inside Slice.
            // This is a hot path and called frequently, so we avoid Slice here since the bounds are already checked by the branch above.
            return MemoryMarshal.CreateSpan(
                ref Unsafe.Add(ref MemoryMarshal.GetReference(currentBuffer), currentWritten),
                remaining);
#else
            return currentBuffer.Slice(currentWritten, remaining);
#endif
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    Span<byte> GetSpanSlow(int sizeHint)
    {
        if (sizeHint < 0) Throws.ArgumentOutOfRange();
        if (sizeHint == 0) sizeHint = 1;

        if (currentBuffer.Length - currentWritten < sizeHint)
        {
            // the segment table is rented on the first spill
            var segments = pooled ??= PooledSegments.Rent();

            if (pooledCount > 0 && currentWritten == 0)
            {
                // the current pooled array is untouched (e.g. GetSpan() followed by GetSpan(big));
                // replace it in place instead of recording an empty segment and burning a slot
                // (rent before return so a failed Rent never leaves a returned array in the slot)
                var slot = pooledCount - 1;
                var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, GetMinSegmentSize(slot)));
                ArrayPool<byte>.Shared.Return(segments[slot].Array!);
                segments[slot].Array = replacement;
                currentBuffer = replacement;
                return replacement;
            }

            // allocate next segment first (GetMinSegmentSize throws once all 16 slots are used),
            // so a failed Rent leaves the buffer untouched
            var minSize = GetMinSegmentSize(pooledCount);
            var requiredSize = Math.Max(sizeHint, minSize);
            var newArray = ArrayPool<byte>.Shared.Rent(requiredSize);

            // finish current segment (the scratch span on the first spill, possibly at length 0)
            if (pooledCount == 0)
            {
                scratchLength = currentWritten;
            }
            else
            {
                segments[pooledCount - 1].Length = currentWritten;
            }
            completedTotal += currentWritten;

            segments[pooledCount++].Array = newArray;
            currentBuffer = newArray;
            currentWritten = 0;
        }

        return currentBuffer.Slice(currentWritten);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        if ((uint)bytesWritten > (uint)(currentBuffer.Length - currentWritten))
        {
            Throws.AdvancedTooFar();
        }
        currentWritten += bytesWritten;
    }

    /// <summary>Copies the written message into a new array.</summary>
    public byte[] ToArray()
    {
        if (pooled is null)
        {
            // everything is still in the scratch span: one allocation, one copy
            var length = currentWritten;
            if (length == 0) return [];

            var array = GC.AllocateUninitializedArray<byte>(length);
            scratchBuffer.Slice(0, length).CopyTo(array);
            return array;
        }

        var totalLength = checked((int)BytesWritten);
        if (totalLength == 0) return [];

        var result = GC.AllocateUninitializedArray<byte>(totalLength);
        WriteTo(result);
        return result;
    }

    /// <summary>
    /// Copies the written message into <paramref name="destination"/>, which must be at least <see cref="BytesWritten"/> bytes long.
    /// <paramref name="destination"/> is not retained (it is <c>scoped</c>), so it may be stack memory.
    /// </summary>
    public void WriteTo(scoped Span<byte> destination)
    {
        var segments = pooled;
        if (segments is null)
        {
            scratchBuffer.Slice(0, currentWritten).CopyTo(destination);
            return;
        }

        // copy scratch buffer
        var scratchLen = scratchLength;
        if (scratchLen > 0)
        {
            scratchBuffer.Slice(0, scratchLen).CopyTo(destination);
            destination = destination.Slice(scratchLen);
        }

        // copy pooled buffers
        for (int i = 0; i < pooledCount; i++)
        {
            ref var segment = ref segments[i];
            var len = i < pooledCount - 1 ? segment.Length : currentWritten;
            segment.Array!.AsSpan(0, len).CopyTo(destination);
            destination = destination.Slice(len);
        }
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
        var segments = pooled;
        if (segments is not null)
        {
            pooled = null;
            PooledSegments.Return(segments, pooledCount);
        }
        pooledCount = 0;
        scratchLength = 0;
        completedTotal = 0;
        currentWritten = 0;
        currentBuffer = default;
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

    /// <summary>Borrowed zero-copy view of the written message; valid until the next write or Dispose.</summary>
    public BufferSegments GetWrittenSegments()
    {
        var segments = pooled;
        if (segments is null)
        {
            return new BufferSegments(scratchBuffer.Slice(0, currentWritten), null, 0, 0, currentWritten);
        }

        // GetSpanSlow never records an empty completed segment, so the only possible empty
        // segment is a freshly rented, still-unwritten tail; drop it so every segment is non-empty
        var visiblePooled = pooledCount;
        var lastLength = currentWritten;
        if (currentWritten == 0)
        {
            visiblePooled--;
            lastLength = visiblePooled > 0 ? segments[visiblePooled - 1].Length : 0; // finished length of the new last pooled segment
        }
        return new BufferSegments(scratchBuffer.Slice(0, scratchLength), segments, visiblePooled, lastLength, BytesWritten);
    }
}

// Boxed instances double as the interface-shaped staging buffer (interface calls mutate the box in place).
// However, that needs to be handled carefully.
// (The Analyzer will warn about this, so only disable it if you understand what it means)

/// <summary>
/// An <see cref="ArrayPoolListWriteBuffer"/> variant for target frameworks without <c>allows ref struct</c> support.
/// </summary>
public struct CompatibleArrayPoolListWriteBuffer : IWriteBuffer, IDisposable, IBufferWriter<byte>
{
    PooledSegment[]? pooled; // [i].Length = finished length of pooled segment i (i < pooledCount - 1)
    int pooledCount;

    byte[]? currentArray; // == pooled[pooledCount - 1].Array once the first segment is rented
    int currentWritten;
    long completedTotal;  // bytes in every finished segment, excludes currentWritten

    public long BytesWritten
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => completedTotal + currentWritten;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        var array = currentArray;
        if (array != null)
        {
            var remaining = array.Length - currentWritten;
            if (remaining != 0 && (uint)remaining >= (uint)sizeHint)
            {
                return array.AsSpan(currentWritten);
            }
        }
        return GetSpanSlow(sizeHint);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    Span<byte> GetSpanSlow(int sizeHint)
    {
        if (sizeHint < 0) Throws.ArgumentOutOfRange();
        if (sizeHint == 0) sizeHint = 1;

        var array = currentArray;
        if (array == null || array.Length - currentWritten < sizeHint)
        {
            // the segment table is rented together with the first array
            var segments = pooled ??= PooledSegments.Rent();

            if (pooledCount > 0 && currentWritten == 0)
            {
                // the current pooled array is untouched; replace it in place instead of
                // recording an empty segment and burning a slot (rent before return, see the ref variant)
                var slot = pooledCount - 1;
                var replacement = ArrayPool<byte>.Shared.Rent(Math.Max(sizeHint, GetMinSegmentSize(slot)));
                ArrayPool<byte>.Shared.Return(array!);
                segments[slot].Array = replacement;
                currentArray = replacement;
                return replacement;
            }

            // allocate next segment first (GetMinSegmentSize throws once all 16 slots are used),
            // so a failed Rent leaves the buffer untouched
            var minSize = GetMinSegmentSize(pooledCount);
            var requiredSize = Math.Max(sizeHint, minSize);
            array = ArrayPool<byte>.Shared.Rent(requiredSize);

            // finish current segment
            if (pooledCount > 0)
            {
                segments[pooledCount - 1].Length = currentWritten;
            }
            completedTotal += currentWritten;

            segments[pooledCount++].Array = array;
            currentArray = array;
            currentWritten = 0;
        }
        return array.AsSpan(currentWritten);
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        GetSpan(sizeHint); // ensures capacity in currentArray
        return currentArray!.AsMemory(currentWritten);
    }

    // same guard as the ref variant, maintaining 0 <= currentWritten <= capacity;
    // before the first rent the capacity is 0, so any nonzero Advance throws
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Advance(int bytesWritten)
    {
        var capacity = currentArray is null ? 0 : currentArray.Length;
        if ((uint)bytesWritten > (uint)(capacity - currentWritten))
        {
            Throws.AdvancedTooFar();
        }
        currentWritten += bytesWritten;
    }

    /// <summary>Copies the written message into a new array.</summary>
    public byte[] ToArray()
    {
        var totalLength = checked((int)BytesWritten);
        if (totalLength == 0) return [];

        var result = GC.AllocateUninitializedArray<byte>(totalLength);
        WriteTo(result);
        return result;
    }

    /// <summary>
    /// Copies the written message into <paramref name="destination"/>, which must be at least <see cref="BytesWritten"/> bytes long.
    /// <paramref name="destination"/> is not retained (it is <c>scoped</c>), so it may be stack memory.
    /// </summary>
    public void WriteTo(scoped Span<byte> destination)
    {
        var segments = pooled;
        for (int i = 0; i < pooledCount; i++)
        {
            ref var segment = ref segments![i];
            var len = i < pooledCount - 1 ? segment.Length : currentWritten;
            segment.Array!.AsSpan(0, len).CopyTo(destination);
            destination = destination.Slice(len);
        }
    }

    public void Flush()
    {
    }

    public void Dispose()
    {
        var segments = pooled;
        if (segments is not null)
        {
            pooled = null;
            PooledSegments.Return(segments, pooledCount);
        }
        pooledCount = 0;
        completedTotal = 0;
        currentWritten = 0;
        currentArray = null;
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

    // iterator

    /// <summary>Borrowed zero-copy view of the written message; valid until the next write or Dispose.</summary>
    public BufferSegments GetWrittenSegments()
    {
        var segments = pooled;
        if (segments is null)
        {
            return new BufferSegments(default, null, 0, 0, 0);
        }

        // drop a freshly rented, still-unwritten tail so every segment is non-empty
        var visiblePooled = pooledCount;
        var lastLength = currentWritten;
        if (currentWritten == 0)
        {
            visiblePooled--;
            lastLength = visiblePooled > 0 ? segments[visiblePooled - 1].Length : 0; // finished length of the new last pooled segment
        }
        return new BufferSegments(default, segments, visiblePooled, lastLength, BytesWritten);
    }
}
