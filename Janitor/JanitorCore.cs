using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using Majordomo;
using Majordomo_Protocol;
using MySql.Data.MySqlClient;

namespace Janitor
{
    /// <summary>
    /// Janitor's Core.
    /// Expects a standard request, with the following additions:
    /// Frame 5: Type of cleanup to perform, one of: ReplayOrphans, GenerateCriticalErrors, EmailCriticalErrorsPastWeek
    /// </summary>
    public class JanitorCore
    {
        private const String _address = "tcp://127.0.0.1:5555";
        private String _outboundService = "logger";
        private String _inboundService = "janitor";

        private DateTime MaxAge { get; set; }
        private DateTime MinAge { get; set; }

        /// <summary>
        /// Provides data that is used to operate. By default this is a MySQL interface.
        /// </summary>
        public IJanitorDataProvider DataProvider { get; set; }

        private MajordomoClient _client;
        /// <summary>
        /// The out-facing side of the janitor (this is used to resend requests)
        /// </summary>
        public MajordomoClient Client
        {
            get
            {
                return _client ?? (_client = new MajordomoClient(_address, this._outboundService));
            }
            set
            {
                _client = value;
            }
        }

        private MajordomoWorker _worker;
        /// <summary>
        /// The incoming side of the janitor (this is used to receive requests)
        /// </summary>
        public MajordomoWorker Worker
        {
            get
            {
                if (_worker == null)
                {
                    _worker = new MajordomoWorker(_address, this._inboundService);
                }
                return _worker;
            }
            set
            {
                _worker = value;
            }
        }

        public JanitorCore()
        {
            int maxAge = -14;
            int minAge = -7;

            List<byte[]> configRequest = new List<byte[]>()
                                         {
                                             "maxage".ToBytes(),
                                             "minage".ToBytes(),
                                             "smtp_host".ToBytes(),
                                             "smtp_port".ToBytes(),
                                             "smtp_user".ToBytes(),
                                             "smtp_pass".ToBytes(),
                                             "smtp_from".ToBytes(),
                                             "smtp_to".ToBytes(),
                                             "smtp_subject".ToBytes(),
                                             "smtp_body".ToBytes()
                                         };

            var values = Worker.ConfigClient.SendReceiveRaw(configRequest);

            if (values.Count-3 >= configRequest.Count)
            {
                values = values.GetRange(3, values.Count - 3);
                maxAge = Int32.Parse(values[0].ToUnicodeString());
                minAge = Int32.Parse(values[1].ToUnicodeString());
                JanitorAlert.host = values[2].ToUnicodeString();
                JanitorAlert.port = Int32.Parse(values[3].ToUnicodeString());
                JanitorAlert.user = values[4].ToUnicodeString();
                JanitorAlert.pass = values[5].ToUnicodeString();
                JanitorAlert.from = values[6].ToUnicodeString();
                JanitorAlert.to = values[7].ToUnicodeString();
                JanitorAlert.default_subject = values[8].ToUnicodeString();
                JanitorAlert.default_body = values[9].ToUnicodeString();
            }

            DataProvider = new MySQLJanitorDataProvider();
            MaxAge = DateTime.Now.AddDays(maxAge);
            MinAge = DateTime.Now.AddDays(minAge);

            Worker.GenerateResponse = this.HandleMessage;
        }

        public List<byte[]> HandleMessage(List<byte[]> input)
        {
            switch (input[5].ToUnicodeString().ToLowerInvariant())
            {
                case "replayorphans":
                    this.ReplayOrphans();
                    break;
                case "generatecriticalerrors":
                    this.GenerateCriticalErrors();
                    break;
                case "emailcriticalerrors":
                    this.EmailCriticalErrorsPastWeek();
                    break;
            }



            var content = new List<byte[]>()
                   {
                       "Processed order".ToBytes(),
                       input[5]
                   };

            return MDP.Worker.Reply(input[3], content);
        }

        /// <summary>
        /// Replays a request to the broker so that it looks new
        /// </summary>
        /// <param name="m">The MessageContainer containing the message to resend (raw, unprocessed)</param>
        public void Replay(MessageContainer m)
        {
            var binaryRequest = DataProvider.GetOrphanedBinaryRequest(m);
            var messageContent = this.StripRaw(binaryRequest);

            Client.Service = m.Service;
            Client.SendReceiveString(messageContent);
        }

        /// <summary>
        /// Replays all orphaned requests
        /// </summary>
        public void ReplayOrphans()
        {
            foreach (var message in DataProvider.GetOrphanedMessages())
            {
                this.Replay(message);
            }
        }

        /// <summary>
        /// Generates Critical errors for relevant items using DataProvider.GetElderOrphans.
        /// </summary>
        public void GenerateCriticalErrors()
        {
            foreach (var message in DataProvider.GetElderOrphans(MaxAge, MinAge))
            {
                DataProvider.AddCriticalError(DateTime.Now, String.Format("CRITICAL_ERROR: {0} at {1}",message.Message, message.Time), message.Service, message.Origin, message.Target);
            }
        }

        /// <summary>
        /// Sends out emails for critical errors that have occurred over the past week. Needs to be called only once a week, obviously.
        /// </summary>
        public void EmailCriticalErrorsPastWeek()
        {
            var errors = this.DataProvider.GetCriticalErrors(this.MinAge, DateTime.Now)
                .Select(message => 
                    String.Format("{0}: {1}, from {2} to {3}", 
                    message.Time, 
                    message.Message, 
                    message.Origin, 
                    message.Target))
                    .ToList();

            JanitorAlert.EmailAdmin("errorreport", new {Errors = errors});
        }

        /// <summary>
        /// A helper function that strips a raw message of it's header so it can be replayed
        /// </summary>
        /// <param name="rawinput">Data straight from BinaryRequest</param>
        /// <returns>The input, sans the first 6 items</returns>
        public List<byte[]> StripRaw(List<byte[]> rawinput)
        {
            return rawinput.GetRange(6, rawinput.Count - 6);
        }
    }

    /// <summary>
    /// A POCO Containing all relevant DB information (database = taskserver)
    /// </summary>
    public class MessageContainer
    {
        public int Id { get; set; }
        public DateTime Time { get; set; }
        public String Message { get; set; }
        public String Service { get; set; }
        public String Target { get; set; }
        public String Origin { get; set; }
    }

    /// <summary>
    /// An abstraction to handle dataproviders, in case this ever needs to be refactored to work on top of a framework.
    /// </summary>
    public interface IJanitorDataProvider
    {
        List<MessageContainer> GetOrphanedMessages();
        List<byte[]> GetOrphanedBinaryRequest(MessageContainer reference);
        List<MessageContainer> GetElderOrphans(DateTime maxAge, DateTime minAge); // These orphans have been around for too long
        List<MessageContainer> GetCriticalErrors(DateTime from, DateTime to);

        void AddCriticalError(DateTime time, String message = "CRITICAL_ERROR", String service = "janitor",
                              String origin = "0x01",
                              String target = "0x01");
    }

    /// <summary>
    /// The default dataprovider. Taps directly into the database using a MySQLDataProvider.
    /// </summary>
    public class MySQLJanitorDataProvider : IJanitorDataProvider
    {
        private const String MessageQuery = 
            "select a.id, a.time, a.service, a.message, a.origin, a.target from message a left join message b on a.origin = b.target and a.service = b.service and b.id > a.id where b.id is null and a.message = 'DO_WORK' order by a.id desc";

        private const String BinaryRequestQuery =
            "SELECT binarydata FROM binaryrequest WHERE messageId = @messageid ORDER BY id ASC";

        private const string ElderMessageQuery =
            @"select a.id, a.time, a.service, a.message, a.origin, a.target 
                from message a left join message b 
                on a.origin = b.target 
	                and a.service = b.service 
	                and b.id > a.id 
                where b.id is null 
	                and a.message = 'DO_WORK' 
	                and a.time > @minAge
	                and a.time < @maxAge
                order by a.id desc";

        private const string CriticalErrorsQuery =
            @"select a.id, a.time, a.service, a.message, a.origin, a.target 
                from message a
                where a.message = 'CRITICAL_ERROR' 
	                and a.time > @from
	                and a.time < @to
                order by a.id desc";

        private const string InsertCriticalErrorQuery =
            @"insert into message (time, service, origin, target, message) values (@time, @service, @origin, @target, @message)";

        private const String ConnectorFallback =
            "SERVER=95.211.74.140;PORT=3306;DATABASE=taskserver;UID=taskserveruser;PASSWORD=5qwiw34f347w34!";

        private MySqlConnection _connection;

        private MySqlConnection Connection
        {
            get
            {
                if (this._connection == null)
                    this._connection = new MySqlConnection(ConnectorFallback);

                if (this._connection.State != ConnectionState.Open)
                    this._connection.Open();

                return this._connection;
            }
        }

        private MySqlCommand Messages
        {
            get { return new MySqlCommand(MessageQuery, this.Connection); }
        }

        private MySqlCommand BinaryRequest
        {
            get { return new MySqlCommand(BinaryRequestQuery, this.Connection); }
        }

        private MySqlCommand ElderMessages
        {
            get { return new MySqlCommand(ElderMessageQuery, this.Connection); }
        }

        private MySqlCommand CriticalErrors
        {
            get { return new MySqlCommand(CriticalErrorsQuery, this.Connection); }
        }

        private MySqlCommand InsertCriticalError
        {
            get { return new MySqlCommand(InsertCriticalErrorQuery, this.Connection); }
        }

        public List<MessageContainer> GetOrphanedMessages()
        {
            var reader = Messages.ExecuteReader();
            return this.MessageReaderToList(reader);
        }

        /// <summary>
        /// Gets binary request data to go along with a MessageContainer (for replay purposes).
        /// </summary>
        /// <param name="reference">The MessageContainer for which we are fetching info</param>
        /// <returns>The relevant rows from the BinaryRequest table</returns>
        public List<byte[]> GetOrphanedBinaryRequest(MessageContainer reference)
        {
            var msg = new List<byte[]>();

            var query = BinaryRequest;
            query.Parameters.AddWithValue("@messageid", reference.Id);
            query.Prepare();

            var reader = query.ExecuteReader();
            while (reader.Read())
            {
                msg.Add((byte[])reader["binarydata"]);
            }

            return msg;
        }

        /// <summary>
        /// Gets orphans (incomplete requests) that fall into the given age range
        /// </summary>
        /// <param name="maxAge">maximum orphan age</param>
        /// <param name="minAge">minimum orphan age</param>
        /// <returns>A collection (list) of MessageContainers</returns>
        public List<MessageContainer> GetElderOrphans(DateTime maxAge, DateTime minAge)
        {
            var query = ElderMessages;
            query.Parameters.AddWithValue("@maxAge", maxAge);
            query.Parameters.AddWithValue("@minAge", minAge);
            query.Prepare();

            var reader = query.ExecuteReader();

            return this.MessageReaderToList(reader);
        }

        /// <summary>
        /// Fetches all critical errors (Message LIKE 'CRITICAL_ERROR%') from the db
        /// </summary>
        /// <param name="from">from this date</param>
        /// <param name="to">to this date</param>
        /// <returns>messages concerning critical errors</returns>
        public List<MessageContainer> GetCriticalErrors(DateTime @from, DateTime to)
        {
            var query = CriticalErrors;
            query.Parameters.AddWithValue("@from", from);
            query.Parameters.AddWithValue("@to", to);
            query.Prepare();

            var reader = query.ExecuteReader();

            return this.MessageReaderToList(reader);
        }

        /// <summary>
        /// Inserts a critical error
        /// </summary>
        /// <param name="time">The time at which this occurred</param>
        /// <param name="message">Needs to be either CRITICAL_ERROR or CRITICAL_ERROR: MessageHere</param>
        /// <param name="service">The service that was involved. Is Janitor by default</param>
        /// <param name="origin">The calling party involved in generating the error</param>
        /// <param name="target">The receiving party involved in generating the error</param>
        public void AddCriticalError(DateTime time, String message = "CRITICAL_ERROR", String service = "janitor", String origin = "0x01",
                                     String target = "0x01")
        {
            var query = InsertCriticalError;
            query.Parameters.AddWithValue("@time", time);
            query.Parameters.AddWithValue("@message", message);
            query.Parameters.AddWithValue("@service", service);
            query.Parameters.AddWithValue("@origin", origin);
            query.Parameters.AddWithValue("@target", target);
            query.Prepare();

            query.ExecuteNonQuery();
        }

        /// <summary>
        /// A private helper function to shunt a reader into a list of MessageContainers, for ease of use
        /// </summary>
        /// <param name="reader">the reader to process</param>
        /// <returns>a collection (List) of MessageContainers</returns>
        private List<MessageContainer> MessageReaderToList(MySqlDataReader reader)
        {
            var messages = new List<MessageContainer>();

            while (reader.Read())
                messages.Add(new MessageContainer()
                {
                    Id = reader.GetInt32("id"),
                    Time = reader.GetMySqlDateTime("time").GetDateTime(),
                    Origin = reader.GetString("origin"),
                    Target = reader.GetString("target"),
                    Message = reader.GetString("message"),
                    Service = reader.GetString("service")
                });

            return messages;
        }
    }
}
