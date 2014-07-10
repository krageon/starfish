using System;
using System.Collections.Generic;
using System.Linq;

namespace Majordomo_Protocol
{
    public static class MDP
    {
        public const byte W_READY = 0x01;
        public const byte W_REQUEST = 0x02;
        public const byte W_REPLY = 0x03;
        public const byte W_HEARTBEAT = 0x04;
        public const byte W_DISCONNECT = 0x05;

        public const String C_CLIENT = "MDPC01";
        public static readonly byte[] C_CLIENT_B;
        public const String W_WORKER = "MDPW01";
        public static readonly byte[] W_WORKER_B;

        public static readonly byte[] M_EMPTY;

        static MDP()
        {
            C_CLIENT_B = C_CLIENT.ToBytes();
            W_WORKER_B = W_WORKER.ToBytes();
            M_EMPTY = new byte[0];
        }

        /// <summary>
        ///     A helper class to construct Client messages as described in http://rfc.zeromq.org/spec:7
        ///     Return types are always a Queue of byte[]. Strings that are passed are converted using
        ///     Encoding.UTF8.GetBytes(String s)
        /// </summary>
        public static class Client
        {
            public static List<byte[]> Request(byte[] service, List<byte[]> requestBody)
            {
                var result = new List<byte[]>(requestBody.Count + 2)
                             {
                                 C_CLIENT_B,
                                 service
                             };

                result.AddRange(requestBody);

                return result;
            }

            public static List<byte[]> Request(byte[] service, byte[] requestBody)
            {
                return Request(service, new List<byte[]>() {requestBody});
            }

            public static List<byte[]> Request(String service, List<byte[]> requestBody)
            {
                return Request(service.ToBytes(), requestBody);
            }

            public static List<byte[]> Request(byte[] service, String requestBody)
            {
                return Request(service, requestBody.ToBytes());
            }

            public static List<byte[]> Request(String service, String requestBody)
            {
                return Request(service.ToBytes(), requestBody.ToBytes());
            }

            public static List<byte[]> Reply(byte[] service, List<byte[]> replyBody)
            {
                return Request(service, replyBody);
            }

            public static List<byte[]> Reply(byte[] service, byte[] replyBody)
            {
                var wrap = new Queue<byte[]>();
                wrap.Enqueue(replyBody);

                return Reply(service, new List<byte[]>() { replyBody });
            }

            public static List<byte[]> Reply(String service, List<byte[]> replyBody)
            {
                return Request(service.ToBytes(), replyBody);
            }

            public static List<byte[]> Reply(byte[] service, String replyBody)
            {
                return Request(service, replyBody.ToBytes());
            }

            public static List<byte[]> Reply(String service, String replyBody)
            {
                return Request(service.ToBytes(), replyBody.ToBytes());
            }
        }

        /// <summary>
        ///     A helper class to construct Worker messages as described in http://rfc.zeromq.org/spec:7
        ///     Return types are always Queue of byte[]. Strings that are passed are converted using Encoding.UTF8.GetBytes(String
        ///     s)
        /// </summary>
        public static class Worker
        {
            public static List<byte[]> Ready(String service)
            {
                var msg = new List<byte[]>()
                          {
                              M_EMPTY,
                              W_WORKER_B,
                              new[] {W_READY},
                              service.ToBytes()
                          };
                
                return msg;
            }

            public static List<byte[]> Request(IEnumerable<byte[]> requestBody)
            {
                var msg = new List<byte[]>()
                          {
                              M_EMPTY,
                              W_WORKER_B,
                              new[] {W_REQUEST},
                          };

                msg.AddRange(requestBody);

                return msg;
            }

            public static List<byte[]> Request(byte[] clientAddress, IEnumerable<byte[]> requestBody)
            {
                var msg = new List<byte[]>()
                          {
                              M_EMPTY,
                              W_WORKER_B,
                              new[] {W_REQUEST},
                              clientAddress,
                              M_EMPTY
                          };

                msg.AddRange(requestBody);

                return msg;
            }

            public static List<byte[]> Request(byte[] clientAddress, byte[] requestBody)
            {
                var rest = new List<byte[]>()
                           {
                               requestBody
                           };

                return Request(clientAddress, rest);
            }

            public static List<byte[]> Request(byte[] clientAddress, String requestBody)
            {
                return Request(clientAddress, requestBody.ToBytes());
            }

            public static List<byte[]> Reply(byte[] clientAddress, IEnumerable<byte[]> requestBody)
            {
                var msg = new List<byte[]>()
                          {
                              M_EMPTY,
                              W_WORKER_B,
                              new[] {W_REPLY},
                              clientAddress,
                              M_EMPTY
                          };

                msg.AddRange(requestBody);

                return msg;
            }

            public static List<byte[]> Reply(byte[] clientAddress, byte[] requestBody)
            {
                var rest = new List<byte[]>()
                           {
                               requestBody
                           };

                return Reply(clientAddress, rest);
            }

            public static List<byte[]> Reply(byte[] clientAddress, String requestBody)
            {
                return Reply(clientAddress, requestBody.ToBytes());
            }

            public static List<byte[]> Heartbeat()
            {
                var msg = new List<byte[]>()
                          {
                              M_EMPTY,
                              W_WORKER_B,
                              new [] {W_HEARTBEAT}
                          };

                return msg;
            }

            public static List<byte[]> Disconnect()
            {
                var msg = new List<byte[]>()
                          {
                              M_EMPTY,
                              W_WORKER_B,
                              new[] {W_DISCONNECT}
                          };

                return msg;
            }
        }
    }
}