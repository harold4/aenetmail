using System;
using System.Text.RegularExpressions;

namespace AE.Net.Mail {
	public sealed class Pop3Client : TextClient, IMailClient {
		public Pop3Client() { }
		public Pop3Client(string host, string username, string password, int port = 110, bool secure = false, bool skipSslValidation = false) {
			Connect(host, port, secure, skipSslValidation);
			Login(username, password);
		}

		internal override void OnLogin(string username, string password) {
			SendCommandCheckOk("USER " + username);
			SendCommandCheckOk("PASS " + password);
		}

		internal override void OnLogout() {
			if (Stream != null) {
				SendCommand("QUIT");
			}
		}

		internal override void CheckResultOk(string result) {
			if (!result.StartsWith("+OK", StringComparison.OrdinalIgnoreCase)) {
				throw new Exception(result.Substring(result.IndexOf(' ') + 1).Trim());
			}
		}

		public int GetMessageCount() {
			CheckConnectionStatus();
			var result = SendCommandGetResponse("STAT");
			CheckResultOk(result);
			return int.Parse(result.Split(' ')[1]);
		}

		public MailMessage GetMessage(int index, bool headersOnly = false) {
			return GetMessage((index + 1).ToString(), headersOnly);
		}

		private static readonly Regex RxOctets = new Regex(@"(\d+)\s+octets", RegexOptions.IgnoreCase);
		public MailMessage GetMessage(string uid, bool headersOnly = false) {
			CheckConnectionStatus();
			var line = SendCommandGetResponse(string.Format(headersOnly ? "TOP {0} 0" : "RETR {0}", uid));
			var size = RxOctets.Match(line).Groups[1].Value.ToInt();
			CheckResultOk(line);
			var msg = new MailMessage();
			msg.Load(Stream, headersOnly, size, '.');

			msg.Uid = uid;
			var last = GetResponse();
			if (string.IsNullOrEmpty(last))
			{
				try
				{
					last = GetResponse();
				}
				catch (System.IO.IOException)
				{
					// There was really nothing back to read from the remote server
				}
			}

			if (last != ".") {
#if DEBUG
				System.Diagnostics.Debugger.Break();
#endif
				RaiseWarning(msg, "Expected \".\" in stream, but received \"" + last + "\"");
			}

			return msg;
		}

		public void DeleteMessage(string uid) {
			SendCommandCheckOk("DELE " + uid);

		}

		public void DeleteMessage(int index) {
			DeleteMessage((index + 1).ToString());
		}

		public void DeleteMessage(AE.Net.Mail.MailMessage msg) {
			DeleteMessage(msg.Uid);
		}

	    public event EventHandler<EmailReadedEventArgs> Rfc822Readed;
	}
}