namespace StackExchange.Redis.Tests.Issues
{
    public class Issue2164Tests
    {
        [Fact]
        public void LoadSimpleScript()
        {
            LuaScript.Prepare("return 42");
        }
        [Fact]
        public void LoadComplexScript()
        {
            LuaScript.Prepare(@"
-------------------------------------------------------------------------------
-- API definitions
-------------------------------------------------------------------------------
local MessageStoreAPI = {}

MessageStoreAPI.confirmPendingDelivery = function(smscMessageId, smscDeliveredAt, smscMessageState)
    local messageId = redis.call('hget', ""smId:"" .. smscMessageId, 'mId')
    if not messageId then
        return nil
    end
    -- delete pending delivery
    redis.call('del', ""smId:"" .. smscMessageId)

    local mIdK = 'm:'..messageId

    local result = redis.call('hsetnx', mIdK, 'sState', smscMessageState)
    if result == 1 then
        redis.call('hset', mIdK, 'sDlvAt', smscDeliveredAt)
        redis.call('zrem', ""msg.validUntil"", messageId)
        return redis.call('hget', mIdK, 'payload')
    else
        return nil
    end
end


-------------------------------------------------------------------------------
-- Function lookup
-------------------------------------------------------------------------------

-- None of the function calls accept keys
if #KEYS > 0 then error('No Keys should be provided') end

-- The first argument must be the function that we intend to call, and it must
-- exist
local command_name = assert(table.remove(ARGV, 1), 'Must provide a command as first argument')
local command      = assert(MessageStoreAPI[command_name], 'Unknown command ' .. command_name)

return command(unpack(ARGV))");
        }
    }
}
