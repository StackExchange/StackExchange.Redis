namespace StackExchange.Redis.Gui;

internal abstract class RespPayloadBase : IDisposable
{
    public abstract string GetRequest(int sizeHint = int.MaxValue);
    public abstract string GetResponse(int sizeHint = int.MaxValue);

    public virtual void Dispose() { }
}

internal sealed class RespLogPayload(string request, string response) : RespPayloadBase
{
    private readonly string request = request.Trim();
    private readonly string response = response.Trim();
    public override string GetRequest(int sizeHint) => request;
    public override string GetResponse(int sizeHint) => response;
}

internal sealed class RespPayload(LeasedRespResult? request, LeasedRespResult? response) : RespPayloadBase
{
    private bool _isDisposed;

    public override void Dispose()
    {
        base.Dispose();
        _isDisposed = true;
        var req = Request;
        var resp = Response;
        Request = null;
        Response = null;
        req?.Dispose();
        resp?.Dispose();
    }
    public LeasedRespResult? Request { get; private set; } = request;
    public LeasedRespResult? Response { get; private set; } = response;

    public void SetResponse(LeasedRespResult response)
    {
        var oldValue = Response;
        Response = null;
        oldValue?.Dispose();

        if (_isDisposed)
        {
            response?.Dispose();
        }
        else
        {
            Response = response;
        }
    }

    public override string GetRequest(int sizeHint)
    {
        try
        {
            return Request is { } req ? Utils.GetCommandText(req.Content, sizeHint) : "(out-of-band)";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public override string GetResponse(int sizeHint)
    {
        try
        {
            return Response is { } resp ? Utils.GetSimpleText(resp.Content, sizeHint: sizeHint) : "(pending)";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
