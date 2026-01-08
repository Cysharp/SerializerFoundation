using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SerializationFramework.Tests.Mini;

public interface IMiniSerializer
{
}


public readonly record struct SerializationContext
{

}

public readonly record struct DeserializationContext
{
}




public interface IMiniSerializer<TWriteBuffer, TReadBuffer, T> : IMiniSerializer
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    void Serialize(ref TWriteBuffer buffer, in T value, in SerializationContext serializationContext);
    T Deserialize(ref TReadBuffer buffer, in DeserializationContext deserializationContext);
}

public static class MiniSerializerExtensions
{
    extension<TWriteBuffer, TReadBuffer, T>(IMiniSerializer<TWriteBuffer, TReadBuffer, T> serializer)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        public bool IsRegistered()
        {
            return serializer != NotRegisteredSerializer<TWriteBuffer, TReadBuffer, T>.Instance;
        }
    }
}

public interface IMiniSerializerProvider
{
    IMiniSerializer<TWriteBuffer, TReadBuffer, T> GetMiniSerializer<TWriteBuffer, TReadBuffer, T>()
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct;
}

public sealed class DefaultMiniSerializerProvider : IMiniSerializerProvider
{
    public static readonly DefaultMiniSerializerProvider Instance = new();

    DefaultMiniSerializerProvider()
    {
    }

    public IMiniSerializer<TWriteBuffer, TReadBuffer, T> GetMiniSerializer<TWriteBuffer, TReadBuffer, T>()
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        return Cache<TWriteBuffer, TReadBuffer, T>.Instance;
    }

    static class Cache<TWriteBuffer, TReadBuffer, T>
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        public static IMiniSerializer<TWriteBuffer, TReadBuffer, T> Instance;

        static Cache()
        {
            IMiniSerializer? serializer = null;
            if (typeof(T) == typeof(int))
            {
                serializer = IntMiniSerializer<TWriteBuffer, TReadBuffer>.Default;
            }
            else if (typeof(T) == typeof(int[]))
            {
                serializer = new ArrayMiniSerializer<TWriteBuffer, TReadBuffer, IntMiniSerializer<TWriteBuffer, TReadBuffer>, int>(IntMiniSerializer<TWriteBuffer, TReadBuffer>.Default);
            }

            if (serializer != null)
            {
                Instance = (IMiniSerializer<TWriteBuffer, TReadBuffer, T>)serializer;
            }
            else
            {
                Instance = NotRegisteredSerializer<TWriteBuffer, TReadBuffer, T>.Instance;
            }
        }
    }
}

public sealed class NotRegisteredSerializer<TWriteBuffer, TReadBuffer, T> : IMiniSerializer<TWriteBuffer, TReadBuffer, T>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    readonly string message = $"Serializer is not registered. Type: {typeof(T).FullName}";

    public static readonly NotRegisteredSerializer<TWriteBuffer, TReadBuffer, T> Instance = new();

    NotRegisteredSerializer()
    {
    }

    public void Serialize(ref TWriteBuffer buffer, in T value, in SerializationContext serializationContext)
    {
        throw new InvalidOperationException(message);
    }

    public T Deserialize(ref TReadBuffer buffer, in DeserializationContext deserializationContext)
    {
        throw new InvalidOperationException(message);
    }
}

public sealed class IntMiniSerializer<TWriteBuffer, TReadBuffer> : IMiniSerializer<TWriteBuffer, TReadBuffer, int>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    public static readonly IntMiniSerializer<TWriteBuffer, TReadBuffer> Default = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Serialize(ref TWriteBuffer buffer, in int value, in SerializationContext serializationContext)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        buffer.Advance(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Deserialize(ref TReadBuffer buffer, in DeserializationContext deserializationContext)
    {
        ReadOnlySpan<byte> span = buffer.GetSpan(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(span);
        buffer.Advance(4);
        return value;
    }
}

public sealed class ArrayMiniSerializer<TWriteBuffer, TReadBuffer, TSerializer, T>(TSerializer elementSerializer) : IMiniSerializer<TWriteBuffer, TReadBuffer, T[]>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
    where TSerializer : IMiniSerializer<TWriteBuffer, TReadBuffer, T>
{
    public void Serialize(ref TWriteBuffer buffer, in T[] value, in SerializationContext serializationContext)
    {
        // add length prefix
        var span = buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value.Length);
        buffer.Advance(4);

        var serializer = elementSerializer;
        for (int i = 0; i < value.Length; i++)
        {
            serializer.Serialize(ref buffer, in value[i], serializationContext);
        }
    }

    public T[] Deserialize(ref TReadBuffer buffer, in DeserializationContext deserializationContext)
    {
        // read length
        var span = buffer.GetSpan(4);
        var length = BinaryPrimitives.ReadInt32LittleEndian(span);
        buffer.Advance(4);

        var serializer = elementSerializer;

        // for security reasons, limit the maximum length to avoid OOM
        if (length < 512) // TODO: make configurable(in DeserializationContext)
        {
            var array = GC.AllocateUninitializedArray<T>(length);
            for (int i = 0; i < length; i++)
            {
                array[i] = serializer.Deserialize(ref buffer, deserializationContext);
            }
            return array;
        }
        else
        {
            // write to temporary buffer(segments) and copy to final array
            // requires copy-cost but important for safety.
            using var builder = new SafeArrayBuilder<T>();

            var segment = builder.GetNextSegment();
            var j = 0;
            for (int i = 0; i < length; i++)
            {
                if (segment.Length == j)
                {
                    segment = builder.GetNextSegment();
                    j = 0;
                }

                segment[j++] = serializer.Deserialize(ref buffer, deserializationContext);
            }

            return builder.ToArray(lastSegmentCount: j);
        }
    }
}




public static partial class MiniSerializer
{
}
