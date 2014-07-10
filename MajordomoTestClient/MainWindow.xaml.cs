using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Majordomo;
using Majordomo_Protocol;

namespace MajordomoTestClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private MajordomoClient client;

        public MainWindow()
        {
            InitializeComponent();

            var broker = ConfigurationManager.AppSettings["broker"];
            client = new MajordomoClient(broker);

            var messages = GetMessages();

            foreach (var message in messages)
            {
                MessageList.Items.Add(message.SectionInformation);
            }
            MessageList.SelectionChanged += MessageListSelected;
        }

        private IEnumerable<ConfigurationSection> GetMessages()
        {
            var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
            return from ConfigurationSection item in config.SectionGroups["Messages"].Sections
                           select item;
        }

        private void MessageListSelected(object sender, SelectionChangedEventArgs args)
        {
            var sauce = (sender as ListView).SelectedItems[0] as SectionInformation;
            var kvpairs = this.GetMessageContents(sauce.SectionName);

            MessageContents.Items.Clear();

            foreach (var item in kvpairs)
            {
                MessageContents.Items.Add(item);
            }
        }

        private IEnumerable<KeyValuePair<String, String>> GetMessageContents(String messageName)
        {
            var collection = (ConfigurationManager.GetSection(messageName) as NameValueCollection);

            return from key in collection.Cast<String>()
                   select new KeyValuePair<string, string>(key, collection[key]);
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            var messageContents = MessageContents.Items
                .Cast<KeyValuePair<String, String>>()
                .ToList();

            client.Service = messageContents[0].Value;

            var message = messageContents
                .GetRange(1, messageContents.Count - 1)
                .Select(pair => pair.Value.ToBytes())
                .ToList();

            var response = client.SendReceiveString(message);

            MessageBox.Show(String.Format("Test message {0} sent, response {1}", ((SectionInformation)MessageList.SelectedItem).Name, response));
        }
    }
}
