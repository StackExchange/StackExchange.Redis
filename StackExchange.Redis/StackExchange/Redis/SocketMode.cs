using System;

namespace StackExchange.Redis
{
    /// <summary>
    /// Specifies socket mode for the physical connection
    /// </summary>
    public struct SocketMode : IEquatable<SocketMode>
    {
        private enum _SocketMode
        {
            Abort,
            Poll,
            Async
        }

        private readonly _SocketMode socketMode;

        internal static SocketMode Abort => new SocketMode(_SocketMode.Abort);

        /// <summary>
        /// Using polling threads
        /// </summary>
        public static SocketMode Poll => new SocketMode(_SocketMode.Poll);

        /// <summary>
        /// Using async socket API
        /// </summary>
        public static SocketMode Async => new SocketMode(_SocketMode.Async);

        private SocketMode(_SocketMode socketMode)
        {
            this.socketMode = socketMode;
        }

        #region Equality
        /// <summary>
        /// See Object.Equals
        /// </summary>
        public override bool Equals(object obj)
        {
            if (obj is SocketMode other)
            {
                return Equals(other);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// See Object.GetHashCode
        /// </summary>
        public override int GetHashCode()
        {
            return this.socketMode.GetHashCode();
        }

        /// <summary>
        /// Indicate whether two socket modes are equal
        /// </summary>
        public bool Equals(SocketMode other)
        {
            return this.socketMode == other.socketMode;
        }

        /// <summary>
        /// Indicate whether two socket modes are equal
        /// </summary>
        public static bool operator ==(SocketMode x, SocketMode y)
        {
            return x.Equals(y);
        }

        /// <summary>
        /// Indicate whether two socket modes are not equal
        /// </summary>
        public static bool operator !=(SocketMode x, SocketMode y)
        {
            return !(x == y);
        }
        #endregion

        /// <summary>
        /// Obtain string representation of the socket mode
        /// </summary>
        public override string ToString()
        {
            return this.socketMode.ToString();
        }
    }
}
