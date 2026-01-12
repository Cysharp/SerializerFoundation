using System;
using System.Collections.Generic;
using System.Text;

namespace SerializerFoundation;

#if !NET9_0_OR_GREATER

internal static class Polyfill
{
    extension(GC)
    {
        internal static T[] AllocateUninitializedArray<T>(int length)
        {
            return new T[length];
        }
    }
    
    extension(Array)
    {
        internal static int MaxLength => 0X7FFFFFC7;
    }
}

#endif
