using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Majordomo_Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZeroMQ;

namespace ClrZmqTest
{
    public class EchoService
    {
        private ZmqContext ctx;
        private ZmqSocket sock;

        public String addr = "tcp://127.0.0.1:5555";

        public EchoService()
        {
            ctx = ZmqContext.Create();
            sock = ctx.CreateSocket(SocketType.REP);
        }

        public void Work()
        {
            sock.Bind(addr);
            sock.Linger = new TimeSpan(250*TimeSpan.TicksPerMillisecond);

            sock.ReceiveReady += (sender, args) =>
                                 {
                                     var received = args.Socket.ReceiveMessage();
                                     args.Socket.SendMessage(received);
                                 };

            var poller = new Poller(new[] {sock});

            while (true)
            {
                poller.Poll();
            }
        }
    }

    [TestClass]
    public class DisposabilityTest
    {
        public CancellationTokenSource canceller;

        [TestInitialize]
        public void createTokenSource()
        {
            this.canceller = new CancellationTokenSource();
        }

        [TestCleanup]
        public void cancelRunningTasks()
        {
            this.canceller.Cancel();
        }

        [TestMethod]
        public void AutomaticDispose()
        {
            var serv = new EchoService();

            Task.Factory.StartNew(serv.Work, canceller.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            using(var context = ZmqContext.Create())
            using (var socket = context.CreateSocket(SocketType.REQ))
            {
                socket.Connect(serv.addr);
                
                socket.SendMessage(new ZmqMessage(new[] {"hoi".ToBytes()}));

                var response = socket.ReceiveMessage();

                Assert.IsFalse(String.IsNullOrEmpty(response.Select(f => (byte[]) f).ToStringBlob()));
            }
        }
    }
}
