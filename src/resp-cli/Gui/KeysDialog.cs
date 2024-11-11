using System.Globalization;
using System.Text;
using RESPite.Resp.Commands;
using Terminal.Gui;
using static RESPite.Resp.Commands.Type;
using Type = RESPite.Resp.Commands.Type;
namespace StackExchange.Redis.Gui;

internal class KeysDialog : ServerToolDialog
{
    private readonly TableView _keys;
    private readonly TextField _match;
    private readonly TextField _top;
    private readonly KeysRowSource _rows = new();

    private async Task FetchKeys()
    {
        try
        {
            if (!int.TryParse(_top.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
            {
                StatusText = "Invalid count: " + _top.Text;
            }

            var match = _match.Text;
            if (match is null or "*") match = "";

            var cmd = new Scan(Match: Encoding.UTF8.GetBytes(match), Count: 100, Type: _match.Text);
            do
            {
                StatusText = $"Fetching next page...";
                using var reply = await Transport.SendAsync<Scan, Scan.Response>(cmd, CancellationToken);

                int start = _rows.Rows;
                int added = reply.Keys.ForEach(
                    static (i, span, state) =>
                    {
                        state.Add(Encoding.UTF8.GetString(span));
                        return true;
                    },
                    _rows);

                _keys.SetNeedsDisplay();

                StatusText = $"Fetching types...";

                for (int i = 0; i < added; i++)
                {
                    var obj = _rows[i];
                    obj.Type = await Transport.SendAsync<Type, KnownType>(new(obj.Key), CancellationToken);
                    _keys.SetNeedsDisplay();
                }

                // update the cursor
                cmd = cmd.Next(reply);
            }
            while (cmd.Cursor != 0);
            StatusText = $"All done!";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    public KeysDialog()
    {
        Title = "Keys";
        StatusText = "original status";

        var topLabel = new Label()
        {
            Text = "Top",
        };
        _top = new()
        {
            X = Pos.Right(topLabel) + 1,
            Width = 8,
            Text = "100",
        };
        var matchLabel = new Label()
        {
            Text = "Match",
            X = Pos.Right(_top) + 1,
        };
        _match = new()
        {
            X = Pos.Right(matchLabel) + 1,
            Width = Dim.Fill(10),
            Text = "*",
        };
        var btn = new Button()
        {
            X = Pos.Right(_match) + 1,
            Width = Dim.Fill(),
            Text = "Go",
        };
        btn.Accept += (s, e) => _ = FetchKeys();
        _keys = new TableView
        {
            Y = Pos.Bottom(matchLabel),
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Table = _rows,
        };

        Add(topLabel, _top, matchLabel, _match, btn, _keys);
    }

    private sealed class KeysRowSource : ITableSource
    {
        private readonly List<KeysRow> _rows = new();

        public object this[int row, int col]
        {
            get
            {
                var obj = _rows[row];
                return col switch
                {
                    0 => obj.Key,
                    1 => obj.Type,
                    2 => obj.Content,
                    _ => throw new IndexOutOfRangeException(),
                };
            }
        }

        public string[] ColumnNames => ["Key", "Type", "Contents"];

        public int Columns => 3;

        public int Rows => _rows.Count;

        public void Add(string key) => _rows.Add(new(key));

        public KeysRow this[int index] => _rows[index];
    }

    public sealed class KeysRow(string key)
    {
        public string Key => key;
        public KnownType Type { get; set; }
        public string Content { get; set; } = "";
    }

    protected override async void OnStart()
    {
        try
        {
            StatusText = $"Querying database size...";
            var count = await Transport.SendAsync<DbSize, int>(CancellationToken);

            StatusText = $"Keys in database: {count}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }
}
