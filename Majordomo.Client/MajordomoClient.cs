using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Majordomo_Protocol;
using ZeroMQ;

namespace Majordomo
{
    public class MajordomoClient
    {
        private ZmqContext _context;

        protected ZmqContext Context
        {
            get { return this._context ?? (this._context = ZmqContext.Create()); }
        }

        private ZmqSocket _socket;

        protected ZmqSocket Socket
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
        }

        private readonly String _address;
        public String Service { get; set; }

        public MajordomoClient(String address)
        {
            if (String.IsNullOrEmpty(address))
                throw new ArgumentException("address is null");

            this._address = address;
            this.Socket.Connect(this._address);
        }

        public MajordomoClient(String address, String service)
            : this(address)
        {
            this.Service = service;
        }

        /// <summary>
        ///     Will send a passed message to whatever endpoint is defined, with whatever service is defined.
        /// </summary>
        /// <param name="msg">a queue of byte arrays, containing the request body as defined in the Majordomo Protocol</param>
        public virtual String SendReceiveString(List<byte[]> msg)
        {
            var outp = MDP.Client.Request(this.Service, msg);
            outp.Prepend(new[] {new byte[0]});

            String message = String.Empty;

            Socket.ReceiveReady += (sender, args) =>
            {
                var m = args.Socket.ReceiveMessage();
                var origin = m.Unwrap();

                m.Unwrap();
                m.Unwrap();
                m.Unwrap();

                message = m.Unwrap().ToString();
            };

            var poller = new Poller(new List<ZmqSocket>() {Socket});

            this.Socket.SendAll(outp);

            poller.Poll(new TimeSpan(0, 0, 0, 5));

            return message;
        }

        /// <summary>
        /// Creates a request using the input and MDP.Client.request (Majordomo-Protocol).
        /// Essentially: 
        /// prepends 
        ///    C_CLIENT_B,
        ///    service
        /// 
        /// To the message as input and sends it to the specified broker (at _address)
        /// </summary>
        /// <param name="sendThis"></param>
        /// <returns></returns>
        public List<byte[]> SendReceiveRaw(List<byte[]> sendThis)
        {
            var outp = MDP.Client.Request(this.Service, sendThis);
            outp.Prepend(new[] {new byte[0]});

            List<byte[]> message = new List<byte[]>();

            Socket.ReceiveReady += (sender, args) =>
                                   {
                                       message = args.Socket.ReceiveMessage().Select(frame => frame.Buffer).ToList();
                                   };

            var poller = new Poller(new List<ZmqSocket>() { Socket });

            this.Socket.SendAll(outp);

            poller.Poll(new TimeSpan(0, 0, 0, 10));

            return message;
        }

        // Some overrides to do things, or maybe a stack of statics. Overrides are probably prettier. And some utility functions.
    }
}