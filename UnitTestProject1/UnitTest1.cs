using DebugOutputToasts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace UnitTestProject1
{
    [TestClass]
    public class UnitTest1
    {
        [TestMethod]
        public void TestConstructor()
        {
            var monitor = new DebugOutputMonitor(output => { return; });

            monitor.Dispose();
        }

        [TestMethod]
        public async Task TestRead()
        {
            ConcurrentQueue<string> outQueue = new ConcurrentQueue<string>();

            var monitor = new DebugOutputMonitor(output => { outQueue.Enqueue(output.outputDebugString); });

            var msg = "Hello World!";
            Debug.WriteLine(msg);

            await Task.Delay(50);

            Assert.IsTrue(outQueue.Contains(msg + Environment.NewLine));
            
            monitor.Dispose();
        }
    }
}
