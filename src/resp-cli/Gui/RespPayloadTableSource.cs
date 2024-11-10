using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespPayloadTableSource(TableView parent) : ITableSource
{
    private readonly List<RespPayloadBase> items = new();

    public int Count => items.Count;

    public RespPayloadBase this[int index] => items[index];

    public int Width => parent.GetContentSize().Width;
    object ITableSource.this[int row, int col]
    {
        get
        {
            var obj = items[row];
            int maxWidth = col switch
            {
                0 => 15,
                _ => Width - 18,
            };
            var val = col switch
            {
                0 => obj.GetRequest(maxWidth + 1),
                1 => obj.GetResponse(maxWidth + 1),
                _ => throw new IndexOutOfRangeException(),
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

    public void Insert(int index, RespPayloadBase value) => items.Insert(0, value);
}
