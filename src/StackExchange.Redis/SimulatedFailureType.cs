using System;

namespace StackExchange.Redis
{
    [Flags]
    internal enum SimulatedFailureType
    {
        None                 = 0,
        InteractiveInbound   = 1 << 0,
        InteractiveOutbound  = 1 << 1,
        SubscriptionInbound  = 1 << 2,
        SubscriptionOutbound = 1 << 3,

        AllInbound = InteractiveInbound | SubscriptionInbound,
        AllOutbound = InteractiveOutbound | SubscriptionOutbound,

        AllInteractive = InteractiveInbound | InteractiveOutbound,
        AllSubscription = SubscriptionInbound | SubscriptionOutbound,

        All = AllInbound | AllOutbound,
    }
}
