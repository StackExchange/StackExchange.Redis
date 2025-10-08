#if NET5_0_OR_GREATER
[module:global::System.Runtime.CompilerServices.SkipLocalsInit]
#else
// we've gone some disambiguation to do...
extern alias seredis;
global using DoesNotReturnAttribute = seredis::System.Diagnostics.CodeAnalysis.DoesNotReturnAttribute;

[module:seredis::System.Runtime.CompilerServices.SkipLocalsInit]
#endif
