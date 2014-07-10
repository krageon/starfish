using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Janitor
{
    class Program
    {
        static void Main(string[] args)
        {
            var canceller = new CancellationTokenSource();

            Console.WriteLine("Janitor starting");

            var worker = new JanitorCore();

            Console.WriteLine("created instance");

            Task.Factory.StartNew(worker.Worker.Work, canceller.Token, TaskCreationOptions.LongRunning,
                                  TaskScheduler.Default);

            Console.ReadKey();
            Console.WriteLine("shutting down...");
            canceller.Cancel();
        }
    }
}
