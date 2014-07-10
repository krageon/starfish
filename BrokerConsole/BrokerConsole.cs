using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Majordomo;

namespace BrokerConsole
{
    public class BrokerConsole
    {
        static void Main(string[] args)
        {
            Console.WriteLine("starting broker");
            var broker = new MajordomoBroker(true);

            var brokerlocation = ConfigurationManager.AppSettings["broker"];
            broker.Bind(brokerlocation); // config file
            CancellationTokenSource tokensource = new CancellationTokenSource();
            Task.Factory.StartNew(broker.Mediate, tokensource.Token, TaskCreationOptions.LongRunning,
                                  TaskScheduler.Default);

            Console.WriteLine("Broker started at "+brokerlocation);
            Console.ReadKey();
        }
    }
}
