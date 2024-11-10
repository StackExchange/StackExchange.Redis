using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespDesktopWindow : Window
{
    private readonly CancellationTokenSource endOfLifeSource = new();

    private readonly TextField input;
    private readonly TabView servers;
    private readonly StatusBar statusBar;
    private readonly RespConnectView connect;

    private CancellationToken EndOfLife => endOfLifeSource.Token;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            endOfLifeSource.Cancel();
        }

        base.Dispose(disposing);
    }

    public RespDesktopWindow(string host, int port, bool tls, string? user, string? pass, bool resp3)
    {
        Title = $"resp-cli desktop ({Application.QuitKey} to exit)";
        statusBar = new();

        var lbl = new Label
        {
            Text = ">",
        };

        input = new TextField
        {
            X = Pos.Right(lbl) + 1,
            Width = Dim.Fill(),
        };
        input.HasFocusChanged += (s, e) =>
        {
            if (input.HasFocus) SetStatusText("Send a RESP command");
        };

        servers = new TabView
        {
            Y = Pos.Bottom(input),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            Style =
            {
                // TabsOnBottom = true,
                // ShowBorder = true,
                ShowTopLine = true,
            },
        };
        servers.ApplyStyleChanges();
        servers.SelectedTabChanged += (s, e) =>
        {
            if (servers.SelectedTab?.View is ServerView { } selected)
            {
                SetStatusText(selected.StatusCaption);
            }
            else if (servers.SelectedTab?.View is RespConnectView)
            {
                SetStatusText("Create a new RESP connection");
            }
            else
            {
                SetStatusText("");
            }
        };
        connect = new RespConnectView(host, port, tls, resp3);
        var tab = new Tab
        {
            DisplayText = " + ",
            View = connect,
        };
        connect.Connect += AddServer;

        servers.AddTab(tab, true);
        AddServer();

        input.KeyDown += (sender, key) =>
        {
            if (key == Key.Enter)
            {
                if (servers.SelectedTab?.View is ServerView server)
                {
                    if (server.Send(input.Text.ToString()))
                    {
                        input.Text = "";
                    }
                }
            }
        };

        Add(lbl, input, servers, statusBar);
    }

    public void SetStatusText(string text) => statusBar.Text = text;

    public void AddServer()
    {
        Application.Invoke(() =>
        {
            if (!connect.Validate(out var host, out var port))
            {
                return;
            }

            var view = new ServerView(host, port, connect.Tls, EndOfLife);
            view.StatusChanged += SetStatusText;
            var tabNumber = servers.Tabs.Count;
            var tab = new Tab
            {
                DisplayText = $" {tabNumber} ",
                View = view,
            };

            view.RepeatCommand += command =>
            {
                input.Text = command;
                input.SetFocus();
            };

            var key = tabNumber switch
            {
                1 => Key.F1,
                2 => Key.F2,
                3 => Key.F3,
                4 => Key.F4,
                5 => Key.F5,
                6 => Key.F6,
                7 => Key.F7,
                8 => Key.F8,
                9 => Key.F9,
                10 => Key.F10,
                11 => Key.F11,
                12 => Key.F12,
                _ => null,
            };

            if (key is not null)
            {
                Add(new Shortcut
                {
                    Key = key,
                    Action = () => servers.SelectedTab = tab,
                    Visible = false,
                });
                view.StatusCaption = $"({key}) " + view.StatusCaption;
            }
            servers.AddTab(tab, true);
        });
    }
}
