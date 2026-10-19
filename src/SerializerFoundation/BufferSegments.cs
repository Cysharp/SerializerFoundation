namespace SerializerFoundation;

// Backing storage shared by the pooled-list write buffers (both tiers) and the BufferSegments view.
// The chain of rented arrays lives in a PooledSegment[] that is itself rented from ArrayPool<PooledSegment>.Shared
// on the first spill, so the writer struct carries a single reference instead of 16 inline array references.
// The inline form (InlineArray(16) of byte[] + InlineArray(17) of int) made the struct ~240 bytes, and since
// GC references can never be left uninitialized, every construction paid for zeroing all of it even when the
// message never left the scratch span. 16 exponentially-growing segments are enough to reach Array.MaxLength.

/// <summary>One rented array of a pooled-list write buffer and its finished length (meaningful for every segment but the live last one).</summary>
internal struct PooledSegment
{
    public byte[]? Array;
    public int Length;
}

internal static class PooledSegments
{
    // the segment table always has this many slots; GetMinSegmentSize throws before a 17th segment is ever requested
    public const int MaxCount = 16;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledSegment[] Rent() => ArrayPool<PooledSegment>.Shared.Rent(MaxCount);

    /// <summary>Returns every rented array in <paramref name="segments"/> and the table itself to their pools.</summary>
    public static void Return(PooledSegment[] segments, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var array = segments[i].Array;
            if (array != null)
            {
                ArrayPool<byte>.Shared.Return(array);
            }
        }
        // the table goes back to a shared pool, so drop the array references instead of leaving them to be found by the next renter
        segments.AsSpan(0, count).Clear();
        ArrayPool<PooledSegment>.Shared.Return(segments);
    }
}

// a concrete ref struct works as a parameter type on every TFM (only generic positions need `allows ref struct`),

/// <summary>
/// A borrowed, forward-only view of a written message, exposed as zero-copy segments.
/// Valid only while the source buffer is alive and unmodified.
/// Every segment is non-empty, and <see cref="SegmentCount"/> tells how many there are up front.
/// Instead of foreach, you can loop with `while (segments.TryGetNext(out var segment))`.
/// </summary>
public ref struct BufferSegments
{
    // first (scratch, pre-sliced; empty means "no first segment") followed by
    // rented arrays ([i].Length = finished length of pooled segment i, the last one uses lastPooledLength).
    // The producers guarantee no pooled segment is empty (the writers replace an untouched array
    // in place rather than finishing it empty, and GetWrittenSegments drops an unwritten tail).
    readonly ReadOnlySpan<byte> first;
    readonly PooledSegment[]? pooled; // non-null whenever pooledCount > 0
    readonly int pooledCount;
    readonly int lastPooledLength;
    readonly long length;
    int index;

    /// <summary>Total message length in bytes.</summary>
    public readonly long Length => length;

    /// <summary>
    /// Number of segments <see cref="TryGetNext"/> yields over a full pass (none of them empty).
    /// Lets a header that depends on the segment count be written without a counting pass.
    /// </summary>
    public readonly int SegmentCount => (first.Length > 0 ? 1 : 0) + pooledCount;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal BufferSegments(ReadOnlySpan<byte> first, PooledSegment[]? pooled, int pooledCount, int lastPooledLength, long length)
    {
        this.first = first;
        this.pooled = pooled;
        this.pooledCount = pooledCount;
        this.lastPooledLength = lastPooledLength;
        this.length = length;
        index = -1;
    }

    /// <summary>Retrieves the next segment. Returns false when no segments remain.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetNext(out ReadOnlySpan<byte> segment)
    {
        index++;

        if (index == 0)
        {
            if (first.Length > 0)
            {
                segment = first;
                return true;
            }
            index++;
        }

        var pooledIndex = index - 1;
        if ((uint)pooledIndex < (uint)pooledCount)
        {
            ref var entry = ref pooled![pooledIndex];
            var len = pooledIndex < pooledCount - 1
                ? entry.Length
                : lastPooledLength;
            segment = entry.Array!.AsSpan(0, len);
            return true;
        }

        segment = default;
        return false;
    }

    /// <summary>Restarts iteration from the first segment.</summary>
    public void Reset()
    {
        index = -1;
    }
}
