namespace RESPite.Connections;

public interface IRespContextSource
{
    ref readonly RespContext Context { get; }
}
