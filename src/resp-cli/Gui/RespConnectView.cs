using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespConnectView : View
{
    private readonly TextField hostField;
    private readonly TextField portField;
    private readonly CheckBox tlsCheck;
    private readonly CheckBox resp3Check;
    private readonly CheckBox handshakeCheck;

    public bool Tls => tlsCheck.CheckedState == CheckState.Checked;

    public bool Handshake => handshakeCheck.CheckedState == CheckState.Checked;

    public bool Validate([NotNullWhen(true)] out string? host, out int port)
    {
        try
        {
            host = hostField.Text;
            return int.TryParse(portField.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out port);
        }
        catch
        {
            host = null;
            port = 0;
            return false;
        }
    }

    public event Action? Connect;

    public RespConnectView(string host, int port, bool tls, bool resp3)
    {
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;

        var lbl = Add(new Label
        {
            Text = "Host ",
        });
        hostField = new TextField
        {
            X = Pos.Right(lbl),
            Y = lbl.Y,
            Width = Dim.Fill(),
            Text = host,
        };
        Add(hostField);

        lbl = Add(new Label
        {
            Text = "Port ",
            Y = Pos.Bottom(hostField),
        });
        portField = new TextField
        {
            X = Pos.Right(lbl),
            Y = lbl.Y,
            Width = Dim.Absolute(8),
            Text = port.ToString(CultureInfo.InvariantCulture),
        };
        Add(portField);

        lbl = Add(new Label
        {
            Text = "TLS",
            Y = Pos.Bottom(portField) + 1,
        });
        tlsCheck = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = tls ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(tlsCheck);

        lbl = Add(new Label
        {
            Text = "RESP 3",
            Y = Pos.Bottom(tlsCheck),
        });
        resp3Check = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = resp3 ? CheckState.Checked : CheckState.UnChecked,
        };
        Add(resp3Check);

        lbl = Add(new Label
        {
            Text = "Handshake",
            Y = Pos.Bottom(tlsCheck),
        });
        handshakeCheck = new CheckBox
        {
            X = Pos.Right(lbl) + 1,
            Y = lbl.Y,
            CheckedState = CheckState.Checked,
        };
        Add(handshakeCheck);

        var btn = new Button
        {
            Y = Pos.Bottom(lbl) + 2,
            Text = "connect",
            IsDefault = true,
        };
        Add(btn);
        btn.Accept += (s, e) => Connect?.Invoke();
    }
}
