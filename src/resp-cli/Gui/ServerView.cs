using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using RESPite;
using RESPite.Messages;
using RESPite.Resp;
using RESPite.Resp.Commands;
using RESPite.Resp.KeyValueStore;
using RESPite.Resp.Readers;
using RESPite.Resp.Writers;
using RESPite.Transports;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal class ServerView : View
{
    private sealed class InterceptingScanner : IFrameScanner<RespFrameScanner.RespFrameState>, IFrameValidator
    {
        private readonly IFrameScanner<RespFrameScanner.RespFrameState> tailScanner;
        private readonly IFrameValidator? tailValidator;

        public InterceptingScanner(IFrameScanner<RespFrameScanner.RespFrameState>? tail = null)
        {
            if (tail is not null)
            {
                tailScanner = tail;
                tailValidator = tail as IFrameValidator;
            }
            else
            {
                var tmp = RespFrameScanner.Default;
                tailScanner = tmp;
                tailValidator = tmp;
            }
        }

        public void OnBeforeFrame(ref RespFrameScanner.RespFrameState state, ref FrameScanInfo info)
            => tailScanner.OnBeforeFrame(ref state, ref info);
        public OperationStatus TryRead(ref RespFrameScanner.RespFrameState state, in ReadOnlySequence<byte> data, ref FrameScanInfo info)
            => tailScanner.TryRead(ref state, data, ref info);
        public void Trim(ref RespFrameScanner.RespFrameState state, ref ReadOnlySequence<byte> data, ref FrameScanInfo info)
        {
            tailScanner.Trim(ref state, ref data, ref info);

            (info.IsOutOfBand ? InboundOutOfBand : InboundResponse)?.Invoke(in data);
        }
        public void Validate(in ReadOnlySequence<byte> message)
        {
            tailValidator?.Validate(message);
            OutboundRequest?.Invoke(in message);
        }

        public event MessageCallback? OutboundRequest;
        public event MessageCallback? InboundResponse;
        public event MessageCallback? InboundOutOfBand;
    }

    public IRequestResponseTransport? Transport { get; private set; }

    private TableView? table;
    private RespPayloadTableSource? data;
    private readonly CancellationToken endOfLife;

    private string statusCaption;
    public string StatusCaption => statusCaption;

    public event Action<string>? StatusChanged;

    [MemberNotNull(nameof(statusCaption))]
    public void SetStatus(string status)
    {
        statusCaption = status;
        StatusChanged?.Invoke(status);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Transport?.Dispose();
        }
        base.Dispose(disposing);
    }

    public async Task<bool> SendAsync(string command, Action<LeasedRespResult>? callback = null)
    {
        try
        {
            if (Transport is not { } transport || data is null)
            {
                SetStatus("Not connected, unable to send command");
                return false;
            }

            List<SimpleString> strings = new List<SimpleString>();
            foreach (var value in Utils.Tokenize(command))
            {
                strings.Add(value);
            }
            using LeasedStrings cmd = new(strings);
            if (cmd.IsEmpty) return false;

            var result = await transport.SendAsync(cmd, CommandWriter.AdHoc, LeasedRespResult.Reader, endOfLife).AsTask();
            SetStatus($"Sent command: {command}");
            callback?.Invoke(result);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            return false;
        }
    }

    public ServerView(string host, int port, bool tls, bool handshake, CancellationToken endOfLife)
    {
        _performRowDelta = PerformRowDelta;
        SetStatus($"{host}, port {port}{(tls ? " (TLS)" : "")}");
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

        var frameScanner = new InterceptingScanner();
        frameScanner.OutboundRequest += OnOutboundRequest;
        frameScanner.InboundResponse += OnInboundResponse;
        frameScanner.InboundOutOfBand += OnInboundOutOfBand;

        Add(log);
        _ = Task.Run(async () =>
        {
            Action<string> writeLog = msg => Application.Invoke(() =>
            {
                log.MoveEnd();
                log.ReadOnly = false;
                log.InsertText(msg + Environment.NewLine);
                log.ReadOnly = true;
            });
            Transport = await Utils.ConnectAsync(host, port, tls, writeLog, frameScanner, FrameValidation.Enabled);

            if (Transport is not null)
            {
                Application.Invoke(async () =>
                {
                    var txt = log.Text;
                    Remove(log);
                    CreateTable();
                    AddLogEntry("(Connect)", txt);

                    // common courtesy operations; non-destructive, purely metadata
                    if (handshake)
                    {
                        // identify ourselves to the server
                        try
                        {
                            await Server.CLIENT.SETNAME.Success().SendAsync(Transport, Environment.MachineName, endOfLife);
                            await Server.CLIENT.SETINFO.Success().SendAsync(Transport, ("LIB-NAME", "resp-cli"), endOfLife);
                            if (typeof(Program).Assembly.GetCustomAttribute<AssemblyFileVersionAttribute>() is { } ver)
                            {
                                await Server.CLIENT.SETINFO.Success().SendAsync(Transport, ("LIB-VER", ver.Version), endOfLife);
                            }
                        }
                        catch { }

                        // ask the server about itself
                        try
                        {
                            await Keys.DBSIZE.Success().SendAsync(Transport, endOfLife);
                            await Server.INFO.Success().SendAsync(Transport, "server", endOfLife);
                            await Server.CONFIG.GET.Success().SendAsync(Transport, "databases", endOfLife);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(ex.Message);
                        }
                    }
                });
            }
        });
    }

    private readonly ConcurrentQueue<RespPayload> _rowsToAdd = [];
    private readonly ConcurrentQueue<RespPayload> _awaitingReply = [];
    private int _rowChangePending;
    private readonly Action _performRowDelta;
    private void PerformRowDelta()
    {
        try
        {
            Interlocked.Exchange(ref _rowChangePending, 0);

            while (_rowsToAdd.TryDequeue(out var row))
            {
                data?.Insert(0, row);
            }
            table?.SetNeedsDisplay();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void SignalRowDelta()
    {
        if (Interlocked.CompareExchange(ref _rowChangePending, 0, 1) == 0)
        {
            Application.Invoke(_performRowDelta);
        }
    }

    private void OnInboundOutOfBand(in ReadOnlySequence<byte> payload)
    {
        RespPayload row = new(null, LeasedRespResult.Reader.Read(in payload));
        _rowsToAdd.Enqueue(row);
        SignalRowDelta();
    }

    private void OnInboundResponse(in ReadOnlySequence<byte> payload)
    {
        if (_awaitingReply.TryDequeue(out var next))
        {
            var reply = LeasedRespResult.Reader.Read(in payload);
            next.SetResponse(reply);
            SignalRowDelta();
        }
    }

    private void OnOutboundRequest(in ReadOnlySequence<byte> payload)
    {
        RespPayload row = new(new LeasedRespResult(payload), null);
        _awaitingReply.Enqueue(row);
        _rowsToAdd.Enqueue(row);
        SignalRowDelta();
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

                if (resp.Response is { } response)
                {
                    var node = BuildTree(response);
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
                    respText.Text = response.ToString();
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

internal static class NopCommandExtensions
{
    /// <summary>
    /// Accepts anything that isn't an error, without processing it.
    /// </summary>
    public static RespCommand<TRequest, Empty> Success<TRequest, TResponse>(this in RespCommand<TRequest, TResponse> command)
        => command.WithReader<Empty>(EmptyReader.Instance);

    private sealed class EmptyReader : IRespReader<Empty, Empty>
    {
        public static readonly EmptyReader Instance = new();
        private EmptyReader() { }

        public Empty Read(in Empty request, ref RespReader reader) => request;

        public Empty Read(in Empty request, in ReadOnlySequence<byte> content)
        {
            new RespReader(content, throwOnErrorResponse: true); // just for ERR validation
            return request;
        }
    }
}
