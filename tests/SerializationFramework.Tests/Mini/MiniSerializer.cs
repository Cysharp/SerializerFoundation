using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SerializationFramework.Tests.Mini;

public interface IMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
    where TSerializerProvider : IMiniSerializerProvider<TSerializerProvider>
{
    void Serialize(ref TWriteBuffer buffer, in T value);
    T Deserialize(ref TReadBuffer buffer);
}

public static class MiniSerializerExtensions
{
    extension<TWriteBuffer, TReadBuffer, TSerializerProvider, T>(IMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T> serializer)
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct
        where TSerializerProvider : IMiniSerializerProvider<TSerializerProvider>
    {
        public bool IsRegisteredSerializer()
        {
            return serializer is not NotRegisteredSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T>;
        }
    }
}

public interface IMiniSerializerProvider<TSelf>
    where TSelf : IMiniSerializerProvider<TSelf>
{
    static abstract IMiniSerializer<TWriteBuffer, TReadBuffer, TSelf, T> GetMiniSerializer<TWriteBuffer, TReadBuffer, T>()
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct;
}

public sealed class DefaultMiniSerializerProvider : IMiniSerializerProvider<DefaultMiniSerializerProvider>
{
    DefaultMiniSerializerProvider()
    {
    }

    public static IMiniSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider, T> GetMiniSerializer<TWriteBuffer, TReadBuffer, T>()
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        return Cache<TWriteBuffer, TReadBuffer, T>.Instance;
    }

    static class Cache<TWriteBuffer, TReadBuffer, T>
        where TWriteBuffer : struct, IWriteBuffer, allows ref struct
        where TReadBuffer : struct, IReadBuffer, allows ref struct
    {
        public static readonly IMiniSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider, T> Instance;

        static Cache()
        {
            if (typeof(T) == typeof(int))
            {
                Instance = (IMiniSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider, T>)(object)new IntMiniSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider>();
            }
            else if (typeof(T) == typeof(int[]))
            {
                Instance = (IMiniSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider, T>)(object)new ArrayMiniSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider, int>();
            }

            if (Instance == null)
            {
                Instance = NotRegisteredSerializer<TWriteBuffer, TReadBuffer, DefaultMiniSerializerProvider, T>.Instance;
            }
        }
    }
}

public class NotRegisteredSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T> : IMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
    where TSerializerProvider : IMiniSerializerProvider<TSerializerProvider>
{
    readonly string message = $"Serializer is not registered in {typeof(TSerializerProvider).Name}. Type: {typeof(T).FullName}";

    public static readonly NotRegisteredSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T> Instance = new();

    NotRegisteredSerializer()
    {
    }

    public void Serialize(ref TWriteBuffer buffer, in T value)
    {
        throw new InvalidOperationException(message);
    }

    public T Deserialize(ref TReadBuffer buffer)
    {
        throw new InvalidOperationException(message);
    }
}

public sealed class IntMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider> : IMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, int>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
    where TSerializerProvider : IMiniSerializerProvider<TSerializerProvider>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Serialize(ref TWriteBuffer buffer, in int value)
    {
        Span<byte> span = buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        buffer.Advance(4);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Deserialize(ref TReadBuffer buffer)
    {
        ReadOnlySpan<byte> span = buffer.GetSpan(4);
        var value = BinaryPrimitives.ReadInt32LittleEndian(span);
        buffer.Advance(4);
        return value;
    }
}

public sealed class ArrayMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T> : IMiniSerializer<TWriteBuffer, TReadBuffer, TSerializerProvider, T[]>
    where TWriteBuffer : struct, IWriteBuffer, allows ref struct
    where TReadBuffer : struct, IReadBuffer, allows ref struct
    where TSerializerProvider : IMiniSerializerProvider<TSerializerProvider>
{
    public void Serialize(ref TWriteBuffer buffer, in T[] value)
    {
        // add length prefix
        var span = buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value.Length);
        buffer.Advance(4);

        var serializer = TSerializerProvider.GetMiniSerializer<TWriteBuffer, TReadBuffer, T>();
        for (int i = 0; i < value.Length; i++)
        {
            serializer.Serialize(ref buffer, in value[i]);
        }
    }

    public T[] Deserialize(ref TReadBuffer buffer)
    {
        // read length
        var span = buffer.GetSpan(4);
        var length = BinaryPrimitives.ReadInt32LittleEndian(span);
        buffer.Advance(4);

        var serializer = TSerializerProvider.GetMiniSerializer<TWriteBuffer, TReadBuffer, T>();
        var array = GC.AllocateUninitializedArray<T>(length);
        for (int i = 0; i < length; i++)
        {
            array[i] = serializer.Deserialize(ref buffer);
        }
        return array;
    }
}




public static partial class MiniSerializer
{
}