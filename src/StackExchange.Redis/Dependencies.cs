using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace StackExchange.Redis;

internal static class Dependencies
{
    internal static void Assert()
    {
        string err = GetAssemblyFailureMessage();
        if (!string.IsNullOrEmpty(err)) Throw(err);
        static void Throw(string message) => throw new InvalidOperationException(message);
    }

    // ReSharper disable once InconsistentNaming
    private static string? s_assemblyFailureMessssage;

    private static string GetAssemblyFailureMessage()
    {
        static string ComputeAssemblyFailureMessage()
        {
            List<string>? failures = null;
            void AddFailure(string assembly)
            {
                if (failures is null) failures = new List<string>();
                failures.Add(assembly);
            }

            try { CheckBclAsync(); } catch { AddFailure("Microsoft.Bcl.AsyncInterfaces"); }
            try { CheckBuffers(); } catch { AddFailure("System.Buffers"); }
            try { CheckMemory(); } catch { AddFailure("System.Memory"); }
            try { CheckUnsafe(); } catch { AddFailure("System.Runtime.CompilerServices.Unsafe"); }
            try { CheckNumerics(); } catch { AddFailure("System.Numerics.Vectors"); }
            try { CheckPipe(); } catch { AddFailure("System.IO.Pipelines"); }
            try { CheckChannels(); } catch { AddFailure("System.Threading.Channels"); }

            try
            {
                ExecutePipe(out var assembly);
                if (assembly is not null) AddFailure(assembly);
            }
            catch (Exception ex)
            {
                // things went bad, but not in a way we expected
                return ex.Message;
            }

            if (failures is null || failures.Count == 0) return "";

            return "The assembly for " + string.Join(" + ", failures) + " could not be loaded; this usually means a missing assembly binding redirect - try checking this, and adding any that are missing;"
                   + " note that it is not always possible to add this redirects - for example 'azure functions v1'; it looks like you may need to use 'azure functions v2' for that - sorry, but that's out of our control";
        }
        return s_assemblyFailureMessssage ??= ComputeAssemblyFailureMessage();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ExecutePipe(out string? assembly)
        {
            // try everything combined
            assembly = null;
            try
            {
                var pipe = new Pipe();
                pipe.Writer.GetSpan(4);
                pipe.Writer.Advance(4);
                pipe.Writer.Complete();
                pipe.Reader.TryRead(out var _);
            }
            catch (Exception ex)
            {
                // look (non-greedy) for either 'System.Blah' or 'System.Blah,...
                var match = Regex.Match(ex.Message, @"'(System\..*?)[,']");
                if (match.Success)
                {
                    assembly = match.Groups[1].Value;
                }
                else
                {
                    throw;
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckBclAsync() => GC.KeepAlive(typeof(IAsyncDisposable));

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckChannels() => GC.KeepAlive(typeof(Channel<string>));

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckPipe() => GC.KeepAlive(PipeOptions.Default);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckBuffers()
        {
            var arr = ArrayPool<byte>.Shared.Rent(64);
            GC.KeepAlive(arr);
            ArrayPool<byte>.Shared.Return(arr);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckMemory()
        {
            var buffer = MemoryPool<byte>.Shared.Rent(64);
            GC.KeepAlive(buffer);
            buffer.Dispose();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckUnsafe() => _ = Unsafe.SizeOf<int>();

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void CheckNumerics() => _ = Vector.IsHardwareAccelerated;
    }
}
