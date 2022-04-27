using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Xml;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace GSSpeaker
{
    public sealed partial class AddConnection : ContentDialog
    {
        public AddConnection()
        {
            this.InitializeComponent();
        }
		public AddConnection(ref System.Xml.XmlDocument doc)
		{
			InitializeComponent();
			//
			//TODO: Add the constructor code here
			//
			xmlConnection = doc;
			hostAdded = false;
			IsPrimaryButtonEnabled = false;
		}

		private System.Xml.XmlDocument xmlConnection;
		public bool hostAdded;

		private void ContentDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
			bool dup = false;
			String cName = connectionName.Text.ToString();
			String cHost = hostIP.Text.ToString();
			XmlNodeList connections = xmlConnection.SelectNodes("/SpeakerGS/Connections");
			XmlNode  root = connections.Item(0);

			for (int i = 0; i < root.ChildNodes.Count; i++)
			{
				XmlNode child = root.ChildNodes.Item(i);
				XmlNode name = child.Attributes.GetNamedItem("name");
				if (name.Value == cName)
				{
					dup = true;
					break;
				}
			}
			if (dup == true)
			{
				uniqueLbl.Visibility = Visibility.Visible;
			}
			else
			{
				XmlElement connection = xmlConnection.CreateElement("connection");
				XmlAttribute attribute = xmlConnection.CreateAttribute("name");
				attribute.Value = cName;
				connection.Attributes.Append(attribute);
				attribute = xmlConnection.CreateAttribute("host");
				attribute.Value = cHost;
				connection.Attributes.Append(attribute);
				root.AppendChild(connection);
				hostAdded = true;
				this.Hide();
			}
		}

        private void ContentDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
        {
			hostAdded = false;
			this.Hide();
        }

        private void hostIP_TextChanging(TextBox sender, TextBoxTextChangingEventArgs args)
        {
			bool state = false;

			uniqueLbl.Visibility = Visibility.Collapsed;
			String hostStr = hostIP.Text.ToString();
			if (hostStr.Length > 0)
			{
				System.Net.IPAddress  i;
				try
				{
					int cnt = 0;
					i = IPAddress.Parse(hostStr);
					foreach(char ch in hostStr)
					{
						if (ch == '.')
						{
							cnt++;
							continue;
						}
					}
					if (cnt == 3)
					{
						state = true;
					}
				}
				catch (Exception)
			{
					bool alpha = false;
					int cnt = 0;
					foreach(char ch in hostStr)
					{
						if (ch == '.')
						{
							cnt++;
							continue;
						}
						if (char.IsLetter(ch) || (ch == '-'))
						{
							alpha = true;
						}
					}
					if ((cnt > 0) && (alpha == true))
					{
						state = (Uri.CheckHostName(hostStr) != UriHostNameType.Unknown);

					}
					//try hostlookup?
				}
				}
				IsPrimaryButtonEnabled = state;

			}

        private void ContentDialog_Closing(ContentDialog sender, ContentDialogClosingEventArgs args)
        {
			if (uniqueLbl.Visibility == Visibility.Visible)
            {
				args.Cancel = true;
            }
        }

        private void connectionName_TextChanged(object sender, TextChangedEventArgs e)
        {
			uniqueLbl.Visibility = Visibility.Collapsed;
		}
	}
}
