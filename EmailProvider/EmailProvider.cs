using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using RazorTemplates.Core;

namespace EmailProvider
{
    public class EmailProvider
    {
        public static string host = ConfigurationManager.AppSettings["email_host"];
        public static int port = Int32.Parse(ConfigurationManager.AppSettings["email_port"]);

        public static string user = ConfigurationManager.AppSettings["email_user"];
        public static string pass = ConfigurationManager.AppSettings["email_pass"];

        public static string from = ConfigurationManager.AppSettings["email_from"];
        public static string default_to = ConfigurationManager.AppSettings["email_default_to"];

        public static string default_subject = ConfigurationManager.AppSettings["email_default_subject"];

        public static string default_body = ConfigurationManager.AppSettings["email_default_body"];

        public static SmtpClient EmailClient
        {
            get { return _emailClient ?? (_emailClient = new SmtpClient(host, port)); }
        }

        private static SmtpClient _emailClient;

        /// <summary>
        /// Send an email to a person
        /// </summary>
        /// <param name="templateName">Email template to use. Located under templates.</param>
        /// <param name="model">A dynamic that contains the data the template uses (it is your responsibility to give this thing the right values, the templates will fail compilation at runtime if this is incomplete)</param>
        /// <param name="subject">The text that goes into the subject line. Will default to something sensible if nothing is provided.</param>
        public static void SendEmail(String templateName, dynamic model, String subject = null, String to = null)
        {
            string body = null;
            string templateFull = Path.Combine("Templates", templateName + ".txt");

            try
            {
                String templateText = File.ReadAllText(templateFull);
                var template = Template.Compile(templateText);
                body = template.Render(model);
            }
            catch (Exception e)
            {
                throw e;
            }

            var mailmessage = new MailMessage(from, to ?? default_to, subject ?? default_subject, body ?? String.Format(default_body, templateFull));

            EmailClient.Send(mailmessage);
        }
    }
}
