using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Majordomo_Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZeroMQ;

namespace Majordomo.Test
{
    public static class MajordomoTestHelper
    {
        public static CancellationTokenSource Canceller;

        public static String RequestService(
            String address = "tcp://127.0.0.1:5555",
            String service = "mmi.service")
        {
            using (var context = ZmqContext.Create())
            {
                var socket = context.CreateSocket(SocketType.REQ);
                socket.Linger = new TimeSpan(500*TimeSpan.TicksPerMillisecond);
                String reply = String.Empty;
                socket.Connect(address);

                var msg = new List<String>()
                            {
                                MDP.C_CLIENT,
                                service,
                                "HERRO"
                            };

                socket.SendAll(msg);

                socket.ReceiveReady +=
                    (sender, args) => reply = args.Socket.ReceiveMessage().Select(f => (byte[]) f).ToList().ToStringBlob();

                var poller = new Poller(new [] {socket});
                poller.Poll(new TimeSpan(750*TimeSpan.TicksPerMillisecond));
                socket.Close();

                return reply;
            }
        }

        public static void InitialiseWorker(String broker = "tcp://127.0.0.1:5555",
                                            String service = "testservice")
        {
            Task.Factory.StartNew(() =>
                                  {
                                      var worker = new MajordomoWorker(broker, service);
                                      worker.Work();
                                  }, Canceller.Token);
        }

        public static void InitialiseBroker(String address = "tcp://127.0.0.1:5555")
        {
            var broker = new MajordomoBroker(true);
            broker.Bind(address);

            Task.Factory.StartNew(broker.Mediate, Canceller.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    [TestClass]
    public class BrokerTest
    {
        [TestInitialize]
        public void CreateTokenSource()
        {
            MajordomoTestHelper.Canceller = new CancellationTokenSource();
        }

        [TestCleanup]
        public void Cleanup()
        {
            MajordomoTestHelper.Canceller.Cancel();
        }

        [TestMethod]
        public void BindBroker()
        {
            MajordomoTestHelper.InitialiseBroker();
            Thread.Sleep(250);
            MajordomoTestHelper.Canceller.Cancel();
        }

        [TestMethod]
        public void NonExistentServiceRequest()
        {
            MajordomoTestHelper.InitialiseBroker();

            Thread.Sleep(500);

            string response = MajordomoTestHelper.RequestService();

            MajordomoTestHelper.Canceller.Cancel();

            Assert.IsTrue(response.Contains("404"), String.Format("Response was: {0}", response));
        }

        [TestMethod]
        public void InitialiseWorker()
        {
            MajordomoTestHelper.InitialiseBroker();
            MajordomoTestHelper.InitialiseWorker();

            MajordomoTestHelper.Canceller.Cancel();
        }

        [TestMethod]
        public void TestService()
        {
            MajordomoTestHelper.InitialiseBroker();
            MajordomoTestHelper.InitialiseWorker();

            string response = MajordomoTestHelper.RequestService("tcp://127.0.0.1:5555", "testservice");
            MajordomoTestHelper.Canceller.Cancel();

            Console.WriteLine("Output: {0}", response);
        }
    }
}