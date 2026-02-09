using System.Buffers;

namespace SerializerFoundation.Tests;

public class ReadOnlySequenceReadBufferTest
{
    [Test]
    public void SingleSegment_Basic()
    {
        byte[] data = [1, 2, 3, 4, 5];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            var span = buffer.GetSpan(3);
            span[0].IsEqualTo((byte)1);
            span.Length.IsEqualTo(5);

            buffer.Advance(2);
            buffer.BytesConsumed.IsEqualTo(2);
            buffer.BytesRemaining.IsEqualTo(3);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void SingleSegment_GetReference()
    {
        byte[] data = [0xAA, 0xBB, 0xCC];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            ref readonly byte r = ref buffer.GetReference(1);
            r.IsEqualTo((byte)0xAA);

            buffer.Advance(1);
            ref readonly byte r2 = ref buffer.GetReference(1);
            r2.IsEqualTo((byte)0xBB);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void MultiSegment_AdvanceAcrossSegments()
    {
        // [1,2,3] [4,5,6] [7,8]
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var seq = SequenceHelper.CreateMultiSegment(data, 3, 3, 2);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            // Read from first segment
            var span = buffer.GetSpan(2);
            span[0].IsEqualTo((byte)1);

            // Consume entire first segment
            buffer.Advance(3);
            buffer.BytesConsumed.IsEqualTo(3);

            // Should move to next segment
            span = buffer.GetSpan(1);
            span[0].IsEqualTo((byte)4);

            // Consume into third segment
            buffer.Advance(3);
            span = buffer.GetSpan(1);
            span[0].IsEqualTo((byte)7);

            buffer.BytesConsumed.IsEqualTo(6);
            buffer.BytesRemaining.IsEqualTo(2);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void MultiSegment_TempBufferCopy_WhenSizeHintSpansSegments()
    {
        // [1,2] [3,4] — request 4 bytes when first segment only has 2
        byte[] data = [1, 2, 3, 4];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 2);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            // sizeHint=4 but first segment is only 2 bytes → tempBuffer copy
            var span = buffer.GetSpan(4);
            span.Length.IsEqualTo(4);
            span[0].IsEqualTo((byte)1);
            span[1].IsEqualTo((byte)2);
            span[2].IsEqualTo((byte)3);
            span[3].IsEqualTo((byte)4);

            // Advance through tempBuffer should trigger AdvanceSlow
            buffer.Advance(4);
            buffer.BytesConsumed.IsEqualTo(4);
            buffer.BytesRemaining.IsEqualTo(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void MultiSegment_PartialAdvanceWithinSpan()
    {
        byte[] data = [10, 20, 30, 40, 50];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            buffer.Advance(1); // fast path: bytesConsumed < currentSpan.Length
            var span = buffer.GetSpan(1);
            span[0].IsEqualTo((byte)20);

            buffer.Advance(1);
            span = buffer.GetSpan(1);
            span[0].IsEqualTo((byte)30);

            buffer.BytesConsumed.IsEqualTo(2);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void MultiSegment_TempBuffer_ThenContinueReading()
    {
        // [1,2] [3,4] [5,6,7,8]
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 2, 4);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            // Force tempBuffer: request 3 bytes but first segment is 2
            var span = buffer.GetSpan(3);
            span[0].IsEqualTo((byte)1);
            span[1].IsEqualTo((byte)2);
            span[2].IsEqualTo((byte)3);

            buffer.Advance(3);

            // Continue reading — tempBuffer returned, move to remaining data
            span = buffer.GetSpan(1);
            span[0].IsEqualTo((byte)4);

            buffer.Advance(1);
            span = buffer.GetSpan(4);
            span[0].IsEqualTo((byte)5);

            buffer.BytesConsumed.IsEqualTo(4);
            buffer.BytesRemaining.IsEqualTo(4);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task GetSpan_ThrowsWhenInsufficient()
    {
        byte[] data = [1, 2];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            buffer.Advance(2);
            try
            {
                buffer.GetSpan(1);
                Assert.Fail("Expected exception was not thrown.");
            }
            catch (Exception ex)
            {
                ex.IsTypeOf<Exception, InvalidOperationException>();
            }
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public async Task GetSpan_ThrowsWhenSizeHintExceedsTotal()
    {
        // [1,2] [3] — total 3 bytes, request 4
        byte[] data = [1, 2, 3];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 1);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            try
            {
                buffer.GetSpan(4);
                Assert.Fail("Expected exception was not thrown.");
            }
            catch (Exception ex)
            {
                ex.IsTypeOf<Exception, InvalidOperationException>();
            }
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void BytesConsumed_InitiallyZero()
    {
        byte[] data = [1, 2, 3];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            buffer.BytesConsumed.IsEqualTo(0);
            buffer.BytesRemaining.IsEqualTo(3);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void GetSpan_ZeroSizeHint()
    {
        byte[] data = [1, 2, 3];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            var span = buffer.GetSpan(0);
            span.Length.IsGreaterThan(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        byte[] data = [1, 2, 3];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);

        buffer.GetSpan(3);
        buffer.Advance(1);

        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }

    [Test]
    public void MultiSegment_ManySmallSegments()
    {
        // 10 segments of 1 byte each
        byte[] data = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        var segmentSizes = Enumerable.Repeat(1, 10).ToArray();
        var seq = SequenceHelper.CreateMultiSegment(data, segmentSizes);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            for (int i = 0; i < 10; i++)
            {
                var span = buffer.GetSpan(1);
                span[0].IsEqualTo((byte)i);
                buffer.Advance(1);
            }

            buffer.BytesConsumed.IsEqualTo(10);
            buffer.BytesRemaining.IsEqualTo(0);
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test]
    public void MultiSegment_TempBuffer_GetReference()
    {
        // [1,2] [3,4] — request 3 via GetReference
        byte[] data = [1, 2, 3, 4];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 2);
        var buffer = new ReadOnlySequenceReadBuffer(in seq);
        try
        {
            ref readonly byte r = ref buffer.GetReference(3);
            r.IsEqualTo((byte)1);

            buffer.Advance(3);
            ref readonly byte r2 = ref buffer.GetReference(1);
            r2.IsEqualTo((byte)4);
        }
        finally
        {
            buffer.Dispose();
        }
    }
}

