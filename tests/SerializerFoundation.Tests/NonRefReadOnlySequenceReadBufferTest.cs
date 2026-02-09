using System.Buffers;

namespace SerializerFoundation.Tests;

public class NonRefReadOnlySequenceReadBufferTest
{
    [Test]
    public void SingleSegment_Basic()
    {
        byte[] data = [1, 2, 3, 4, 5];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var seq = SequenceHelper.CreateMultiSegment(data, 3, 3, 2);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
        try
        {
            var span = buffer.GetSpan(2);
            span[0].IsEqualTo((byte)1);

            buffer.Advance(3);
            buffer.BytesConsumed.IsEqualTo(3);

            span = buffer.GetSpan(1);
            span[0].IsEqualTo((byte)4);

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
        byte[] data = [1, 2, 3, 4];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 2);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
        try
        {
            var span = buffer.GetSpan(4);
            span.Length.IsEqualTo(4);
            span[0].IsEqualTo((byte)1);
            span[1].IsEqualTo((byte)2);
            span[2].IsEqualTo((byte)3);
            span[3].IsEqualTo((byte)4);

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
    public void MultiSegment_TempBuffer_ThenContinueReading()
    {
        byte[] data = [1, 2, 3, 4, 5, 6, 7, 8];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 2, 4);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
        try
        {
            var span = buffer.GetSpan(3);
            span[0].IsEqualTo((byte)1);
            span[1].IsEqualTo((byte)2);
            span[2].IsEqualTo((byte)3);

            buffer.Advance(3);

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
    public void MultiSegment_PartialAdvanceWithinSpan()
    {
        byte[] data = [10, 20, 30, 40, 50];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
        try
        {
            buffer.Advance(1);
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
    public async Task GetSpan_ThrowsWhenInsufficient()
    {
        byte[] data = [1, 2];
        var seq = new ReadOnlySequence<byte>(data);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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
                await Assert.That(ex).IsTypeOf<InvalidOperationException>();
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
        byte[] data = [1, 2, 3];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 1);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
        try
        {
            try
            {
                buffer.GetSpan(4);
                Assert.Fail("Expected exception was not thrown.");
            }
            catch (Exception ex)
            {
                await Assert.That(ex).IsTypeOf<InvalidOperationException>();
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
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);

        buffer.GetSpan(1);
        buffer.Advance(1);

        buffer.Dispose();
        buffer.Dispose(); // Should not throw
    }

    [Test]
    public void MultiSegment_ManySmallSegments()
    {
        byte[] data = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
        var segmentSizes = Enumerable.Repeat(1, 10).ToArray();
        var seq = SequenceHelper.CreateMultiSegment(data, segmentSizes);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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
        byte[] data = [1, 2, 3, 4];
        var seq = SequenceHelper.CreateMultiSegment(data, 2, 2);
        var buffer = new NonRefReadOnlySequenceReadBuffer(in seq);
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

