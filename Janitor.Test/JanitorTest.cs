using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Mail.Fakes;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Majordomo_Protocol;
using Microsoft.QualityTools.Testing.Fakes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using RazorTemplates.Core;
using ZeroMQ;
using Exception = System.Exception;
using SocketType = ZeroMQ.SocketType;

namespace Janitor.Test
{
    public class DummyJanitorDataProvider : IJanitorDataProvider
    {
        public List<MessageContainer> GetOrphanedMessages()
        {
            return new List<MessageContainer>()
                   {
                       new MessageContainer()
                       {
                           Id = 1,
                           Origin = "0x00A56BB175828140B186DC95EC881BBA8F",
                           Target = "0x00D28F093634B84955AA7A9D531D76EC5E",
                           Service = "dummyservice",
                           Message = "DO_WORK",
                           Time = DateTime.Now
                       }
                   };
        }

        public List<byte[]> GetOrphanedBinaryRequest(MessageContainer reference)
        {
            var msg = new List<byte[]>()
                      {
                          "servicea".HexStrToByteArray(),
                          MDP.M_EMPTY,
                          MDP.W_WORKER_B,
                          new byte[]{0x02},
                          "0x00A56BB175828140B186DC95EC881BBA8F".HexStrToByteArray(),
                          MDP.M_EMPTY,
                          "0x123123".ToBytes(),
                          "toservicea".ToBytes(),
                          MDP.M_EMPTY,
                          new byte[] {0x0, 0x1, 0x2}
                      };

            return msg;
        }

        public List<MessageContainer> GetElderOrphans(DateTime maxAge, DateTime minAge)
        {
            return new List<MessageContainer>()
                   {
                       new MessageContainer()
                       {
                           Id = 1,
                           Origin = "0x00A56BB175828140B186DC95EC881BBA8F",
                           Target = "0x00D28F093634B84955AA7A9D531D76EC5E",
                           Service = "dummyservice",
                           Message = "DO_WORK",
                           Time = maxAge
                       }
                   };
        }

        public List<MessageContainer> GetCriticalErrors(DateTime from, DateTime to)
        {
            return new List<MessageContainer>()
                   {
                       new MessageContainer()
                       {
                           Id = 1,
                           Origin = "0x00A56BB175828140B186DC95EC881BBA8F",
                           Target = "0x00D28F093634B84955AA7A9D531D76EC5E",
                           Service = "dummyservice",
                           Message = "CRITICAL_ERROR",
                           Time = from
                       }
                   };
        }

        public void AddCriticalError(DateTime time, string message = "CRITICAL_ERROR", string service = "janitor",
                                     string origin = "0x01", string target = "0x01")
        {
            return;
        }
    }


    [TestClass]
    public class JanitorTest
    {
        // Send orphans a day old
        // when N orphans exist, critical error
        // email X with critical errors

        public const string endpoint = "tcp://127.0.0.1:5555";

        public CancellationTokenSource Canceller;

        [TestInitialize]
        public void CreateTokenSource()
        {
            Canceller = new CancellationTokenSource();
        }

        [TestCleanup]
        public void CancelRunningTasks()
        {
            Canceller.Cancel();
        }

        [TestMethod]
        public void TestRazorTemplate()
        {
            var test = @"@foreach (var test in @Model.Tests) {@test <br \>}";

            dynamic model = new { Tests = new List<string>() { "test1", "test2" } };
            var template = Template.Compile(test);
            string body = template.Render(model);

            Assert.AreEqual(@"test1 <br \>test2 <br \>", body);
        }

        [TestMethod]
        public void TestTemplatesFill()
        {
            var test = File.ReadAllText(Path.Combine("Templates", "test.txt"));

            dynamic model = new { Tests = new List<string>() { "test1", "test2" } };
            var template = Template.Compile(test);
            string body = template.Render(model);

            StringAssert.Contains(body, "test1");
            StringAssert.Contains(body, "test2");
        }

        [TestMethod]
        public void TestStrip()
        {
            var janitor = new JanitorCore
                          {
                              DataProvider = new DummyJanitorDataProvider()
                          };

            var message = janitor.DataProvider.GetOrphanedMessages().First();
            var raw = janitor.DataProvider.GetOrphanedBinaryRequest(message);

            var outp = janitor.StripRaw(raw);

            var desired = new List<byte[]>()
                          {
                              "0x123123".ToBytes(),
                              "toservicea".ToBytes(),
                              MDP.M_EMPTY,
                              new byte[] {0x0, 0x1, 0x2}
                          };

            for(int i = 0; i < outp.Count; i++)
                for (int j = 0; j < outp[i].Length; j++)
                    Assert.AreEqual(desired[i][j], outp[i][j]);
        }

        [TestMethod]
        public void TestReplay()
        {
            var janitor = new JanitorCore
                          {
                              DataProvider = new DummyJanitorDataProvider()
                          };

            var output = new List<byte[]>();
            var receptionist = Task.Factory.StartNew(() =>
                                                     {
                                                        using (var context = ZmqContext.Create())
                                                        using (var socket = context.CreateSocket(SocketType.ROUTER))
                                                            {
                                                                socket.Linger = new TimeSpan(250 * TimeSpan.TicksPerMillisecond);
                                                                socket.Bind(endpoint);

                                                                socket.ReceiveReady += (sender, args) =>
                                                                                        {
                                                                                            var stuff = args.Socket.ReceiveMessage();

                                                                                            if (!((byte[])stuff[3]).ToUnicodeString().Equals("config"))
                                                                                                output.AddRange(stuff.Select(f=> (byte[])f));
                                                                                        };

                                                                var poller = new Poller(new[] {socket});

                                                                while (output.Count == 0)
                                                                    poller.Poll();
                                                            }
                                                     }, 
                                                     Canceller.Token,
                                                     TaskCreationOptions.LongRunning, 
                                                     TaskScheduler.Default);

            var message = janitor.DataProvider.GetOrphanedMessages()[0];
            
            janitor.Replay(message);

            if (output.Count == 0)
                Thread.Sleep(50);

            var stripped = janitor.StripRaw(janitor.DataProvider.GetOrphanedBinaryRequest(message));
            var wanted = new List<byte[]>()
                         {
                             message.Target.HexStrToByteArray(),
                             MDP.M_EMPTY,
                             MDP.C_CLIENT_B,
                             message.Service.ToBytes(),
                         };
            
            wanted.AddRange(stripped);

            Assert.AreEqual(output.Count, wanted.Count);

            for(int i = 1; i < output.Count; i++)
                for(int j = 0; j < output[i].Length; j++)
                    Assert.AreEqual(wanted[i][j], output[i][j]);
        }

        [TestMethod]
        public void TestEmail()
        {
            MailMessage output = null;

            using (ShimsContext.Create())
            {
                ShimSmtpClient.AllInstances.SendMailMessage = (client, message) => output = message;

                JanitorAlert.EmailAdmin("errorreport", new {Errors = new List<string>() {"error1", "error2"}}, "test_error");

                StringAssert.Contains(output.Body, "error1");
                StringAssert.Contains(output.Body, "error2");
            }
        }

        [TestMethod]
        public void TestCriticalErrorReport()
        {
            using (ShimsContext.Create())
            {
                MailMessage mail = null;

                ShimSmtpClient.AllInstances.SendMailMessage = (client, message) => mail = message;

                var janitor = new JanitorCore
                {
                    DataProvider = new DummyJanitorDataProvider()
                };

                janitor.EmailCriticalErrorsPastWeek();
            }
        }
    }
}
