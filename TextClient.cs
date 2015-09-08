using System;
using System.IO;
using System.Net.Sockets;

namespace AE.Net.Mail {
	public abstract class TextClient : IDisposable {
		protected TcpClient Connection;
		protected Stream Stream;

		public virtual string Host { get; private set; }
		public virtual int Port { get; set; }
		public virtual bool Ssl { get; set; }
		public virtual bool IsConnected { get; private set; }
		public virtual bool IsAuthenticated { get; private set; }
		public virtual bool IsDisposed { get; private set; }
        public System.Text.Encoding Encoding { get; set; }
		public int ServerTimeout { get; set; }

		public event EventHandler<WarningEventArgs> Warning;

		protected virtual void RaiseWarning(MailMessage mailMessage, string message) {
			var warning = Warning;
			if (warning != null) {
				warning(this, new WarningEventArgs { MailMessage = mailMessage, Message = message });
			}
		}

	    protected TextClient() {
			Encoding = System.Text.Encoding.GetEncoding(1252);
			ServerTimeout = 10000;
		}

		internal abstract void OnLogin(string username, string password);
		internal abstract void OnLogout();
		internal abstract void CheckResultOk(string result);

		protected virtual void OnConnected(string result) {
			CheckResultOk(result);
		}

		public virtual void Login(string username, string password) {
			if (!IsConnected) {
				throw new Exception("You must connect first!");
			}
			IsAuthenticated = false;
			OnLogin(username, password);
			IsAuthenticated = true;
		}

		public virtual void Logout() {
			OnLogout();
			IsAuthenticated = false;
		}

		public virtual void Connect(string hostname, int port, bool ssl, bool skipSslValidation) {
			System.Net.Security.RemoteCertificateValidationCallback validateCertificate = null;
			if (skipSslValidation)
				validateCertificate = (sender, cert, chain, err) => true;
			Connect(hostname, port, ssl, validateCertificate);
		}

		public virtual void Connect(string hostname, int port, bool ssl, System.Net.Security.RemoteCertificateValidationCallback validateCertificate) {
			try {
				Host = hostname;
				Port = port;
				Ssl = ssl;

				Connection = new TcpClient(hostname, port);
				Stream = Connection.GetStream();
				if (ssl) {
					System.Net.Security.SslStream sslStream;
					if (validateCertificate != null)
						sslStream = new System.Net.Security.SslStream(Stream, false, validateCertificate);
					else
						sslStream = new System.Net.Security.SslStream(Stream, false);
					Stream = sslStream;
					sslStream.AuthenticateAsClient(hostname);
				}

				OnConnected(GetResponse());

				IsConnected = true;
				Host = hostname;
			} catch (Exception) {
				IsConnected = false;
				Utilities.TryDispose(ref Stream);
				throw;
			}
		}

		protected virtual void CheckConnectionStatus() {
			if (IsDisposed)
				throw new ObjectDisposedException(GetType().Name);
			if (!IsConnected)
				throw new Exception("You must connect first!");
			if (!IsAuthenticated)
				throw new Exception("You must authenticate first!");
		}

		protected virtual void SendCommand(string command) {
			var bytes = System.Text.Encoding.Default.GetBytes(command + "\r\n");
			Stream.Write(bytes, 0, bytes.Length);
		}

		protected virtual string SendCommandGetResponse(string command) {
			SendCommand(command);
			return GetResponse();
		}

		protected virtual string GetResponse()
		{
			return GetResponse(ServerTimeout);
		}

		protected virtual string GetResponse(int timeout) {
			int max = 0;
			return Stream.ReadLine(ref max, Encoding, null, timeout);
		}

		protected virtual void SendCommandCheckOk(string command) {
			CheckResultOk(SendCommandGetResponse(command));
		}

		public virtual void Disconnect() {
			if (!IsConnected)
				return;
			if (IsAuthenticated) {
				Logout();
			}
			IsConnected = false;
			Utilities.TryDispose(ref Stream);
			Utilities.TryDispose(ref Connection);
		}

		~TextClient() {
			Dispose(false);
		}
		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}
		protected virtual void Dispose(bool disposing) {
			if (!IsDisposed && disposing)
				lock (this)
					if (!IsDisposed && disposing) {
						IsDisposed = true;
						Disconnect();
					}

			Stream = null;
			Connection = null;
		}
	}
}
