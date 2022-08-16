using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DebugOutputHelper
{
    class Program
    {
        static void Main(string[] args)
        {
            string msg = args.Length > 0 ? string.Join(" ", args) : "Hello World!";
            Debug.WriteLine(msg);
            Console.WriteLine(msg);
        }
    }
}
