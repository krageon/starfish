using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using ZeroMQ;
using SocketFlags = ZeroMQ.SocketFlags;

namespace Majordomo_Protocol
{
    /// <summary>
    /// This is a collection of extension functions which makes the common conversions involved with using the majordomo-protocol and majordomo
    ///     projects a lot less annoying.
    /// </summary>
    public static class Helpers
    {
        public static char[] Lookup = {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'};

        /// <summary>
        ///     An extension method to byte[] that converts it to a hex string (should be similar to python's hexlify)
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string ToHexStr(this byte[] data)
        {
            int i = 0,
                p = 2,
                l = data.Length;

            var c = new char[l*2 + 2];
            c[0] = '0';
            c[1] = 'x';

            while (i < l)
            {
                byte d = data[i++];
                c[p++] = Lookup[d/0x10];
                c[p++] = Lookup[d%0x10];
            }
            return new string(c, 0, c.Length);
        }

        /// <summary>
        ///     Converts a hex str (with *UPPERCASE* characters) back into a byte[]
        /// </summary>
        /// <param name="data">A hex string like 0x202122FF (variable length, %2 = 0, 0x is mandatory)</param>
        /// <returns>a byte array</returns>
        public static byte[] HexStrToByteArray(this String data)
        {
            var arr = new byte[data.Length - 1 >> 1];

            int j = 1; // We have 0x in front of hex strings
            for (int i = 0; i < arr.Length; ++i)
            {
                arr[i] = (byte) ((GetHexVal(data[j << 1]) << 4) + (GetHexVal(data[(j << 1) + 1])));
                ++j;
            }

            return arr;
        }

        private static int GetHexVal(char hex)
        {
            int val = hex;
            return val - (val < 58 ? 48 : 55);
        }

        /// <summary>
        ///     An extension for ZMQ's socket interface that allows a whole queue of Strings to be sent in one go
        /// </summary>
        /// <param name="socket"></param>
        /// <param name="data"></param>
        /// <param name="flags"></param>
        /// <returns></returns>
        public static SendStatus SendAll(this ZmqSocket socket, List<String> data)
        {
            var dataMod = data.Select(s => s.ToBytes());

            ZmqMessage message = new ZmqMessage(dataMod);

            return socket.SendMessage(message);
        }

        public static SendStatus SendAll(this ZmqSocket socket, List<byte[]> data)
        {
            return socket.SendMessage(new ZmqMessage(data));
        }

        /// <summary>
        ///     An extension for the generic List interface that allows for the push word
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="me"></param>
        /// <param name="item"></param>
        public static void Push<T>(this List<T> me, T item)
        {
            me.Add(item);
        }

        /// <summary>
        ///     An extension for the generic List that allows a pop operation
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="me"></param>
        /// <returns></returns>
        public static T Pop<T>(this List<T> me)
        {
            int last = me.Count - 1;
            if (last < 0)
                throw new InvalidOperationException("Cannot pop: List is empty");

            T item = me[last];
            me.RemoveAt(last);
            return item;
        }

        /// <summary>
        /// Removes the top item from a list and returns it. Returns the default value if list is empty
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="me"></param>
        /// <returns></returns>
        public static T Dequeue<T>(this List<T> me)
        {
            if (me.Count == 0)
                return default(T);

            T item = me[0];
            me.RemoveAt(0);

            return item;
        }

        /// <summary>
        /// "dequeue"s from a HashSet. As these elements are in no particular order, the returned value can come from anywhere.
        /// </summary>
        /// <typeparam name="T">I/O type</typeparam>
        /// <param name="me">HashSet that we're performing work on</param>
        /// <returns></returns>
        public static T Dequeue<T>(this HashSet<T> me)
        {
            if (me.Count == 0)
                return default(T);

            T item = me.First();
            me.Remove(item);

            return item;
        }

        /// <summary>
        ///     Allows prepending to a queue. Note that it's probably better to switch into a different data format if you need to
        ///     do this.
        /// </summary>
        /// <param name="msg"></param>
        /// <param name="start"></param>
        public static void Prepend<T>(this List<T> msg, IEnumerable<T> start)
        {
            var startList = start.ToList();

            for(int i = startList.Count-1; i > -1; i--)
                msg.Insert(0, startList[i]);
        }

        /// <summary>
        ///     A simple dump function for a Queue of strings
        /// </summary>
        /// <param name="msg"></param>
        /// <returns></returns>
        public static String ToStringBlob(this IEnumerable<String> msg)
        {
            var op = new StringBuilder();
            foreach (var item in msg)
            {
                op.AppendLine(item);
            }

            return op.ToString();
        }

        public static String ToStringBlob(this IEnumerable<byte[]> msg)
        {
            var op = new StringBuilder();

            foreach (var item in msg)
            {
                op.AppendLine(item.ToUnicodeString());
            }

            return op.ToString();
        }

        /// <summary>
        ///     Translate a unicode-encoded string (default in C#) to bytes
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static byte[] ToBytes(this String message)
        {
            return Encoding.UTF8.GetBytes(message ?? "");
        }

        /// <summary>
        ///     Translate a unicode-encoded string that was converted to bytes back into a string
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static String ToUnicodeString(this byte[] message)
        {
            return Encoding.UTF8.GetString(message);
        }
    }
}