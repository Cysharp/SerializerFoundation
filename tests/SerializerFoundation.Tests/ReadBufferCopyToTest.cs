using System.Buffers;
using Xunit;

namespace SerializerFoundation.Tests;

// IReadBuffer.CopyTo contract: writes the next destination.Length bytes and consumes
// nothing, on every buffer shape. The sequence buffers must serve a straddling payload
// out of their segments WITHOUT stitching, which makes the "sequence stays positioned at
// the start of the current window" invariant load-bearing: the slow path indexes into it
// with currentConsumed, and that has to hold after a stitch and after a skip that
// overshot the window. Over-long destinations throw InvalidOperationException, the same
// contract-bug guard as Advance (the read layer validates payload lengths at the header).
public class ReadBufferCopyToTest
{
    sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory) => Memory = memory;

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }

    static ReadOnlySequence<byte> Split(byte[] data, int chunkSize)
    {
        var first = new Segment(data.AsMemory(0, chunkSize));
        var last = first;
        for (var i = chunkSize; i < data.Length; i += chunkSize)
        {
            last = last.Append(data.AsMemory(i, Math.Min(chunkSize, data.Length - i)));
        }
        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    static byte[] Ramp(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)i;
        return data;
    }

    // skip then copy, asserting both the bytes and that CopyTo consumed nothing
    static void AssertCopy<TReadBuffer>(ref TReadBuffer buffer, byte[] data, int skip, int count)
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        buffer.Advance(skip);
        var destination = new byte[count];
        buffer.CopyTo(destination);
        Assert.Equal(data.AsSpan(skip, count).ToArray(), destination);
        Assert.Equal(skip, buffer.BytesConsumed);
    }

    [Theory]
    [InlineData(0, 0)]    // vacuous, and at a segment start
    [InlineData(0, 3)]    // inside the first segment
    [InlineData(3, 1)]
    [InlineData(3, 10)]   // straddles: the segment size below is 5
    [InlineData(0, 64)]   // the whole data, every seam
    [InlineData(60, 4)]   // to the very end
    [InlineData(64, 0)]   // vacuous at exhaustion
    public void CopyTo_ContiguousAndStraddling_OnEveryReadBuffer(int skip, int count)
    {
        var data = Ramp(64);

        {
            var buffer = new ReadOnlySpanReadBuffer(data);
            AssertCopy(ref buffer, data, skip, count);
        }

        unsafe
        {
            fixed (byte* p = data)
            {
                var buffer = new CompatibleReadOnlySpanReadBuffer(p, data.Length);
                AssertCopy(ref buffer, data, skip, count);
            }
        }

        {
            var buffer = new ReadOnlySequenceReadBuffer(Split(data, 5));
            try { AssertCopy(ref buffer, data, skip, count); }
            finally { buffer.Dispose(); }
        }

        {
            var buffer = new ReadOnlySequenceReadBuffer(data.AsSpan());
            try { AssertCopy(ref buffer, data, skip, count); }
            finally { buffer.Dispose(); }
        }

        {
            var buffer = new CompatibleReadOnlySequenceReadBuffer(Split(data, 5));
            try { AssertCopy(ref buffer, data, skip, count); }
            finally { buffer.Dispose(); }
        }
    }

    [Fact]
    public void CopyTo_PastTheData_Throws_OnEveryReadBuffer()
    {
        var data = Ramp(16);
        var tooLong = new byte[17];

        Assert.Throws<InvalidOperationException>(() =>
        {
            var buffer = new ReadOnlySpanReadBuffer(data);
            buffer.CopyTo(tooLong);
        });

        var unsafeThrown = false;
        unsafe
        {
            fixed (byte* p = data)
            {
                try
                {
                    var buffer = new CompatibleReadOnlySpanReadBuffer(p, data.Length);
                    buffer.CopyTo(tooLong);
                }
                catch (InvalidOperationException) { unsafeThrown = true; }
            }
        }
        Assert.True(unsafeThrown, "CompatibleReadOnlySpanReadBuffer");

        Assert.Throws<InvalidOperationException>(() =>
        {
            var buffer = new ReadOnlySequenceReadBuffer(Split(data, 5));
            try { buffer.CopyTo(tooLong); }
            finally { buffer.Dispose(); }
        });

        Assert.Throws<InvalidOperationException>(() =>
        {
            var buffer = new ReadOnlySequenceReadBuffer(data.AsSpan());
            try { buffer.CopyTo(tooLong); }
            finally { buffer.Dispose(); }
        });

        Assert.Throws<InvalidOperationException>(() =>
        {
            var buffer = new CompatibleReadOnlySequenceReadBuffer(Split(data, 5));
            try { buffer.CopyTo(tooLong); }
            finally { buffer.Dispose(); }
        });

        // the bound follows the cursor, not just the data length
        Assert.Throws<InvalidOperationException>(() =>
        {
            var buffer = new ReadOnlySequenceReadBuffer(Split(data, 5));
            try
            {
                buffer.Advance(10);
                buffer.CopyTo(new byte[7]); // only 6 remain
            }
            finally { buffer.Dispose(); }
        });
    }

    [Fact]
    public void SequenceBuffer_CopyToAfterSkipOvershoot()
    {
        // Advance past the current WINDOW is legal (skip semantics), which leaves
        // currentConsumed > currentSpan.Length and a NEGATIVE window remainder. That has
        // to fall through to the slow path rather than read the stale window.
        var data = Ramp(64);
        var buffer = new ReadOnlySequenceReadBuffer(Split(data, 5));
        try
        {
            buffer.Advance(12); // the window is only 5 bytes
            var destination = new byte[8];
            buffer.CopyTo(destination);
            Assert.Equal(data.AsSpan(12, 8).ToArray(), destination);
            Assert.Equal(12, buffer.BytesConsumed);
        }
        finally { buffer.Dispose(); }
    }

    [Fact]
    public void SequenceBuffer_CopyToFromStitchedWindow()
    {
        // TryGetSpan repoints the window at the pooled stitch buffer without moving the
        // sequence, so copies both inside and beyond that window must still land on the
        // right bytes.
        var data = Ramp(64);
        var buffer = new ReadOnlySequenceReadBuffer(Split(data, 5));
        try
        {
            Assert.True(buffer.TryGetSpan(8, out _)); // stitches 8 bytes over the 5-byte segments

            var inside = new byte[4];
            buffer.CopyTo(inside); // served from the stitched window
            Assert.Equal(data.AsSpan(0, 4).ToArray(), inside);

            var beyond = new byte[20];
            buffer.CopyTo(beyond); // outgrows the stitched window, back to the segments
            Assert.Equal(data.AsSpan(0, 20).ToArray(), beyond);

            buffer.Advance(3);
            var offset = new byte[20];
            buffer.CopyTo(offset); // cursor inside the stitched window, payload beyond it
            Assert.Equal(data.AsSpan(3, 20).ToArray(), offset);
        }
        finally { buffer.Dispose(); }
    }

    // CopyTo + Advance with no window request in between, the shape of reading fixed-size elements one after
    // another. Advance never commits, so the CopyTo slow path has to: otherwise the sequence stays parked at the
    // first window and every copy re-walks all the segments consumed so far (quadratic in the segment count).
    static void CopyAdvanceLoop<TReadBuffer>(ref TReadBuffer buffer, byte[] data, int elementSize)
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        var destination = new byte[elementSize];
        var position = 0;
        while (buffer.BytesRemaining >= elementSize)
        {
            buffer.CopyTo(destination);
            Assert.True(data.AsSpan(position, elementSize).SequenceEqual(destination), $"element at {position}");
            buffer.Advance(elementSize);
            position += elementSize;
            Assert.Equal(position, buffer.BytesConsumed);
        }
        Assert.Equal(data.Length % elementSize, buffer.BytesRemaining);
    }

    [Fact]
    public void SequenceBuffers_CopyToAdvanceLoop_StaysLinearInTheSegmentCount()
    {
        // 200,000 segments of 16 bytes, read as 24-byte elements so every copy straddles a seam.
        // Linear this takes a few milliseconds; re-walking from the first window took well over ten seconds,
        // so the generous bound separates the two on any machine without being a timing-sensitive assertion.
        var data = Ramp(200_000 * 16);
        var sequence = Split(data, 16);
        var limit = TimeSpan.FromSeconds(3);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var buffer = new ReadOnlySequenceReadBuffer(sequence);
        try { CopyAdvanceLoop(ref buffer, data, 24); }
        finally { buffer.Dispose(); }
        Assert.True(stopwatch.Elapsed < limit, $"ReadOnlySequenceReadBuffer took {stopwatch.Elapsed}");

        stopwatch.Restart();
        var compatible = new CompatibleReadOnlySequenceReadBuffer(sequence);
        try { CopyAdvanceLoop(ref compatible, data, 24); }
        finally { compatible.Dispose(); }
        Assert.True(stopwatch.Elapsed < limit, $"CompatibleReadOnlySequenceReadBuffer took {stopwatch.Elapsed}");
    }

    [Fact]
    public void SequenceBuffers_CopyToThatCommits_KeepsAHeldStitchedSpanValid()
    {
        // The CopyTo slow path commits once the window is used up. The interface promises that CopyTo does not
        // invalidate a held span, so the commit must leave the stitch storage (scratch, or the rented temp) alone.
        var data = Ramp(64);

        {
            var buffer = new ReadOnlySequenceReadBuffer(Split(data, 5), stackalloc byte[16]);
            try
            {
                Assert.True(buffer.TryGetSpan(8, out var held)); // stitched into scratch
                buffer.Advance(8); // the stitched window is used up

                var destination = new byte[20];
                buffer.CopyTo(destination); // slow path, commits
                Assert.Equal(data.AsSpan(8, 20).ToArray(), destination);
                Assert.Equal(data.AsSpan(0, 8).ToArray(), held.Slice(0, 8).ToArray());
                Assert.Equal(8, buffer.BytesConsumed);

                // the window continues at the cursor
                Assert.True(data.AsSpan(8).StartsWith(buffer.GetUnreadSpan()));
                Assert.False(buffer.GetUnreadSpan().IsEmpty);
            }
            finally { buffer.Dispose(); }
        }

        {
            var buffer = new CompatibleReadOnlySequenceReadBuffer(Split(data, 5));
            try
            {
                Assert.True(buffer.TryGetSpan(8, out var held)); // stitched into the rented temp
                buffer.Advance(8);

                var destination = new byte[20];
                buffer.CopyTo(destination);
                Assert.Equal(data.AsSpan(8, 20).ToArray(), destination);
                Assert.Equal(data.AsSpan(0, 8).ToArray(), held.Slice(0, 8).ToArray());
                Assert.Equal(8, buffer.BytesConsumed);

                Assert.True(data.AsSpan(8).StartsWith(buffer.GetUnreadSpan()));
                Assert.False(buffer.GetUnreadSpan().IsEmpty);
            }
            finally { buffer.Dispose(); }
        }
    }
}
