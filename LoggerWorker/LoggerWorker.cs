using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Majordomo_Protocol;
using MySql.Data.MySqlClient;
using MySql.Data.Types;

namespace Majordomo
{
    /// <summary>
    /// The log worker. Accepts messages and writes them out over a data interface.
    /// 
    /// Expects a standard worker request, with these frames:
    ///     Frame 0: Empty frame
    ///     Frame 1: "MDPW01" (six bytes, representing MDP/Worker v0.1)
    ///     Frame 2: 0x02 (one byte, representing REQUEST)
    ///     Frame 3: Client address (envelope stack)
    ///     Frame 4: Empty (zero bytes, envelope delimiter)
    ///     Frame 5: Origin (Address, byte[])
    ///     Frame 6: Target (Address, byte[])
    ///     Frame 7: Message (String, UTF8)
    ///     Frame 8: Service (String, UTF8)
    ///     Frame 9+: Binary request data
    ///     A client request counts from frame 3 instead of 5
    /// </summary>
    public class LoggerWorker : MajordomoWorker
    {
        public IDataWriter DataWriter { get; set; }

        private readonly IFormatProvider culture = new CultureInfo("nl-NL", true);

        public LoggerWorker(String address)
            : base(address, "logger")
        {
            var dataWriter = new MysqlDataWriter();

            var content = new List<byte[]>()
                          {
                              "mysql_connector".ToBytes()
                          };

            var result = this.ConfigClient.SendReceiveRaw(content);
            if (result.Count > 2)
                dataWriter.Connector = result[3].ToUnicodeString();

            this.DataWriter = dataWriter;
            

            this.GenerateResponse = msg =>
                                    {
                                        var reply = new List<byte[]>();

                                        try
                                        {
                                            byte[] caller = msg[0];

                                            string time = DateTime.Now.ToString(this.culture);
                                            string origin = msg[5].ToHexStr();
                                            string target = msg[6].ToHexStr();
                                            string message = msg[7].ToUnicodeString();
                                            string service = msg[8].ToUnicodeString();
                                            this.DataWriter.WriteMessage(time, origin, target, service, message, msg);

                                            reply = MDP.Worker.Reply(caller, "logged successfully");
                                        }
                                        catch (Exception e)
                                        {
                                            string a = String.Format("exception:\n{0}\n{1}", e.Message, e.StackTrace);

                                            try
                                            {
                                                File.WriteAllText("error.log", a);
                                            }
                                            catch (Exception)
                                            {
                                            }
                                            Console.WriteLine(a);
                                            // This is totally unrecoverable. Don't send mangled messages here or they'll just be thrown away.
                                            //  Yes, it's possible to fail in a prettier way but I'm not going to.
                                        }

                                        return reply;
                                    };
        }
    }

    /// <summary>
    /// An interface definition for a DataWriter, which provides an abstracted way to write to a place
    /// </summary>
    public interface IDataWriter
    {
        bool WriteMessage(
            String time, 
            String caller, 
            String target, 
            String service,
            String message,
            List<byte[]> rawMessage);
    }

    /// <summary>
    /// An implementation of DataWriter for testing purposes. This stores the written data in the class so that it can be verified after.
    /// </summary>
    public class DummyDataWriter : IDataWriter
    {
        public String Time { get; set; }
        public String Caller { get; set; }
        public String Target { get; set; }
        public String Message { get; set; }

        public String Service { get; set; }
        public List<byte[]> RawMessage;

        public DummyDataWriter()
        {
            this.RawMessage = new List<byte[]>();
        }

        public bool WriteMessage(string time, string caller, string target, string service,
            string message, List<byte[]> rawMessage)
        {
            this.Time = time;
            this.Caller = caller;
            this.Target = target;
            this.Message = message;
            this.Service = service;

            Console.WriteLine("Writing passed message {3} at {0} from {1} to {2} for {4}", time, caller, target, message, service);

            this.RawMessage = new List<byte[]>();

            for (int i = 8; i < rawMessage.Count(); i++)
            {
                this.RawMessage.Add(rawMessage[i]);
            }

            return true;
        }
    }

    /// <summary>
    /// A DataWriter that writes to a MySQL database. Used in production.
    /// </summary>
    public class MysqlDataWriter : IDataWriter, IDisposable
    {
        private MySqlConnection _connection;

        /// <summary>
        /// The MySqlConnection used to write data.
        /// </summary>
        private MySqlConnection Connection
        {
            get
            {
                if (this.Connector == null)
                    Connector = ConnectorFallback;

                if (this._connection == null)
                    this._connection = new MySqlConnection(Connector);

                if (this._connection.State != ConnectionState.Open)
                    this._connection.Open();

                return this._connection;
            }
        }

        private MySqlCommand MessageCreate
        {
            get { return new MySqlCommand(MessageCreateFallback, this.Connection); }
        }

        private MySqlCommand BlobEntryCreate
        {
            get { return new MySqlCommand(BlobEntryCreateFallback, this.Connection); }
        }

        // TODO: Load this from a place (discuss if ini file or whatever)
        public String Connector = null;

        private const string ConnectorFallback = "SERVER=95.211.74.140;PORT=3306;DATABASE=taskserver;UID=taskserveruser;PASSWORD=5qwiw34f347w34!";

        private const String MessageCreateFallback =
            "INSERT INTO message (time, service, message, origin, target) VALUES (STR_TO_DATE(@time, '%e-%c-%Y %T'), @service, @message, @origin, @target)";

        private const String BlobEntryCreateFallback =
            "INSERT INTO binaryrequest (messageid, binarydata) VALUES (@messageid, @binaryarray)";

        public bool WriteMessage(string time, string caller, string target, string service,
            string message, List<byte[]> rawMessage)
        {
            Console.WriteLine("Writing passed message {3} at {0} from {1} to {2} for {4}", time, caller, target, message, service);
            MySqlCommand messageCreate = this.MessageCreate;
            messageCreate.Parameters.AddWithValue("@time", time);
            messageCreate.Parameters.AddWithValue("@origin", caller);
            messageCreate.Parameters.AddWithValue("@target", target);
            messageCreate.Parameters.AddWithValue("@message", message);
            messageCreate.Parameters.AddWithValue("@service", service);
            messageCreate.Prepare();

            messageCreate.ExecuteNonQuery();

            long messageId = messageCreate.LastInsertedId;

            for (int i = 8; i < rawMessage.Count(); i++)
            {
                var item = rawMessage[i];
                MySqlCommand blobCreate = this.BlobEntryCreate;
                blobCreate.Parameters.AddWithValue("@messageid", messageId);
                blobCreate.Parameters.AddWithValue("@binaryarray", item);
                blobCreate.Prepare();
                blobCreate.ExecuteNonQuery();
                blobCreate.Dispose();
            }

            return true;
        }

        ~MysqlDataWriter()
        {
            this.Dispose();
        }

        public void Dispose()
        {
            if (_connection != null)
                _connection.Dispose();
        }
    }
}