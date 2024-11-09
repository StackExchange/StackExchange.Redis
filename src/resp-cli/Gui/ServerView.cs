using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using RESPite.Resp;
using RESPite.Transports;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal class ServerView : View
{
    private IRequestResponseTransport? transport;
    private TableView? table;
    private Action<Task>? asyncUpdateTable;
    private RespPayloadTableSource? data;
    private readonly CancellationToken endOfLife;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            transport?.Dispose();
        }
        base.Dispose(disposing);
    }

    public bool Send(string command)
    {
        if (transport is null || data is null || asyncUpdateTable is null)
        {
            return false;
        }
        ReadOnlyMemory<string> cmd = Utils.Tokenize(command).ToArray();
        if (cmd.IsEmpty)
        {
            return false;
        }

        var pending = transport.SendAsync(cmd, RespWriters.Strings, LeasedRespResult.Reader, endOfLife).AsTask();
        if (!pending.IsCompleted)
        {
            _ = pending.ContinueWith(asyncUpdateTable);
        }
        data.Insert(0, new RespPayload(command, pending));
        return true;
    }

    public ServerView(string host, int port, bool tls, CancellationToken endOfLife)
    {
        CanFocus = true;
        Width = Dim.Fill();
        Height = Dim.Fill();

        this.endOfLife = endOfLife;
        var log = new TextView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            ReadOnly = true,
        };
        Add(log);
        _ = Task.Run(async () =>
        {
            transport = await Utils.ConnectAsync(host, port, tls, msg => Application.Invoke(() =>
            {
                log.MoveEnd();
                log.InsertText(msg);
            }));

            if (transport is not null)
            {
                Remove(log);
                CreateTable();
            }
        });
    }

    private void CreateTable()
    {
        table = new TableView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
        };

        data = new RespPayloadTableSource(table);
        table.Table = data;
        asyncUpdateTable = t => Application.Invoke(() => table.SetNeedsDisplay());
        Add(table);

        table.KeyDown += (sender, key) =>
        {
            if (key == Key.DeleteChar)
            {
                var idx = table.SelectedRow;
                if (idx >= 0 && data.Count > idx)
                {
                    data.RemoveAt(idx);
                    table.SetNeedsDisplay();
                }
            }
        };
        table.MultiSelect = false;
        table.FullRowSelect = true;
        table.CellActivated += (sender, args) =>
        {
            var row = args.Row;
            if (row >= data.Count)
            {
                return;
            }
            var obj = data[row];

            using var popup = new Dialog
            {
                Title = "Command",
                Height = Dim.Percent(80),
                Width = Dim.Percent(80),
            };
            using var reqText = new TextView
            {
                ReadOnly = true,
                Width = Dim.Fill(),
                Height = 1,
                Text = obj.Request,
            };
            using var tabs = new TabView
            {
                Y = Pos.Bottom(reqText),
                Width = Dim.Fill(),
                Height = Dim.Fill(1),
            };
            using var tree = new TreeView
            {
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            using var treeTab = new Tab
            {
                DisplayText = "Tree",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                View = tree,
            };
            using var respText = new TextView
            {
                ReadOnly = true,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            if (obj.ResponseTask.IsCompletedSuccessfully)
            {
                var result = obj.ResponseTask.GetAwaiter().GetResult();
                var node = BuildTree(result);
                tree.AddObject(node);
                tree.Expand(node);

                respText.Text = result.ToString();
            }
            using var respTab = new Tab
            {
                DisplayText = "RESP",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                View = respText,
            };
            tabs.AddTab(treeTab, true);
            tabs.AddTab(respTab, false);
            using var okBtn = new Button
            {
                Y = Pos.Bottom(tabs),
                Text = "ok",
            };
            using var repeatBtn = new Button
            {
                IsDefault = true,
                Y = okBtn.Y,
                X = Pos.Right(okBtn),
                Text = "repeat",
            };
            okBtn.Accept += (s, e) => Application.RequestStop();
            repeatBtn.Accept += (s, e) =>
            {
                Application.RequestStop();
                RepeatCommand?.Invoke(obj.Request);
            };
            popup.Add(reqText, tabs, okBtn, repeatBtn);
            Application.Run(popup);
        };
    }

    public event Action<string>? RepeatCommand;

    private void Transport_OutOfBandData(ReadOnlySequence<byte> obj) { }

    private ITreeNode BuildTree(LeasedRespResult value)
    {
        var reader = new RespReader(value.Span);
        if (TryCreateNode(ref reader, out var node))
        {
            return node;
        }
        return new TreeNode(" ???");
    }

    private static bool TryCreateNode(ref RespReader reader, [NotNullWhen(true)] out ITreeNode? node)
    {
        if (!Utils.TryGetSimpleText(ref reader, 0, out _, out var text, iterateChildren: false))
        {
            node = null;
            return false;
        }

        node = new TreeNode(" " + text);
        if (reader.IsAggregate)
        {
            var count = reader.ChildCount;
            for (int i = 0; i < count && TryCreateNode(ref reader, out var child); i++)
            {
                node.Children.Add(child);
            }
        }
        return true;
    }
}
