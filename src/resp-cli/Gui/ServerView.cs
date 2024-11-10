using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
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

    public string StatusCaption { get; set; }

    public event Action<string>? StatusChanged;

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
        StatusCaption = $"{host}, port {port}{(tls ? " (TLS)" : "")}";
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
                log.ReadOnly = false;
                log.InsertText(msg + Environment.NewLine);
                log.ReadOnly = true;
            }));

            if (transport is not null)
            {
                Application.Invoke(() =>
                {
                    var txt = log.Text;
                    Remove(log);
                    CreateTable();
                    AddLogEntry("(Connect)", txt);
                });
            }
        });
    }

    public void AddLogEntry(string category, string message)
        => data?.Insert(data.Count, new RespLogPayload(category, message));

    private void CreateTable()
    {
        table = new TableView
        {
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            BorderStyle = LineStyle.None,
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
                Text = obj.GetRequest(),
            };
            if (obj is RespPayload resp)
            {
                var tabs = new TabView
                {
                    Y = Pos.Bottom(reqText),
                    Width = Dim.Fill(),
                    Height = Dim.Fill(1),
                };
                var tree = new TreeView
                {
                    Width = Dim.Fill(),
                    Height = Dim.Fill(),
                };
                var treeTab = new Tab
                {
                    DisplayText = "Tree",
                    Width = Dim.Fill(),
                    Height = Dim.Fill(),
                    View = tree,
                };
                var respText = new TextView
                {
                    ReadOnly = true,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(),
                };
                if (resp.ResponseTask.IsCompletedSuccessfully)
                {
                    var result = resp.ResponseTask.GetAwaiter().GetResult();
                    var node = BuildTree(result);
                    tree.AddObject(node);
                    tree.Expand(node);
                    tree.SelectionChanged += (s, e) =>
                    {
                        string? status = null;
                        if (e.NewValue is RespTreeNode typedNode)
                        {
                            status = typedNode.Prefix switch
                            {
                                RespPrefix.BigNumber => "big number",
                                RespPrefix.BulkError => "bulk error",
                                RespPrefix.BulkString => "bulk string",
                                RespPrefix.SimpleError => "simple error",
                                RespPrefix.None => "(none)",
                                RespPrefix.SimpleString => "simple string",
                                RespPrefix.Integer => "integer",
                                RespPrefix.Array => "array",
                                RespPrefix.Null => "null",
                                RespPrefix.Boolean => "boolean",
                                RespPrefix.Double => "double",
                                RespPrefix.VerbatimString => "verbatim string",
                                RespPrefix.Map => "map",
                                RespPrefix.Set => "set",
                                RespPrefix.Push => "push",
                                _ => typedNode.Prefix.ToString(),
                            };
                        }
                        StatusChanged?.Invoke(status ?? "");
                    };

                    respText.Text = result.ToString();
                }
                var respTab = new Tab
                {
                    DisplayText = "RESP",
                    Width = Dim.Fill(),
                    Height = Dim.Fill(),
                    View = respText,
                };
                tabs.AddTab(treeTab, true);
                tabs.AddTab(respTab, false);
                var okBtn = new Button
                {
                    Y = Pos.Bottom(tabs),
                    Text = "ok",
                };
                var repeatBtn = new Button
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
                    RepeatCommand?.Invoke(obj.GetRequest());
                };
                popup.Add(reqText, tabs, okBtn, repeatBtn);
            }
            else
            {
                popup.Title = obj.GetRequest();
                reqText.Text = obj.GetResponse();
                reqText.Height = Dim.Fill(2);
                var okBtn = new Button
                {
                    Y = Pos.Bottom(reqText),
                    Text = "ok",
                    IsDefault = true,
                };
                okBtn.Accept += (s, e) => Application.RequestStop();
                popup.Add(reqText, okBtn);
            }
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
        return new RespTreeNode(" ???", RespPrefix.None);
    }

    private sealed class RespTreeNode(string text, RespPrefix prefix) : TreeNode(text)
    {
        public RespPrefix Prefix => prefix;
    }

    private static bool TryCreateNode(ref RespReader reader, [NotNullWhen(true)] out ITreeNode? node)
    {
        var sb = new StringBuilder(" ");
        if (!Utils.TryGetSimpleText(sb, ref reader, Utils.AggregateMode.CountOnly))
        {
            node = null;
            return false;
        }

        node = new RespTreeNode(sb.ToString(), reader.Prefix);
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
