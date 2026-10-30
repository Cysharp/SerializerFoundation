using System.Buffers;
using SerializerFoundation.CodeAnalysis;

namespace SerializerFoundation.Tests;

// ReadOnlySequenceReadBuffer over a single span, so a reader holding just this type takes both inputs.
// The buffer keeps an empty sequence and the span as its one window; the slow paths read the empty
// sequence as "the window is all remaining data" and commit into the window instead of the sequence.
// A failed demand must not lose that window: there is no sequence to rebuild it from.
public class ReadOnlySequenceReadBufferSpanTest
{
    static byte[] Ramp(int length)
    {
        var data = new byte[length];
        for (var i = 0; i < length; i++) data[i] = (byte)i;
        return data;
    }

    [Fact]
    public void WholeSpanIsOneWindow()
    {
        var data = Ramp(16);
        var buffer = new ReadOnlySequenceReadBuffer(data.AsSpan());
        try
        {
            Assert.Equal(data, buffer.GetUnreadSpan().ToArray());
            Assert.True(buffer.TryGetSpan(16, out var span));
            Assert.Equal(16, span.Length);
            Assert.Equal(16, buffer.BytesRemaining);
        }
        finally { buffer.Dispose(); }
    }

    [Fact]
    public void SequentialReads_FollowTheCursor()
    {
        var data = Ramp(16);
        var buffer = new ReadOnlySequenceReadBuffer(data.AsSpan());
        try
        {
            for (var offset = 0; offset < 16; offset += 4)
            {
                Assert.True(buffer.TryGetSpan(4, out var span));
                Assert.Equal(data.AsSpan(offset, 4).ToArray(), span.Slice(0, 4).ToArray());
                buffer.Advance(4);
            }
            Assert.Equal(16, buffer.BytesConsumed);
            Assert.Equal(0, buffer.BytesRemaining);
        }
        finally { buffer.Dispose(); }
    }

    [Fact]
    public void ShortDemand_ReturnsFalse_AndKeepsTheRemainingBytes()
    {
        var data = Ramp(16);
        var buffer = new ReadOnlySequenceReadBuffer(data.AsSpan());
        try
        {
            buffer.Advance(10);
            Assert.False(buffer.TryGetSpan(7, out _)); // only 6 remain; this commits the 10 consumed bytes

            Assert.Equal(10, buffer.BytesConsumed);
            Assert.Equal(6, buffer.BytesRemaining);
            Assert.Equal(data.AsSpan(10).ToArray(), buffer.GetUnreadSpan().ToArray());

            Assert.True(buffer.TryGetSpan(6, out var span));
            Assert.Equal(data.AsSpan(10).ToArray(), span.ToArray());
            buffer.Advance(2);
            var destination = new byte[4];
            buffer.CopyTo(destination);
            Assert.Equal(data.AsSpan(12).ToArray(), destination);
        }
        finally { buffer.Dispose(); }
    }

    [Fact]
    public void Exhausted_ReportsEmpty()
    {
        var data = Ramp(16);
        var buffer = new ReadOnlySequenceReadBuffer(data.AsSpan());
        try
        {
            buffer.Advance(16);
            Assert.True(buffer.GetUnreadSpan().IsEmpty);
            Assert.True(buffer.TryGetSpan(0, out var empty));
            Assert.True(empty.IsEmpty);
            Assert.False(buffer.TryGetSpan(1, out _));
            Assert.Equal(16, buffer.BytesConsumed);
        }
        finally { buffer.Dispose(); }
    }

    [Fact]
    public void EmptySpan_IsExhaustedFromTheStart()
    {
        var buffer = new ReadOnlySequenceReadBuffer(ReadOnlySpan<byte>.Empty);
        try
        {
            Assert.True(buffer.GetUnreadSpan().IsEmpty);
            Assert.False(buffer.TryGetSpan(1, out _));
            Assert.Equal(0, buffer.BytesRemaining);
        }
        finally { buffer.Dispose(); }
    }

    [NonCopyable]
    ref struct SingleFieldReader
    {
        ReadOnlySequenceReadBuffer buffer;

        public SingleFieldReader(ReadOnlySequence<byte> sequence) => buffer = new ReadOnlySequenceReadBuffer(in sequence);

        public SingleFieldReader(ReadOnlySpan<byte> span) => buffer = new ReadOnlySequenceReadBuffer(span);

        public byte ReadByte()
        {
            Assert.True(buffer.TryGetSpan(1, out var span));
            var value = span[0];
            buffer.Advance(1);
            return value;
        }

        public void Dispose() => buffer.Dispose();
    }

    [Fact]
    public void OneReaderType_TakesStackallocAndSequenceInput()
    {
        Span<byte> stack = stackalloc byte[] { 1, 2, 3 };
        var fromSpan = new SingleFieldReader(stack);
        var fromSequence = new SingleFieldReader(new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 }));
        try
        {
            for (byte expected = 1; expected <= 3; expected++)
            {
                Assert.Equal(expected, fromSpan.ReadByte());
                Assert.Equal(expected, fromSequence.ReadByte());
            }
        }
        finally
        {
            fromSpan.Dispose();
            fromSequence.Dispose();
        }
    }
}
