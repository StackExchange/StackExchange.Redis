using System;
using System.Runtime.CompilerServices;

namespace RESPite.Resp.Commands;

/// <summary>
/// Queries keys in a RESP database.
/// </summary>
public readonly record struct Scan(long Cursor = 0, SimpleString Match = default, int Count = 10, string? Type = null)
{
    /// <summary>
    /// Process the result of a scan operation to update the <see cref="Cursor"/>.
    /// </summary>
    public Scan Next(in Response reply) => this with { Cursor = reply.Cursor };

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
