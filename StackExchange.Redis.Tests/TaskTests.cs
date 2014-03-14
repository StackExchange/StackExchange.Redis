using System.Threading.Tasks;
using NUnit.Framework;

namespace StackExchange.Redis.Tests
{
    [TestFixture]
    public class TaskTests
    {
        [Test]
        public void CheckContinuationCheck()
        {
            TaskCompletionSource<int> tcs = new TaskCompletionSource<int>();

            Assert.IsTrue(TaskContinationCheck.NoContinuations(tcs.Task), "vanilla");

            tcs.Task.ContinueWith(x => { });

            Assert.IsFalse(TaskContinationCheck.NoContinuations(tcs.Task), "dirty");
        }
    }
}

