using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using RESPite.Resp.Commands;
using RESPite.Transports;

namespace RESPite.Resp.KeyValueStore;

/// <summary>
/// Queries keys in a RESP database.
/// </summary>
public static class Scan
{
    /// <summary>
    /// Iterate over SCAN results, fetching all matching records.
    /// </summary>
    public static IEnumerable<SimpleString> ReadAll(this in RespCommand<Request, Response> command, ISyncMessageTransport transport, in Request request)
    {
        return Impl(command, transport, request);
        static IEnumerable<SimpleString> Impl(RespCommand<Request, Response> command, ISyncMessageTransport transport, Request request)
        {
            do
            {
                using var response = command.Send(transport, in request);
                foreach (var key in response.Keys)
                {
                    yield return key;
                }
                request = request.Next(response);
            }
            while (request.Cursor != 0);
        }
    }

    /// <summary>
    /// Iterate over SCAN results, fetching all matching records.
    /// </summary>
    public static IAsyncEnumerable<SimpleString> ReadAllAsync(this in RespCommand<Request, Response> command, IAsyncMessageTransport transport, in Request request, CancellationToken token = default)
    {
        return Impl(command, transport, request, token);
        static async IAsyncEnumerable<SimpleString> Impl(RespCommand<Request, Response> command, IAsyncMessageTransport transport, Request request, [EnumeratorCancellation] CancellationToken token)
        {
            do
            {
                token.ThrowIfCancellationRequested();
                using var response = await command.SendAsync(transport, in request, token).ConfigureAwait(false);
                foreach (var key in response.Keys)
                {
                    yield return key;
                }
                request = request.Next(response);
            }
            while (request.Cursor != 0);
        }
    }

    /// <summary>
    /// Requests the next page of SCAN results from the given <paramref name="Cursor"/>.
    /// </summary>
    public readonly record struct Request(long Cursor = 0, SimpleString Match = default, int Count = 10, string? Type = null)
    {
        /// <summary>
        /// Process the result of a scan operation to update the <see cref="Cursor"/>.
        /// </summary>
        public Request Next(in Response reply) => this with { Cursor = reply.Cursor };
    }

    /// <summary>
    /// Provides the keys associated with a single iteration of a scan operation,
    /// and the cursor to continue the scan operation.
    /// </summary>
    /// <remarks>The keys can be any number, including zero and more than was requested in the request.</remarks>
    public readonly struct Response(long cursor, LeasedStrings keys) : IDisposable
    {
        private readonly LeasedStrings _keys = keys;

        /// <summary>
        /// Gets the cursor to use to continue this scan operation.
        /// </summary>
        public long Cursor { get; } = cursor;

        /// <summary>
        /// Gets the keys returned from this iteration of the scan operation.
        /// </summary>
        public LeasedStrings Keys => _keys;

        /// <inheritdoc/>
        public void Dispose()
        {
            var keys = _keys;
            Unsafe.AsRef(in _keys) = default;
            keys.Dispose();
        }
    }
}
