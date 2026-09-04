using System;
using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    public static readonly ResultProcessor<RespResult> RespResult = new RespResultProcessor();

    private sealed class RespResultProcessor : ResultProcessor<RespResult>
    {
        public override bool SetResult(PhysicalConnection connection, Message message, ref RespReader reader)
        {
            // capture the raw, undecoded frame - header bytes included - before anything advances the
            // reader; this only works because we're called before the base implementation's MovePastBof(),
            // which would otherwise consume the leading prefix/length bytes we need to capture too
            var totalBytes = checked((int)reader.ProtocolBytesRemaining);

            // peek at an independent copy to learn the prefix/error/null status, leaving the raw capture untouched
            var probe = reader;
            probe.MovePastBof();

            if (probe.IsError)
            {
                return base.SetResult(connection, message, ref reader);
            }

            var pool = connection.BridgeCouldBeNull?.Multiplexer?.RawConfig?.ResponseBufferPool;
            SetResult(message, StackExchange.Redis.RespResult.Capture(probe.Prefix, probe.IsNull, ref reader, totalBytes, pool));
            return true;
        }

        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader) =>
            throw new NotSupportedException(); // SetResult is fully overridden above; this is never invoked
    }
}
