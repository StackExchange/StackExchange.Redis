using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace StackExchange.Redis
{
    /// <summary>
    /// Big ol' wrapper around most of the profiling storage logic, 'cause it got too big to just live in ConnectionMultiplexer.
    /// </summary>
    sealed class ProfileContextTracker
    {
        /// <summary>
        /// Necessary, because WeakReference can't be readily comparable (since the reference is... weak).
        /// 
        /// This lets us detect leaks* with some reasonable confidence, and cleanup periodically.
        /// 
        /// Some calisthenics are done to avoid allocating WeakReferences for no reason, as often
        /// we're just looking up ProfileStorage.
        /// 
        /// * Somebody starts profiling, but for whatever reason never *stops* with a context object
        /// </summary>
        struct ProfileContextCell : IEquatable<ProfileContextCell>
        {
            // This is a union of (object|WeakReference); if it's a WeakReference
            //   then we're actually interested in it's Target, otherwise
            //   we're concerned about the actual value of Reference
            object Reference;
            
            // It is absolutely crucial that this value **never change** once instantiated
            readonly int HashCode;

            public bool IsContextLeaked
            {
                get
                {
                    object ignored;
                    return !TryGetTarget(out ignored);
                }
            }

            private ProfileContextCell(object forObj, bool isEphemeral)
            {
                HashCode = forObj.GetHashCode();

                if (isEphemeral)
                {
                    Reference = forObj;
                }
                else
                {
                    Reference = new WeakReference(forObj, trackResurrection: true); // ughhh, have to handle finalizers
                }
            }

            /// <summary>
            /// Suitable for use as a key into something.
            /// 
            /// This instance **WILL NOT** keep forObj alive, so it can
            /// be copied out of the calling method's scope.
            /// </summary>
            public static ProfileContextCell ToStoreUnder(object forObj)
            {
                return new ProfileContextCell(forObj, isEphemeral: false);
            }

            /// <summary>
            /// Only suitable for looking up.
            /// 
            /// This instance **ABSOLUTELY WILL** keep forObj alive, so this
            /// had better not be copied into anything outside the scope of the
            /// calling method.
            /// </summary>
            public static ProfileContextCell ToLookupBy(object forObj)
            {
                return new ProfileContextCell(forObj, isEphemeral: true);
            }

            bool TryGetTarget(out object target)
            {
                var asWeakRef = Reference as WeakReference;

                if (asWeakRef == null)
                {
                    target = Reference;
                    return true;
                }

                // Do not use IsAlive here, it's race city
                target = asWeakRef.Target;
                return target != null;
            }

            public override bool Equals(object obj)
            {
                if (!(obj is ProfileContextCell)) return false;

                return Equals((ProfileContextCell)obj);
            }

            public override int GetHashCode()
            {
                return HashCode;
            }

            public bool Equals(ProfileContextCell other)
            {
                object thisObj, otherObj;

                if (other.TryGetTarget(out otherObj) != TryGetTarget(out thisObj)) return false;

                // dead references are equal
                if (thisObj == null) return true;

                return thisObj.Equals(otherObj);
            }
        }

        // provided so default behavior doesn't do any boxing, for sure
        sealed class ProfileContextCellComparer : IEqualityComparer<ProfileContextCell>
        {
            public static readonly ProfileContextCellComparer Singleton = new ProfileContextCellComparer();

            private ProfileContextCellComparer() { }

            public bool Equals(ProfileContextCell x, ProfileContextCell y)
            {
                return x.Equals(y);
            }

            public int GetHashCode(ProfileContextCell obj)
            {
                return obj.GetHashCode();
            }
        }

        private long lastCleanupSweep;
        private ConcurrentDictionary<ProfileContextCell, ConcurrentProfileStorageCollection> profiledCommands;

        public int ContextCount => profiledCommands.Count;

        public ProfileContextTracker()
        {
            profiledCommands = new ConcurrentDictionary<ProfileContextCell, ConcurrentProfileStorageCollection>(ProfileContextCellComparer.Singleton);
            lastCleanupSweep = DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Registers the passed context with a collection that can be retried with subsequent calls to TryGetValue.
        /// 
        /// Returns false if the passed context object is already registered.
        /// </summary>
        public bool TryCreate(object ctx)
        {
            var cell = ProfileContextCell.ToStoreUnder(ctx);

            // we can't pass this as a delegate, because TryAdd may invoke the factory multiple times,
            //   which would lead to over allocation.
            var storage = ConcurrentProfileStorageCollection.GetOrCreate();
            return profiledCommands.TryAdd(cell, storage);
        }

        /// <summary>
        /// Returns true and sets val to the tracking collection associated with the given context if the context
        /// was registered with TryCreate.
        /// 
        /// Otherwise returns false and sets val to null.
        /// </summary>
        public bool TryGetValue(object ctx, out ConcurrentProfileStorageCollection val)
        {
            var cell = ProfileContextCell.ToLookupBy(ctx);
            return profiledCommands.TryGetValue(cell, out val);
        }

        /// <summary>
        /// Removes a context, setting all commands to a (non-thread safe) enumerable of
        /// all the commands attached to that context.
        /// 
        /// If the context was never registered, will return false and set commands to null.
        /// 
        /// Subsequent calls to TryRemove with the same context will return false unless it is
        /// re-registered with TryCreate.
        /// </summary>
        public bool TryRemove(object ctx, out ProfiledCommandEnumerable commands)
        {
            var cell = ProfileContextCell.ToLookupBy(ctx);
            ConcurrentProfileStorageCollection storage;
            if (!profiledCommands.TryRemove(cell, out storage))
            {
                commands = default(ProfiledCommandEnumerable);
                return false;
            }

            commands = storage.EnumerateAndReturnForReuse();
            return true;
        }

        /// <summary>
        /// If enough time has passed (1 minute) since the last call, this does walk of all contexts
        /// and removes those that the GC has collected.
        /// </summary>
        public bool TryCleanup()
        {
            const long SweepEveryTicks = 600000000; // once a minute, tops

            var now = DateTime.UtcNow.Ticks;    // resolution on this isn't great, but it's good enough
            var last = lastCleanupSweep;
            var since = now - last;
            if (since < SweepEveryTicks) return false;

            // this is just to keep other threads from wasting time, in theory
            //  it'd be perfectly safe for this to run concurrently
            var saw = Interlocked.CompareExchange(ref lastCleanupSweep, now, last);
            if (saw != last) return false;

            if (profiledCommands.Count == 0) return false;

            using(var e = profiledCommands.GetEnumerator())
            {
                while(e.MoveNext())
                {
                    var pair = e.Current;
                    if(pair.Key.IsContextLeaked)
                    {
                        ConcurrentProfileStorageCollection abandoned;
                        if(profiledCommands.TryRemove(pair.Key, out abandoned))
                        {
                            // shove it back in the pool, but don't bother enumerating
                            abandoned.ReturnForReuse();
                        }
                    }
                }
            }

            return true;
        }
    }
}
