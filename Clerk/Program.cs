using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Clerk
{
    class Program
    {
        static void Main(string[] args)
        {
            var canceller = new CancellationTokenSource();

            Console.WriteLine("Clerk starting");

            var worker = new Clerk();

            Console.WriteLine("created instance");

            Task.Factory.StartNew(worker.Work, canceller.Token, TaskCreationOptions.LongRunning,
                                  TaskScheduler.Default);

            Console.ReadKey();
            Console.WriteLine("shutting down...");
            canceller.Cancel();
        }
    }
}
