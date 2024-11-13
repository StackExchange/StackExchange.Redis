using System.Buffers;
using Terminal.Gui;

namespace StackExchange.Redis.Gui;

internal sealed class RespPayloadTableSource(TableView parent, int maxCount = 100) : ITableSource
{
    private readonly List<RespPayloadBase> items = new();

    public int Count => items.Count;

    public int MaxCount { get; } = maxCount;
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

            return Utils.Truncate(val, maxWidth);
        }
    }

    string[] ITableSource.ColumnNames => ["Request", "Response"];

    int ITableSource.Columns => 2;

    int ITableSource.Rows => items.Count;

    public void TrimToLength(int count)
    {
        if (items.Count > count)
        {
            var remove = items.Count - count;
            if (remove == 1)
            {
                var kill = items[count];
                items.RemoveAt(count);
                kill.Dispose();
            }
            else
            {
                var cleanup = ArrayPool<RespPayloadBase>.Shared.Rent(remove);
                items.CopyTo(count, cleanup, 0, remove);
                items.RemoveRange(index: count, count: remove);

                for (int i = 0; i < remove; i++)
                {
                    cleanup[i].Dispose();
                }
                ArrayPool<RespPayloadBase>.Shared.Return(cleanup);
            }
        }
    }

    public void RemoveAt(int index) => items.RemoveAt(index);

    public void Insert(int index, RespPayloadBase value)
    {
        items.Insert(0, value);
        TrimToLength(MaxCount);
    }
}
