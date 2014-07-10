using System;
using System.Threading;
using System.Threading.Tasks;

namespace LoggerWorker
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var canceller = new CancellationTokenSource();

            const string address = "tcp://127.0.0.1:5555";

            Console.WriteLine("loggerworker starting at {0}", address);

            var worker = new Majordomo.LoggerWorker(address);

            Console.WriteLine("created instance");

            Task.Factory.StartNew(worker.Work, canceller.Token, TaskCreationOptions.LongRunning,
                                  TaskScheduler.Default);

            Console.ReadKey();
            Console.WriteLine("shutting down...");
            canceller.Cancel();
        }
    }
}