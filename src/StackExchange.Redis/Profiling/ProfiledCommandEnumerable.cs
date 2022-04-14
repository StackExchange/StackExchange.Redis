using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

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
            private ProfiledCommand? Head, CurrentBacker;

            private bool IsEmpty => Head == null;
            private bool IsUnstartedOrFinished => CurrentBacker == null;

            internal Enumerator(ProfiledCommand? head)
            {
                Head = head;
                CurrentBacker = null;
            }

            /// <summary>
            /// The current element.
            /// </summary>
            public IProfiledCommand Current => CurrentBacker!;

            object System.Collections.IEnumerator.Current => CurrentBacker!;

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
                    CurrentBacker = CurrentBacker!.NextElement;
                }

                return CurrentBacker != null;
            }

            /// <summary>
            /// Resets the enumeration.
            /// </summary>
            public void Reset() => CurrentBacker = null;

            /// <summary>
            /// Disposes the enumeration.
            /// subsequent attempts to enumerate results in undefined behavior.
            /// </summary>
            public void Dispose() => CurrentBacker = Head = null;
        }

        private readonly ProfiledCommand? _head;
        private readonly int _count;
        /// <summary>
        /// Returns the number of commands captured in this snapshot
        /// </summary>
        public int Count() => _count;

        /// <summary>
        /// Returns the number of commands captured in this snapshot that match a condition
        /// </summary>
        /// <param name="predicate">The predicate to match.</param>
        public int Count(Func<IProfiledCommand, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));
            if (_count == 0) return 0;

            int result = 0;
            var cur = _head;
            for (int i = 0; i < _count; i++)
            {
                if (cur != null && predicate(cur)) result++;
                cur = cur!.NextElement;
            }
            return result;
        }

        /// <summary>
        /// Returns the captured commands as an array
        /// </summary>
        public IProfiledCommand[] ToArray()
        {   // exploit the fact that we know the length
            if (_count == 0) return Array.Empty<IProfiledCommand>();

            var arr = new IProfiledCommand[_count];
            ProfiledCommand? cur = _head;
            for (int i = 0; i < _count; i++)
            {
                arr[i] = cur!;
                cur = cur!.NextElement;
            }
            return arr;
        }

        /// <summary>
        /// Returns the captured commands as a list
        /// </summary>
        public List<IProfiledCommand> ToList()
        {   // exploit the fact that we know the length
            var list = new List<IProfiledCommand>(_count);
            ProfiledCommand? cur = _head;
            while (cur != null)
            {
                list.Add(cur);
                cur = cur.NextElement;
            }
            return list;
        }
        internal ProfiledCommandEnumerable(int count, ProfiledCommand? head)
        {
            _count = count;
            _head = head;

            Debug.Assert(_count == Enumerable.Count(this));
        }

        /// <summary>
        /// <para>
        /// Returns an implementor of IEnumerator that, provided it isn't accessed
        /// though an interface, avoids allocations.
        /// </para>
        /// <para>`foreach` will automatically use this method.</para>
        /// </summary>
        public Enumerator GetEnumerator() => new Enumerator(_head);

        IEnumerator<IProfiledCommand> IEnumerable<IProfiledCommand>.GetEnumerator() => GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
