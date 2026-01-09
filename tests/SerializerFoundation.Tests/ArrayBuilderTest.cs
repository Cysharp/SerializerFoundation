using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SerializerFoundation.Tests;

public class ArrayBuilderTest
{
    // TODO:...
    [Test]
    public void Foo()
    {
        using var builder = new ArrayBuilder<int>();

        var a = builder.GetNextSegment();
        var b = builder.GetNextSegment();
        var c = builder.GetNextSegment();
        var d = builder.GetNextSegment();
        var e = builder.GetNextSegment();

    }

    // TODO:...

    [Test]
    public async Task BuildString()
    {
        var builder = new ArrayBuilder<char>();
        try
        {
            var a = builder.GetNextSegment();
            a[0] = 'a';
            a[1] = 'b';
            a[2] = 'c';

            var foo = builder.ToString(3);

            await Assert.That(foo).IsEqualTo("abc");
        }
        finally
        {
            builder.Dispose();
        }
    }
}
