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
    [TestClass]
    public class HelperTest
    {
        public static CancellationTokenSource Canceller;

        [TestInitialize()]
        public void CreateTokenSource()
        {
            Canceller = new CancellationTokenSource();
        }

        [TestCleanup()]
        public void Cleanup()
        {
            Canceller.Cancel();
        }

        [ClassCleanup]
        public void SleepAfterDone()
        {
            Thread.Sleep(250);
        }

        [TestMethod]
        public void HexStrTest()
        {
            var source = new byte[] { 0x20, 0x21, 0x22, 0xff };
            const string goal = "0x202122FF";

            Assert.AreEqual(goal, source.ToHexStr());
        }

        [TestMethod]
        public void FromHexText()
        {
            var goal = new byte[] { 0x20, 0x21, 0x22, 0xff };
            const string source = "0x202122FF";

            byte[] output = source.HexStrToByteArray();

            Console.WriteLine(output.ToHexStr());

            CollectionAssert.AreEqual(goal, output);
        }

        [TestMethod]
        public void SendAllTest()
        {
            Task.Factory.StartNew(() =>
                                  {
                                      using (var context = ZmqContext.Create())
                                      using (var socket = context.CreateSocket(SocketType.REP))
                                      {
                                          socket.Linger = new TimeSpan(250 * TimeSpan.TicksPerMillisecond);
                                          socket.Bind("tcp://127.0.0.1:5555");

                                          socket.ReceiveReady += (sender, args) =>
                                                                 {
                                                                     args.Socket.ReceiveMessage();

                                                                     args.Socket.SendAll(new List<string>()
                                                                                             {
                                                                                                 "1",
                                                                                                 "2"
                                                                                             });
                                                                 };

                                          new Poller(new[] { socket }).Poll();
                                      }
                                  }, Canceller.Token);

            using (var context = ZmqContext.Create())
            using (var socket = context.CreateSocket(SocketType.REQ))
            {
                socket.Linger = new TimeSpan(250 * TimeSpan.TicksPerMillisecond);
                socket.Connect("tcp://127.0.0.1:5555");

                var msg = new List<string>();

                socket.ReceiveReady +=
                    (sender, args) =>
                    {
                        msg = args.Socket.ReceiveMessage().Select(f => ((byte[])f).ToUnicodeString()).ToList();
                    };


                socket.Send(" ", Encoding.UTF8);

                var poller = new Poller(new[] { socket });
                poller.Poll(new TimeSpan(750*TimeSpan.TicksPerMillisecond));

                Assert.AreEqual("1", msg.Dequeue());
                Assert.AreEqual("2", msg.Dequeue());
            }
        }

        [TestMethod]
        public void PushTest()
        {
            var list = new List<String>();

            list.Push("1");

            Assert.AreEqual("1", list.First());
        }

        [TestMethod]
        public void PopTest()
        {
            var list = new List<String> { "1" };

            Assert.AreEqual("1", list.Pop());
            Assert.IsFalse(list.Any());
        }

        [TestMethod]
        public void PrependTest()
        {
            var msg = new List<string>() { "3" };

            msg.Prepend(new[] { "1", "2" });

            Assert.AreEqual("1", msg[0]);
            Assert.AreEqual("2", msg[1]);
            Assert.AreEqual("3", msg[2]);
        }

        [TestMethod]
        public void ToStringBlobTest()
        {
            var msg = new List<String>()
                      {
                          "1",
                          "2"
                      };

            Assert.AreEqual(String.Format("1{0}2{0}", Environment.NewLine), msg.ToStringBlob());
        }

        [TestMethod()]
        public void ByteConvertTest()
        {
            const string source = "1";
            byte[] sourcetogoal = source.ToBytes();
            var goal = new byte[] { 49 };
            string goaltosource = goal.ToUnicodeString();

            CollectionAssert.AreEqual(goal, sourcetogoal);
            Assert.AreEqual(source, goaltosource);
        }

        [TestMethod()]
        public void RouterTest()
        {
            Task server = Task.Factory.StartNew(() =>
                                                {
                                                    Console.WriteLine("Router starting");

                                                    using (var context = ZmqContext.Create())
                                                    using (var socket = context.CreateSocket(SocketType.ROUTER))
                                                    {
                                                        socket.Linger = new TimeSpan(250 * TimeSpan.TicksPerMillisecond);
                                                        socket.Bind("tcp://127.0.0.1:5555");

                                                        socket.ReceiveReady += (sender, args) =>
                                                                               {
                                                                                   var message =
                                                                                       args.Socket.ReceiveMessage();
                                                                                   byte[] address = message[0];
                                                                                   var response = new List<byte[]>()
                                                                                                  {
                                                                                                      address,
                                                                                                      "".ToBytes(),
                                                                                                      "Hello world".ToBytes()
                                                                                                  };

                                                                                   args.Socket.SendAll(response);
                                                                               };
                                                        var poller = new Poller(new[] { socket });

                                                        poller.Poll();
                                                    }
                                                }, Canceller.Token, TaskCreationOptions.LongRunning,
                                                TaskScheduler.Default);

            server.Wait(500);

            using (var context = ZmqContext.Create())
            using (var socket = context.CreateSocket(SocketType.REQ))
            {
                socket.Linger = new TimeSpan(250 * TimeSpan.TicksPerMillisecond);
                socket.Connect("tcp://127.0.0.1:5555");

                var msg = new ZmqMessage();

                socket.ReceiveReady += (sender, args) =>
                                       {
                                           msg = args.Socket.ReceiveMessage();
                                       };

                socket.Send(" ", Encoding.UTF8);

                var poller = new Poller(new[] { socket });
                poller.Poll(new TimeSpan(500 * TimeSpan.TicksPerMillisecond));

                string response = "";

                byte[] tmp = new byte[0];
                if (msg.FrameCount > 0)
                    tmp = (byte[])msg.Unwrap();
                response = tmp.ToUnicodeString();

                Assert.AreEqual("Hello world", response);
            }
        }
    }
}