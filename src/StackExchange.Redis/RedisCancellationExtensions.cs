using System;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// Extension methods for adding ambient cancellation support to Redis operations.
    /// </summary>
    public static class RedisCancellationExtensions
    {
        private static readonly AsyncLocal<CancellationContext?> _context = new();

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
        {
            return new CancellationScope(cancellationToken, null);
        }

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
        {
            return new CancellationScope(default, timeout);
        }

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
        {
            return new CancellationScope(cancellationToken, timeout);
        }

        /// <summary>
        /// Gets the effective cancellation token for the current async context,
        /// combining any ambient cancellation token and timeout.
        /// </summary>
        /// <returns>The effective cancellation token, or CancellationToken.None if no ambient context is set.</returns>
        internal static CancellationToken GetEffectiveCancellationToken()
        {
            var context = _context.Value;
            return context?.GetEffectiveToken() ?? default;
        }

        /// <summary>
        /// Gets the current cancellation context for diagnostic purposes.
        /// </summary>
        /// <returns>The current context, or null if no ambient context is set.</returns>
        internal static CancellationContext? GetCurrentContext() => _context.Value;

        /// <summary>
        /// Represents the cancellation context for Redis operations.
        /// </summary>
        internal record CancellationContext(CancellationToken Token, TimeSpan? Timeout)
        {
            /// <summary>
            /// Gets the effective cancellation token, combining the explicit token with any timeout.
            /// </summary>
            /// <returns>A cancellation token that will be cancelled when either the explicit token is cancelled or the timeout expires.</returns>
            public CancellationToken GetEffectiveToken()
            {
                if (!Timeout.HasValue) return Token;

                var timeoutSource = new CancellationTokenSource(Timeout.Value);
                return Token.CanBeCanceled
                    ? CancellationTokenSource.CreateLinkedTokenSource(Token, timeoutSource.Token).Token
                    : timeoutSource.Token;
            }

            /// <summary>
            /// Gets a string representation of this context for debugging.
            /// </summary>
            public override string ToString()
            {
                var parts = new System.Collections.Generic.List<string>();
                if (Token.CanBeCanceled) parts.Add($"Token: {(Token.IsCancellationRequested ? "Cancelled" : "Active")}");
                if (Timeout.HasValue) parts.Add($"Timeout: {Timeout.Value.TotalMilliseconds}ms");
                return parts.Count > 0 ? string.Join(", ", parts) : "None";
            }
        }

        /// <summary>
        /// A disposable scope that manages the ambient cancellation context.
        /// </summary>
        private sealed class CancellationScope : IDisposable
        {
            private readonly CancellationContext? _previous;
            private bool _disposed;

            /// <summary>
            /// Creates a new cancellation scope with the specified token and timeout.
            /// </summary>
            /// <param name="cancellationToken">The cancellation token for this scope.</param>
            /// <param name="timeout">The timeout for this scope.</param>
            public CancellationScope(CancellationToken cancellationToken, TimeSpan? timeout)
            {
                _previous = _context.Value;
                _context.Value = new CancellationContext(cancellationToken, timeout);
            }

            /// <summary>
            /// Restores the previous cancellation context.
            /// </summary>
            public void Dispose()
            {
                if (!_disposed)
                {
                    _context.Value = _previous;
                    _disposed = true;
                }
            }
        }
    }
}
