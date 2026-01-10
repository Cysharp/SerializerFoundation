

using SerializerFoundation;

Span<byte> scratch = stackalloc byte[1024];
var buffer = new ArrayPoolWriteBuffer(scratch);

var span1 = buffer.GetSpan(1_073_741_824);
buffer.Advance(1);

var span2 = buffer.GetSpan(1_073_741_824);
buffer.Advance(1);
