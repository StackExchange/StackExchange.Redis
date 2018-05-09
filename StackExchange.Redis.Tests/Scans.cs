using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace StackExchange.Redis.Tests
{
    public class Scans : TestBase
    {
        public Scans(ITestOutputHelper output) : base (output) { }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void KeysScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "scan" };
            using (var conn = Create(disabledCommands: disabledCommands, allowAdmin: true))
            {
                const int DB = 7;
                var db = conn.GetDatabase(DB);
                var server = GetServer(conn);
                server.FlushDatabase(DB);
                for (int i = 0; i < 100; i++)
                {
                    db.StringSet("KeysScan:" + i, Guid.NewGuid().ToString(), flags: CommandFlags.FireAndForget);
                }
                var seq = server.Keys(DB, pageSize: 50);
                bool isScanning = seq is IScanningCursor;
                Assert.Equal(supported, isScanning);
                Assert.Equal(100, seq.Distinct().Count());
                Assert.Equal(100, seq.Distinct().Count());
                Assert.Equal(100, server.Keys(DB, "KeysScan:*").Distinct().Count());
                // 7, 70, 71, ..., 79
                Assert.Equal(11, server.Keys(DB, "KeysScan:7*").Distinct().Count());
            }
        }

        [Fact]
        public void ScansIScanning()
        {
            using (var conn = Create(allowAdmin: true))
            {
                const int DB = 7;
                var db = conn.GetDatabase(DB);
                var server = GetServer(conn);
                server.FlushDatabase(DB);
                for (int i = 0; i < 100; i++)
                {
                    db.StringSet("ScansRepeatable:" + i, Guid.NewGuid().ToString(), flags: CommandFlags.FireAndForget);
                }
                var seq = server.Keys(DB, pageSize: 15);
                using (var iter = seq.GetEnumerator())
                {
                    IScanningCursor s0 = (IScanningCursor)seq, s1 = (IScanningCursor)iter;

                    Assert.Equal(15, s0.PageSize);
                    Assert.Equal(15, s1.PageSize);

                    // start at zero                    
                    Assert.Equal(0, s0.Cursor);
                    Assert.Equal(s0.Cursor, s1.Cursor);

                    for (int i = 0; i < 47; i++)
                    {
                        Assert.True(iter.MoveNext());
                    }

                    // non-zero in the middle
                    Assert.NotEqual(0, s0.Cursor);
                    Assert.Equal(s0.Cursor, s1.Cursor);

                    for (int i = 0; i < 53; i++)
                    {
                        Assert.True(iter.MoveNext());
                    }

                    // zero "next" at the end
                    Assert.False(iter.MoveNext());
                    Assert.NotEqual(0, s0.Cursor);
                    Assert.NotEqual(0, s1.Cursor);
                }
            }
        }

        [Fact(Skip = "Windows Redis 3.x is flaky here. The test runs fine against other servers...")]
        public void ScanResume()
        {
            using (var conn = Create(allowAdmin: true))
            {
                const int DB = 7;
                var db = conn.GetDatabase(DB);
                var server = GetServer(conn);
                server.FlushDatabase(DB);
                int i;
                for (i = 0; i < 100; i++)
                {
                    db.StringSet("ScanResume:" + i, Guid.NewGuid().ToString());
                }

                var expected = new HashSet<string>();
                long snapCursor = 0;
                int snapOffset = 0, snapPageSize = 0;

                i = 0;
                var seq = server.Keys(DB, "ScanResume:*", pageSize: 15);
                foreach (var key in seq)
                {
                    if (i == 57)
                    {
                        snapCursor = ((IScanningCursor)seq).Cursor;
                        snapOffset = ((IScanningCursor)seq).PageOffset;
                        snapPageSize = ((IScanningCursor)seq).PageSize;
                        Output.WriteLine($"i: {i}, Cursor: {snapCursor}, Offset: {snapOffset}, PageSize: {snapPageSize}");
                    }
                    if (i >= 57)
                    {
                        expected.Add((string)key);
                    }
                    i++;
                }
                Output.WriteLine($"Expected: 43, Actual: {expected.Count}, Cursor: {snapCursor}, Offset: {snapOffset}, PageSize: {snapPageSize}");
                Assert.Equal(43, expected.Count);
                Assert.NotEqual(0, snapCursor);
                Assert.Equal(12, snapOffset);
                Assert.Equal(15, snapPageSize);

                seq = server.Keys(DB, "ScanResume:*", pageSize: 15, cursor: snapCursor, pageOffset: snapOffset);
                var seqCur = (IScanningCursor)seq;
                Assert.Equal(snapCursor, seqCur.Cursor);
                Assert.Equal(snapPageSize, seqCur.PageSize);
                Assert.Equal(snapOffset, seqCur.PageOffset);
                using (var iter = seq.GetEnumerator())
                {
                    var iterCur = (IScanningCursor)iter;
                    Assert.Equal(snapCursor, iterCur.Cursor);
                    Assert.Equal(snapOffset, iterCur.PageOffset);
                    Assert.Equal(snapCursor, seqCur.Cursor);
                    Assert.Equal(snapOffset, seqCur.PageOffset);

                    Assert.True(iter.MoveNext());
                    Assert.Equal(snapCursor, iterCur.Cursor);
                    Assert.Equal(snapOffset, iterCur.PageOffset);
                    Assert.Equal(snapCursor, seqCur.Cursor);
                    Assert.Equal(snapOffset, seqCur.PageOffset);

                    Assert.True(iter.MoveNext());
                    Assert.Equal(snapCursor, iterCur.Cursor);
                    Assert.Equal(snapOffset + 1, iterCur.PageOffset);
                    Assert.Equal(snapCursor, seqCur.Cursor);
                    Assert.Equal(snapOffset + 1, seqCur.PageOffset);
                }

                int count = 0;
                foreach (var key in seq)
                {
                    expected.Remove((string)key);
                    count++;
                }
                Assert.Empty(expected);
                Assert.Equal(43, count);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SetScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "sscan" };
            using (var conn = Create(disabledCommands: disabledCommands))
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.SetAdd(key, "a");
                db.SetAdd(key, "b");
                db.SetAdd(key, "c");
                var arr = db.SetScan(key).ToArray();
                Assert.Equal(3, arr.Length);
                Assert.True(arr.Contains("a"), "a");
                Assert.True(arr.Contains("b"), "b");
                Assert.True(arr.Contains("c"), "c");
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SortedSetScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "zscan" };
            using (var conn = Create(disabledCommands: disabledCommands))
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.SortedSetAdd(key, "a", 1);
                db.SortedSetAdd(key, "b", 2);
                db.SortedSetAdd(key, "c", 3);

                var arr = db.SortedSetScan(key).ToArray();
                Assert.Equal(3, arr.Length);
                Assert.True(arr.Any(x => x.Element == "a" && x.Score == 1), "a");
                Assert.True(arr.Any(x => x.Element == "b" && x.Score == 2), "b");
                Assert.True(arr.Any(x => x.Element == "c" && x.Score == 3), "c");

                var dictionary = arr.ToDictionary();
                Assert.Equal(1, dictionary["a"]);
                Assert.Equal(2, dictionary["b"]);
                Assert.Equal(3, dictionary["c"]);

                var sDictionary = arr.ToStringDictionary();
                Assert.Equal(1, sDictionary["a"]);
                Assert.Equal(2, sDictionary["b"]);
                Assert.Equal(3, sDictionary["c"]);

                var basic = db.SortedSetRangeByRankWithScores(key, order: Order.Ascending).ToDictionary();
                Assert.Equal(3, basic.Count);
                Assert.Equal(1, basic["a"]);
                Assert.Equal(2, basic["b"]);
                Assert.Equal(3, basic["c"]);

                basic = db.SortedSetRangeByRankWithScores(key, order: Order.Descending).ToDictionary();
                Assert.Equal(3, basic.Count);
                Assert.Equal(1, basic["a"]);
                Assert.Equal(2, basic["b"]);
                Assert.Equal(3, basic["c"]);

                var basicArr = db.SortedSetRangeByScoreWithScores(key, order: Order.Ascending);
                Assert.Equal(3, basicArr.Length);
                Assert.Equal(1, basicArr[0].Score);
                Assert.Equal(2, basicArr[1].Score);
                Assert.Equal(3, basicArr[2].Score);
                basic = basicArr.ToDictionary();
                Assert.Equal(3, basic.Count); //asc
                Assert.Equal(1, basic["a"]);
                Assert.Equal(2, basic["b"]);
                Assert.Equal(3, basic["c"]);

                basicArr = db.SortedSetRangeByScoreWithScores(key, order: Order.Descending);
                Assert.Equal(3, basicArr.Length);
                Assert.Equal(3, basicArr[0].Score);
                Assert.Equal(2, basicArr[1].Score);
                Assert.Equal(1, basicArr[2].Score);
                basic = basicArr.ToDictionary();
                Assert.Equal(3, basic.Count); // desc
                Assert.Equal(1, basic["a"]);
                Assert.Equal(2, basic["b"]);
                Assert.Equal(3, basic["c"]);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void HashScan(bool supported)
        {
            string[] disabledCommands = supported ? null : new[] { "hscan" };
            using (var conn = Create(disabledCommands: disabledCommands))
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                db.HashSet(key, "a", "1");
                db.HashSet(key, "b", "2");
                db.HashSet(key, "c", "3");

                var arr = db.HashScan(key).ToArray();
                Assert.Equal(3, arr.Length);
                Assert.True(arr.Any(x => x.Name == "a" && x.Value == "1"), "a");
                Assert.True(arr.Any(x => x.Name == "b" && x.Value == "2"), "b");
                Assert.True(arr.Any(x => x.Name == "c" && x.Value == "3"), "c");

                var dictionary = arr.ToDictionary();
                Assert.Equal(1, (long)dictionary["a"]);
                Assert.Equal(2, (long)dictionary["b"]);
                Assert.Equal(3, (long)dictionary["c"]);

                var sDictionary = arr.ToStringDictionary();
                Assert.Equal("1", sDictionary["a"]);
                Assert.Equal("2", sDictionary["b"]);
                Assert.Equal("3", sDictionary["c"]);

                var basic = db.HashGetAll(key).ToDictionary();
                Assert.Equal(3, basic.Count);
                Assert.Equal(1, (long)basic["a"]);
                Assert.Equal(2, (long)basic["b"]);
                Assert.Equal(3, (long)basic["c"]);
            }
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void HashScanLarge(int pageSize)
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                for (int i = 0; i < 2000; i++)
                    db.HashSet(key, "k" + i, "v" + i, flags: CommandFlags.FireAndForget);

                int count = db.HashScan(key, pageSize: pageSize).Count();
                Assert.Equal(2000, count);
            }
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void SetScanLarge(int pageSize)
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                for (int i = 0; i < 2000; i++)
                    db.SetAdd(key, "s" + i, flags: CommandFlags.FireAndForget);

                int count = db.SetScan(key, pageSize: pageSize).Count();
                Assert.Equal(2000, count);
            }
        }

        [Theory]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        [InlineData(10000)]
        public void SortedSetScanLarge(int pageSize)
        {
            using (var conn = Create())
            {
                RedisKey key = Me();
                var db = conn.GetDatabase();
                db.KeyDelete(key);

                for (int i = 0; i < 2000; i++)
                    db.SortedSetAdd(key, "z" + i, i, flags: CommandFlags.FireAndForget);

                int count = db.SortedSetScan(key, pageSize: pageSize).Count();
                Assert.Equal(2000, count);
            }
        }
    }
}
