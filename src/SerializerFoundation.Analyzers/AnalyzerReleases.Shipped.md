; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SF001 | SerializerFoundation.Correctness | Error | BufferMustBeStructAnalyzer: IWriteBuffer/IReadBuffer implementations must be structs
SF002 | SerializerFoundation.Correctness | Error | NonCopyableBufferAnalyzer: buffer structs are single-owner and must not be copied
SF003 | SerializerFoundation.Design | Error | RequireOverrideAnalyzer: [RequireOverride] virtuals must be overridden
