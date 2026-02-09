namespace SerializerFoundation.Tests;

public class PointerReadBufferTest
{
    [Test]
    public void GetSpan_Basic()
    {
        byte[] data = [1, 2, 3, 4, 5];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);

                var span = buffer.GetSpan(3);
                span.Length.IsEqualTo(5);
                span[0].IsEqualTo((byte)1);
            }
        }
    }

    [Test]
    public void GetReference_Basic()
    {
        byte[] data = [0xAB, 0xCD];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);

                ref readonly byte reference = ref buffer.GetReference(1);
                reference.IsEqualTo((byte)0xAB);
            }
        }
    }

    [Test]
    public void Advance_UpdatesState()
    {
        byte[] data = [1, 2, 3, 4, 5];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);

                buffer.Advance(2);
                buffer.BytesConsumed.IsEqualTo(2);
                buffer.BytesRemaining.IsEqualTo(3);

                var span = buffer.GetSpan(1);
                span[0].IsEqualTo((byte)3);
            }
        }
    }

    [Test]
    public void BytesConsumed_InitiallyZero()
    {
        byte[] data = [1, 2, 3];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);
                buffer.BytesConsumed.IsEqualTo(0);
                buffer.BytesRemaining.IsEqualTo(3);
            }
        }
    }

    [Test]
    public void GetSpan_ThrowsWhenInsufficient()
    {
        byte[] data = [1, 2];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);
                buffer.Advance(2);

                var threw = false;
                try
                {
                    buffer.GetSpan(1);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                threw.IsEqualTo(true);
            }
        }
    }

    [Test]
    public void GetReference_ThrowsWhenInsufficient()
    {
        byte[] data = [1];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);
                buffer.Advance(1);

                var threw = false;
                try
                {
                    buffer.GetReference(1);
                }
                catch (InvalidOperationException)
                {
                    threw = true;
                }
                threw.IsEqualTo(true);
            }
        }
    }

    [Test]
    public void GetSpan_ZeroSizeHint()
    {
        byte[] data = [1, 2, 3];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);
                buffer.Advance(1);

                var span = buffer.GetSpan(0);
                span.Length.IsEqualTo(2);
                span[0].IsEqualTo((byte)2);
            }
        }
    }

    [Test]
    public void MultipleAdvances()
    {
        byte[] data = [10, 20, 30, 40, 50];
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var buffer = new PointerReadBuffer(ptr, data.Length);

                buffer.Advance(1);
                buffer.GetReference(1).IsEqualTo((byte)20);

                buffer.Advance(2);
                buffer.GetReference(1).IsEqualTo((byte)40);

                buffer.BytesConsumed.IsEqualTo(3);
                buffer.BytesRemaining.IsEqualTo(2);
            }
        }
    }
}

