using System.Buffers;

namespace RESPite.Connections.Internal;

/// <summary>
/// Holds basic RespOperation, queue and release - turns
/// multiple send/send-many calls into a single send-many call.
/// </summary>
internal sealed class BasicBatchConnection(in RespContext context, int sizeHint) : BufferingBatchConnection(context, sizeHint)
{
    public override Task FlushAsync()
    {
        try
        {
            var count = Flush(out var oversized, out var single);
            return count switch
            {
                0 => Task.CompletedTask,
                1 => Tail.WriteAsync(single!),
                _ => SendAndRecycleAsync(Tail, oversized, count),
            };
        }
        catch (Exception ex)
        {
            OnConnectionError(ex);
            throw;
        }

        static async Task SendAndRecycleAsync(RespConnection tail, RespOperation[] oversized, int count)
        {
            try
            {
                await tail.WriteAsync(oversized.AsMemory(0, count)).ConfigureAwait(false);
                ArrayPool<RespOperation>.Shared.Return(oversized); // only on success, in case captured
            }
            catch (Exception ex)
            {
                TrySetException(oversized.AsSpan(0, count), ex);
                throw;
            }
        }
    }

    public override void Flush()
    {
        string operation = nameof(Flush);
        int count;
        RespOperation[] oversized;
        RespOperation single;
        try
        {
            count = Flush(out oversized, out single);
            switch (count)
            {
                case 0:
                    return;
                case 1:
                    operation = nameof(Tail.Write);
                    Tail.Write(single!);
                    return;
            }
        }
        catch (Exception ex)
        {
            OnConnectionError(ex, operation);
            throw;
        }

        try
        {
            Tail.Write(oversized.AsSpan(0, count));
        }
        catch (Exception ex)
        {
            TrySetException(oversized.AsSpan(0, count), ex);
            throw;
        }
        finally
        {
            // in the sync case, Send takes a span - hence can't have been captured anywhere; always recycle
            ArrayPool<RespOperation>.Shared.Return(oversized);
        }
    }
}
