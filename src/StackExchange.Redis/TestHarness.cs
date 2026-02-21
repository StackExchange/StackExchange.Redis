using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using RESPite;
using RESPite.Messages;

namespace StackExchange.Redis;

/// <summary>
/// Allows unit testing RESP formatting and parsing.
/// </summary>
[Experimental(Experiments.UnitTesting, UrlFormat = Experiments.UrlFormat)]
public class TestHarness(CommandMap? commandMap = null, RedisChannel channelPrefix = default, RedisKey keyPrefix = default)
{
    /// <summary>
    /// Channel prefix to use when writing <see cref="RedisChannel"/> values.
    /// </summary>
    public RedisChannel ChannelPrefix { get; } = channelPrefix;

    /// <summary>
    /// Channel prefix to use when writing <see cref="RedisChannel"/> values.
    /// </summary>
    public RedisKey KeyPrefix => _keyPrefix;
    private readonly byte[]? _keyPrefix = keyPrefix;

    /// <summary>
    /// The command map to use when writing root commands.
    /// </summary>
    public CommandMap CommandMap { get; } = commandMap ?? CommandMap.Default;

    /// <summary>
    /// Write a RESP frame from a command and set of arguments.
    /// </summary>
    public byte[] Write(string command, params ICollection<object> args)
    {
        var msg = new RedisDatabase.ExecuteMessage(CommandMap, -1, CommandFlags.None, command, Fixup(args));
        var writer = new MessageWriter(ChannelPrefix, CommandMap);
        ReadOnlyMemory<byte> payload = default;
        try
        {
            msg.WriteTo(writer);
            payload = writer.Flush();
            return payload.Span.ToArray();
        }
        catch
        {
            writer.Revert();
            throw;
        }
        finally
        {
            MessageWriter.Release(payload);
        }
    }

    /// <summary>
    /// Write a RESP frame from a command and set of arguments.
    /// </summary>
    public void Write(IBufferWriter<byte> target, string command, params ICollection<object> args)
    {
        // if we're using someone else's buffer writer, then we don't need to worry about our local
        // memory-management rules
        if (target is null) throw new ArgumentNullException(nameof(target));
        var msg = new RedisDatabase.ExecuteMessage(CommandMap, -1, CommandFlags.None, command, Fixup(args));
        var writer = new MessageWriter(ChannelPrefix, CommandMap, target);
        msg.WriteTo(writer);
    }

    /// <summary>
    /// Report a validation failure.
    /// </summary>
    protected virtual void OnValidateFail(in RedisKey expected, in RedisKey actual)
        => throw new InvalidOperationException($"Routing key is not equal: '{expected}' vs '{actual}' (hint: override {nameof(OnValidateFail)})");

    /// <summary>
    /// Report a validation failure.
    /// </summary>
    protected virtual void OnValidateFail(string expected, string actual)
        => throw new InvalidOperationException($"RESP is not equal: '{expected}' vs '{actual}' (hint: override {nameof(OnValidateFail)})");

    /// <summary>
    /// Report a validation failure.
    /// </summary>
    protected virtual void OnValidateFail(ReadOnlyMemory<byte> expected, ReadOnlyMemory<byte> actual)
        => OnValidateFail(Encoding.UTF8.GetString(expected.Span), Encoding.UTF8.GetString(actual.Span));

    /// <summary>
    /// Write a RESP frame from a command and set of arguments, and allow a callback to validate
    /// the RESP content.
    /// </summary>
    public void ValidateResp(ReadOnlySpan<byte> expected, string command, params ICollection<object> args)
    {
        var msg = new RedisDatabase.ExecuteMessage(CommandMap, -1, CommandFlags.None, command, Fixup(args));
        var writer = new MessageWriter(ChannelPrefix, CommandMap);
        ReadOnlyMemory<byte> actual = default;
        byte[]? lease = null;
        try
        {
            msg.WriteTo(writer);
            actual = writer.Flush();
            if (!expected.SequenceEqual(actual.Span))
            {
                lease = ArrayPool<byte>.Shared.Rent(expected.Length);
                expected.CopyTo(lease);
                OnValidateFail(lease.AsMemory(0, expected.Length), lease);
            }
        }
        catch
        {
            writer.Revert();
            throw;
        }
        finally
        {
            if (lease is not null) ArrayPool<byte>.Shared.Return(lease);
            MessageWriter.Release(actual);
        }
    }

    private ICollection<object> Fixup(ICollection<object>? args)
    {
        if (_keyPrefix is { Length: > 0 } && args is { } && args.Any(x => x is RedisKey))
        {
            object[] copy = new object[args.Count];
            int i = 0;
            foreach (object value in args)
            {
                if (value is RedisKey key)
                {
                    copy[i++] = RedisKey.WithPrefix(_keyPrefix, key);
                }
                else
                {
                    copy[i++] = value;
                }
            }

            return copy;
        }

        return args ?? [];
    }

    /// <summary>
    /// Write a RESP frame from a command and set of arguments, and allow a callback to validate
    /// the RESP content.
    /// </summary>
    public void ValidateResp(string expected, string command, params ICollection<object> args)
    {
        var msg = new RedisDatabase.ExecuteMessage(CommandMap, -1, CommandFlags.None, command, Fixup(args));
        var writer = new MessageWriter(ChannelPrefix, CommandMap);
        ReadOnlyMemory<byte> payload = default;
        char[]? lease = null;
        try
        {
            msg.WriteTo(writer);
            payload = writer.Flush();
            lease = ArrayPool<char>.Shared.Rent(Encoding.UTF8.GetMaxCharCount(payload.Length));
            var chars = Encoding.UTF8.GetChars(payload.Span, lease.AsSpan());
            var actual = lease.AsSpan(0, chars);
            if (!actual.SequenceEqual(expected))
            {
                OnValidateFail(expected, actual.ToString());
            }
        }
        catch
        {
            writer.Revert();
            throw;
        }
        finally
        {
            if (lease is not null) ArrayPool<char>.Shared.Return(lease);
            MessageWriter.Release(payload);
        }
    }

    /// <summary>
    /// A callback with a payload buffer.
    /// </summary>
    public delegate void BufferValidator(scoped ReadOnlySpan<byte> buffer);

    /// <summary>
    /// Deserialize a RESP frame as a <see cref="RedisResult"/>.
    /// </summary>
    public RedisResult Read(ReadOnlySpan<byte> value)
    {
        var reader = new RespReader(value);
        if (!RedisResult.TryCreate(null, ref reader, out var result))
        {
            throw new ArgumentException(nameof(value));
        }
        return result;
    }

    /// <summary>
    /// Convenience handler for comparing span fragments, typically used with "Assert.Equal" or similar
    /// as the handler.
    /// </summary>
    public static void AssertEqual(
        ReadOnlySpan<byte> expected,
        ReadOnlySpan<byte> actual,
        Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> handler)
    {
        if (!expected.SequenceEqual(actual)) Fault(expected, actual, handler);
        static void Fault(
            ReadOnlySpan<byte> expected,
            ReadOnlySpan<byte> actual,
            Action<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> handler)
        {
            var lease = ArrayPool<byte>.Shared.Rent(expected.Length + actual.Length);
            try
            {
                var leaseMemory = lease.AsMemory();
                var x = leaseMemory.Slice(0, expected.Length);
                var y = leaseMemory.Slice(expected.Length, actual.Length);
                expected.CopyTo(x.Span);
                actual.CopyTo(y.Span);
                handler(x, y);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(lease);
            }
        }
    }

    /// <summary>
    /// Convenience handler for comparing span fragments, typically used with "Assert.Equal" or similar
    /// as the handler.
    /// </summary>
    public static void AssertEqual(
        string expected,
        ReadOnlySpan<byte> actual,
        Action<string, string> handler)
    {
        var lease = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetMaxByteCount(expected.Length));
        try
        {
            var bytes = Encoding.UTF8.GetBytes(expected.AsSpan(), lease.AsSpan());
            var span = lease.AsSpan(0, bytes);
            if (!span.SequenceEqual(actual)) handler(expected, Encoding.UTF8.GetString(span));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lease);
        }
    }

    /// <summary>
    /// Verify that the routing of a command matches the intent.
    /// </summary>
    public void ValidateRouting(in RedisKey expected, params ICollection<object> args)
    {
        var expectedWithPrefix = RedisKey.WithPrefix(_keyPrefix, expected);
        var actual = ServerSelectionStrategy.NoSlot;

        RedisKey last = RedisKey.Null;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (args is not null)
        {
            foreach (var arg in args)
            {
                if (arg is RedisKey key)
                {
                    last = RedisKey.WithPrefix(_keyPrefix, key);
                    var slot = ServerSelectionStrategy.GetHashSlot(last);
                    actual = ServerSelectionStrategy.CombineSlot(actual, slot);
                }
            }
        }

        if (ServerSelectionStrategy.GetHashSlot(expectedWithPrefix) != actual) OnValidateFail(expectedWithPrefix, last);
    }
}
