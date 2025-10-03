namespace RESPite.Connections.Internal;

internal sealed class ConfiguredConnection(in RespContext tail, RespConfiguration configuration)
    : DecoratorConnection(tail, configuration);
