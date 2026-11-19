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

    // Every table carries a generation that advances each time the table goes back to the pool. A writer and
    // every BufferSegments view remember the generation they were handed, so anything that still holds the
    // table after it was returned (a view kept past Dispose, a writer that was copied and disposed through
    // the other copy) can tell that the table is no longer its own, even once another writer has rented it.
    // Without that, such a holder reads or returns whatever the new owner put in the table: another message.
    //
    // The generation lives in the Length of the last slot, which is never a segment length: a finished length
    // is recorded only when a later segment follows, and nothing can follow slot MaxCount - 1. That keeps the
    // table at exactly MaxCount slots.
    const int GenerationSlot = MaxCount - 1;

    /// <summary>Rents a table and reports the generation its holders must present from now on.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PooledSegment[] Rent(out int generation)
    {
        var segments = ArrayPool<PooledSegment>.Shared.Rent(MaxCount);
        generation = segments[GenerationSlot].Length;
        return segments;
    }

    /// <summary>False once <paramref name="segments"/> has been returned to the pool since <paramref name="generation"/> was handed out.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCurrent(PooledSegment[] segments, int generation) => segments[GenerationSlot].Length == generation;

    /// <summary>
    /// Returns every rented array in <paramref name="segments"/> and the table itself to their pools.
    /// Does nothing when the table was already returned by another holder of the same generation.
    /// </summary>
    public static void Return(PooledSegment[] segments, int count, int generation)
    {
        if (!IsCurrent(segments, generation))
        {
            // already returned through a copy of the writer, and possibly rented again since:
            // the arrays in it are not this caller's to return, and neither is the table
            return;
        }

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
        // after the clear, which wipes the generation slot too when all slots were in use
        segments[GenerationSlot].Length = generation + 1;
        ArrayPool<PooledSegment>.Shared.Return(segments);
    }
}

// a concrete ref struct works as a parameter type on every TFM (only generic positions need `allows ref struct`),

/// <summary>
/// A borrowed, forward-only view of a written message, exposed as zero-copy segments.
/// Valid only while the source buffer is alive and unmodified.
/// Once the source buffer has been disposed, <see cref="TryGetNext"/> throws <see cref="ObjectDisposedException"/>
/// for a segment that lived in pooled storage instead of reading storage that has gone back to the pool.
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
    readonly int generation; // of pooled when the view was taken, see PooledSegments
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
    internal BufferSegments(ReadOnlySpan<byte> first, PooledSegment[]? pooled, int pooledCount, int lastPooledLength, int generation, long length)
    {
        this.first = first;
        this.pooled = pooled;
        this.pooledCount = pooledCount;
        this.lastPooledLength = lastPooledLength;
        this.generation = generation;
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
            var segments = pooled!;
            if (!PooledSegments.IsCurrent(segments, generation))
            {
                // the source buffer was disposed: the table, and the arrays it pointed at, may already serve another message
                Throws.SegmentsOutlivedBuffer();
            }
            ref var entry = ref segments[pooledIndex];
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
