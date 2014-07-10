using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Majordomo_Protocol;
using ZeroMQ;
using Exception = System.Exception;

namespace Majordomo
{
    /// <summary>
    /// A template version of a MajordomoWorker. Inherit this if you plan on doing work as a worker.
    /// </summary>
    public class MajordomoWorker
    {
        private ZmqContext _context;

        public ZmqContext Context
        {
            get { return this._context ?? (this._context = ZmqContext.Create()); }
        }

        private ZmqSocket _socket;

        public ZmqSocket Socket
        {
            get
            {
                if (_socket == null)
                {
                    this._socket = this.Context.CreateSocket(SocketType.DEALER);
                    this._socket.Linger = new TimeSpan(250 * TimeSpan.TicksPerMillisecond);
                }
                return this._socket;
            }
            set
            {
                this._socket = value;
            }
        }

        private Poller poller;

        public MajordomoClient _client;

        /// <summary>
        /// A client that provides the interface to the config service.
        /// </summary>
        public MajordomoClient ConfigClient
        {
            get
            {
                if (_client == null)
                    _client = new MajordomoClient(_address, "config"); // TODO: Config

                return _client;
            }
        }

        // Both broker and worker MUST send heartbeats at regular and agreed-upon intervals. 
        // A peer MUST consider the other peer "disconnected" if no heartbeat arrives within some multiple of that interval (usually 3-5).
        private const int HeartbeatLiveness = 4;
        private const int HeartbeatInterval = 1500;
        private const int HeartbeatExpiry = HeartbeatInterval*HeartbeatLiveness;

        private DateTime _heartbeatAt;
        private DateTime _heartbeatExpires;

        public String _address;
        public String _service;

        private Boolean _working;

        /// <summary>
        /// This property handles response generation. Modify this in a subclass to get different behaviour (ie custom stuff goes here).
        /// This thing will receive a List<byte[]> as input, so handle that.
        /// </summary>
        public delegate List<byte[]> ResponseGenerator(List<byte[]> msg);

        /// <summary>
        /// Needs to be the de facto way to generate responses. Is a step-based exception-handling framework that guarantees exception-free execution.
        /// This allows cost-incurring steps (ie sending orders out or sending emails) to happen if and only if preceding steps have gone ok. Additionally,
        /// it makes handling cleanup after errors (or after successful execution) very easy.
        /// </summary>
        public ResponseGenerator GenerateResponse { get; set; }

        public MajordomoWorker(String service) : this(ConfigurationManager.AppSettings["broker"], service)
        {

        }

        /// <summary>
        /// Construct the worker.
        /// </summary>
        /// <param name="address">The address at which a broker is reachable</param>
        /// <param name="service">The servicename to register under</param>
        public MajordomoWorker(String address, String service)
        {
            if (this.GenerateResponse == null)
                this.GenerateResponse = msg =>
                                        {
                                            byte[] caller = msg[3];

                                            var content = new List<byte[]>()
                                                          {
                                                              "Hello, world!".ToBytes(),
                                                              "Original request content:".ToBytes()
                                                          };
                                            
                                            content.AddRange(msg);

                                            var reply = MDP.Worker.Reply(caller, content);

                                            return reply;
                                        };

            this._address = address;
            this._service = service;
            this._working = false;
        }

        ~MajordomoWorker()
        {
            this.Socket.Close();
            this.Socket.Dispose();
            this.Context.Terminate();
            this.Context.Dispose();
        }

        public void Work()
        {
            this.InitializeSocket();
            this._working = true;

            // TODO: Poller needs init on socket reset

            while (this._working)
            {
                try
                {
                    poller.Poll(new TimeSpan(100 * TimeSpan.TicksPerMillisecond));
                }
                catch (ZmqSocketException zse)
                {
                    // TODO: Sane logging
                }

                if (DateTime.Now >= this._heartbeatAt)
                    this.Heartbeat();

                if (DateTime.Now >= this._heartbeatExpires)
                {
                    this.InitializeSocket();
                }
            }

            WorkExtension();
        }

        /// <summary>
        /// Override this in a subclass, or not. Allows you to extend the Work() cycle of a regular MajordomoWorker
        ///     this is useful when you need to extend this functionality somehow.
        /// </summary>
        public virtual void WorkExtension()
        {
            
        }

        private void InitializeSocket()
        {
            if (this._socket != null)
            {
                this._socket.Close();
                this._socket = null;
            }

            if (poller == null)
                poller = new Poller();

            this.Socket.Connect(this._address);
            this.Socket.ReceiveReady += this.MessageReceived;

            poller.ClearSockets();
            poller.AddSocket(this.Socket);

            this.Socket.SendAll(MDP.Worker.Ready(this._service));
            this._heartbeatAt = DateTime.Now.AddMilliseconds(HeartbeatInterval);
            this._heartbeatExpires = DateTime.Now.AddMilliseconds(HeartbeatExpiry);
        }

        private void Heartbeat()
        {
            var status = this.Socket.SendAll(MDP.Worker.Heartbeat());

            if (status == SendStatus.Sent)
                this._heartbeatAt = DateTime.Now.AddMilliseconds(HeartbeatInterval);
            else
                this.InitializeSocket();
        }

        private void MessageReceived(object sender, SocketEventArgs args)
        {
            var socket = args.Socket;
            List<byte[]> msg = socket.ReceiveMessage().Select(f => (byte[])f).ToList();

            if (msg.Count < 3)
            {
                this.Error();
                return;
            }

            byte[] target = msg[1];
            byte[] command = msg[2];

            if (!target.ToUnicodeString().Equals(MDP.W_WORKER))
            {
                this.Error();
                return;
            }

            switch (command[0])
            {
                    //case MDP.W_HEARTBEAT:
                    //    Heartbeat();
                    //    break;
                case MDP.W_DISCONNECT:
                    this.Disconnect();
                    this._working = false;
                    break;
                case MDP.W_REQUEST:
                    this.Reply(msg);
                    this._heartbeatExpires = DateTime.Now.AddMilliseconds(HeartbeatExpiry);
                    break;
                default:
                    // Any received command except DISCONNECT acts as a heartbeat. 
                    this._heartbeatExpires = DateTime.Now.AddMilliseconds(HeartbeatExpiry);
                    break;
            }
        }

        protected virtual void Reply(List<byte[]> msg)
        {
            if (msg.Count < 1)
            {
                this.Error();
                return;
            }

            var reply = this.GenerateResponse(msg);

            this.Socket.SendAll(reply);

            //  Peers SHOULD NOT send HEARTBEAT commands while also sending other commands.
            this._heartbeatAt = DateTime.Now.AddMilliseconds(HeartbeatInterval);
        }

        private void Disconnect()
        {
            var msg = MDP.Worker.Disconnect();

            this.Socket.SendAll(msg);
        }

        private void Error(String message = "Error processing message")
        {
            Console.WriteLine("[Worker] " + message);
        }
    }

    /// <summary>
    /// Contains a list of functions to execute (steps for execution of a task). The Cleanup routine is called whenever an error occurs (no matter where).
    /// Execution continues only if no exceptions occur. If an exception does occur anywhere, the Error routine handles it.
    /// Run the work using Work
    /// </summary>
    /// <typeparam name="T">The input (and output) type for all the intermediate functions. Something like string is reasonable.</typeparam>
    public class SequencedWork<T>
    {
        private CancellationTokenSource _canceller;

        /// <summary>
        /// This exists solely so the task that is run can be prematurely cancelled in a "clean" way.
        /// Note that this isn't guaranteed to call cleanup.
        /// </summary>
        public CancellationTokenSource Canceller
        {
            get { return _canceller ?? (_canceller = new CancellationTokenSource()); }
        }

        public Task<T> StartTask;
        public Task WaitTask;

        public List<Func<Task<T>, T>> intermediates;

        public Func<T> Begin;
        public Action<Task<T>> End;
        public Action<Task> Cleanup;

        public Action<Task<T>> Error;

        public void Initialise()
        {
            var currentTask = new Task<T>(Begin, Canceller.Token);
            StartTask = currentTask;

            foreach (var task in intermediates)
            {
                currentTask
                    .ContinueWith(this.Error, TaskContinuationOptions.OnlyOnFaulted)
                    .ContinueWith(this.Cleanup, TaskContinuationOptions.OnlyOnFaulted);

                currentTask = currentTask.ContinueWith(task, TaskContinuationOptions.OnlyOnRanToCompletion);
            }

            currentTask.ContinueWith(this.Error, TaskContinuationOptions.OnlyOnFaulted)
                       .ContinueWith(this.Cleanup, TaskContinuationOptions.OnlyOnFaulted);

            WaitTask = currentTask
                .ContinueWith(End, TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(this.Cleanup);
        }

        public void Run()
        {
            if (WaitTask == null || WaitTask.IsCompleted || WaitTask.IsFaulted || WaitTask.IsCanceled)
                this.Initialise();

            StartTask.Start();

            try
            {
                // TODO: Timeout?
                WaitTask.Wait();
                WaitTask = null;
                StartTask = null;
            }
            catch (Exception)
            {
            }
        }
    }
}