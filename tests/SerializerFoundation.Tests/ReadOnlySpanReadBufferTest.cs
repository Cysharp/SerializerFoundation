namespace SerializerFoundation.Tests;

public class ReadOnlySpanReadBufferTest
{
    [Test]
    public void GetSpan_Basic()
    {
        ReadOnlySpan<byte> data = [1, 2, 3, 4, 5];
        var buffer = new ReadOnlySpanReadBuffer(data);

        var span = buffer.GetSpan(3);
        span.Length.IsEqualTo(5);
        span[0].IsEqualTo((byte)1);
        span[1].IsEqualTo((byte)2);
        span[2].IsEqualTo((byte)3);
    }

    [Test]
    public void GetReference_Basic()
    {
        ReadOnlySpan<byte> data = [0xAB, 0xCD, 0xEF];
        var buffer = new ReadOnlySpanReadBuffer(data);

        ref readonly byte reference = ref buffer.GetReference(1);
        reference.IsEqualTo((byte)0xAB);
    }

    [Test]
    public void Advance_UpdatesState()
    {
        ReadOnlySpan<byte> data = [1, 2, 3, 4, 5];
        var buffer = new ReadOnlySpanReadBuffer(data);

        buffer.Advance(2);
        buffer.BytesConsumed.IsEqualTo(2);
        buffer.BytesRemaining.IsEqualTo(3);

        var span = buffer.GetSpan(3);
        span[0].IsEqualTo((byte)3);
    }

    [Test]
    public void Advance_ConsumeAll()
    {
        ReadOnlySpan<byte> data = [1, 2, 3];
        var buffer = new ReadOnlySpanReadBuffer(data);

        buffer.Advance(3);
        buffer.BytesConsumed.IsEqualTo(3);
        buffer.BytesRemaining.IsEqualTo(0);
    }

    [Test]
    public void BytesConsumed_InitiallyZero()
    {
        ReadOnlySpan<byte> data = [1, 2, 3];
        var buffer = new ReadOnlySpanReadBuffer(data);

        buffer.BytesConsumed.IsEqualTo(0);
        buffer.BytesRemaining.IsEqualTo(3);
    }

    [Test]
    public async Task GetSpan_ThrowsWhenInsufficient()
    {
        ReadOnlySpan<byte> data = [1, 2, 3];
        var buffer = new ReadOnlySpanReadBuffer(data);
        buffer.Advance(3);

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

    [Test]
    public async Task GetReference_ThrowsWhenInsufficient()
    {
        ReadOnlySpan<byte> data = [1, 2, 3];
        var buffer = new ReadOnlySpanReadBuffer(data);
        buffer.Advance(2);

        try
        {
            buffer.GetReference(2);
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception ex)
        {
            await Assert.That(ex).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    public async Task GetSpan_ThrowsOnEmptyBuffer()
    {
        var buffer = new ReadOnlySpanReadBuffer(ReadOnlySpan<byte>.Empty);

        try
        {
            buffer.GetSpan();
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception ex)
        {
            await Assert.That(ex).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    public void GetSpan_ZeroSizeHint_ReturnsRemaining()
    {
        ReadOnlySpan<byte> data = [1, 2, 3, 4, 5];
        var buffer = new ReadOnlySpanReadBuffer(data);
        buffer.Advance(2);

        var span = buffer.GetSpan(0);
        span.Length.IsEqualTo(3);
    }

    [Test]
    public void Peek_SingleByte()
    {
        ReadOnlySpan<byte> data = [0xAA, 0xBB, 0xCC];
        var buffer = new ReadOnlySpanReadBuffer(data);

        var b = buffer.Peek();
        b.IsEqualTo((byte)0xAA);

        // Peek should not advance
        buffer.BytesConsumed.IsEqualTo(0);
    }

    [Test]
    public void Peek_MultipleBytes()
    {
        ReadOnlySpan<byte> data = [1, 2, 3, 4, 5];
        var buffer = new ReadOnlySpanReadBuffer(data);

        var peeked = buffer.Peek(3);
        peeked.Length.IsEqualTo(5); // GetSpan returns full remaining
        peeked[0].IsEqualTo((byte)1);

        buffer.BytesConsumed.IsEqualTo(0);
    }

    [Test]
    public void MultipleAdvances()
    {
        ReadOnlySpan<byte> data = [10, 20, 30, 40, 50];
        var buffer = new ReadOnlySpanReadBuffer(data);

        buffer.Advance(1);
        buffer.GetReference(1).IsEqualTo((byte)20);

        buffer.Advance(2);
        buffer.GetReference(1).IsEqualTo((byte)40);

        buffer.BytesConsumed.IsEqualTo(3);
        buffer.BytesRemaining.IsEqualTo(2);
    }
}

