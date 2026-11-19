using System.Buffers;

namespace SerializerFoundation.Tests;

// The segment table of the pooled-list write buffers carries a generation that advances whenever the table
// goes back to the pool. Whatever still holds the table afterwards must notice instead of using it:
// a BufferSegments view kept past Dispose, and a buffer that was copied and disposed through the other copy.
// Both used to read, or return, whatever the table's next owner had put in it: another message.
public class PooledStorageGenerationTest
{
    static void Fill(ref ArrayPoolListWriteBuffer buffer, byte value, int count)
    {
        buffer.GetSpan(count).Slice(0, count).Fill(value);
        buffer.Advance(count);
    }

    static void Fill(ref CompatibleArrayPoolListWriteBuffer buffer, byte value, int count)
    {
        buffer.GetSpan(count).Slice(0, count).Fill(value);
        buffer.Advance(count);
    }

    // a ref struct cannot be captured by Assert.Throws, so the calls under test are wrapped by hand
    static bool NextThrowsDisposed(ref BufferSegments segments)
    {
        try
        {
            segments.TryGetNext(out _);
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    [Fact]
    public void ViewAfterDispose_ThrowsForPooledSegments()
    {
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[8]);
        Fill(ref buffer, 0x11, 4);     // stays in the scratch span
        Fill(ref buffer, 0x22, 1000);  // spills into a pooled segment
        var view = buffer.GetWrittenSegments();
        buffer.Dispose();

        // the scratch span is the caller's own memory and is still served
        Assert.True(view.TryGetNext(out var scratch));
        Assert.Equal(4, scratch.Length);
        // the pooled segment went back to the pool with the buffer
        Assert.True(NextThrowsDisposed(ref view));
    }

    [Fact]
    public void ViewAfterDispose_DoesNotReadTheNextMessage()
    {
        var first = new ArrayPoolListWriteBuffer(stackalloc byte[4]);
        Fill(ref first, 0x41, 1000);
        var stale = first.GetWrittenSegments();
        first.Dispose();

        // another message takes the table and the array back out of the pool
        var second = new ArrayPoolListWriteBuffer(stackalloc byte[4]);
        try
        {
            Fill(ref second, 0x42, 1000);

            stale.Reset();
            Assert.True(NextThrowsDisposed(ref stale));

            Assert.All(second.ToArray(), static b => Assert.Equal(0x42, b));
            var own = second.GetWrittenSegments();
            Assert.True(own.TryGetNext(out var segment));
            Assert.Equal(1000, segment.Length);
        }
        finally
        {
            second.Dispose();
        }
    }

    [Fact]
    public void Compatible_ViewAfterDispose_DoesNotReadTheNextMessage()
    {
        var first = new CompatibleArrayPoolListWriteBuffer();
        Fill(ref first, 0x41, 1000);
        var stale = first.GetWrittenSegments();
        first.Dispose();
        Assert.True(NextThrowsDisposed(ref stale));

        var second = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            Fill(ref second, 0x42, 1000);

            stale.Reset();
            Assert.True(NextThrowsDisposed(ref stale));
            Assert.All(second.ToArray(), static b => Assert.Equal(0x42, b));
        }
        finally
        {
            second.Dispose();
        }
    }

    [Fact]
    public void ViewKeptAcrossLaterWrites_StillReadsItsSnapshot()
    {
        // writing does not return the table, so the generation does not move: only Dispose ends a view
        var buffer = new ArrayPoolListWriteBuffer(stackalloc byte[4]);
        try
        {
            Fill(ref buffer, 0x41, 1000);
            var view = buffer.GetWrittenSegments();
            Fill(ref buffer, 0x42, 100_000); // a second pooled segment

            var total = 0;
            while (view.TryGetNext(out var segment))
            {
                Assert.All(segment.ToArray(), static b => Assert.Equal(0x41, b));
                total += segment.Length;
            }
            Assert.Equal(1000, total);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Fact]
    public void CopiedBufferDisposedTwice_DoesNotReturnTheTableTwice()
    {
        var original = new ArrayPoolListWriteBuffer(stackalloc byte[8]);
        Fill(ref original, 0x41, 1000);
#pragma warning disable SF002 // the copy is the misuse under test
        var copy = original;
#pragma warning restore SF002
        copy.Dispose();
        original.Dispose(); // the table has moved on: nothing is returned again

        // a table returned twice is handed to two renters; the second one overwrites the first one's segments
        var x = new ArrayPoolListWriteBuffer(stackalloc byte[8]);
        var y = new ArrayPoolListWriteBuffer(stackalloc byte[8]);
        try
        {
            Fill(ref x, (byte)'X', 2000);
            Fill(ref y, (byte)'Y', 2000);
            Assert.All(x.ToArray(), static b => Assert.Equal((byte)'X', b));
            Assert.All(y.ToArray(), static b => Assert.Equal((byte)'Y', b));
        }
        finally
        {
            x.Dispose();
            y.Dispose();
        }
    }

    [Fact]
    public void Compatible_CopiedBufferDisposedTwice_DoesNotReturnTheTableTwice()
    {
        var original = new CompatibleArrayPoolListWriteBuffer();
        Fill(ref original, 0x41, 1000);
#pragma warning disable SF002 // the copy is the misuse under test
        var copy = original;
#pragma warning restore SF002
        copy.Dispose();
        original.Dispose();

        var x = new CompatibleArrayPoolListWriteBuffer();
        var y = new CompatibleArrayPoolListWriteBuffer();
        try
        {
            Fill(ref x, (byte)'X', 2000);
            Fill(ref y, (byte)'Y', 2000);
            Assert.All(x.ToArray(), static b => Assert.Equal((byte)'X', b));
            Assert.All(y.ToArray(), static b => Assert.Equal((byte)'Y', b));
        }
        finally
        {
            x.Dispose();
            y.Dispose();
        }
    }

    [Fact]
    public void BufferWhoseCopyWasDisposed_ThrowsWhenItReachesForTheTable()
    {
        var original = new ArrayPoolListWriteBuffer(stackalloc byte[8]);
        Fill(ref original, 0x41, 1000);
#pragma warning disable SF002 // the copy is the misuse under test
        var copy = original;
#pragma warning restore SF002
        copy.Dispose();

        // every member that reads or edits the table reports the dead buffer rather than touching it
        var threw = 0;
        try { original.ToArray(); } catch (ObjectDisposedException) { threw++; }
        try { original.WriteTo(new byte[1000]); } catch (ObjectDisposedException) { threw++; }
        try { original.GetWrittenSegments(); } catch (ObjectDisposedException) { threw++; }
        try { original.GetSpan(1_000_000); } catch (ObjectDisposedException) { threw++; } // needs a new segment
        Assert.Equal(4, threw);

        original.Dispose(); // and Dispose stays silent
    }

    [Fact]
    public void Compatible_BufferWhoseCopyWasDisposed_ThrowsWhenItReachesForTheTable()
    {
        var original = new CompatibleArrayPoolListWriteBuffer();
        Fill(ref original, 0x41, 1000);
#pragma warning disable SF002 // the copy is the misuse under test
        var copy = original;
#pragma warning restore SF002
        copy.Dispose();

        var threw = 0;
        try { original.ToArray(); } catch (ObjectDisposedException) { threw++; }
        try { original.WriteTo(new byte[1000]); } catch (ObjectDisposedException) { threw++; }
        try { original.GetWrittenSegments(); } catch (ObjectDisposedException) { threw++; }
        try { original.GetSpan(1_000_000); } catch (ObjectDisposedException) { threw++; }
        Assert.Equal(4, threw);

        original.Dispose();
    }

    [Fact]
    public void FullTable_KeepsItsGenerationThroughTheClear()
    {
        // the generation shares the last slot with the sixteenth array reference, and returning a full table
        // clears that slot; a real buffer only gets there after gigabytes, so the table is driven directly
        var table = PooledSegments.Rent(out var generation);
        for (var i = 0; i < PooledSegments.MaxCount; i++)
        {
            table[i].Array = ArrayPool<byte>.Shared.Rent(16);
            if (i < PooledSegments.MaxCount - 1)
            {
                table[i].Length = 1; // a finished length is only ever recorded below the last slot
            }
        }
        Assert.True(PooledSegments.IsCurrent(table, generation));

        PooledSegments.Return(table, PooledSegments.MaxCount, generation);
        Assert.False(PooledSegments.IsCurrent(table, generation));

        // a second return by a stale holder is ignored: the pool must not hand the same table out twice
        PooledSegments.Return(table, PooledSegments.MaxCount, generation);
        var first = PooledSegments.Rent(out var firstGeneration);
        var second = PooledSegments.Rent(out var secondGeneration);
        try
        {
            Assert.NotSame(first, second);
            if (ReferenceEquals(first, table))
            {
                Assert.Equal(generation + 1, firstGeneration);
            }
        }
        finally
        {
            PooledSegments.Return(first, 0, firstGeneration);
            PooledSegments.Return(second, 0, secondGeneration);
        }
    }
}
