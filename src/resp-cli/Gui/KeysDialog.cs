using System.Globalization;
using RESPite.Resp;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal class KeysDialog : ServerToolDialog
{
    private readonly TableView _keys;
    private readonly TextField _match;
    private readonly TextField _top;
    private readonly KeysRowSource _rows = new();

    private void Fetch()
    {
        if (!int.TryParse(_top.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            StatusText = "Invalid count: " + _top.Text;
        }

        var match = _match.Text;
        if (match == "*") match = "";
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
        btn.Accept += (s, e) => Fetch();
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
    }
    private sealed class KeysRow(string key)
    {
        public string Key => key;
        public string Type { get; set; } = "";
        public string Content { get; set; } = "";
    }

    protected override async void OnStart()
    {
        StatusText = $"Querying database size...";
        var count = await RespCommands.DbSize.SendAsync(Transport, RespReaders.Int32, CancellationToken);
        StatusText = $"Keys in database: {count}";
    }
}
