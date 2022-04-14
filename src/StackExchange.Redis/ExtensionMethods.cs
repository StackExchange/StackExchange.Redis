using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net.Security;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Pipelines.Sockets.Unofficial.Arenas;

namespace StackExchange.Redis
{
    /// <summary>
    /// Utility methods.
    /// </summary>
    public static class ExtensionMethods
    {
        /// <summary>
        /// Create a dictionary from an array of HashEntry values.
        /// </summary>
        /// <param name="hash">The entry to convert to a dictionary.</param>
        [return: NotNullIfNotNull("hash")]
        public static Dictionary<string,string>? ToStringDictionary(this HashEntry[]? hash)
        {
            if (hash is null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(hash.Length, StringComparer.Ordinal);
            for(int i = 0; i < hash.Length; i++)
            {
                result.Add(hash[i].name!, hash[i].value!);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of HashEntry values.
        /// </summary>
        /// <param name="hash">The entry to convert to a dictionary.</param>
        [return: NotNullIfNotNull("hash")]
        public static Dictionary<RedisValue, RedisValue>? ToDictionary(this HashEntry[]? hash)
        {
            if (hash is null)
            {
                return null;
            }

            var result = new Dictionary<RedisValue, RedisValue>(hash.Length);
            for (int i = 0; i < hash.Length; i++)
            {
                result.Add(hash[i].name, hash[i].value);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of SortedSetEntry values.
        /// </summary>
        /// <param name="sortedSet">The set entries to convert to a dictionary.</param>
        [return: NotNullIfNotNull("sortedSet")]
        public static Dictionary<string, double>? ToStringDictionary(this SortedSetEntry[]? sortedSet)
        {
            if (sortedSet is null)
            {
                return null;
            }

            var result = new Dictionary<string, double>(sortedSet.Length, StringComparer.Ordinal);
            for (int i = 0; i < sortedSet.Length; i++)
            {
                result.Add(sortedSet[i].element!, sortedSet[i].score);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of SortedSetEntry values.
        /// </summary>
        /// <param name="sortedSet">The set entries to convert to a dictionary.</param>
        [return: NotNullIfNotNull("sortedSet")]
        public static Dictionary<RedisValue, double>? ToDictionary(this SortedSetEntry[]? sortedSet)
        {
            if (sortedSet is null)
            {
                return null;
            }

            var result = new Dictionary<RedisValue, double>(sortedSet.Length);
            for (int i = 0; i < sortedSet.Length; i++)
            {
                result.Add(sortedSet[i].element, sortedSet[i].score);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of key/value pairs.
        /// </summary>
        /// <param name="pairs">The pairs to convert to a dictionary.</param>
        [return: NotNullIfNotNull("pairs")]
        public static Dictionary<string, string>? ToStringDictionary(this KeyValuePair<RedisKey, RedisValue>[]? pairs)
        {
            if (pairs is null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i++)
            {
                result.Add(pairs[i].Key!, pairs[i].Value!);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of key/value pairs.
        /// </summary>
        /// <param name="pairs">The pairs to convert to a dictionary.</param>
        [return: NotNullIfNotNull("pairs")]
        public static Dictionary<RedisKey, RedisValue>? ToDictionary(this KeyValuePair<RedisKey, RedisValue>[]? pairs)
        {
            if (pairs is null)
            {
                return null;
            }

            var result = new Dictionary<RedisKey, RedisValue>(pairs.Length);
            for (int i = 0; i < pairs.Length; i++)
            {
                result.Add(pairs[i].Key, pairs[i].Value);
            }
            return result;
        }

        /// <summary>
        /// Create a dictionary from an array of string pairs.
        /// </summary>
        /// <param name="pairs">The pairs to convert to a dictionary.</param>
        [return: NotNullIfNotNull("pairs")]
        public static Dictionary<string, string>? ToDictionary(this KeyValuePair<string, string>[]? pairs)
        {
            if (pairs is null)
            {
                return null;
            }

            var result = new Dictionary<string, string>(pairs.Length, StringComparer.Ordinal);
            for (int i = 0; i < pairs.Length; i++)
            {
                result.Add(pairs[i].Key, pairs[i].Value);
            }
            return result;
        }

        /// <summary>
        /// Create an array of RedisValues from an array of strings.
        /// </summary>
        /// <param name="values">The string array to convert to RedisValues.</param>
        [return: NotNullIfNotNull("values")]
        public static RedisValue[]? ToRedisValueArray(this string[]? values)
        {
            if (values is null)
            {
                return null;
            }

            if (values.Length == 0) return Array.Empty<RedisValue>();
            return Array.ConvertAll(values, x => (RedisValue)x);
        }

        /// <summary>
        /// Create an array of strings from an array of values.
        /// </summary>
        /// <param name="values">The values to convert to an array.</param>
        [return: NotNullIfNotNull("values")]
        public static string?[]? ToStringArray(this RedisValue[]? values)
        {
            if (values == null)
            {
                return null;
            }

            if (values.Length == 0) return Array.Empty<string>();
            return Array.ConvertAll(values, x => (string?)x);
        }

        internal static void AuthenticateAsClient(this SslStream ssl, string host, SslProtocols? allowedProtocols, bool checkCertificateRevocation)
        {
            if (!allowedProtocols.HasValue)
            {
                //Default to the sslProtocols defined by the .NET Framework
                AuthenticateAsClientUsingDefaultProtocols(ssl, host);
                return;
            }

            var certificateCollection = new X509CertificateCollection();
            ssl.AuthenticateAsClient(host, certificateCollection, allowedProtocols.Value, checkCertificateRevocation);
        }

        private static void AuthenticateAsClientUsingDefaultProtocols(SslStream ssl, string host)
        {
            ssl.AuthenticateAsClient(host);
        }

        /// <summary>
        /// Represent a byte-Lease as a read-only Stream.
        /// </summary>
        /// <param name="bytes">The lease upon which to base the stream.</param>
        /// <param name="ownsLease">If true, disposing the stream also disposes the lease.</param>
        [return: NotNullIfNotNull("bytes")]
        public static Stream? AsStream(this Lease<byte>? bytes, bool ownsLease = true)
        {
            if (bytes is null)
            {
                return null; // GIGO
            }

            var segment = bytes.ArraySegment;
            if (ownsLease)
            {
                return new LeaseMemoryStream(segment, bytes);
            }
            return new MemoryStream(segment.Array!, segment.Offset, segment.Count, false, true);
        }

        /// <summary>
        /// Decode a byte-Lease as a String, optionally specifying the encoding (UTF-8 if omitted).
        /// </summary>
        /// <param name="bytes">The bytes to decode.</param>
        /// <param name="encoding">The encoding to use.</param>
        [return: NotNullIfNotNull("bytes")]
        public static string? DecodeString(this Lease<byte> bytes, Encoding? encoding = null)
        {
            if (bytes is null)
            {
                return null;
            }

            encoding ??= Encoding.UTF8;
            if (bytes.Length == 0)
            {
                return "";
            }
            var segment = bytes.ArraySegment;
            return encoding.GetString(segment.Array!, segment.Offset, segment.Count);
        }

        /// <summary>
        /// Decode a byte-Lease as a String, optionally specifying the encoding (UTF-8 if omitted).
        /// </summary>
        /// <param name="bytes">The bytes to decode.</param>
        /// <param name="encoding">The encoding to use.</param>
        [return: NotNullIfNotNull("bytes")]
        public static Lease<char>? DecodeLease(this Lease<byte>? bytes, Encoding? encoding = null)
        {
            if (bytes is null)
            {
                return null;
            }

            encoding ??= Encoding.UTF8;
            if (bytes.Length == 0)
            {
                return Lease<char>.Empty;
            }
            var bytesSegment = bytes.ArraySegment;
            var charCount = encoding.GetCharCount(bytesSegment.Array!, bytesSegment.Offset, bytesSegment.Count);
            var chars = Lease<char>.Create(charCount, false);
            var charsSegment = chars.ArraySegment;
            encoding.GetChars(bytesSegment.Array!, bytesSegment.Offset, bytesSegment.Count, charsSegment.Array!, charsSegment.Offset);
            return chars;
        }

        private sealed class LeaseMemoryStream : MemoryStream
        {
            private readonly IDisposable _parent;
            public LeaseMemoryStream(ArraySegment<byte> segment, IDisposable parent)
                : base(segment.Array!, segment.Offset, segment.Count, false, true)
                => _parent = parent;

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) _parent.Dispose();
            }
        }

        // IMPORTANT: System.Numerics.Vectors is just... broken on .NET with anything < net472; the dependency
        // indirection routinely fails and causes epic levels of fail. We're going to get around this by simply
        // *not using SpanHelpers.IndexOf* (which is what uses it) for net < net472 builds. I've tried every
        // trick (including some that are pure evil), and I can't see a better mechanism. Ultimately, the bindings
        // fail in unusual and unexpected ways, causing:
        //
        //     Could not load file or assembly 'System.Numerics.Vectors, Version=4.1.3.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a'
        //     or one of its dependencies.The located assembly's manifest definition does not match the assembly reference. (Exception from HRESULT: 0x80131040)
        //
        // also; note that the NuGet tools *do not* reliably (or even occasionally) produce the correct
        // assembly-binding-redirect entries to fix this up, so; it would present an unreasonable support burden
        // otherwise. And yes, I've tried explicitly referencing System.Numerics.Vectors in the manifest to
        // force it... nothing. Nada.

#if VECTOR_SAFE
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int VectorSafeIndexOf(this ReadOnlySpan<byte> span, byte value)
            => span.IndexOf(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int VectorSafeIndexOfCRLF(this ReadOnlySpan<byte> span)
        {
            ReadOnlySpan<byte> CRLF = stackalloc byte[2] { (byte)'\r', (byte)'\n' };
            return span.IndexOf(CRLF);
        }
#else
        internal static int VectorSafeIndexOf(this ReadOnlySpan<byte> span, byte value)
        {
            // yes, this has zero optimization; I'm OK with this as the fallback strategy
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i] == value) return i;
            }
            return -1;
        }
        internal static int VectorSafeIndexOfCRLF(this ReadOnlySpan<byte> span)
        {
            // yes, this has zero optimization; I'm OK with this as the fallback strategy
            for (int i = 1; i < span.Length; i++)
            {
                if (span[i] == '\n' && span[i-1] == '\r') return i - 1;
            }
            return -1;
        }
#endif

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static T[]? ToArray<T>(in this RawResult result, Projection<RawResult, T> selector)
            => result.IsNull ? null : result.GetItems().ToArray(selector);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static TTo[]? ToArray<TTo, TState>(in this RawResult result, Projection<RawResult, TState, TTo> selector, in TState state)
            => result.IsNull ? null : result.GetItems().ToArray(selector, in state);

        /// <summary>
        /// Create an array with the contents of the sequence, applying a projection
        /// </summary>
        internal static TTo[] ToArray<TFrom, TTo>(this in ReadOnlySequence<TFrom> source, Projection<TFrom, TTo> projection)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection);
            return arr;
        }

        /// <summary>
        /// Create an array with the contents of the sequence, applying a projection
        /// </summary>
        internal static TTo[] ToArray<TFrom, TState, TTo>(this in ReadOnlySequence<TFrom> source, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (source.IsEmpty) return Array.Empty<TTo>();
            var arr = new TTo[source.Length];
            source.CopyTo(arr, projection, in state);
            return arr;
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        internal static void CopyTo<TFrom, TTo>(this in ReadOnlySequence<TFrom> source, Span<TTo> destination, Projection<TFrom, TTo> projection)
        {
            if (!TryCopyTo<TFrom, TTo>(in source, destination, projection))
                Throw();
            static void Throw() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        internal static void CopyTo<TFrom, TState, TTo>(this in ReadOnlySequence<TFrom> source, Span<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            if (!TryCopyTo<TFrom, TState, TTo>(in source, destination, projection, in state))
                Throw();
            static void Throw() => throw new InvalidOperationException();
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        internal static bool TryCopyTo<TFrom, TTo>(this in ReadOnlySequence<TFrom> source, Span<TTo> destination, Projection<TFrom, TTo> projection)
        {
            static void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (source.IsSingleSegment)
            {
                var span = source.First.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    destination[i] = projection(in span[i]);
                }
            }
            else
            {
                int offset = 0;
                foreach (var segment in source)
                {
                    var span = segment.Span;
                    for (int i = 0; i < span.Length; i++)
                    {
                        destination[offset++] = projection(in span[i]);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Copy the data from a sequence to a span, applying a projection
        /// </summary>
        internal static bool TryCopyTo<TFrom, TState, TTo>(this in ReadOnlySequence<TFrom> source, Span<TTo> destination, Projection<TFrom, TState, TTo> projection, in TState state)
        {
            static void ThrowNoProjection() => throw new ArgumentNullException(nameof(projection));

            if (projection == null) ThrowNoProjection();
            if (source.Length > destination.Length) return false;

            if (source.IsSingleSegment)
            {
                var span = source.First.Span;
                for (int i = 0; i < span.Length; i++)
                {
                    destination[i] = projection(in span[i], in state);
                }
            }
            else
            {
                int offset = 0;
                foreach (var segment in source)
                {
                    var span = segment.Span;
                    for (int i = 0; i < span.Length; i++)
                    {
                        destination[offset++] = projection(in span[i], in state);
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Create an array with the contents of the sequence; if possible, an existing
        /// wrapped array may be reused
        /// </summary>
        internal static T[] ToArray<T>(this in ReadOnlySequence<T> source)
        {
            if (source.IsEmpty) return Array.Empty<T>();
            if (source.IsSingleSegment)
            {
                if (MemoryMarshal.TryGetArray(source.First, out var segment)
                    && segment.Offset == 0 && segment.Array != null && segment.Count == segment.Array.Length)
                {
                    return segment.Array; // the source was wrapping an array *exactly*
                }
            }
            var arr = new T[source.Length];
            source.CopyTo(arr);
            return arr;
        }

        /// <summary>
        /// Copy the contents of the sequence into a contiguous region
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CopyTo<T>(this in ReadOnlySequence<T> source, Span<T> destination)
        {
            if (source.IsSingleSegment) source.First.Span.CopyTo(destination);
            else if (!TrySlowCopy(source, destination)) ThrowLengthError();

            static bool TrySlowCopy(in ReadOnlySequence<T> source, Span< T> destination)
            {
                if (destination.Length < source.Length) return false;

                foreach (var span in source)
                {
                    span.Span.CopyTo(destination);
                    destination = destination.Slice(span.Length);
                }
                return true;
            }
            static void ThrowLengthError()
            {
                Span<int> one = stackalloc int[1];
                one.CopyTo(default); // this should give use the CLR's error text (let's hope it doesn't mention sizes!)
            }
        }

        internal static ref readonly T GetRef<T>(this in ReadOnlySequence<T> sequence, int index)
        {
            if (sequence.IsSingleSegment) return ref sequence.First.Span[index];
            foreach (var segment in sequence)
            {
                var span = segment.Span;
                if (index < span.Length) return ref span[index];
                index -= span.Length;
            }
            return ref Throw();
            static ref readonly T Throw() => throw new IndexOutOfRangeException(nameof(index));
        }

        internal static ReadOnlySequenceEnumerator<T> AllEnumerator<T>(this in ReadOnlySequence<T> value)
            => new ReadOnlySequenceEnumerator<T>(in value);

        internal ref struct ReadOnlySequenceEnumerator<T>
        {
            private int _remainingThisSpan, _offsetThisSpan;
            private long _remainingOtherSegments;
            private ReadOnlySequenceSegment<T> _nextSegment;
            private ReadOnlySpan<T> _span;

            internal ReadOnlySequenceEnumerator(in ReadOnlySequence<T> value)
            {
                _span = value.First.Span;
                _remainingThisSpan = _span.Length;
                _offsetThisSpan = -1;
                if (value.IsSingleSegment)
                {
                    _remainingOtherSegments = 0;
                    _nextSegment = null;
                }
                else
                {
                    _nextSegment = ((ReadOnlySequenceSegment<T>)value.Start.GetObject()).Next;
                    _remainingOtherSegments = value.Length - _span.Length;
                }
                _offsetThisSpan = -1;
            }

            /// <summary>
            /// Attempt to move the next value
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                if (_remainingThisSpan == 0) return MoveNextNonEmptySegment();
                _offsetThisSpan++;
                _remainingThisSpan--;
                return true;
            }

            /// <summary>
            /// Obtain a reference to the current value
            /// </summary>
            public ref readonly T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _span[_offsetThisSpan];
            }

            private bool MoveNextNonEmptySegment()
            {
                ReadOnlySpan<T> span;
                do
                {
                    if (_remainingOtherSegments == 0) return false;

                    span = _nextSegment.Memory.Span;
                    _nextSegment = _nextSegment.Next;

                    if (_remainingOtherSegments <= span.Length)
                    {   // we're at the end
                        span = span.Slice(0, (int)_remainingOtherSegments);
                        _remainingOtherSegments = 0;
                    }
                    else
                    {
                        _remainingOtherSegments -= span.Length;
                    }

                } while (span.IsEmpty); // check for empty segment

                _span = span;
                _remainingThisSpan = span.Length - 1; // because we're consuming one
                _offsetThisSpan = 0;
                return true;
            }

            /// <summary>
            /// Progresses the iterator, asserting that space is available, returning a reference to the next value
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public ref readonly T GetNext()
            {
                if (!MoveNext()) Throw();
                return ref Current;

                static void Throw() => throw new InvalidOperationException($"Enumerator moved out of range");
            }
        }
    }
}
