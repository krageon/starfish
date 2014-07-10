using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Majordomo.Broker.Test
{
    [TestClass]
    public class WorkerTest
    {
        public CancellationTokenSource _canceller;

        public CancellationTokenSource Canceller
        {
            get
            {
                return _canceller ?? (_canceller = new CancellationTokenSource());
            }
        }

        [TestCleanup]
        public void CleanAfterTest()
        {
            if (_canceller != null)
            {
                _canceller.Cancel();
                _canceller.Dispose();
                _canceller = null;
            }
        }

        [TestMethod]
        public void TestTaskChain()
        {
            var start = new Task<String>(() => "henk", Canceller.Token);

            Func<Task<String>, String> intermediate1 = task => task.Result + " intermediate1";
            Func<Task<String>, String> intermediate2 = task => task.Result + " intermediate2";
            Func<Task<String>, String> intermediate3 = task => { throw new InvalidProgramException("Henkjes!"); };

            Action<Task<String>> error = (x) =>
                                             {
                                                 Console.WriteLine("Error_task: {0}", x.Exception.Flatten().Message);
                                             };

            Action<Task> cleanup = (x) => Console.WriteLine("Cleanup_task");

            Action<Task<string>> final = task => Console.WriteLine(task.Result);

            var items = new List<Func<Task<String>, String>>() {intermediate1, intermediate3, intermediate2};

            var currentTask = start;

            foreach (var item in items)
            {
                currentTask
                    .ContinueWith(error, TaskContinuationOptions.OnlyOnFaulted)
                    .ContinueWith(cleanup, TaskContinuationOptions.OnlyOnFaulted);
                
                currentTask = currentTask
                    .ContinueWith(item, TaskContinuationOptions.OnlyOnRanToCompletion);
            }

            currentTask.ContinueWith(error, TaskContinuationOptions.OnlyOnFaulted);
            var endPoint = currentTask
                .ContinueWith(final, TaskContinuationOptions.OnlyOnRanToCompletion);

            start.Start();
            try
            {
                endPoint.Wait();
            }
            catch (Exception e)
            {
                
            }
        }

        [TestMethod]
        public void SequencedWorkTest()
        {
            var sequence = new SequencedWork<String>()
                           {
                               Begin = () => "Start",
                               Cleanup = task => Console.WriteLine("I'm cleaning {0}", task),
                               End = task => Console.WriteLine("Result: {0}\nEnd",task.Result),
                               Error = task => Console.WriteLine("{0}:\n{1}", task.Exception.Flatten().Message, task.Exception.Flatten().StackTrace),
                               intermediates = new List<Func<Task<string>, string>>()
                                               {
                                                   task => task.Result + "\nintermediate1",
                                                   task => task.Result + "\nintermediate2",
                                                   task => task.Result + "\nintermediate3"
                                               }
                           };

            sequence.Run();
        }

        [TestMethod]
        public void SequencedWorkExceptionTest()
        {
            var sequence = new SequencedWork<String>()
            {
                Begin = () => "Start",
                Cleanup = task => Console.WriteLine("I'm cleaning {0}", task),
                End = task => Console.WriteLine("Result: {0}\nEnd", task.Result),
                Error = task => Console.WriteLine("{0}", task.Exception.GetBaseException()),
                intermediates = new List<Func<Task<string>, string>>()
                                               {
                                                   task => task.Result + "\nintermediate1",
                                                   task => task.Result + "\nintermediate2",
                                                   task => {throw new ArgumentException("generic argument exception");}
                                               }
            };

            sequence.Run();
        }
    }
}
