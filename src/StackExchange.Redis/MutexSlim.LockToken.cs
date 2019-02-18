using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace StackExchange.Redis
{
    partial class MutexSlim
    {
        /// <summary>
        /// The result of a Wait/WaitAsync operation on MutexSlim; the caller *must* check Success to see whether the mutex was obtained
        /// </summary>
        public readonly struct LockToken : IDisposable
        {
            private readonly MutexSlim _parent;
            private readonly int _token;

            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator true(LockToken token) => LockState.GetState(token._token) == LockState.Success;
            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool operator false(LockToken token) => LockState.GetState(token._token) != LockState.Success;

            /// <summary>
            /// Indicates whether the mutex was successfully taken
            /// </summary>
            public bool Success => LockState.GetState(_token) == LockState.Success;

            internal bool IsCompleted => LockState.IsCompleted(_token);

            internal bool IsCompletedSuccessfully => LockState.IsCompletedSuccessfully(_token);

            internal bool IsCanceled => LockState.IsCanceled(_token);

            internal LockToken(MutexSlim parent, int token)
            {
                _parent = parent;
                _token = token;
            }

            /// <summary>
            /// Release the mutex, if obtained
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void Dispose()
            {
                if (Success) _parent.Release(_token, demandMatch: true);
            }

            internal LockToken AssertNotCanceled()
            {
                if (IsCanceled) ThrowCanceled();
                return this;
                void ThrowCanceled() => throw new TaskCanceledException();
            }
        }
    }
}
