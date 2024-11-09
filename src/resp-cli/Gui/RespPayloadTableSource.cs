using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespPayloadTableSource(TableView parent) : ITableSource
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
