

using SerializerFoundation;
using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;




unsafe
{
    var a = Unsafe.SizeOf<ArrayPoolWriteBuffer>(); // 240
    Console.WriteLine(a);

}
