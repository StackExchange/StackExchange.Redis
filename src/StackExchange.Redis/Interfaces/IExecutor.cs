using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Redis.Interfaces
{
    internal interface IExecutor
    {
        ValueTask<TResult> ExecuteAsync<TCommand, TResult>(TCommand command, IResultParser<TResult> parser, CommandOptions options = default)
            where TCommand : ICommand;
    }

    internal static class ExecutorExtensions
    {
        public static ValueTask<RedisResult> ExecuteAsync<TCommand>(this IExecutor executor, TCommand command, CommandOptions options = default)
            where TCommand : ICommand
            => executor.ExecuteAsync<TCommand, RedisResult>(command, ResultParser.RedisResult, options);
    }

    internal interface IDbFoo : IExecutor
    {
        int Database { get; }
        RedisKey KeyPrefix { get; }
    }
    internal static class StringCommands
    {
        public static ValueTask<RedisValue> StringGetAsync(IDbFoo database, RedisKey key, CommandOptions options = default)
            => database.ExecuteAsync(Command.Create(key), ResultParser.RedisValue, options);

    }

    internal static class ListCommands
    {
        public static ValueTask<Lease<RedisValue>> ListRangeAsync(IDbFoo database, RedisKey key, Range range = default, CommandOptions options = default)
            => database.ExecuteAsync(Command.Create(key), ResultParser.LeaseRedisValue, options);

    }
    static class Command
    {
        internal static KeyCommand Create(RedisKey key) => throw new NotImplementedException();
    }
    readonly struct KeyCommand : ICommand
    {

    }
    internal interface ICommand
    {
        
    }

    internal static class ResultParser
    {
        public static IResultParser<RedisResult> RedisResult => throw new NotImplementedException();
        public static IResultParser<RedisValue> RedisValue => throw new NotImplementedException();
        public static IResultParser<Lease<RedisValue>> LeaseRedisValue => throw new NotImplementedException();
    }

    internal readonly struct Lease<T> : IDisposable
    {
        internal Lease(Memory<T> memory, Action<Memory<T>> onDispose)
        {
            Memory = memory;
            _onDispose = onDispose;
        }
        public Memory<T> Memory { get; }
        private readonly Action<Memory<T>> _onDispose;
        public Span<T> Span => Memory.Span;

        public void Dispose()
            => _onDispose?.Invoke(Memory);
    }

    interface IResultParser<TResult>
    {
        TResult Parse(RawResult result);
    }

    internal readonly struct CommandOptions
    {
        public CommandFlags CommandFlags { get; }
        public int Database { get; }
        public CancellationToken CancellationToken { get; }
        public RedisKey KeyPrefix { get; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CommandOptions With(in RedisKey keyPrefix) => new CommandOptions(in this, in keyPrefix);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CommandOptions With(CommandFlags commandFlags) => new CommandOptions(in this, commandFlags);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CommandOptions With(in CancellationToken cancellationToken) => new CommandOptions(in this, in cancellationToken);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private CommandOptions(in CommandOptions value, in CancellationToken cancellationToken)
        {
            if (value.CancellationToken.CanBeCanceled) ThrowInvalidOperation();
            this = value;
            CancellationToken = cancellationToken;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private CommandOptions(in CommandOptions value, CommandFlags commandFlags)
        {
            if (value.CommandFlags != 0) ThrowInvalidOperation();
            this = value;
            CommandFlags = commandFlags;
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private CommandOptions(in CommandOptions value, in RedisKey keyPrefix)
        {
            if (!value.KeyPrefix.IsNull) ThrowInvalidOperation();
            this = value;
            KeyPrefix = keyPrefix;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidOperation() => throw new InvalidOperationException();
    }
}
