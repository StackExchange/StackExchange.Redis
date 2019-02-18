using System.Runtime.CompilerServices;

namespace StackExchange.Redis
{
    partial class MutexSlim
    {
        internal static class LockState
        {   // using this as a glorified enum; can't use enum directly because
            // or Interlocked etc support
            public const int
                Timeout = 0, // we want "completed with failure" to be the implicit zero default, so default(LockToken) is a miss
                Pending = 1,
                Success = 2, // note: careful choice of numbers here allows IsCompletedSuccessfully to check whether the LSB is set
                Canceled = 3;

                //Pending = 0,
                //Canceled = 1,
                //Success = 2, // note: we make use of the fact that Success/Timeout use the
                //Timeout = 3; // 2nd bit for IsCompletedSuccessfully; don't change casually!

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int GetNextToken(int token)
                // 2 low bits are status; 30 high bits are counter
                => (int)((((uint)token >> 2) + 1) << 2) | Success;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int ChangeState(int token, int state)
                => (token & ~3) | state; // retain counter portion; swap state portion

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int GetState(int token) => token & 3;

            // "completed", in Task/ValueTask terms, includes cancelation - only omits pending
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCompleted(int token) => (token & 3) != Pending;

            // note that "successfully" here doesn't mean "with the lock"; as per Task/ValueTask IsCompletedSuccessfully,
            // it means "completed, and not faulted or canceled"; see LockState - we can check that by testing the
            // second bit (Success=2,Timeout=3)
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCompletedSuccessfully(int token) => (token & 1) == 0;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static bool IsCanceled(int token) => (token & 3) == Canceled;
        }

    }
}
