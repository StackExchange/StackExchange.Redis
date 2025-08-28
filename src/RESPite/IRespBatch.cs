namespace RESPite;

public interface IBatchConnection : IRespConnection
{
    Task FlushAsync();
    void Flush();
}
