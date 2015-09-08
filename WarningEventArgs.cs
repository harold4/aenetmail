using System;

namespace AE.Net.Mail {
	public class WarningEventArgs : EventArgs {
		public string Message { get; set; }
		public MailMessage MailMessage { get; set; }
	}

    public class EmailReadedEventArgs: EventArgs
    {
        public StreamInterceptor StreamInterceptor { get; set; }

        public EmailReadedEventArgs(StreamInterceptor streamInterceptor)
        {
            StreamInterceptor = streamInterceptor;
        }
	}
}
