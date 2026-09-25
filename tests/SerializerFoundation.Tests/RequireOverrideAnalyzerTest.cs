using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using SerializerFoundation.Analyzers;

namespace SerializerFoundation.Tests;

// SF003: same corlib-stub approach as FactoryGenericOverrideAnalyzerTest. The modern
// stub carries the [RequireOverride]-marked generic virtual; the downlevel stub has no
// such member, so the analyzer must stay silent there by construction.
public class RequireOverrideAnalyzerTest
{
    const string CorlibStub = """
        namespace System
        {
            public class Object { }
            public abstract class ValueType { }
            public abstract class Enum : ValueType { }
            public struct Void { }
            public struct Boolean { }
            public struct Int32 { }
            public class String { }
            public class Attribute { }
            public enum AttributeTargets { All = 32767 }
            public sealed class AttributeUsageAttribute : Attribute
            {
                public AttributeUsageAttribute(AttributeTargets validOn) { }
                public bool AllowMultiple { get { return false; } set { } }
                public bool Inherited { get { return false; } set { } }
            }
        }
        namespace System.Runtime.CompilerServices
        {
            public sealed class InternalsVisibleToAttribute : System.Attribute
            {
                public InternalsVisibleToAttribute(string assemblyName) { }
            }
        }
        """;

    const string ModernCoreStub = """
        namespace SerializerFoundation.CodeAnalysis
        {
            [System.AttributeUsage(System.AttributeTargets.All)]
            public sealed class RequireOverrideAttribute : System.Attribute { }
        }
        namespace MessagePack
        {
            public abstract class MessageProcessor
            {
                public abstract bool TryEncode(int output);

                [SerializerFoundation.CodeAnalysis.RequireOverride]
                public virtual bool TryEncode<TWriteBuffer>(int message, ref TWriteBuffer output)
                {
                    return TryEncode(message);
                }
            }
        }
        """;

    const string DownlevelCoreStub = """
        namespace MessagePack
        {
            public abstract class MessageProcessor
            {
                public abstract bool TryEncode(int output);
            }
        }
        """;

    const string BridgeRidingSource = """
        public sealed class InterfaceOnlyProcessor : MessagePack.MessageProcessor
        {
            public override bool TryEncode(int output)
            {
                return false;
            }
        }
        """;

    static MetadataReference CompileStub(string assemblyName, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source)],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var emit = compilation.Emit(stream, options: new Microsoft.CodeAnalysis.Emit.EmitOptions(runtimeMetadataVersion: "v4.0.30319"));
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    static Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string userSource, bool modernCore)
        => RunAnalyzerAsync(userSource, modernCore ? ModernCoreStub : DownlevelCoreStub);

    static async Task<ImmutableArray<Diagnostic>> RunAnalyzerAsync(string userSource, string coreSource)
    {
        var corlib = CompileStub("corlibstub", CorlibStub);
        var coreLibrary = CompileStub("MessagePack", coreSource, corlib);

        var compilation = CSharpCompilation.Create(
            "UserAssembly",
            [CSharpSyntaxTree.ParseText(userSource)],
            [corlib, coreLibrary],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));

        var withAnalyzers = compilation.WithAnalyzers([new RequireOverrideAnalyzer()]);
        return await withAnalyzers.GetAnalyzerDiagnosticsAsync(CancellationToken.None);
    }

    [Fact]
    public async Task BridgeRidingOnModernSurface_Reports()
    {
        var diagnostics = await RunAnalyzerAsync(BridgeRidingSource, modernCore: true);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RequireOverrideAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("InterfaceOnlyProcessor", diagnostic.GetMessage());
        Assert.Contains("TryEncode", diagnostic.GetMessage());
    }

    [Fact]
    public async Task OverridesGeneric_Silent()
    {
        const string source = """
            public sealed class FullProcessor : MessagePack.MessageProcessor
            {
                public override bool TryEncode(int output)
                {
                    return false;
                }

                public override bool TryEncode<TWriteBuffer>(int message, ref TWriteBuffer output)
                {
                    return false;
                }
            }
            """;
        var diagnostics = await RunAnalyzerAsync(source, modernCore: true);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task IntermediateBaseOverrides_LeafSilent()
    {
        // an intermediate override covers all leaves below it
        const string source = """
            public abstract class Intermediate : MessagePack.MessageProcessor
            {
                public override bool TryEncode<TWriteBuffer>(int message, ref TWriteBuffer output)
                {
                    return false;
                }
            }

            public sealed class Leaf : Intermediate
            {
                public override bool TryEncode(int output)
                {
                    return false;
                }
            }
            """;
        var diagnostics = await RunAnalyzerAsync(source, modernCore: true);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task NonOverridingIntermediate_ReportsOnLeafOnly()
    {
        // the abstract intermediate itself is exempt (it cannot be instantiated); the
        // requirement lands on the first non-abstract type
        const string source = """
            public abstract class Intermediate : MessagePack.MessageProcessor
            {
            }

            public sealed class Leaf : Intermediate
            {
                public override bool TryEncode(int output)
                {
                    return false;
                }
            }
            """;
        var diagnostics = await RunAnalyzerAsync(source, modernCore: true);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("Leaf", diagnostic.GetMessage());
    }

    [Fact]
    public async Task IntermediateOverrideReappliesAttribute_ReportsOnLeaf()
    {
        // the attribute is not inherited, so an intermediate override that marks itself
        // re-imposes the requirement on its own leaves; the base's original is satisfied
        // by the intermediate override, so exactly one diagnostic (for the intermediate's) lands
        const string source = """
            public abstract class Intermediate : MessagePack.MessageProcessor
            {
                [SerializerFoundation.CodeAnalysis.RequireOverride]
                public override bool TryEncode<TWriteBuffer>(int message, ref TWriteBuffer output)
                {
                    return false;
                }
            }

            public sealed class Leaf : Intermediate
            {
                public override bool TryEncode(int output)
                {
                    return false;
                }
            }

            public sealed class OverridingLeaf : Intermediate
            {
                public override bool TryEncode(int output)
                {
                    return false;
                }

                public override bool TryEncode<TWriteBuffer>(int message, ref TWriteBuffer output)
                {
                    return true;
                }
            }
            """;
        var diagnostics = await RunAnalyzerAsync(source, modernCore: true);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Contains("'Leaf'", diagnostic.GetMessage());
    }

    [Fact]
    public async Task SealedIntermediateOverrideWithAttribute_Silent()
    {
        // a sealed override cannot be overridden, so an attribute on it requires nothing
        const string source = """
            public abstract class Intermediate : MessagePack.MessageProcessor
            {
                [SerializerFoundation.CodeAnalysis.RequireOverride]
                public sealed override bool TryEncode<TWriteBuffer>(int message, ref TWriteBuffer output)
                {
                    return false;
                }
            }

            public sealed class Leaf : Intermediate
            {
                public override bool TryEncode(int output)
                {
                    return false;
                }
            }
            """;
        var diagnostics = await RunAnalyzerAsync(source, modernCore: true);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("internal", false, false)]
    [InlineData("private protected", false, false)]
    [InlineData("protected", false, true)]
    [InlineData("protected internal", false, true)]
    [InlineData("public", false, true)]
    [InlineData("internal", true, true)]
    [InlineData("private protected", true, true)]
    public async Task CrossAssemblyMember_RequiresOverrideOnlyWhenAccessible(string accessibility, bool friendAssembly, bool requiresOverride)
    {
        var friendAttribute = friendAssembly
            ? "[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"UserAssembly\")]\n"
            : "";
        var coreSource = friendAttribute + ModernCoreStub + $$"""

            public abstract class Base
            {
                [SerializerFoundation.CodeAnalysis.RequireOverride]
                {{accessibility}} virtual void Required() { }
            }
            """;

        // CompileStub emits a separate assembly, exercising normal metadata visibility
        // rather than a source compilation reference or MetadataImportOptions.All.
        var diagnostics = await RunAnalyzerAsync("public sealed class Derived : Base { }", coreSource);
        if (requiresOverride)
        {
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal(RequireOverrideAnalyzer.DiagnosticId, diagnostic.Id);
            Assert.Contains("'Derived'", diagnostic.GetMessage());
            Assert.Contains("'Required'", diagnostic.GetMessage());
        }
        else
        {
            Assert.Empty(diagnostics);
        }
    }

    [Theory]
    [InlineData("internal")]
    [InlineData("private protected")]
    public async Task SameAssemblyInternalMember_Reports(string accessibility)
    {
        var source = $$"""
            public abstract class Base
            {
                [SerializerFoundation.CodeAnalysis.RequireOverride]
                {{accessibility}} virtual void Required() { }
            }
            public sealed class Derived : Base { }
            """;

        var diagnostics = await RunAnalyzerAsync(source, modernCore: true);
        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(RequireOverrideAnalyzer.DiagnosticId, diagnostic.Id);
        Assert.Contains("'Derived'", diagnostic.GetMessage());
        Assert.Contains("'Required'", diagnostic.GetMessage());
    }

    [Fact]
    public async Task DownlevelSurface_Silent()
    {
        // the member (and the attribute type itself) do not exist downlevel: nothing to require
        var diagnostics = await RunAnalyzerAsync(BridgeRidingSource, modernCore: false);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task UnrelatedType_Silent()
    {
        var diagnostics = await RunAnalyzerAsync("public class Plain { }", modernCore: true);
        Assert.Empty(diagnostics);
    }
}
