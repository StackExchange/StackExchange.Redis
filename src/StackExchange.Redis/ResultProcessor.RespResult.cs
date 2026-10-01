using System;
using RESPite.Messages;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal abstract partial class ResultProcessor
{
    public static readonly ResultProcessor<RespResult> RespResult = new RespResultProcessor();

    private sealed class RespResultProcessor : ResultProcessor<RespResult>
    {
        /// <remarks>
        /// <para>
        /// An <c>EVALSHA</c> can come back <c>NOSCRIPT</c> at any time - the server may have been flushed,
        /// restarted, or failed over - and this is where that is decided for this path. The base then
        /// re-issues on a <c>Reissue</c> verdict, so the caller never sees it.
        /// </para>
        /// <para>
        /// <b>The buffer must outlive a reissue.</b> Every other error is the end of the road for this
        /// message, so its rendered arguments go back; a message that is about to be written again still
        /// needs them. Keyed on the <i>verdict</i> rather than on "was this a NOSCRIPT", because a second
        /// NOSCRIPT is not retried and does need to release.
        /// </para>
        /// </remarks>
        protected override ReplyVerdict Inspect(PhysicalConnection connection, Message message, in RespReader reader)
        {
            var probe = reader;
            probe.MovePastBof();
            if (!probe.IsError) return ReplyVerdict.Complete; // the success path releases below

            var verdict = NoScriptVerdict(connection, message, in probe);
            if (verdict != ReplyVerdict.Reissue && message is IRenderedArgsOwner errorOwner)
            {
                errorOwner.ReleaseRenderedArgs();
            }

            return verdict;
        }

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
                // the base inspects (which may re-issue, returning false) and otherwise fails the message
                return base.SetResult(connection, message, ref reader);
            }

            var pool = connection.BridgeCouldBeNull?.Multiplexer?.RawConfig?.ResponseBufferPool;
            SetResult(message, StackExchange.Redis.RespResult.Capture(probe.Prefix, probe.IsNull, ref reader, totalBytes, pool));
            if (message is IRenderedArgsOwner owner) owner.ReleaseRenderedArgs();
            return true;
        }

        protected override bool SetResultCore(PhysicalConnection connection, Message message, ref RespReader reader) =>
            throw new NotSupportedException(); // SetResult is fully overridden above; this is never invoked
    }
}
