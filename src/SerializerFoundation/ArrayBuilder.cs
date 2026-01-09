using System.Buffers;
using System.Runtime.CompilerServices;

namespace SerializerFoundation;

// for security usecase
// similar as SegmentedArrayBuilder in System.Linq(use in ToArray/ToList)
public struct ArrayBuilder<T> : IDisposable
{
    internal Arrays segments;
    internal int nextSegmentIndex;

    public Span<T> GetNextSegment()
    {
        var segmentLength = GetSegmentLength(nextSegmentIndex);
        var newArray = ArrayPool<T>.Shared.Rent(segmentLength);
        segments[nextSegmentIndex++] = newArray;
        return newArray.AsSpan(0, segmentLength);
    }

    public T[] ToArray(int lastSegmentCount)
    {
        var length = GetLength(lastSegmentCount);
        if (length == 0)
        {
            return [];
        }

        var array = GC.AllocateUninitializedArray<T>(length);
        WriteTo(array, lastSegmentCount);
        return array;
    }

    public void WriteTo(Span<T> destination, int lastSegmentCount)
    {
        for (int i = 0; i < nextSegmentIndex - 1; i++)
        {
            var segmentLength = GetSegmentLength(i);
            segments[i]!.AsSpan(0, segmentLength).CopyTo(destination);
            destination = destination.Slice(segmentLength);
        }

        if (lastSegmentCount > 0)
        {
            segments[nextSegmentIndex - 1]!.AsSpan(0, lastSegmentCount).CopyTo(destination);
        }
    }

    public int GetLength(int lastSegmentCount)
    {
        return GetPreviousSegmentsTotal(nextSegmentIndex - 1) + lastSegmentCount;
    }

    static int GetSegmentLength(int index) => index switch
    {
        0 => 512,
        1 => 1_024,
        2 => 2_048,
        3 => 4_096,
        4 => 8_192,
        5 => 16_384,
        6 => 32_768,
        7 => 65_536,
        8 => 131_072,
        9 => 262_144,
        10 => 524_288,
        11 => 1_048_576,
        12 => 2_097_152,
        13 => 4_194_304,
        14 => 8_388_608,
        15 => 16_777_216,
        16 => 33_554_432,
        17 => 67_108_864,
        18 => 134_217_728,
        19 => 268_435_456,
        20 => 536_870_912,
        21 => 1_073_741_824,
        _ => Array.MaxLength, // 2,147,483,591
    };

    static int GetPreviousSegmentsTotal(int count) => count switch
    {
        0 => 0,
        1 => 512,
        2 => 1_536,
        3 => 3_584,
        4 => 7_680,
        5 => 15_872,
        6 => 32_256,
        7 => 65_024,
        8 => 130_560,
        9 => 261_632,
        10 => 523_776,
        11 => 1_048_064,
        12 => 2_096_640,
        13 => 4_193_792,
        14 => 8_388_096,
        15 => 16_776_704,
        16 => 33_553_920,
        17 => 67_108_352,
        18 => 134_217_216,
        19 => 268_434_944,
        20 => 536_870_400,
        21 => 1_073_741_312,
        22 => 2_147_483_136,
        _ => 0,
    };

    public void Dispose()
    {
        var clearArray = RuntimeHelpers.IsReferenceOrContainsReferences<T>();

        for (int i = 0; i < nextSegmentIndex; i++)
        {
            var segment = segments[i];
            if (segment != null)
            {
                ArrayPool<T>.Shared.Return(segment, clearArray);
                segments[i] = null;
            }
        }

        nextSegmentIndex = 0;
    }

    [InlineArray(23)]
    internal struct Arrays
    {
        public T[]? values;
    }
}

public static class ArrayBuilderExtensions
{
    extension(ref ArrayBuilder<char> arrayBuilder)
    {
        public string ToString(int lastSegmentCount)
        {
            if (arrayBuilder.nextSegmentIndex == 0) return "";

            var length = arrayBuilder.GetLength(lastSegmentCount);
            if (length == 0) return "";

            // avoid string.Create(Func) struct copy-cost.
            var str = string.Create(length, (object?)null, static (_, _) => { });
            unsafe
            {
                fixed (char* destPointer = str.AsSpan())
                {
                    Span<char> dest = new Span<char>(destPointer, length);
                    arrayBuilder.WriteTo(dest, lastSegmentCount);
                }
            }
            return str;
        }
    }
}
