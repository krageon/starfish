using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using RazorTemplates.Core;

namespace Janitor
{
    public static class JanitorAlert
    {
        public static string host = "smtp.mandrillapp.com";
        public static int port = 587;

        public static string user = "PicturePresent";
        public static string pass = "QTMy2oyjlLq6adCt4mUehw";

        public static string from = "noreply@picturepresent.nl";
        public static string to = "tim@picturepresent.nl";

        public static string default_subject = "Taskserver: Critical error report";

        public static string default_body =
            "I'd have sent you a more useful email but I had some trouble compiling the desired template ({0}) to email content";

        public static SmtpClient EmailClient 
        {
            get { return _emailClient ?? (_emailClient = new SmtpClient(host, port)); }
        }

        private static SmtpClient _emailClient;

        /// <summary>
        /// Send an email to the application admin
        /// </summary>
        /// <param name="templateName">Email template to use. Located under templates.</param>
        /// <param name="model">A dynamic that contains the data the template uses (it is your responsibility to give this thing the right values, the templates will fail compilation at runtime if this is incomplete)</param>
        /// <param name="subject">The text that goes into the subject line. Will default to something sensible if nothing is provided.</param>
        public static void EmailAdmin(String templateName, dynamic model, String subject = null)
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

            var mailmessage = new MailMessage(from, to, subject ?? default_subject, body ?? String.Format(default_body, templateFull));

            EmailClient.Send(mailmessage);
        }
    }
}
