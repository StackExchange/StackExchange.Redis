using RESPite.Transports;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

public abstract class ServerToolDialog : Dialog
{
    public ServerToolDialog()
    {
        Title = GetType().Name;
        Height = Dim.Percent(80);
        Width = Dim.Percent(80);
        Transport = null!; // set later
    }

    internal void SetTransport(IMessageTransport transport)
        => Transport = transport;

    public override void EndInit()
    {
        base.EndInit();
        OnStart();
    }

    protected virtual void OnStart() { }

    private readonly CancellationTokenSource cancelled = new();
    public CancellationToken CancellationToken => cancelled.Token;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            cancelled.Cancel();
        }
        base.Dispose(disposing);
    }

    protected IMessageTransport Transport { get; private set; }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set
        {
            if (value != _statusText)
            {
                _statusText = value;
                StatusTextChanged?.Invoke(this, value);
            }
        }
    }
    public event Action<ServerToolDialog, string>? StatusTextChanged;
}
