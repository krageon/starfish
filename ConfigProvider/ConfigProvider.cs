using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Majordomo;
using Majordomo_Protocol;

namespace ConfigProvider
{
    /// <summary>
    /// This Worker provides a centralised way to distribute key:value pair-type configuration values, 
    ///     allowing for easy maintenance/alteration of config values.
    /// 
    /// Expects a standard worker request, with the following additions:
    /// Frame 5+: Every frame contains a single key to send back.
    /// 
    /// The response will consist of the values, in the same order as they were requested.
    /// </summary>
    public class ConfigProvider : MajordomoWorker
    {
        private const string _config_location = "config.ini";
        private Dictionary<String, String> config_values;

        public ConfigProvider() : base(ConfigurationManager.AppSettings["service_name"])
        {
            this.GenerateResponse = this.HandleMessage;
        }

        private List<byte[]> HandleMessage(List<byte[]> msg)
        {
            var content = new List<byte[]>();

            for (int i = 5; i < msg.Count; i++)
            {
                var key = msg[i].ToUnicodeString();
                var value = "error";

                try
                {
                    value = ConfigurationManager.AppSettings[key];
                }
                catch (Exception e)
                {
                    
                }

                content.Add(value.ToBytes());
            }

            return MDP.Worker.Reply(msg[3], content);
        }
    }
}
