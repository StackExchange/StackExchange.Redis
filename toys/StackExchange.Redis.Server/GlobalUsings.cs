extern alias seredis;
global using Format = seredis::StackExchange.Redis.Format;
global using PhysicalConnection = seredis::StackExchange.Redis.PhysicalConnection;
/*
During the v2/v3 transition, SE.Redis doesn't have RESPite, which
means it needs to merge in a few types like AsciiHash; this causes
conflicts; this file is a place to resolve them. Since the server
is now *mostly* RESPite, it turns out that the most efficient way
to do this is to shunt all of SE.Redis off into an alias, and bring
back just the types we need.
*/
global using RedisChannel = seredis::StackExchange.Redis.RedisChannel;
global using RedisCommand = seredis::StackExchange.Redis.RedisCommand;
global using RedisCommandMetadata = seredis::StackExchange.Redis.RedisCommandMetadata;
global using RedisKey = seredis::StackExchange.Redis.RedisKey;
global using RedisProtocol = seredis::StackExchange.Redis.RedisProtocol;
global using RedisValue = seredis::StackExchange.Redis.RedisValue;
global using ResultType = seredis::StackExchange.Redis.ResultType;
global using ServerSelectionStrategy = seredis::StackExchange.Redis.ServerSelectionStrategy;
global using ServerType = seredis::StackExchange.Redis.ServerType;
global using SlotRange = seredis::StackExchange.Redis.SlotRange;
global using TaskSource = seredis::StackExchange.Redis.TaskSource;
