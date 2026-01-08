using System;
using System.Collections.Generic;
using System.Text;

namespace SerializationFramework.Tests;

public class SafeArrayBuilderTest
{
    [Test]
    public void Foo()
    {
        using var builder = new SafeArrayBuilder<int>();

        var a = builder.GetNextSegment();
        var b = builder.GetNextSegment();
        var c = builder.GetNextSegment();
        var d = builder.GetNextSegment();
        var e = builder.GetNextSegment();

    }
}
