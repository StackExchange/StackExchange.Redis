using System;
using System.Threading.Tasks;

// ReSharper disable once CheckNamespace
namespace StackExchange.Redis;

internal partial class RedisDatabase
{
    public bool KeyBless(RedisKey key, BlessFlags bless, CommandFlags flags = CommandFlags.None)
    {
        var msg = CreateBlessMessage(Database, RedisLiterals.SET, key, bless, flags);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public Task<bool> KeyBlessAsync(RedisKey key, BlessFlags bless, CommandFlags flags = CommandFlags.None)
    {
        var msg = CreateBlessMessage(Database, RedisLiterals.SET, key, bless, flags);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public bool KeyUnbless(RedisKey key, BlessFlags bless, CommandFlags flags = CommandFlags.None)
    {
        var msg = CreateBlessMessage(Database, RedisLiterals.CLEAR, key, bless, flags);
        return ExecuteSync(msg, ResultProcessor.Boolean);
    }

    public Task<bool> KeyUnblessAsync(RedisKey key, BlessFlags bless, CommandFlags flags = CommandFlags.None)
    {
        var msg = CreateBlessMessage(Database, RedisLiterals.CLEAR, key, bless, flags);
        return ExecuteAsync(msg, ResultProcessor.Boolean);
    }

    public BlessFlags KeyBlessFlags(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = CreateBlessFlagsMessage(Database, key, flags);
        return ExecuteSync(msg, ResultProcessor.BlessFlags);
    }

    public Task<BlessFlags> KeyBlessFlagsAsync(RedisKey key, CommandFlags flags = CommandFlags.None)
    {
        var msg = CreateBlessFlagsMessage(Database, key, flags);
        return ExecuteAsync(msg, ResultProcessor.BlessFlags);
    }

    internal static Message CreateBlessMessage(int db, in RedisValue subcommand, in RedisKey key, BlessFlags bless, CommandFlags flags)
    {
        var msg = new BlessMessage(db, flags, subcommand, key, bless);
        // BLESS as a whole is replica-eligible (for GET); SET/CLEAR carry WRITE
        msg.SetPrimaryOnly();
        return msg;
    }

    internal static Message CreateBlessFlagsMessage(int db, in RedisKey key, CommandFlags flags)
        => Message.Create(db, flags.WithCategory(CommandFlags.CommandRetryReadOnly), RedisCommand.BLESS, RedisLiterals.GET, key);

    /// <summary>
    /// Writes one wire token per flag in <paramref name="bless"/>; the server currently accepts exactly one, but
    /// rejecting additional tokens is the server's call, not ours.
    /// </summary>
    internal static int BlessTokenCount(BlessFlags bless, string paramName)
    {
        // no token at all is an arity error on the server, and undefined bits have no token we could write
        if (bless == BlessFlags.None || (bless & ~BlessFlags.NoEvict) != 0)
        {
            throw new ArgumentOutOfRangeException(paramName, bless, "At least one known BLESS flag must be specified.");
        }
        return 1; // only NoEvict is defined today
    }

    internal static void WriteBlessTokens(in MessageWriter writer, BlessFlags bless)
    {
        if ((bless & BlessFlags.NoEvict) != 0) writer.WriteBulkString(RedisLiterals.NO_EVICT);
    }

    // BLESS {SET|CLEAR} key flag [flag ...]
    private sealed class BlessMessage : Message.CommandKeyBase
    {
        private readonly RedisValue subcommand;
        private readonly BlessFlags bless;
        private readonly int tokenCount;

        public BlessMessage(int db, CommandFlags flags, in RedisValue subcommand, in RedisKey key, BlessFlags bless)
            : base(db, flags, RedisCommand.BLESS, key)
        {
            tokenCount = BlessTokenCount(bless, nameof(bless));
            this.subcommand = subcommand;
            this.bless = bless;
        }

        protected override void WriteImpl(in MessageWriter writer)
        {
            writer.WriteHeader(Command, 2 + tokenCount);
            writer.WriteBulkString(subcommand);
            writer.Write(Key);
            WriteBlessTokens(writer, bless);
        }

        public override int ArgCount => 2 + tokenCount;
    }
}
