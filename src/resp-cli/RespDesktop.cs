using System.Net;
using Terminal.Gui;

namespace StackExchange.Redis;

internal class RespDesktop
{
    public static void Run(ConnectionMultiplexer connection)
    {
        Application.Init();

        try
        {
            Application.Run(new RespDesktopWindow(connection));
        }
        finally
        {
            Application.Shutdown();
        }
    }

    private sealed class RespPayload(string request, Task<RedisResult> response)
    {
        public string Request { get; } = request;

        public Task<RedisResult> ResponseTask => response;
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
                    return Utils.GetSimpleText(response.GetAwaiter().GetResult(), 8, out _);
                }
                catch (RedisServerException rex)
                {
                    return Utils.GetSimpleText(RedisResult.Create(rex.Message, ResultType.Error), 0, out _);
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }
        }
    }

    private class MyTable(TableView parent) : ITableSource
    {
        private readonly List<RespPayload> items = new();

        public int Count => items.Count;

        public RespPayload this[int index] => items[index];

        public int Width => parent.GetContentSize().Width;
        object ITableSource.this[int row, int col]
        {
            get
            {
                var obj = items[row];
                var val = col switch
                {
                    0 => obj.Request,
                    1 => obj.ResponseText,
                    _ => throw new IndexOutOfRangeException(),
                };

                int maxWidth = col switch
                {
                    0 => 15,
                    _ => Width - 18,
                };

                return Truncate(val, maxWidth);
            }
        }

        private static string Truncate(string value, int length)
        {
            value ??= "";
            if (value.Length > length)
            {
                if (length <= 1) return "\u2026";
                return value.Substring(0, length - 1) + "\u2026";
            }
            return value;
        }

        string[] ITableSource.ColumnNames => ["Request", "Response"];

        int ITableSource.Columns => 2;

        int ITableSource.Rows => items.Count;

        public void TrimToLength(int count)
        {
            if (items.Count > count)
            {
                items.RemoveRange(count, items.Count - count);
            }
        }

        public void RemoveAt(int index) => items.RemoveAt(index);

        public void Insert(int index, RespPayload value) => items.Insert(0, value);
    }

    private class RespDesktopWindow : Window
    {
        public bool Send(string query)
        {
            var cmd = Utils.Parse(query, out var args);
            if (string.IsNullOrWhiteSpace(cmd)) return false;

            data.TrimToLength(50);
            var pending = connection.GetServers().Single().ExecuteAsync(cmd, args);
            if (!pending.IsCompleted)
            {
                _ = pending.ContinueWith(asyncUpdateTable);
            }
            data.Insert(0, new RespPayload(query, pending));
            return true;
        }

        private readonly TableView table;
        private readonly TextField input;
        private readonly MyTable data;
        private readonly ConnectionMultiplexer connection;

        private readonly Action<Task> asyncUpdateTable;

        public RespDesktopWindow(ConnectionMultiplexer connection)
        {
            this.connection = connection;
            Title = $"resp-cli desktop ({Application.QuitKey} to exit)";

            var lbl = new Label
            {
                Text = ">",
            };

            input = new TextField
            {
                X = Pos.Right(lbl) + 1,
                Width = Dim.Fill(),
            };
            table = new TableView
            {
                Y = Pos.Bottom(input),
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };

            data = new MyTable(table);
            table.Table = data;

            input.KeyDown += (sender, key) =>
            {
                if (key == Key.Enter)
                {
                    if (Send(input.Text.ToString()))
                    {
                        input.Text = "";
                    }
                }
            };

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
                if (obj.ResponseTask.IsCompletedSuccessfully)
                {
                    var node = BuildTree(obj.ResponseTask.GetAwaiter().GetResult());
                    tree.AddObject(node);
                    tree.Expand(node);
                }
                using var respText = new TextView
                {
                    ReadOnly = true,
                    Width = Dim.Fill(),
                    Height = Dim.Fill(),
                    Text = "some\r\ntext\r\nhere",
                };
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
                    input.Text = obj.Request;
                    input.SetFocus();
                };
                popup.Add(reqText, tabs, okBtn, repeatBtn);
                Application.Run(popup);
            };

            Add(lbl, input, table);
            asyncUpdateTable = t => Application.Invoke(() => table.SetNeedsDisplay());
            Send("INFO SERVER");
            Send("CONFIG GET databases");
        }

        private ITreeNode BuildTree(RedisResult redisResult)
        {
            var node = new TreeNode(" " + Utils.GetSimpleText(redisResult, 0, out bool isAgg));

            if (isAgg)
            {
                for (int i = 0; i < redisResult.Length; i++)
                {
                    node.Children.Add(BuildTree(redisResult[i]));
                }
            }
            return node;
        }
    }
}
