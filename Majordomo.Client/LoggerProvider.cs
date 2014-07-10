using System;
using System.Collections.Generic;
using System.Configuration;
using Majordomo_Protocol;

namespace Majordomo
{
    public class LoggerProvider : MajordomoClient
    {
        // This needs to handle messaging the logger service as a client. 
        // You need to think about the format in which the logger will receive messages and what the easiest way to play them back would be
        //  ideally we want to prepend some data about the origin (the loggee) and then dump the whole message unmodified over the line
        //  in such a way that reproducing it from the same location would be possible by loading the whole thing out again.
        //  We want to maybe write out certain message sections to the log table and not others based on the origin point (ie is this a worker, a client or a broker?)
        //  think about this hard! Some services will be replaying these later and they need to be dumped as fast as possible.

        // Maybe a log table with time, caller, frame_0 - frame_5 ?
        private static String LoggingService = ConfigurationManager.AppSettings["logger"];
        private static String ConnectPoint = ConfigurationManager.AppSettings["broker"];

        public LoggerProvider() : base(ConnectPoint, LoggingService)
        {
        }

        public LoggerProvider(String connectPoint) : base(connectPoint, LoggingService)
        {
        }

        /// <summary>
        ///     Use this instead of the base one. No really.
        /// </summary>
        /// <param name="origin">The originating place for the request</param>
        /// <param name="target">The place where this request needs to go</param>
        /// <param name="message">Some kind of message</param>
        /// <param name="service">The service being called</param>
        /// <param name="msg">The original payload</param>
        public void Send(
            String origin, 
            String target, 
            String message, 
            String service,
            List<byte[]> msg)
        {
            var msgClone = new List<byte[]>(msg.Count + 2)
                           {
                               origin.HexStrToByteArray(),
                               target.HexStrToByteArray(),
                               message.ToBytes(),
                               service.ToBytes()
                           };

            msgClone.AddRange(msg);
            
            base.SendReceiveString(msgClone);
        }
    }
}