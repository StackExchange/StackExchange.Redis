using System;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// Extension methods for adding ambient cancellation support to Redis operations.
    /// </summary>
    public static class RedisCancellationExtensions
    {
        /// <summary>
        /// Sets an ambient cancellation token that will be used for all Redis operations
        /// in the current async context until the returned scope is disposed.
        /// </summary>
        /// <param name="redis">The Redis instance (used for extension method syntax).</param>
        /// <param name="cancellationToken">The cancellation token to use for operations in this scope.</param>
        /// <returns>A disposable scope that restores the previous cancellation context when disposed.</returns>
        /// <example>
        /// <code>
        /// using (database.WithCancellation(cancellationToken))
        /// {
        ///     await database.StringSetAsync("key", "value");
        ///     var value = await database.StringGetAsync("key");
        /// }
        /// </code>
        /// </example>
        public static IDisposable WithCancellation(this IRedisAsync redis, CancellationToken cancellationToken)
            => new CancellationScope(cancellationToken, null);

        /// <summary>
        /// Sets an ambient timeout that will be used for all Redis operations
        /// in the current async context until the returned scope is disposed.
        /// </summary>
        /// <param name="redis">The Redis instance (used for extension method syntax).</param>
        /// <param name="timeout">The timeout to use for operations in this scope.</param>
        /// <returns>A disposable scope that restores the previous cancellation context when disposed.</returns>
        /// <example>
        /// <code>
        /// using (database.WithTimeout(TimeSpan.FromSeconds(5)))
        /// {
        ///     await database.StringSetAsync("key", "value");
        /// }
        /// </code>
        /// </example>
        public static IDisposable WithTimeout(this IRedisAsync redis, TimeSpan timeout)
            => new CancellationScope(CancellationToken.None, timeout);

        /// <summary>
        /// Sets both an ambient cancellation token and timeout that will be used for all Redis operations
        /// in the current async context until the returned scope is disposed.
        /// </summary>
        /// <param name="redis">The Redis instance (used for extension method syntax).</param>
        /// <param name="cancellationToken">The cancellation token to use for operations in this scope.</param>
        /// <param name="timeout">The timeout to use for operations in this scope.</param>
        /// <returns>A disposable scope that restores the previous cancellation context when disposed.</returns>
        /// <example>
        /// <code>
        /// using (database.WithCancellationAndTimeout(cancellationToken, TimeSpan.FromSeconds(10)))
        /// {
        ///     await database.StringSetAsync("key", "value");
        /// }
        /// </code>
        /// </example>
        public static IDisposable WithCancellationAndTimeout(
            this IRedisAsync redis,
            CancellationToken cancellationToken,
            TimeSpan timeout)
            => new CancellationScope(cancellationToken, timeout);

        /// <summary>
        /// Gets the effective cancellation token for the current async context,
        /// combining any ambient cancellation token and timeout.
        /// </summary>
        /// <returns>The effective cancellation token, or CancellationToken.None if no ambient context is set.</returns>
        internal static CancellationToken GetEffectiveCancellationToken()
            => _context.Value?.Token ?? CancellationToken.None;

        /// <summary>
        /// Gets the current cancellation context for diagnostic purposes.
        /// </summary>
        /// <returns>The current context, or null if no ambient context is set.</returns>
        internal static object? GetCurrentScope() => _context.Value;

        private static readonly AsyncLocal<CancellationScope?> _context = new();

        /// <summary>
        /// A disposable scope that manages the ambient cancellation context.
        /// </summary>
        private sealed class CancellationScope : IDisposable
        {
            private readonly CancellationTokenSource? _ownedSource;
            private readonly CancellationScope? _previous;
            private bool _disposed;

            public CancellationToken Token { get; }

            /// <summary>
            /// Creates a new cancellation scope with the specified token and timeout.
            /// </summary>
            /// <param name="cancellationToken">The cancellation token for this scope.</param>
            /// <param name="timeout">The timeout for this scope.</param>
            public CancellationScope(CancellationToken cancellationToken, TimeSpan? timeout)
            {
                _previous = _context.Value;
                if (timeout.HasValue)
                {
                    // has a timeout
                    if (cancellationToken.CanBeCanceled)
                    {
                        // need both timeout and cancellation; but we can avoid some layers if
                        // we're already doomed
                        if (cancellationToken.IsCancellationRequested)
                        {
                            Token = cancellationToken;
                        }
                        else
                        {
                            _ownedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            _ownedSource.CancelAfter(timeout.GetValueOrDefault());
                            Token = _ownedSource.Token;
                        }
                    }
                    else
                    {
                        // just a timeout
                        _ownedSource = new CancellationTokenSource(timeout.GetValueOrDefault());
                        Token = _ownedSource.Token;
                    }
                }
                else if (cancellationToken.CanBeCanceled)
                {
                    // nice and simple, just a CT
                    Token = cancellationToken;
                }
                else
                {
                    Token = CancellationToken.None;
                }

                _context.Value = this;
            }

            /// <summary>
            /// Restores the previous cancellation context.
            /// </summary>
            public void Dispose()
            {
                if (!_disposed)
                {
                    _disposed = true;
                    if (ReferenceEquals(_context.Value, this))
                    {
                        // reinstate the previous context
                        _context.Value = _previous;
                    }
                    _ownedSource?.Dispose();
                }
            }
        }
    }
}
