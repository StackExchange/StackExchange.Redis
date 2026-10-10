// Runtime-async is per-assembly, so the benchmark assembly opts in alongside the library; the attribute
// type comes from StackExchange.Redis via InternalsVisibleTo. Removed for the /p:DisableRuntimeAsync=true
// control build and for every target framework below net11.0.
[assembly: System.Runtime.CompilerServices.RuntimeAsyncMethodGeneration(true)]
