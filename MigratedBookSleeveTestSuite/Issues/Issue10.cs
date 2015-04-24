using NUnit.Framework;

namespace Tests.Issues
{
    [TestFixture]
    public class Issue10
    {
        [Test]
        public void Execute()
        {
            using (var muxer = Config.GetUnsecuredConnection())
            {
                const int DB = 5;
                const string Key = "issue-10-list";
                var conn = muxer.GetDatabase(DB);
                conn.KeyDeleteAsync(Key); // contents: nil
                conn.ListLeftPushAsync(Key, "abc"); // "abc"
                conn.ListLeftPushAsync(Key, "def"); // "def", "abc"
                conn.ListLeftPushAsync(Key, "ghi"); // "ghi", "def", "abc",
                conn.ListSetByIndexAsync(Key, 1, "jkl"); // "ghi", "jkl", "abc"

                var contents = conn.Wait(conn.ListRangeAsync(Key, 0, -1));
                Assert.AreEqual(3, contents.Length);
                Assert.AreEqual("ghi", (string)contents[0]);
                Assert.AreEqual("jkl", (string)contents[1]);
                Assert.AreEqual("abc", (string)contents[2]);
            }
        }
    }
}
