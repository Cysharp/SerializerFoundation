namespace SerializerFoundation.Tests;

// BufferSegments contract: SegmentCount equals the number of TryGetNext hits, every segment is
// non-empty, Reset replays the same walk, and the two writer-side empty-segment sources
// (an untouched pooled array widened by a larger GetSpan, and an unwritten freshly rented tail)
// never leak into the view.
public class BufferSegmentsTest
{
    static byte[] Collect(ref BufferSegments segments, out int hits)
    {
        var result = new List<byte>();
        hits = 0;
        while (segments.TryGetNext(out var segment))
        {
            Assert.False(segment.IsEmpty, "BufferSegments must never yield an empty segment");
            result.AddRange(segment.ToArray());
            hits++;
        }
        return result.ToArray();
    }

    static void AssertView(ref BufferSegments segments, byte[] expected, int expectedCount)
    {
        Assert.Equal(expectedCount, segments.SegmentCount);
        Assert.Equal(expected.LongLength, segments.Length);

        var first = Collect(ref segments, out var hits);
        Assert.Equal(expectedCount, hits);
        Assert.Equal(expected, first);

        // Reset replays the same walk
        segments.Reset();
        var second = Collect(ref segments, out hits);
        Assert.Equal(expectedCount, hits);
        Assert.Equal(expected, second);
    }

    static byte[] Fill(Span<byte> span, ref int seed)
    {
        var bytes = new byte[span.Length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(seed++ * 31);
        }
        bytes.CopyTo(span);
        return bytes;
    }

    // --- ref (scratch-first) variant ---

    [Fact]
    public void Ref_Empty_HasNoSegments()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, [], 0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_EmptyScratch_NothingWritten_HasNoSegments()
    {
        var buffer = new ArrayPoolListWriteBuffer(Span<byte>.Empty);
        try
        {
            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, [], 0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_ScratchOnly_IsOneSegment()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var seed = 0;
            var expected = Fill(buffer.GetSpan(20).Slice(0, 20), ref seed);
            buffer.Advance(20);

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected, 1);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_ScratchAndPooled_CountMatchesWalk()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var seed = 0;
            var expected = new List<byte>();
            expected.AddRange(Fill(buffer.GetSpan(32).Slice(0, 32), ref seed));
            buffer.Advance(32);
            // spills into pooled segment 0 (64KB), then forces pooled segment 1
            expected.AddRange(Fill(buffer.GetSpan(1000).Slice(0, 1000), ref seed));
            buffer.Advance(1000);
            expected.AddRange(Fill(buffer.GetSpan(200_000).Slice(0, 200_000), ref seed));
            buffer.Advance(200_000);

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 3);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_WideningUntouchedPooledArray_DoesNotProduceEmptySegment()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var seed = 0;
            var expected = new List<byte>();
            expected.AddRange(Fill(buffer.GetSpan(32).Slice(0, 32), ref seed));
            buffer.Advance(32);

            // GetSpan() rents pooled segment 0 (64KB) but nothing is written into it;
            // GetSpan(bigger) must then replace that array in place rather than finish it empty
            _ = buffer.GetSpan();
            var wide = buffer.GetSpan(200_000);
            Assert.True(wide.Length >= 200_000);
            expected.AddRange(Fill(wide.Slice(0, 200_000), ref seed));
            buffer.Advance(200_000);

            Assert.Equal(expected.Count, buffer.BytesWritten);
            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 2); // scratch + one pooled, no empty one between
            Assert.Equal(expected.ToArray(), buffer.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_RepeatedWidening_ReusesTheSlot()
    {
        // each round widens an untouched pooled array; without in-place replacement every round
        // would record an empty segment and burn one of the 16 pooled slots
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[8]);
        try
        {
            var seed = 0;
            var expected = new List<byte>(Fill(buffer.GetSpan(8).Slice(0, 8), ref seed));
            buffer.Advance(8);

            var request = 100_000;
            scoped Span<byte> span = default;
            for (var round = 0; round < 4; round++)
            {
                span = buffer.GetSpan(request);
                Assert.True(span.Length >= request);
                request = span.Length + 1; // exceed the current array so the next round must widen again
            }
            expected.AddRange(Fill(span.Slice(0, 1000), ref seed));
            buffer.Advance(1000);

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 2); // scratch + the single surviving pooled array
            Assert.Equal(expected.ToArray(), buffer.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_UnwrittenRentedTail_IsDropped()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var seed = 0;
            var expected = new List<byte>();
            expected.AddRange(Fill(buffer.GetSpan(32).Slice(0, 32), ref seed));
            buffer.Advance(32);
            expected.AddRange(Fill(buffer.GetSpan(10).Slice(0, 10), ref seed));
            buffer.Advance(10);

            // forces a new pooled array that stays unwritten
            var tail = buffer.GetSpan(200_000);
            Assert.Equal(expected.Count, buffer.BytesWritten);

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 2); // scratch + the 10-byte segment; tail is not visible

            // writing into the tail afterwards makes it visible again
            expected.AddRange(Fill(tail.Slice(0, 5), ref seed));
            buffer.Advance(5);
            segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 3);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Ref_UnwrittenRentedTail_AfterScratchOnly_LeavesOnlyScratch()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[32]);
        try
        {
            var seed = 0;
            var expected = Fill(buffer.GetSpan(32).Slice(0, 32), ref seed);
            buffer.Advance(32);
            _ = buffer.GetSpan(1); // rents pooled segment 0, never written

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected, 1);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    // --- Compatible (pooled-only) variant ---

    [Fact]
    public void Compat_Empty_HasNoSegments()
    {
        var buffer = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, [], 0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Compat_MultiplePooled_CountMatchesWalk()
    {
        var buffer = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            var seed = 0;
            var expected = new List<byte>();
            expected.AddRange(Fill(buffer.GetSpan(1000).Slice(0, 1000), ref seed));
            buffer.Advance(1000);
            expected.AddRange(Fill(buffer.GetSpan(200_000).Slice(0, 200_000), ref seed));
            buffer.Advance(200_000);
            expected.AddRange(Fill(buffer.GetSpan(300_000).Slice(0, 300_000), ref seed));
            buffer.Advance(300_000);

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 3);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Compat_WideningUntouchedPooledArray_DoesNotProduceEmptySegment()
    {
        var buffer = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            var seed = 0;
            var expected = new List<byte>();
            expected.AddRange(Fill(buffer.GetSpan(10).Slice(0, 10), ref seed));
            buffer.Advance(10);

            // exhaust the 64KB first segment exactly so the next GetSpan() rents a fresh one
            var remaining = buffer.GetSpan();
            expected.AddRange(Fill(remaining, ref seed));
            buffer.Advance(remaining.Length);

            _ = buffer.GetSpan();            // rents an untouched pooled array
            var wide = buffer.GetSpan(400_000); // must replace it in place
            expected.AddRange(Fill(wide.Slice(0, 400_000), ref seed));
            buffer.Advance(400_000);

            Assert.Equal(expected.Count, buffer.BytesWritten);
            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 2);
            Assert.Equal(expected.ToArray(), buffer.ToArray());
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Compat_UnwrittenRentedTail_IsDropped()
    {
        var buffer = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            var seed = 0;
            var expected = new List<byte>();
            expected.AddRange(Fill(buffer.GetSpan(10).Slice(0, 10), ref seed));
            buffer.Advance(10);

            var tail = buffer.GetSpan(200_000); // new pooled array, unwritten
            Assert.Equal(expected.Count, buffer.BytesWritten);

            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 1);

            expected.AddRange(Fill(tail.Slice(0, 5), ref seed));
            buffer.Advance(5);
            segments = buffer.GetWrittenSegments();
            AssertView(ref segments, expected.ToArray(), 2);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void Compat_UnwrittenFirstRent_HasNoSegments()
    {
        var buffer = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            _ = buffer.GetSpan(1); // rents pooled segment 0, never written
            var segments = buffer.GetWrittenSegments();
            AssertView(ref segments, [], 0);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}
