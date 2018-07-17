using System.Collections.Generic;

namespace StackExchange.Redis.Profiling
{
    /// <summary>
    /// <para>A collection of IProfiledCommands.</para>
    /// <para>This is a very light weight data structure, only supporting enumeration.</para>
    /// <para>
    /// While it implements IEnumerable, it there are fewer allocations if one uses
    /// it's explicit GetEnumerator() method.  Using `foreach` does this automatically.
    /// </para>
    /// <para>This type is not threadsafe.</para>
    /// </summary>
    public readonly struct ProfiledCommandEnumerable : IEnumerable<IProfiledCommand>
    {
        /// <summary>
        /// <para>
        /// Implements IEnumerator for ProfiledCommandEnumerable.
        /// This implementation is comparable to List.Enumerator and Dictionary.Enumerator,
        /// and is provided to reduce allocations in the common (ie. foreach) case.
        /// </para>
        /// <para>This type is not threadsafe.</para>
        /// </summary>
        public struct Enumerator : IEnumerator<IProfiledCommand>
        {
            private ProfiledCommand Head;
            private ProfiledCommand CurrentBacker;

            private bool IsEmpty => Head == null;
            private bool IsUnstartedOrFinished => CurrentBacker == null;

            internal Enumerator(ProfiledCommand head)
            {
                Head = head;
                CurrentBacker = null;
            }

            /// <summary>
            /// The current element.
            /// </summary>
            public IProfiledCommand Current => CurrentBacker;

            object System.Collections.IEnumerator.Current => CurrentBacker;

            /// <summary>
            /// Advances the enumeration, returning true if there is a new element to consume and false
            /// if enumeration is complete.
            /// </summary>
            public bool MoveNext()
            {
                if (IsEmpty) return false;

                if (IsUnstartedOrFinished)
                {
                    CurrentBacker = Head;
                }
                else
                {
                    CurrentBacker = CurrentBacker.NextElement;
                }

                return CurrentBacker != null;
            }

            /// <summary>
            /// Resets the enumeration.
            /// </summary>
            public void Reset()
            {
                CurrentBacker = null;
            }

            /// <summary>
            /// Disposes the enumeration.
            /// subsequent attempts to enumerate results in undefined behavior.
            /// </summary>
            public void Dispose()
            {
                CurrentBacker = Head = null;
            }
        }

        private readonly ProfiledCommand Head;

        internal ProfiledCommandEnumerable(ProfiledCommand head)
        {
            Head = head;
        }

        /// <summary>
        /// <para>
        /// Returns an implementor of IEnumerator that, provided it isn't accessed
        /// though an interface, avoids allocations.
        /// </para>
        /// <para>`foreach` will automatically use this method.</para>
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(Head);

        IEnumerator<IProfiledCommand> IEnumerable<IProfiledCommand>.GetEnumerator() => GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
