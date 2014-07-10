using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Majordomo_Protocol;
using ZeroMQ;

namespace Majordomo
{
    /// <summary>
    ///     This is a port of the MajordomoBroker broker written in Java, found at http://zguide.zeromq.org/java:mdbroker
    /// </summary>
    public class MajordomoBroker
    {
        private const String InternalServicePrefix = "mmi.";

        private const int HeartbeatLiveness = 4; // 3-5 is reasonable
        private const int HeartbeatInterval = 1500;
        private const int HeartbeatExpiry = HeartbeatInterval*HeartbeatLiveness;

        private readonly LoggerProvider _log;
        private readonly Dictionary<String, Service> _services;
        private ZmqSocket _socket;
        private String addr = "";

        private readonly Boolean _verbose;
        private readonly HashSet<Worker> _waiting;
        private readonly Dictionary<String, Worker> _workers;
        private DateTime _heartbeatAt;

        public MajordomoBroker(bool verbose = false)
        {
            this._verbose = verbose;
            this._services = new Dictionary<string, Service>();
            this._workers = new Dictionary<String, Worker>();
            this._waiting = new HashSet<Worker>();
            this._heartbeatAt = DateTime.Now.AddMilliseconds(HeartbeatInterval);
            this._log = new LoggerProvider();
        }

        public void Bind(String endpoint)
        {
            this.addr = endpoint;
        }

        // This is half synchronous, half asynchronous - we can use timers for the heartbeating and probably some more nice stuff
        public void Mediate()
        {
            ZmqContext.DefaultEncoding = Encoding.UTF8;
            using (var context = ZmqContext.Create())
            {
                var socket = context.CreateSocket(SocketType.ROUTER);
                socket.Linger = new TimeSpan(HeartbeatInterval * TimeSpan.TicksPerMillisecond);
                this._socket = socket;

                this._socket.Bind(this.addr);

                this._socket.ReceiveReady += this.HandleMessages;
                var poller = new Poller(new List<ZmqSocket>() { this._socket });

                while (Thread.CurrentThread.IsAlive)
                {       
                    poller.Poll(new TimeSpan(HeartbeatInterval/100*TimeSpan.TicksPerMillisecond));
                    this.PurgeWorkers();
                    this.SendHeartbeats();
                }
                socket.Close();
            }
        }

        private void HandleMessages(object origin, SocketEventArgs args)
        {
            var socket1 = args.Socket;

            var msg = socket1.ReceiveMessage().Select(f => (byte[])f).ToList();
            if (msg == null || msg.Count < 3)
                return; // Interrupted

            if (this._verbose)
            {
                Console.WriteLine("Broker: received message:\n{0}", msg.ToStringBlob());
            }

            byte[] sender = msg[0];
            string header = msg[2].ToUnicodeString();

            if (MDP.C_CLIENT.Equals(header))
            {
                this.ProcessClient(sender, msg);
            }
            else if (MDP.W_WORKER.Equals(header))
                this.ProcessWorker(sender, msg);
            else
            {
                var b = new StringBuilder();
                socket1.Send(new byte[] {0x0}, 1, SocketFlags.None);
                foreach (var item in msg)
                    b.AppendLine(item.ToUnicodeString());
                Console.WriteLine("E: invalid message with header {1}:\n{0}", b, header);
            }
        }

        private void ProcessClient(byte[] sender, List<byte[]> msg)
        {
            if (msg.Count < 2) // Service name + body
            {
                if (this._verbose)
                    Console.WriteLine("Received message format wrong");
                return; // Throw an exception?
            }

            string service = msg[3].ToUnicodeString();

            if (service.StartsWith(InternalServicePrefix))
                this.ServiceInternal(service, msg);
            else
                this.Dispatch(this.RequireService(service), msg);
        }

        private void ProcessWorker(byte[] sender, List<byte[]> msg)
        {
            if (msg.Count < 1) // Needs to have a command at least
                return;

            byte[] command = msg[3];

            Worker worker = this.RequireWorker(sender);

            switch (command[0])
            {
                case MDP.W_READY:
                    this.WorkerReady(worker, msg);
                    break;
                case MDP.W_DISCONNECT:
                    this.DeleteWorker(worker, false);
                    break;
                case MDP.W_HEARTBEAT:
                    this.WorkerWaiting(worker);
                    break;
                case MDP.W_REPLY:
                    this.WorkerReply(worker, msg);
                    break;
            }
        }

        private void WorkerReady(Worker worker, List<byte[]> msg)
        {
            if (!msg.Any())
                return; // an exception?

            string service = Encoding.UTF8.GetString(msg[4]);

            if (service.StartsWith(InternalServicePrefix))
                this.DeleteWorker(worker, true);
            else
            {
                worker.Service = this.RequireService(service);
                this.WorkerWaiting(worker);
            }
        }

        private void WorkerReply(Worker worker, List<byte[]> msg)
        {
            byte[] clientAddress = msg[4];

            var replyMsg = msg.GetRange(4, msg.Count - 4);

            var serviceName = worker.Service != null ? worker.Service.Name : "";

            var response = MDP.Client.Reply(serviceName, replyMsg);
            response.Insert(0, MDP.M_EMPTY);

            if (this._log.Service != null && 
                !worker.Service.Name.Equals(this._log.Service))
            {
                this._log.Send(worker.Address.ToHexStr(), clientAddress.ToHexStr(), "END_WORK", worker.Service.Name, response);
            }

            response.Insert(0, clientAddress);

            this._socket.SendAll(response);

            this.WorkerWaiting(worker);
        }

        private void DeleteWorker(Worker worker, bool disconnect)
        {
            if (disconnect)
                this.SendToWorker(worker, MDP.Worker.Disconnect());

            if (worker.Service != null)
                worker.Service.Waiting.Remove(worker);
            this._workers.Remove(worker.Identity);
        }

        private void SendToWorker(Worker worker, List<byte[]> msg, byte[] origin = null)
        {
            if (origin == null)
                origin = new byte[0];

            if (msg == null)
                msg = new List<byte[]>();

            if (!worker.Service.Name.Equals(this._log.Service) &&
                msg.Count > 4)
            {
                this._log.Send(origin.ToHexStr(), worker.Address.ToHexStr(), "DO_WORK", worker.Service.Name, msg);
            }

            msg.Prepend(new List<byte[]> {worker.Address});

            if (this._verbose)
                Console.WriteLine("Broker: Sending message {0} to worker {1}", msg.ToStringBlob(), worker.Address.ToHexStr());

            this._socket.SendAll(msg);
        }

        private Worker RequireWorker(byte[] address)
        {
            if (address == null ||
                address.Length == 0)
                throw new ArgumentException("Address null or invalid");

            if (!this._workers.ContainsKey(address.ToHexStr()))
            {
                var worker = new Worker(address);
                this._workers.Add(worker.Identity, worker);
                if (this._verbose)
                    Console.WriteLine("Broker: Registering new worker {0}", address.ToHexStr());
            }

            return this._workers[address.ToHexStr()];
        }

        private Service RequireService(string service)
        {
            if (String.IsNullOrEmpty(service))
            {
                if (_verbose)
                    Console.WriteLine("empty or null string passed to RequireService");
            }

            if (!this._services.ContainsKey(service))
            {
                this._services.Add(service, new Service(service));
            }
            return this._services[service];
        }

        private void WorkerWaiting(Worker worker)
        {
            if (worker.Service == null)
                return;

            this._waiting.Add(worker);
            worker.Service.Waiting.Add(worker);
            worker.Expiry = DateTime.Now.AddMilliseconds(HeartbeatExpiry);
            this.Dispatch(worker.Service, null);
        }

        private void Dispatch(Service service, List<byte[]> msg)
        {
            if (service == null)
                return;

            if (msg != null)
            {
                service.Requests.Add(msg);
            }
            this.PurgeWorkers();

            while (service.Waiting.Any() &&
                   service.Requests.Any())
            {
                msg = service.Requests.Dequeue();
                Worker worker = service.Waiting.Dequeue();
                this._waiting.Remove(worker);
                var origin = msg[0];
                var dispatch = MDP.Worker.Request(origin, msg.GetRange(4, msg.Count - 4));

                this.SendToWorker(worker, dispatch, origin);
            }
        }

        private void PurgeWorkers()
        {
            var expired = this._waiting.Where(w => w.Expiry < DateTime.Now).ToList();

            foreach (Worker worker in expired)
            {
                Console.WriteLine("Broker: Removing expired worker {0}", worker.Address.ToHexStr());
                this.DeleteWorker(worker, false);
                this._waiting.Remove(worker);
            }
        }

        private void SendHeartbeats()
        {
            if (DateTime.Now >= this._heartbeatAt)
            {
                foreach (Worker worker in this._waiting)
                    this.SendToWorker(worker, MDP.Worker.Heartbeat());
                this._heartbeatAt = DateTime.Now.AddMilliseconds(HeartbeatInterval);
            }
        }

        private void ServiceInternal(string service, IEnumerable<byte[]> msg)
        {
            var msgList = msg.ToList();

            String returnCode = "501";
            if ("mmi.service".Equals(service))
            {
                string name = Encoding.UTF8.GetString(msgList.Last());
                returnCode = this._services.ContainsKey(name) ? "200" : "404";
            }

            msgList.Add(returnCode.ToBytes());

            this._socket.SendAll(msgList);
        }

        /// <summary>
        ///     A single service
        /// </summary>
        private class Service
        {
            public readonly List<List<byte[]>> Requests;
            public readonly HashSet<Worker> Waiting;
            public readonly String Name;

            public Service(String name)
            {
                this.Name = name;
                this.Requests = new List<List<byte[]>>();
                this.Waiting = new HashSet<Worker>();
            }
        }

        private class Worker
        {
            public readonly byte[] Address;
            public readonly String Identity;
            public Service Service;
            public DateTime Expiry;

            public Worker(byte[] address)
            {
                this.Address = address;
                this.Identity = address.ToHexStr();
                this.Expiry = DateTime.Now.AddMilliseconds(HeartbeatExpiry);
            }

            public override bool Equals(object obj)
            {
                var that = obj as Worker;

                return that != null 
                    && that.Identity.Equals(this.Identity);
            }

            public override int GetHashCode()
            {
                return Identity.GetHashCode();
            }
        }
    }
}