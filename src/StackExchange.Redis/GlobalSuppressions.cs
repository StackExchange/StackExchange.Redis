// This file is used by Code Analysis to maintain SuppressMessage
// attributes that are applied to this project.
// Project-level suppressions either have no target or are given
// a specific target and scoped to a namespace, type, member, etc.

using System.Diagnostics.CodeAnalysis;

[assembly: SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>", Scope = "member", Target = "~P:StackExchange.Redis.Message.IsAdmin")]
[assembly: SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.ServerEndPoint.GetBridge(StackExchange.Redis.RedisCommand,System.Boolean)~StackExchange.Redis.PhysicalBridge")]
[assembly: SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.RedisValue.op_Equality(StackExchange.Redis.RedisValue,StackExchange.Redis.RedisValue)~System.Boolean")]
[assembly: SuppressMessage("Style", "IDE0075:Simplify conditional expression", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.RedisSubscriber.Unsubscribe(StackExchange.Redis.RedisChannel@,System.Action{StackExchange.Redis.RedisChannel,StackExchange.Redis.RedisValue},StackExchange.Redis.ChannelMessageQueue,StackExchange.Redis.CommandFlags)~System.Boolean")]
[assembly: SuppressMessage("Roslynator", "RCS1104:Simplify conditional expression.", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.RedisSubscriber.Unsubscribe(StackExchange.Redis.RedisChannel@,System.Action{StackExchange.Redis.RedisChannel,StackExchange.Redis.RedisValue},StackExchange.Redis.ChannelMessageQueue,StackExchange.Redis.CommandFlags)~System.Boolean")]
[assembly: SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.Message.IsPrimaryOnly(StackExchange.Redis.RedisCommand)~System.Boolean")]
[assembly: SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.Message.RequiresDatabase(StackExchange.Redis.RedisCommand)~System.Boolean")]
[assembly: SuppressMessage("Style", "IDE0180:Use tuple to swap values", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.RedisDatabase.ReverseLimits(StackExchange.Redis.Order,StackExchange.Redis.Exclude@,StackExchange.Redis.RedisValue@,StackExchange.Redis.RedisValue@)")]
[assembly: SuppressMessage("Style", "IDE0180:Use tuple to swap values", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.RedisDatabase.GetSortedSetRangeByScoreMessage(StackExchange.Redis.RedisKey,System.Double,System.Double,StackExchange.Redis.Exclude,StackExchange.Redis.Order,System.Int64,System.Int64,StackExchange.Redis.CommandFlags,System.Boolean)~StackExchange.Redis.Message")]
[assembly: SuppressMessage("Reliability", "CA2012:Use ValueTasks correctly", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.PhysicalConnection.FlushSync(System.Boolean,System.Int32)~StackExchange.Redis.WriteResult")]
[assembly: SuppressMessage("Usage", "CA2219:Do not raise exceptions in finally clauses", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.PhysicalBridge.ProcessBacklogAsync~System.Threading.Tasks.Task")]
[assembly: SuppressMessage("Usage", "CA2249:Consider using 'string.Contains' instead of 'string.IndexOf'", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.ClientInfo.AddFlag(StackExchange.Redis.ClientFlags@,System.String,StackExchange.Redis.ClientFlags,System.Char)")]
[assembly: SuppressMessage("Style", "IDE0070:Use 'System.HashCode'", Justification = "<Pending>", Scope = "member", Target = "~M:StackExchange.Redis.CommandBytes.GetHashCode~System.Int32")]
[assembly: SuppressMessage("Roslynator", "RCS1085:Use auto-implemented property.", Justification = "<Pending>", Scope = "member", Target = "~P:StackExchange.Redis.RedisValue.OverlappedValueInt64")]
