namespace StackExchange.Redis.Gui;

internal sealed class RespPayload(string request, Task<LeasedRespResult> response)
{
    public string Request { get; } = request;

    public Task<LeasedRespResult> ResponseTask => response;
    public string ResponseText
    {
        get
        {
            if (!response.IsCompleted)
            {
                return "(pending)";
            }
            try
            {
                return Utils.GetSimpleText(response.GetAwaiter().GetResult(), 8);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }
}
