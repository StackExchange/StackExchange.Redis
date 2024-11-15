using System.Collections.ObjectModel;
using System.Globalization;
using RESPite.Resp;
using RESPite.Resp.Commands;
using RESPite.Resp.KeyValueStore;
using Terminal.Gui;

using static RESPite.Resp.KeyValueStore.Keys;

namespace StackExchange.Redis.Gui;

internal class KeysDialog : ServerToolDialog
{
    private readonly TableView _keys;
    private readonly TextField _match;
    private readonly TextField _top;
    private readonly KeysRowSource _rows = new();
    private readonly ComboBox _type = new();
    private readonly ObservableCollection<string> _types = new() { "string", "list", "set", "zset", "hash", "stream" };

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

            int typeIndex = _type.SelectedItem;
            string? type = typeIndex < 0 ? null : _types[typeIndex];

            _rows.Clear();
            _keys.SetNeedsDisplay();

            var cmd = new Scan.Request(Match: match, Count: 100, Type: type);
            do
            {
                StatusText = $"Fetching next page...";
                using var reply = await SCAN.SendAsync(Transport, cmd, CancellationToken);

                var rowIndex = _rows.Rows;
                int start = _rows.Rows;

                foreach (var key in reply.Keys)
                {
                    _rows.Add(key.ToString() ?? "");
                }

                _keys.SetNeedsDisplay();

                StatusText = $"Fetching types...";

                int end = start + reply.Keys.Count;
                for (int i = start; i < end; i++)
                {
                    var obj = _rows[i];
                    obj.SetQueried();
                    SimpleString key = obj.Key;
                    obj.SetType(await TYPE.SendAsync(Transport, key, CancellationToken));
                    _keys.SetNeedsDisplay();

                    const int MAX_STRING_LEN = 30;
                    string content = obj.Type switch
                    {
                        KnownType.None => "",
                        KnownType.String => $"{await Strings.STRLEN.SendAsync(Transport, key, CancellationToken)} bytes: {Burn(await Strings.GETRANGE.SendAsync(Transport, (key, 0, MAX_STRING_LEN + 1), CancellationToken))}",
                        KnownType.List => $"{await Lists.LLEN.SendAsync(Transport, key, CancellationToken)} elements",
                        KnownType.Set => $"{await Sets.SCARD.SendAsync(Transport, key, CancellationToken)} elements",
                        KnownType.ZSet => $"{await SortedSets.ZCARD.SendAsync(Transport, key, CancellationToken)} elements",
                        KnownType.Hash => $"{await Hashes.HLEN.SendAsync(Transport, key, CancellationToken)} elements",
                        KnownType.Stream => $"{await Streams.XLEN.SendAsync(Transport, key, CancellationToken)} elements",
                        _ => "(???)",
                    };

                    static string Burn(LeasedString value)
                    {
                        var s = Utils.Truncate(value.ToString(), MAX_STRING_LEN);
                        value.Dispose();
                        return s;
                    }

                    obj.SetContent(content);
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
        var typeLabel = new Label
        {
            Text = "Type",
            X = Pos.Right(_top) + 1,
        };
        _type = new()
        {
            X = Pos.Right(typeLabel) + 1,
            Width = 9,
        };
        _type.SetSource(_types);
        _type.CanFocus = true;
        var matchLabel = new Label()
        {
            Text = "Match",
            X = Pos.Right(_type) + 1,
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

        Add(topLabel, _top, typeLabel, _type, matchLabel, _match, btn, _keys);
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
        internal void Clear() => _rows.Clear();

        public KeysRow this[int index] => _rows[index];
    }

    public sealed class KeysRow(string key)
    {
        public string Key => key;

        private int _state;

        public KnownType Type { get; private set; } = KnownType.Unknown;

        public void SetQueried() => _state |= 0b001;
        public bool HaveQueried => (_state & 0b001) != 0;
        public bool HaveType => (_state & 0b010) != 0;
        public void SetType(KnownType type)
        {
            _state |= 0b010;
            Type = type;
        }
        public string Content { get; private set; } = "";

        public bool HaveContent => (_state & 0b100) != 0;

        public void SetContent(string content)
        {
            _state |= 0b100;
            Content = content;
        }
    }

    protected override async void OnStart()
    {
        try
        {
            StatusText = $"Querying database size...";
            var count = await Keys.DBSIZE.SendAsync(Transport, CancellationToken);

            StatusText = $"Keys in database: {count}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }
}
