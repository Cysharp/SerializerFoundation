using SerializationFramework;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ConsoleApp1.MiniMessagePack;

public interface IMessagePackSerializer<TWriteBuffer, TReadBuffer, T>
    where TWriteBuffer : struct, IWriteBuffer
    where TReadBuffer : struct, IReadBuffer
{
    void Serialize(ref TWriteBuffer buffer, ref T value);
    void Deserialize(ref TReadBuffer buffer, ref T value);

    // ValueTask SerializeAsync(IAsyncSequentialBuffer buffer, T value, CancellationToken cancellationToken);
    // ValueTask<T> DeserializeAsync(IAsyncSequentialBuffer buffer, CancellationToken cancellationToken);
}

public class IntMessagePackSerialzier<TWriteBuffer, TReadBuffer> : IMessagePackSerializer<TWriteBuffer, TReadBuffer, int>
    where TWriteBuffer : struct, IWriteBuffer
    where TReadBuffer : struct, IReadBuffer
{
    public void Serialize(ref TWriteBuffer buffer, ref int value)
    {
        ref var reference = ref buffer.GetSpanReference(8);
        var written = MessagePackPrimitives.UnsafeWriteInt32(ref reference, value);
        buffer.Advance(written);
    }

    public ValueTask SerializeAsync(IAsyncWriteBuffer buffer, int value, CancellationToken cancellationToken)
    {
        if (buffer.TryGetSpan(5, out var span))
        {
            var written = MessagePackPrimitives.UnsafeWriteInt32(ref MemoryMarshal.GetReference(span), value);
            buffer.Advance(written);
            return default;
        }
        else
        {
            return SerializeAsyncCore(buffer, value, cancellationToken);
        }
    }

    async ValueTask SerializeAsyncCore(IAsyncWriteBuffer buffer, int value, CancellationToken cancellationToken)
    {
        await buffer.EnsureBufferAsync(5, cancellationToken);

        buffer.TryGetSpan(8, out var span);
        var written = MessagePackPrimitives.UnsafeWriteInt32(ref MemoryMarshal.GetReference(span), value);
        buffer.Advance(written);
    }

    public void Deserialize(ref TReadBuffer buffer, ref int value)
    {
        // buffer.GetSpan(

        // ref var peek = ref buffer.GetSpan(1); // peek

        // 


        throw new NotImplementedException();
    }

    public ValueTask<int> DeserializeAsync(IAsyncWriteBuffer buffer, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}

// Microsoft.Extensions.Abstractions

public static class MessagePackPrimitives
{
    // TODO? performance check, need 8 byte version(1 call optimize for WriteUnaligned)
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int UnsafeWriteInt32(ref byte destination, int value)
    {
        if (BitConverter.IsLittleEndian)
        {
            if (value >= 0)
            {
                if (value <= 127) // positive fixint
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)value);
                    return 1;
                }
                if (value <= 255) // uint 8
                {
                    Unsafe.WriteUnaligned(ref destination, (ushort)(0xcc | (value << 8)));
                    return 2;
                }
                if (value <= 65535) // uint 16
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)0xcd);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), BinaryPrimitives.ReverseEndianness((ushort)value));
                    return 3;
                }
                // uint 32
                Unsafe.WriteUnaligned(ref destination, (byte)0xce);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), BinaryPrimitives.ReverseEndianness((uint)value));
                return 5;
            }
            else
            {
                if (value >= -32) // negative fixint
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)value);
                    return 1;
                }
                if (value >= -128) // int 8
                {
                    Unsafe.WriteUnaligned(ref destination, (ushort)(0xd0 | ((byte)value << 8)));
                    return 2;
                }
                if (value >= -32768) // int 16
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)0xd1);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), BinaryPrimitives.ReverseEndianness((short)value));
                    return 3;
                }
                // int 32
                Unsafe.WriteUnaligned(ref destination, (byte)0xd2);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), BinaryPrimitives.ReverseEndianness(value));
                return 5;
            }
        }
        else
        {
            if (value >= 0)
            {
                if (value <= 127) // positive fixint
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)value);
                    return 1;
                }
                if (value <= 255) // uint 8
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)0xcc);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), (byte)value);
                    return 2;
                }
                if (value <= 65535) // uint 16
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)0xcd);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), (ushort)value);
                    return 3;
                }
                // uint 32
                Unsafe.WriteUnaligned(ref destination, (byte)0xce);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), (uint)value);
                return 5;
            }
            else
            {
                if (value >= -32) // negative fixint
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)value);
                    return 1;
                }
                if (value >= -128) // int 8
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)0xd0);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), (byte)value);
                    return 2;
                }
                if (value >= -32768) // int 16
                {
                    Unsafe.WriteUnaligned(ref destination, (byte)0xd1);
                    Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), (short)value);
                    return 3;
                }
                // int 32
                Unsafe.WriteUnaligned(ref destination, (byte)0xd2);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref destination, 1), value);
                return 5;
            }
        }
    }
}
