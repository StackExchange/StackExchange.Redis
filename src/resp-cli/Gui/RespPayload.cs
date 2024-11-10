namespace StackExchange.Redis.Gui;

internal abstract class RespPayloadBase
{
    public abstract string GetRequest(int sizeHint = int.MaxValue);
    public abstract string GetResponse(int sizeHint = int.MaxValue);
}

internal sealed class RespLogPayload(string request, string response) : RespPayloadBase
{
    public override string GetRequest(int sizeHint) => request;
    public override string GetResponse(int sizeHint) => response;
}

internal sealed class RespPayload(string request, Task<LeasedRespResult> response) : RespPayloadBase
{
    public Task<LeasedRespResult> ResponseTask => response;

    public override string GetRequest(int sizeHint) => request;
    public override string GetResponse(int sizeHint)
    {
        if (!response.IsCompleted)
        {
            return "(pending)";
        }
        try
        {
            return Utils.GetSimpleText(response.GetAwaiter().GetResult(), sizeHint: sizeHint);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
