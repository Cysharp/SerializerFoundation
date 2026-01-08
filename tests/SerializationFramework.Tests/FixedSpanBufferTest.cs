using SerializationFramework.Tests.Mini;

namespace SerializationFramework.Tests;

public static partial class MiniSerializer
{
    public static byte[] SerializeFixedSpanBuffer<T>(T value, IMiniSerializerProvider serializerProvider)
    {
        var serializer = serializerProvider.GetMiniSerializer<FixedSpanBuffer, ReadOnlySpanBuffer, T>();

        Span<byte> buffer = new byte[65536];
        var writeBuffer = new FixedSpanBuffer(buffer);

        serializer.Serialize(ref writeBuffer, value, default);

        writeBuffer.Flush();

        return buffer.Slice(0, (int)writeBuffer.WrittenCount).ToArray();
    }

    public static T DeserializeFixedSpanBuffer<T>(byte[] data, IMiniSerializerProvider serializerProvider)
    {
        var serializer = serializerProvider.GetMiniSerializer<FixedSpanBuffer, ReadOnlySpanBuffer, T>();
        var readBuffer = new ReadOnlySpanBuffer(data);
        return serializer.Deserialize(ref readBuffer, default);
    }
}

public class FixedSpanBufferTest
{
    [Test]
    public async Task FullUse()
    {
        var bytes = new byte[100];
        var buffer = new FixedSpanBuffer(bytes);

        var span = buffer.GetSpan(1);
        span.Length.IsEqualTo(100);

        span = buffer.GetSpan(10);
        span.Length.IsEqualTo(100);

        buffer.Advance(8);
        buffer.WrittenCount.IsEqualTo(8);

        span = buffer.GetSpan(20);
        span.Length.IsEqualTo(92);

        span = buffer.GetSpan(92);
        span.Length.IsEqualTo(92);
        buffer.Advance(92);
        buffer.WrittenCount.IsEqualTo(100);

        buffer.Flush(); // no-op

        try
        {
            span = buffer.GetSpan(1);
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception ex)
        {
            await Assert.That(ex).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    public async Task OverAdvance()
    {
        var bytes = new byte[100];
        var buffer = new FixedSpanBuffer(bytes);

        buffer.Advance(42);
        buffer.WrittenCount.IsEqualTo(42);

        var span = buffer.GetSpan(58); // ok
        span.Length.IsEqualTo(58);

        try
        {
            span = buffer.GetSpan(59); // ng
            Assert.Fail("Expected exception was not thrown.");
        }
        catch (Exception ex)
        {
            await Assert.That(ex).IsTypeOf<InvalidOperationException>();
        }
    }

    [Test]
    public async Task IntSerializer()
    {
        var expected = 123456789;
        var bytes = MiniSerializer.SerializeFixedSpanBuffer(expected, DefaultMiniSerializerProvider.Instance);
        await Assert.That(bytes).IsEquivalentTo(new byte[] { 21, 205, 91, 7 });
    }

    [Test]
    public async Task ArraySerializer()
    {
        int[] expected = [1, 10, 100, 1000, 10000];
        var bytes = MiniSerializer.SerializeFixedSpanBuffer(expected, DefaultMiniSerializerProvider.Instance);

        await Assert.That(bytes).IsEquivalentTo(new byte[]
        {
            5, 0, 0, 0,   // Length: 5
            1, 0, 0, 0,   // 1
            10, 0, 0, 0,  // 10
            100, 0, 0, 0, // 100
            232, 3, 0, 0, // 1000
            16, 39, 0, 0  // 10000
        });
    }

    [Test]
    public async Task ArraySerializerDeserialize()
    {
        {
            var expected = Enumerable.Range(1, 100).ToArray();
            var bytes = MiniSerializer.SerializeFixedSpanBuffer(expected, DefaultMiniSerializerProvider.Instance);

            var actual = MiniSerializer.DeserializeFixedSpanBuffer<int[]>(bytes, DefaultMiniSerializerProvider.Instance);

            await Assert.That(actual).IsEquivalentTo(expected);
        }
        {
            var expected = Enumerable.Range(1, 1000).ToArray();
            var bytes = MiniSerializer.SerializeFixedSpanBuffer(expected, DefaultMiniSerializerProvider.Instance);

            var actual = MiniSerializer.DeserializeFixedSpanBuffer<int[]>(bytes, DefaultMiniSerializerProvider.Instance);

            await Assert.That(actual).IsEquivalentTo(expected);
        }
    }
}
