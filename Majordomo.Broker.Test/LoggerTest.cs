using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Majordomo_Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Majordomo.Broker.Test
{
    /// <summary>
    ///     Summary description for LoggerTest
    /// </summary>
    [TestClass]
    public class LoggerTest
    {
        #region Additional test attributes

        //
        // You can use the following additional attributes as you write your tests:
        //
        // Use ClassInitialize to run code before running the first test in the class
        // [ClassInitialize()]
        // public static void MyClassInitialize(TestContext testContext) { }
        //
        // Use ClassCleanup to run code after all tests in a class have run
        // [ClassCleanup()]
        // public static void MyClassCleanup() { }
        //
        // Use TestInitialize to run code before running each test 
        // [TestInitialize()]
        // public void MyTestInitialize() { }
        [TestCleanup]
        public void Cleanup()
        {
            if (_canceller != null)
            {
                _canceller.Cancel();
                _canceller.Dispose();
                _canceller = null;
            }

            this._dataWriter = null;
            this._logger = null;
        }

        #endregion

        private static CancellationTokenSource _canceller;

        public static CancellationTokenSource Canceller
        {
            get { return _canceller ?? (_canceller = new CancellationTokenSource()); }
        }

        private IDataWriter _dataWriter;

        public IDataWriter DataWriter
        {
            get { return this._dataWriter ?? (this._dataWriter = new DummyDataWriter()); }
        }

        private LoggerWorker _logger;

        public LoggerWorker Logger
        {
            get
            {
                if (this._logger == null)
                {
                    this._logger = new LoggerWorker("tcp://127.0.0.1:5555")
                                   {
                                       DataWriter = this.DataWriter
                                   };
                }

                return this._logger;
            }
        }

        [TestMethod]
        public void LogWriteReadTest()
        {
            var messageConstruct = new
                                   {
                                       Caller = "0xDEADBEEF",
                                       Target = "0xFAFA",
                                       Message = "UnitTest",
                                       Rest = new List<String>
                                              {
                                                  "this",
                                                  "is",
                                                  "the",
                                                  "rest"
                                              }
                                   };

            ///     Frame 0: Empty frame
            ///     Frame 1: "MDPW01" (six bytes, representing MDP/Worker v0.1)
            ///     Frame 2: 0x02 (one byte, representing REQUEST)
            ///     Frame 3: Client address (envelope stack)
            ///     Frame 4: Empty (zero bytes, envelope delimiter)
            ///     Frame 5: Origin (Address, byte[])
            ///     Frame 6: Target (Address, byte[])
            ///     Frame 7: Message (String)
            ///     Frame 8+: Binary request data

            var msg = new List<byte[]>()
                      {
                          MDP.M_EMPTY,
                          MDP.W_WORKER_B,
                          new [] {MDP.W_REQUEST},
                          "0x100001".HexStrToByteArray(),
                          MDP.M_EMPTY,
                          messageConstruct.Caller.HexStrToByteArray(),
                          messageConstruct.Target.HexStrToByteArray(),
                          messageConstruct.Message.ToBytes()
                      };

            foreach (var item in messageConstruct.Rest)
            {
                msg.Add(item.ToBytes());
            }

            this.Logger.GenerateResponse(msg);
            var result = (DummyDataWriter) this.DataWriter;
            Assert.AreEqual(messageConstruct.Caller, result.Caller);
            Assert.AreEqual(messageConstruct.Target, result.Target);
            Assert.AreEqual(messageConstruct.Message, result.Message);

            for (int i = 0; i < result.RawMessage.Count; i++)
            {
                string original = messageConstruct.Rest.ElementAt(i);
                string output = result.RawMessage.ElementAt(i).ToUnicodeString();

                Assert.AreEqual(original, output);
            }
        }
    }
}