using DebugOutputToasts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
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
            string s_out = "";

            var monitor = new DebugOutputMonitor(output => { s_out = output.outputDebugString; });

            var s = "Hello World!";
            Debug.WriteLine(s);

            await Task.Delay(500);

            Assert.AreEqual(s, s_out);

            monitor.Dispose();
        }
    }
}
