using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.Resources.Core;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Globalization;
using Windows.Media.SpeechRecognition;
using Windows.UI.Core;
using Windows.UI.ViewManagement;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace GSSpeaker
{
	/// <summary>
	/// An empty page that can be used on its own or navigated to within a Frame.
	/// </summary>
	/// 

	public struct tMessageHeader {
		public UInt16 messageType;
		public UInt16 messageArg;
	};

    public sealed partial class MainPage : Page
    {

        public MainPage()
        {
            this.InitializeComponent();
            ApplicationView.PreferredLaunchViewSize = new Size(773, 336);
            ApplicationView.PreferredLaunchWindowingMode = ApplicationViewWindowingMode.PreferredLaunchViewSize;
			sendBtn.IsEnabled = false;
            pasteBtn.IsEnabled = false;
			microphoneBtn.IsEnabled = false;
			speakerConfig = new XmlDocument();
            statusText.Document.SetText(Windows.UI.Text.TextSetOptions.None, "SpeakerGS V1.0");
            statusText.Document.GetText(Windows.UI.Text.TextGetOptions.None, out string prevText);
            statusText.Document.SetText(Windows.UI.Text.TextSetOptions.None, prevText + "===========");
			dictatedTextBuilder = new StringBuilder();
			try
			{
				bool enable = false;

				Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
				string connectionsPath = localFolder.Path.ToString() + "\\connections.cfg";


				speakerConfig.Load(connectionsPath.ToString());
				XmlNodeList prefsList = speakerConfig.GetElementsByTagName("Preferences");
				if (prefsList != null)
				{
					XmlNode prefs = prefsList.Item(0);
					if (prefs != null)
					{
						XmlNode last = prefs.Attributes.GetNamedItem("lastConnection");
						if (last != null && last.Value.ToString().Length != 0)
						{
							rebuildHostListCtl(last.Value.ToString());
							enable = true;
						}
					}
					else
					{
						rebuildHostListCtl("");
					}
				}
				else
				{
					rebuildHostListCtl("");
				}
				connectBtn.IsEnabled = enable;
				deleteBtn.IsEnabled = enable;
			}
			catch (Exception)
			{
				XmlNode rootNode = speakerConfig.CreateElement("SpeakerGS");
				speakerConfig.AppendChild(rootNode);
				XmlNode node = speakerConfig.CreateElement("Preferences");
				rootNode.AppendChild(node);
				node = speakerConfig.CreateElement("Connections");
				rootNode.AppendChild(node);
				connectBtn.IsEnabled = false;
				deleteBtn.IsEnabled = false;
			}
		}

		private XmlDocument speakerConfig;
		private string hostname;
		private TcpClient client;
		private BackgroundWorker worker;
		public SpeechRecognizer speechRecognizer;
		public IAsyncOperation<SpeechRecognitionResult> recognitionOperation;
		public ResourceContext speechContext;
		private ResourceMap speechResourceMap;
		private bool permissionGained;
		public const int LISTEN_STATE_MSG = 1;
		public const int LISTEN_TEXT_MSG = 2;
		public const int LISTEN_SEND_MORE = 3;
		public static uint HResultPrivacyStatementDeclined = 0x80045509;
		private StringBuilder dictatedTextBuilder;
		private bool isListening;

		protected async override void OnNavigatedTo(NavigationEventArgs e)
		{
			permissionGained = await AudioCapturePermissions.RequestMicrophonePermission();

			if (permissionGained)
			{
				Language speechLanguage = SpeechRecognizer.SystemSpeechLanguage;
				speechContext = ResourceContext.GetForCurrentView();
				speechContext.Languages = new string[] { speechLanguage.LanguageTag };

				speechResourceMap = ResourceManager.Current.MainResourceMap.GetSubtree("LocalizationSpeechResources");

				PopulateLanguageDropdown();
				await InitializeRecognizer(SpeechRecognizer.SystemSpeechLanguage);
			}
			else
			{
				appendStatusText("Permission to access capture resources was not given by the user; please set the application setting in Settings->Privacy->Microphone.");
				microphoneBtn.IsEnabled = false;
			}
		}

		private void PopulateLanguageDropdown()
		{
			Language defaultLanguage = SpeechRecognizer.SystemSpeechLanguage;
			IEnumerable<Language> supportedLanguages = SpeechRecognizer.SupportedTopicLanguages;
			XmlNodeList prefsList = speakerConfig.GetElementsByTagName("Preferences");
			XmlNode language = null;
			languages.Items.Clear();
			if (prefsList != null)
			{
				XmlNode prefs = prefsList.Item(0);
				if (prefs != null)
				{
					language = prefs.Attributes.GetNamedItem("Language");
				}
				if (language == null)
				{
					XmlAttribute langAttribute = speakerConfig.CreateAttribute("Language");
					prefs.Attributes.Append(langAttribute);
					langAttribute.Value = defaultLanguage.LanguageTag;
					language = langAttribute;
				}
			}

			foreach (Language lang in supportedLanguages)
			{
				ToggleMenuFlyoutItem item = new ToggleMenuFlyoutItem();
				item.Tag = lang;
				item.Text = lang.DisplayName;
				item.Click += clickedLanguage;

				languages.Items.Add(item);
				if ((language != null) && ((item.Tag as String) == language.Value))
				{
					item.IsChecked = true;
				}
			}
		}

		private async void clickedLanguage(object sender, RoutedEventArgs e)
        {
			ToggleMenuFlyoutItem selectedItem = null;
			foreach (ToggleMenuFlyoutItem item in languages.Items)
			{
				item.IsChecked = false;
				if (item.Tag == (sender as ToggleMenuFlyoutItem).Tag)
				{
					item.IsChecked = true;
					selectedItem = item;
				}
			}
			saveConfig();
			if (speechRecognizer != null && selectedItem != null)
			{
				Language newLanguage = (Language)selectedItem.Tag;
				if (speechRecognizer.CurrentLanguage != newLanguage)
				{
					// trigger cleanup and re-initialization of speech.
					try
					{
						speechContext.Languages = new string[] { newLanguage.LanguageTag };

						await InitializeRecognizer(newLanguage);
					}
					catch (Exception exception)
					{
						var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
						await messageDialog.ShowAsync();
					}
				}
			}
		}

		private async Task InitializeRecognizer(Language recognizerLanguage)
		{
			if (speechRecognizer != null)
			{
				// cleanup prior to re-initializing this scenario.
				speechRecognizer.StateChanged -= SpeechRecognizer_StateChanged;
				speechRecognizer.ContinuousRecognitionSession.Completed -= ContinuousRecognitionSession_Completed;
				speechRecognizer.ContinuousRecognitionSession.ResultGenerated -= ContinuousRecognitionSession_ResultGenerated;
				speechRecognizer.HypothesisGenerated -= SpeechRecognizer_HypothesisGenerated;

				this.speechRecognizer.Dispose();
				this.speechRecognizer = null;
			}

			this.speechRecognizer = new SpeechRecognizer(recognizerLanguage);

			// Provide feedback to the user about the state of the recognizer. This can be used to provide visual feedback in the form
			// of an audio indicator to help the user understand whether they're being heard.
			speechRecognizer.StateChanged += SpeechRecognizer_StateChanged;

			// Apply the dictation topic constraint to optimize for dictated freeform speech.
			var dictationConstraint = new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation");
			speechRecognizer.Constraints.Add(dictationConstraint);
			SpeechRecognitionCompilationResult result = await speechRecognizer.CompileConstraintsAsync();
			if (result.Status != SpeechRecognitionResultStatus.Success)
			{
				appendStatusText("Grammar Compilation Failed: " + result.Status.ToString());
				//btnContinuousRecognize.IsEnabled = false;
			}

			// Handle continuous recognition events. Completed fires when various error states occur. ResultGenerated fires when
			// some recognized phrases occur, or the garbage rule is hit. HypothesisGenerated fires during recognition, and
			// allows us to provide incremental feedback based on what the user's currently saying.
			speechRecognizer.ContinuousRecognitionSession.Completed += ContinuousRecognitionSession_Completed;
			speechRecognizer.ContinuousRecognitionSession.ResultGenerated += ContinuousRecognitionSession_ResultGenerated;
			speechRecognizer.HypothesisGenerated += SpeechRecognizer_HypothesisGenerated;
		}

		private async void ContinuousRecognitionSession_ResultGenerated(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
		{
			// We may choose to discard content that has low confidence, as that could indicate that we're picking up
			// noise via the microphone, or someone could be talking out of earshot.
			if (args.Result.Confidence == SpeechRecognitionConfidence.Medium ||
				args.Result.Confidence == SpeechRecognitionConfidence.High)
			{

				await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
				{
					appendStatusText("Sending: " + args.Result.Text + " ");
					sendText(args.Result.Text + " ");
				});
			}
			else
			{
				// In some scenarios, a developer may choose to ignore giving the user feedback in this case, if speech
				// is not the primary input mechanism for the application.
				// Here, just remove any hypothesis text by resetting it to the last known good.
				await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
				{
					appendStatusText(dictatedTextBuilder.ToString());
					string discardedText = args.Result.Text;
					if (!string.IsNullOrEmpty(discardedText))
					{
						discardedText = discardedText.Length <= 25 ? discardedText : (discardedText.Substring(0, 25) + "...");

						appendStatusText("Discarded due to low/rejected Confidence: " + discardedText);
					}
				});
			}
		}

		private async void ContinuousRecognitionSession_Completed(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
		{
			if (args.Status != SpeechRecognitionResultStatus.Success)
			{
				// If TimeoutExceeded occurs, the user has been silent for too long. We can use this to 
				// cancel recognition if the user in dictation mode and walks away from their device, etc.
				// In a global-command type scenario, this timeout won't apply automatically.
				// With dictation (no grammar in place) modes, the default timeout is 20 seconds.
				if (args.Status == SpeechRecognitionResultStatus.TimeoutExceeded)
				{
					await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
					{
#if DEBUG
						appendStatusText("Automatic Time Out of Dictation");
						appendStatusText(dictatedTextBuilder.ToString());
#endif
						isListening = false;
						microphoneBtn.Visibility = Visibility.Visible;
						redmicrophoneBtn.Visibility = Visibility.Collapsed;
						pasteBtn.IsEnabled = true;
						sendBtn.IsEnabled = true;
						languages.IsEnabled = true;
					});
				}
				else
				{
					await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
					{
#if DEBUG
						appendStatusText("Continuous Recognition Completed: " + args.Status.ToString());
#endif 
						microphoneBtn.Visibility = Visibility.Visible;
						redmicrophoneBtn.Visibility = Visibility.Collapsed;
						isListening = false;
						pasteBtn.IsEnabled = true;
						sendBtn.IsEnabled = true;
						languages.IsEnabled = true;
					});
				}
			}
		}
		private async void SpeechRecognizer_HypothesisGenerated(SpeechRecognizer sender, SpeechRecognitionHypothesisGeneratedEventArgs args)
		{
			string hypothesis = args.Hypothesis.Text;

			// Update the textbox with the currently confirmed text, and the hypothesis combined.
			string textboxContent = dictatedTextBuilder.ToString() + " " + hypothesis + " ...";
			await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
#if DEBUG
				appendStatusText("hypothesis=" + hypothesis);
#endif
			});
		}

		private async void SpeechRecognizer_StateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
		{
			await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
#if DEBUG
				appendStatusText("Speech recognizer state: " + args.State.ToString());
#endif
			});
		}
		private void rebuildHostListCtl(String current)
		{
			int selected = -1;
			hostnameCmbx.Items.Clear();

			XmlNodeList connections = speakerConfig.SelectNodes("/SpeakerGS/Connections");
			XmlNode root = connections.Item(0);

			for (int i = 0; i < root.ChildNodes.Count; i++)
			{
				XmlNode child = root.ChildNodes.Item(i);
				XmlNode name = child.Attributes.GetNamedItem("name");
				hostnameCmbx.Items.Add(name.Value.ToString());
				if (name.Value == current)
				{
					selected = i;
				}
			}
			if (selected == -1)
			{
				selected = hostnameCmbx.Items.Count - 1;
			}
			if (selected >= 0)
			{
				hostnameCmbx.SelectedIndex = selected;
				deleteBtn.IsEnabled = true;
				connectBtn.IsEnabled = true;
			}
			else
			{
				deleteBtn.IsEnabled = false;
				connectBtn.IsEnabled = false;
			}
		}

		private void saveConfig()
		{
			XmlNodeList prefsList = speakerConfig.SelectNodes("/SpeakerGS/Preferences");
			XmlNode prefs, last;
			Windows.Storage.StorageFolder localFolder = Windows.Storage.ApplicationData.Current.LocalFolder;
			string connectionsPath = localFolder.Path.ToString() + "\\connections.cfg";

			if (prefsList == null)
			{
				prefs = speakerConfig.CreateElement("Preferences");
				XmlAttribute attribute = speakerConfig.CreateAttribute("lastConnection");
				prefs.Attributes.Append(attribute);
				speakerConfig.AppendChild(prefs);

				last = attribute;
			}
			else
			{
				prefs = prefsList.Item(0);
				last = prefs.Attributes.GetNamedItem("lastConnection");
				if (last == null)
				{
					XmlAttribute attribute = speakerConfig.CreateAttribute("lastConnection");
					prefs.Attributes.Append(attribute);
					last = attribute;
				}
				XmlNode language;
				language = prefs.Attributes.GetNamedItem("Language");
				if (language == null)
                {
					XmlAttribute langAttribute = speakerConfig.CreateAttribute("Language");
					prefs.Attributes.Append(langAttribute);
					language = langAttribute;
				}
				foreach (ToggleMenuFlyoutItem item in languages.Items)
				{
					if (item.IsChecked == true)
					{
						language.Value = item.Tag.ToString();
						break;
					}
				}
			}
			if (hostnameCmbx.SelectedItem != null)
			{
				last.Value = hostnameCmbx.SelectedItem.ToString();

			}
			else
			{
				last.Value = "";
			}
			speakerConfig.Save(connectionsPath);
		}

		private async void AddBtn_Click(object sender, RoutedEventArgs e)
        {
			AddConnection add =  new AddConnection(ref speakerConfig);
			await add.ShowAsync();

			if (add.hostAdded ==  true)
            {
				rebuildHostListCtl("");
				saveConfig();
			}
			if (hostnameCmbx.Items.Count > 0)
            {
				connectBtn.IsEnabled = true;
            } else
            {
				connectBtn.IsEnabled = false;
			}

		}

        private void deleteBtn_Click(object sender, RoutedEventArgs e)
        {
			XmlNodeList rootList = speakerConfig.SelectNodes("/SpeakerGS/Connections");
			XmlNode root = rootList.Item(0);
			int selected = hostnameCmbx.SelectedIndex;
			root.RemoveChild(root.ChildNodes[selected]);
			hostnameCmbx.Items.RemoveAt(selected);
			saveConfig();
			if (hostnameCmbx.Items.Count > 0)
			{
				deleteBtn.IsEnabled = false;
				connectBtn.IsEnabled = false;
			}

		}

        private void connectBtn_Click(object sender, RoutedEventArgs e)
        {
			XmlNodeList connections = speakerConfig.SelectNodes("/SpeakerGS/Connections");
			XmlNode root = connections.Item(0);
			XmlNode connection = null;

			for (int i = 0; i < root.ChildNodes.Count; i++)
			{
				XmlNode child = root.ChildNodes.Item(i);
				XmlNode name = child.Attributes.GetNamedItem("name");
				hostnameCmbx.Items.Add(name.Value.ToString());
				if (name.Value == hostnameCmbx.SelectedItem.ToString())
				{
					connection = child;
					break;
				}
			}

			if (connection != null)
			{
				hostname = connection.Attributes.GetNamedItem("host").Value.ToString();
				addBtn.IsEnabled = false;
				deleteBtn.IsEnabled = false;
				connectBtn.IsEnabled = false;
				disconnectBtn.Visibility = Visibility.Visible;
				connectBtn.Visibility = Visibility.Collapsed;
				connectBtn.IsEnabled = true;
				sendBtn.IsEnabled = false;
				worker = new BackgroundWorker();
				worker.WorkerReportsProgress = true;
				worker.DoWork += worker_DoWork;
				worker.ProgressChanged += worker_ProgressChanged;
				worker.RunWorkerCompleted += worker_RunWorkerCompleted;
				worker.WorkerSupportsCancellation = true;
				worker.RunWorkerAsync(10000);
				editText.IsEnabled = true;
				if (permissionGained)
				{
					// Enable the recognition buttons.
					microphoneBtn.IsEnabled = true;
				}
			}

		}

		private void worker_DoWork(Object  sender, DoWorkEventArgs e)
		{
			try
			{
				string statusOutput = string.Format("Connecting to {0}", hostname.ToString());
				(sender as BackgroundWorker).ReportProgress(0, statusOutput);

				client = new TcpClient(hostname.ToString(), 19026);
				statusOutput = string.Format("Connected to {0}", hostname.ToString());
				(sender as BackgroundWorker).ReportProgress(1, statusOutput);
			}
			catch (Exception ex) {
				string errorText = string.Format("There was an error: {0}", ex.Message);
				(sender as BackgroundWorker).ReportProgress(0, errorText);
			}

			while (true && (client != null))
			{
				if ((sender as BackgroundWorker).CancellationPending) //if it was cancelled
				{
					e.Cancel = true;
					break;
				}
				if (client.Connected == false)
                {
					appendStatusText("Connection closed by remote server");
					e.Cancel = true;
					break;
                }
			}
		}

		public void appendStatusText(string data)
        {
			String txt;
			statusText.IsReadOnly = false;
			statusText.Document.GetText(Windows.UI.Text.TextGetOptions.None, out string prev);
			statusText.Document.SetText(Windows.UI.Text.TextSetOptions.None, prev + data);
			statusText.IsReadOnly = true;
			statusText.Document.GetText(Windows.UI.Text.TextGetOptions.None, out txt);
			statusText.Document.Selection.SetRange(txt.Length - 1, txt.Length - 1);
			statusText.Focus(FocusState.Keyboard);

		}
		private void worker_ProgressChanged(Object  sender, ProgressChangedEventArgs  e)
		{
			appendStatusText(e.UserState.ToString());

			if (e.ProgressPercentage == 1)
			{
				if (editText.Text.Length > 0)
				{
					sendBtn.IsEnabled = true;
				}
				pasteBtn.IsEnabled = true;
			}
		}
		private void worker_RunWorkerCompleted(Object sender, RunWorkerCompletedEventArgs e)
		{
			client.Close();
			connectBtn.Visibility = Visibility.Visible;
			disconnectBtn.Visibility = Visibility.Collapsed;
			addBtn.IsEnabled = true;
			deleteBtn.IsEnabled = true;
			sendBtn.IsEnabled = false;
			pasteBtn.IsEnabled = false;
			microphoneBtn.IsEnabled = false;
			appendStatusText("Connection Closed");
		}

		private byte[] ObjectToByteArray(Object obj)
		{
			int len = Marshal.SizeOf(obj);
			byte[] arr = new byte[len];

			IntPtr ptr = Marshal.AllocHGlobal(len);
			Marshal.StructureToPtr(obj, ptr, true);
			Marshal.Copy(ptr, arr, 0, len);
			Marshal.FreeHGlobal(ptr);

			return arr;
		}
		public void sendText(string data)
		{
			NetworkStream netStream = client.GetStream();
			tMessageHeader stateMsg = new tMessageHeader();
			stateMsg.messageType = LISTEN_TEXT_MSG;
			stateMsg.messageArg = Convert.ToUInt16(data.Length);
			byte[] buffer = ObjectToByteArray(stateMsg); 
			netStream.Write(buffer, 0, Marshal.SizeOf(stateMsg));

			buffer = new byte[data.Length];
			buffer = System.Text.Encoding.UTF8.GetBytes(data);
			netStream.Write(buffer, 0, buffer.Length);
		}

		private void sendBtn_Click(object sender, RoutedEventArgs e)
        {
			appendStatusText("Sending " + editText.Text.Length.ToString() + " bytes of text");
			sendText(editText.Text);
			editText.Text = "";
		}

        private async void pasteBtn_Click(object sender, RoutedEventArgs e)
        {
			DataPackageView dataPackageView = Clipboard.GetContent();

			if (dataPackageView.Contains(StandardDataFormats.Text))
			{
				string data = await dataPackageView.GetTextAsync();
				appendStatusText("Sending " + data.Length.ToString() + " bytes of Clipboard text");
				sendText(data);
			}
		}

        private void editText_TextChanged(object sender, TextChangedEventArgs e)
        {
			if (editText.Text.Length > 0)
			{
				sendBtn.IsEnabled = true;
			}
			else
			{
				sendBtn.IsEnabled = false;
			}
		}

        private void disconnectBtn_Click(object sender, RoutedEventArgs e)
        {
			worker.CancelAsync();
			client.Close();
		}

		private async void microphoneBtn_Click(object sender, RoutedEventArgs e)
        {
			// Start recognition.

			if (isListening == false)
			{
				// The recognizer can only start listening in a continuous fashion if the recognizer is currently idle.
				// This prevents an exception from occurring.
				if (speechRecognizer.State == SpeechRecognizerState.Idle)
				{
					microphoneBtn.Visibility = Visibility.Collapsed;
					redmicrophoneBtn.Visibility = Visibility.Visible;
					pasteBtn.IsEnabled = false;
					sendBtn.IsEnabled = false;
					languages.IsEnabled = false;
					editText.IsEnabled = false;

					try
					{
						isListening = true;
						await speechRecognizer.ContinuousRecognitionSession.StartAsync();
					}
					catch (Exception ex)
					{
						if ((uint)ex.HResult == HResultPrivacyStatementDeclined)
						{
							// Show a UI link to the privacy settings.
							//hlOpenPrivacySettings.Visibility = Visibility.Visible;
						}
						else
						{
							var messageDialog = new Windows.UI.Popups.MessageDialog(ex.Message, "Exception");
							await messageDialog.ShowAsync();
						}

						isListening = false;
						microphoneBtn.Visibility = Visibility.Visible;
						redmicrophoneBtn.Visibility = Visibility.Collapsed;
						pasteBtn.IsEnabled = true;
						sendBtn.IsEnabled = true;
						languages.IsEnabled = true;
						editText.IsEnabled = true;
					}
				}
			}
			else
			{
				isListening = false;
				microphoneBtn.Visibility = Visibility.Visible;
				redmicrophoneBtn.Visibility = Visibility.Collapsed;
				pasteBtn.IsEnabled = true;
				sendBtn.IsEnabled = true;
				languages.IsEnabled = true;
				editText.IsEnabled = true;

				if (speechRecognizer.State != SpeechRecognizerState.Idle)
				{
					// Cancelling recognition prevents any currently recognized speech from
					// generating a ResultGenerated event. StopAsync() will allow the final session to 
					// complete.
					try
					{
						await speechRecognizer.ContinuousRecognitionSession.StopAsync();

#if DEBUG
						// Ensure we don't leave any hypothesis text behind
						if (dictatedTextBuilder.ToString().Length > 0)
                        {
							appendStatusText(dictatedTextBuilder.ToString());
					}
#endif
					}
					catch (Exception exception)
					{
						var messageDialog = new Windows.UI.Popups.MessageDialog(exception.Message, "Exception");
						await messageDialog.ShowAsync();
					}
				}
			}
		}
    }
}
