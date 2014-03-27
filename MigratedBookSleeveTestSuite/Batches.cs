//using NUnit.Framework;
//using System;
//using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;

//namespace Tests
//{
//    [TestFixture]
//    public class Batches
//    {
//        [Test]
//        public void TestBatchNotSent()
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "batch");
//                conn.Strings.Set(0, "batch", "batch-not-sent");
//                var tasks = new List<Task>();
//                using (var batch = conn.CreateBatch())
//                {
//                    tasks.Add(batch.Keys.Remove(0, "batch"));
//                    tasks.Add(batch.Sets.Add(0, "batch", "a"));
//                    tasks.Add(batch.Sets.Add(0, "batch", "b"));
//                    tasks.Add(batch.Sets.Add(0, "batch", "c"));
//                }
//                Assert.AreEqual("batch-not-sent", conn.Wait(conn.Strings.GetString(0, "batch")));
//            }
//        }

//        [Test]
//        public void TestBatchSentTogether()
//        {
//            TestBatchSent(true);
//        }
//        [Test]
//        public void TestBatchSentApart()
//        {
//            TestBatchSent(false);
//        }
//        private void TestBatchSent(bool together)
//        {
//            using (var conn = Config.GetUnsecuredConnection())
//            {
//                conn.Keys.Remove(0, "batch");
//                conn.Strings.Set(0, "batch", "batch-sent");
//                var tasks = new List<Task>();
//                using (var batch = conn.CreateBatch())
//                {
//                    tasks.Add(batch.Keys.Remove(0, "batch"));
//                    tasks.Add(batch.Sets.Add(0, "batch", "a"));
//                    tasks.Add(batch.Sets.Add(0, "batch", "b"));
//                    tasks.Add(batch.Sets.Add(0, "batch", "c"));
//                    batch.Send(together);
//                }
//                var result = conn.Sets.GetAllString(0, "batch");
//                tasks.Add(result);
//                Task.WhenAll(tasks.ToArray());

//                var arr = result.Result;
//                Array.Sort(arr);
//                Assert.AreEqual(3, arr.Length);
//                Assert.AreEqual("a", arr[0]);
//                Assert.AreEqual("b", arr[1]);
//                Assert.AreEqual("c", arr[2]);
//            }
//        }
//    }
//}
