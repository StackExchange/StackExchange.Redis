using Xunit;

namespace StackExchange.Redis.Tests.ResultProcessorUnitTests;

public class StreamInfo(ITestOutputHelper log) : ResultProcessorUnitTest(log)
{
    [Fact]
    public void BasicFormat_Success()
    {
        // XINFO STREAM mystream (basic format, not FULL)
        // Interleaved key-value array with entries like first-entry and last-entry as nested arrays
        var resp = "*32\r\n" +
                   "$6\r\nlength\r\n" +
                   ":2\r\n" +
                   "$15\r\nradix-tree-keys\r\n" +
                   ":1\r\n" +
                   "$16\r\nradix-tree-nodes\r\n" +
                   ":2\r\n" +
                   "$17\r\nlast-generated-id\r\n" +
                   "$15\r\n1638125141232-0\r\n" +
                   "$20\r\nmax-deleted-entry-id\r\n" +
                   "$3\r\n0-0\r\n" +
                   "$13\r\nentries-added\r\n" +
                   ":2\r\n" +
                   "$23\r\nrecorded-first-entry-id\r\n" +
                   "$15\r\n1719505260513-0\r\n" +
                   "$13\r\nidmp-duration\r\n" +
                   ":100\r\n" +
                   "$12\r\nidmp-maxsize\r\n" +
                   ":100\r\n" +
                   "$12\r\npids-tracked\r\n" +
                   ":1\r\n" +
                   "$12\r\niids-tracked\r\n" +
                   ":1\r\n" +
                   "$10\r\niids-added\r\n" +
                   ":1\r\n" +
                   "$15\r\niids-duplicates\r\n" +
                   ":0\r\n" +
                   "$6\r\ngroups\r\n" +
                   ":1\r\n" +
                   "$11\r\nfirst-entry\r\n" +
                   "*2\r\n" +
                   "$15\r\n1638125133432-0\r\n" +
                   "*2\r\n" +
                   "$7\r\nmessage\r\n" +
                   "$5\r\napple\r\n" +
                   "$10\r\nlast-entry\r\n" +
                   "*2\r\n" +
                   "$15\r\n1638125141232-0\r\n" +
                   "*2\r\n" +
                   "$7\r\nmessage\r\n" +
                   "$6\r\nbanana\r\n";

        var result = Execute(resp, ResultProcessor.StreamInfo);

        Assert.Equal(2, result.Length);
        Assert.Equal(1, result.RadixTreeKeys);
        Assert.Equal(2, result.RadixTreeNodes);
        Assert.Equal(1, result.ConsumerGroupCount);
        Assert.Equal("1638125141232-0", result.LastGeneratedId.ToString());
        Assert.Equal("0-0", result.MaxDeletedEntryId.ToString());
        Assert.Equal(2, result.EntriesAdded);
        Assert.Equal("1719505260513-0", result.RecordedFirstEntryId.ToString());
        Assert.Equal(100, result.IdmpDuration);
        Assert.Equal(100, result.IdmpMaxSize);
        Assert.Equal(1, result.PidsTracked);
        Assert.Equal(1, result.IidsTracked);
        Assert.Equal(1, result.IidsAdded);
        Assert.Equal(0, result.IidsDuplicates);

        Assert.Equal("1638125133432-0", result.FirstEntry.Id.ToString());
        Assert.Equal("apple", result.FirstEntry["message"]);

        Assert.Equal("1638125141232-0", result.LastEntry.Id.ToString());
        Assert.Equal("banana", result.LastEntry["message"]);
    }

    [Fact]
    public void MinimalFormat_Success()
    {
        // Minimal XINFO STREAM response with just required fields
        var resp = "*14\r\n" +
                   "$6\r\nlength\r\n" +
                   ":0\r\n" +
                   "$15\r\nradix-tree-keys\r\n" +
                   ":1\r\n" +
                   "$16\r\nradix-tree-nodes\r\n" +
                   ":1\r\n" +
                   "$6\r\ngroups\r\n" +
                   ":0\r\n" +
                   "$11\r\nfirst-entry\r\n" +
                   "$-1\r\n" +
                   "$10\r\nlast-entry\r\n" +
                   "$-1\r\n" +
                   "$17\r\nlast-generated-id\r\n" +
                   "$3\r\n0-0\r\n";

        var result = Execute(resp, ResultProcessor.StreamInfo);

        Assert.Equal(0, result.Length);
        Assert.Equal(1, result.RadixTreeKeys);
        Assert.Equal(1, result.RadixTreeNodes);
        Assert.Equal(0, result.ConsumerGroupCount);
        Assert.True(result.FirstEntry.IsNull);
        Assert.True(result.LastEntry.IsNull);
    }

    [Fact]
    public void NotArray_Failure()
    {
        var resp = "$5\r\nhello\r\n";

        ExecuteUnexpected(resp, ResultProcessor.StreamInfo);
    }

    [Fact]
    public void Null_Failure()
    {
        var resp = "$-1\r\n";

        ExecuteUnexpected(resp, ResultProcessor.StreamInfo);
    }
}
