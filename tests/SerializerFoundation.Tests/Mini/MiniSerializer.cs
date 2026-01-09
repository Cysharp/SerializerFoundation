using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace SerializerFoundation.Tests.Mini;

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
            using var builder = new ArrayBuilder<T>();

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

public sealed class StringSerializer<TWriteBuffer, TReadBuffer> : IMiniSerializer<TWriteBuffer, TReadBuffer, string>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
{
    public void Serialize(ref TWriteBuffer buffer, in string value, in SerializationContext serializationContext)
    {
        var str = value.AsSpan();

        var maxByteCount = Encoding.UTF8.GetMaxByteCount(str.Length);
        var dest = buffer.GetSpan(maxByteCount + 4); // 4 bytes for length prefix
        var destHead = dest; // keep the head for length prefix write

        var status = Utf8.FromUtf16(str, dest, out var bytesRead, out var charsWritten);
        if (status != System.Buffers.OperationStatus.Done)
        {
            throw new InvalidOperationException();
        }

        BinaryPrimitives.WriteInt32LittleEndian(destHead, charsWritten);
        buffer.Advance(charsWritten + 4);
    }

    public string Deserialize(ref TReadBuffer buffer, in DeserializationContext deserializationContext)
    {
        // read length
        var span = buffer.GetSpan(4);
        var length = BinaryPrimitives.ReadInt32LittleEndian(span);
        buffer.Advance(4);

        // for security reasons, limit the maximum length to avoid OOM
        if (length < 512) // TODO: make configurable(in DeserializationContext)
        {
            var src = buffer.GetSpan(length)[..length];
            return Encoding.UTF8.GetString(src);
        }
        else
        {
            // write to temporary buffer(segments) and copy to final array
            // requires copy-cost but important for safety.
            var builder = new ArrayBuilder<char>();
            try
            {
                var segment = builder.GetNextSegment();

                var totalWritten = 0;

                var src = buffer.GetSpan(0); // get current ensured buffer


                var isFinalBlock = src.Length + totalWritten >= length;


                var status = Utf8.ToUtf16(src, segment, out var bytesRead, out var bytesWritten, isFinalBlock);

                // TODO: loop...
                return builder.ToString(bytesWritten);

            }
            finally
            {
                builder.Dispose();
            }
        }
    }
}



public static partial class MiniSerializer
{
}
