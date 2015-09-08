using AE.Net.Mail.Imap;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace AE.Net.Mail
{

    public enum AuthMethods
    {
        Login,
        // ReSharper disable once InconsistentNaming
        CRAMMD5,
        SaslOAuth
    }

    public sealed class ImapClient : TextClient, IMailClient
    {
        private string _selectedMailbox;
        private Mailbox _mailbox;
        private int _tag = 0;
        private string[] _capability;

        private bool _idling;
        private Task _idleTask;
        private Task _responseTask;
        private readonly ModifiedUtf7Encoding _utf7 = new ModifiedUtf7Encoding();

        private string _fetchHeaders = null;

        public ImapClient()
        {
            IdleTimeout = 1200000;
        }
        public ImapClient(string host, string username, string password, AuthMethods method = AuthMethods.Login, int port = 143, bool secure = false, bool skipSslValidation = false)
            : this()
        {
            Connect(host, port, secure, skipSslValidation);
            AuthMethod = method;
            Login(username, password);
        }

        public int IdleTimeout { get; set; }

        public AuthMethods AuthMethod { get; set; }

        public Mailbox Mailbox
        {
            get
            {
                CheckMailboxSelected();
                return _mailbox;
            }
        }

        private string GetTag()
        {
            _tag++;
            return string.Format("xm{0:000} ", _tag);
        }

        public bool Supports(string command)
        {
            return (_capability ?? Capability()).Contains(command, StringComparer.OrdinalIgnoreCase);
        }

        private EventHandler<MessageEventArgs> _newMessage;
        public event EventHandler<MessageEventArgs> NewMessage
        {
            add
            {
                _newMessage += value;
                IdleStart();
            }
            remove
            {
                // ReSharper disable once DelegateSubtraction
                _newMessage -= value;
                if (!HasEvents)
                {
                    IdleStop();
                }
            }
        }

        private EventHandler<MessageEventArgs> _messageDeleted;
        public event EventHandler<MessageEventArgs> MessageDeleted
        {
            add
            {
                _messageDeleted += value;
                IdleStart();
            }
            remove
            {
                // ReSharper disable once DelegateSubtraction
                _messageDeleted -= value;
                if (!HasEvents)
                {
                    IdleStop();
                }
            }
        }

        public event EventHandler<ImapClientExceptionEventArgs> ImapException;

        private void IdleStart()
        {
            CheckMailboxSelected();

            _idling = true;
            if (!Supports("IDLE"))
            {
                throw new InvalidOperationException("This IMAP server does not support the IDLE command");
            }
            IdleResume();
        }

        private void IdlePause()
        {
            if (_idleTask == null || !_idling)
                return;
            CheckConnectionStatus();
            SendCommand("DONE");

            if (!_idleTask.Wait(ServerTimeout))
            {
                //Not responding
                Disconnect();
                ImapClientException e = new ImapClientException("Lost communication to IMAP server, connection closed.");
                ImapClientExceptionEventArgs args = new ImapClientExceptionEventArgs(e);
                Task.Factory.StartNew(() => ImapException.Fire(this, args));
            }
            _idleTask.Dispose();
            _idleTask = null;
        }

        private void IdleResume()
        {
            if (!_idling)
                return;

            IdleResumeCommand();

            if (_idleTask == null)
            {
                _idleTask = new Task(() => WatchIdleQueue());
                _idleTask.Start();
            }
        }

        private void IdleResumeCommand()
        {
            SendCommandGetResponse(GetTag() + "IDLE");
        }

        private bool HasEvents
        {
            get
            {
                return _messageDeleted != null || _newMessage != null;
            }
        }

        private void IdleStop()
        {
            IdlePause();
            _idling = false;
        }

        public bool TryGetResponse(out string response)
        {
            string resp = response = null;

            _responseTask = Task.Factory.StartNew(() =>
            {
                resp = GetResponse(IdleTimeout + ServerTimeout * 3);
            });

            try
            {
                if (_responseTask.Wait(IdleTimeout))
                {
                    response = resp;
                    _responseTask.Dispose();
                    _responseTask = null;
                    return true;
                }
                else
                    return false;
            }
            catch (AggregateException)
            {
                throw;
            }
        }

        private void WatchIdleQueue()
        {
            try
            {
                string last = null;

                while (true)
                {
                    string resp;
                    if (!TryGetResponse(out resp))
                    {
                        //Child task should still running on ReadByte here.
                        //Need to send some data to get it to exit.

                        SendCommand("DONE"); //_ResponseTask should pick up response and exit
                        Debug.Assert(_responseTask != null, "_responseTask != null");
                        if (!_responseTask.Wait(ServerTimeout))
                        {
                            //Not responding
                            Disconnect();
                            throw new ImapClientException("Lost communication to IMAP server, connection closed.");
                        }
                        _responseTask.Dispose();
                        _responseTask = null;

                        IdleResumeCommand();

                        continue;
                    }

                    if (resp.Contains("OK IDLE"))  //Server response after DONE
                        return;

                    var data = resp.Split(' ');
                    if (data[0] == "*" && data.Length >= 3)
                    {
                        var e = new MessageEventArgs { Client = this, MessageCount = int.Parse(data[1]) };
                        if (data[2].Is("EXISTS") && !last.Is("EXPUNGE") && e.MessageCount > 0)
                        {
                            Task.Factory.StartNew(() => _newMessage.Fire(this, e)); //Fire the event in a task
                        }
                        else if (data[2].Is("EXPUNGE"))
                        {
                            Task.Factory.StartNew(() => _messageDeleted.Fire(this, e));
                        }
                        last = data[2];
                    }
                }
            }
            catch (Exception e)
            {
                ImapClientExceptionEventArgs args = new ImapClientExceptionEventArgs(e);
                Task.Factory.StartNew(() => ImapException.Fire(this, args));
            }
        }

        public void AppendMail(MailMessage email, string mailbox = null)
        {
            var body = new StringBuilder();
            using (var txt = new System.IO.StringWriter(body))
                email.Save(txt);
            AppendMail(body.ToString(), email.RawFlags.Length > 0 ? email.Flags : (Flags?)null, mailbox);
        }

        public void AppendMail(string rawMessage, Flags? messageFlags = null, string mailbox = null)
        {
            IdlePause();

            mailbox = _utf7.Encode(mailbox);
            string flags = String.Empty;

            string size = rawMessage.Length.ToString();
            if (messageFlags.HasValue)
            {
                flags = " (" + string.Join(" ", messageFlags.Value) + ")";
            }

            if (mailbox == null)
                CheckMailboxSelected();
            mailbox = mailbox ?? _selectedMailbox;

            string body = rawMessage;
            string command = GetTag() + "APPEND " + (mailbox ?? _selectedMailbox).QuoteString() + flags + " {" + size + "}";
            string response = SendCommandGetResponse(command);
            if (response.StartsWith("+"))
            {
                response = SendCommandGetResponse(body.ToString());
            }
            IdleResume();
        }

        public void Noop()
        {
            IdlePause();

            var tag = GetTag();
            var response = SendCommandGetResponse(tag + "NOOP");
            while (!response.StartsWith(tag))
            {
                response = GetResponse();
            }

            IdleResume();
        }

        public string[] Capability()
        {
            IdlePause();
            string command = GetTag() + "CAPABILITY";
            string response = SendCommandGetResponse(command);
            IdleResume();
            return _capability;
        }

        public void Copy(string messageset, string destination)
        {
            CheckMailboxSelected();
            IdlePause();
            string prefix = null;
            if (messageset.StartsWith("UID ", StringComparison.OrdinalIgnoreCase))
            {
                messageset = messageset.Substring(4);
                prefix = "UID ";
            }
            string command = string.Concat(GetTag(), prefix, "COPY ", messageset, " " + destination.QuoteString());
            SendCommandCheckOk(command);
            IdleResume();
        }

        public void CreateMailbox(string mailbox)
        {
            IdlePause();
            string command = GetTag() + "CREATE " + _utf7.Encode(mailbox).QuoteString();
            SendCommandCheckOk(command);
            IdleResume();
        }

        public void DeleteMailbox(string mailbox)
        {
            IdlePause();
            string command = GetTag() + "DELETE " + _utf7.Encode(mailbox).QuoteString();
            SendCommandCheckOk(command);
            IdleResume();
        }

        public Mailbox Examine(string mailboxName)
        {
            IdlePause();

            Mailbox mailbox = mailboxName.Equals(_selectedMailbox) ? _mailbox : new Mailbox(mailboxName);
            string tag = GetTag();
            string command = tag + "EXAMINE " + _utf7.Encode(mailboxName).QuoteString();
            string response = SendCommandGetResponse(command, mailbox);

            IdleResume();
            return mailbox;
        }

        public void Expunge()
        {
            CheckMailboxSelected();
            IdlePause();

            string tag = GetTag();
            string command = tag + "EXPUNGE";
            string response = SendCommandGetResponse(command);

            IdleResume();
        }

        public void DeleteMessage(AE.Net.Mail.MailMessage msg)
        {
            DeleteMessage(msg.Uid);
        }

        public event EventHandler<EmailReadedEventArgs> Rfc822Readed;

        public void DeleteMessage(string uid)
        {
            CheckMailboxSelected();
            Store("UID " + uid, true, "\\Seen \\Deleted");
        }

        public void MoveMessage(string uid, string folderName)
        {
            CheckMailboxSelected();
            Copy("UID " + uid, folderName);
            DeleteMessage(uid);
        }

        private void CheckMailboxSelected()
        {
            if (string.IsNullOrEmpty(_selectedMailbox))
            {
                SelectMailbox("INBOX");
            }
        }

        public MailMessage GetMessage(string uid, bool headersonly = false)
        {
            return GetMessage(uid, headersonly, true);
        }

        public MailMessage GetMessage(int index, bool headersonly = false)
        {
            return GetMessage(index, headersonly, true);
        }

        public MailMessage GetMessage(int index, bool headersonly, bool setseen)
        {
            return GetMessages(index, index, headersonly, setseen).FirstOrDefault();
        }

        public MailMessage GetMessage(string uid, bool headersonly, bool setseen)
        {
            return GetMessages(uid, uid, headersonly, setseen).FirstOrDefault();
        }

        public MailMessage[] GetMessages(string startUid, string endUid, bool headersonly = true, bool setseen = false)
        {
            return GetMessages(startUid, endUid, true, headersonly, setseen);
        }

        public MailMessage[] GetMessages(int startIndex, int endIndex, bool headersonly = true, bool setseen = false)
        {
            return GetMessages((startIndex + 1).ToString(), (endIndex + 1).ToString(), false, headersonly, setseen);
        }

        public void DownloadMessage(System.IO.Stream stream, int index, bool setseen)
        {
            GetMessages((index + 1).ToString(), (index + 1).ToString(), false, false, false, setseen, (message, size, headers) =>
            {
                Utilities.CopyStream(message, stream, size);
                return null;
            });
        }

        public void DownloadMessage(System.IO.Stream stream, string uid, bool setseen)
        {
            GetMessages(uid, uid, true, false, false, setseen, (message, size, headers) =>
            {
                Utilities.CopyStream(message, stream, size);
                return null;
            });
        }

        public MailMessage[] GetMessages(string start, string end, bool uid, bool headersonly, bool setseen)
        {
            var x = new List<MailMessage>();

            GetMessages(start, end, uid, false, headersonly, setseen, (stream, size, imapHeaders) =>
            {
                var mail = new MailMessage { Encoding = Encoding };
                mail.Size = size;

                if (imapHeaders["UID"] != null)
                    mail.Uid = imapHeaders["UID"];

                if (imapHeaders["Flags"] != null)
                    mail.SetFlags(imapHeaders["Flags"]);

                mail.Load(stream, headersonly, mail.Size);

                foreach (var key in imapHeaders.AllKeys.Except(new[] { "UID", "Flags", "BODY[]", "BODY[HEADER]" }, StringComparer.OrdinalIgnoreCase))
                    mail.Headers.Add(key, new HeaderValue(imapHeaders[key]));

                x.Add(mail);

                return mail;
            });

            return x.ToArray();
        }

        public void GetMessages(string start, string end, bool uid, bool uidsonly, bool headersonly, bool setseen, Action<MailMessage> processCallback)
        {

            GetMessages(start, end, uid, uidsonly, headersonly, setseen, (stream, size, imapHeaders) =>
            {
                var mail = new MailMessage { Encoding = Encoding };
                mail.Size = size;

                if (imapHeaders["UID"] != null)
                    mail.Uid = imapHeaders["UID"];

                if (imapHeaders["Flags"] != null)
                    mail.SetFlags(imapHeaders["Flags"]);

                mail.Load(stream, headersonly, mail.Size);

                foreach (var key in imapHeaders.AllKeys.Except(new[] { "UID", "Flags", "BODY[]", "BODY[HEADER]" }, StringComparer.OrdinalIgnoreCase))
                    mail.Headers.Add(key, new HeaderValue(imapHeaders[key]));

                processCallback(mail);

                return mail;
            });
        }

        public void GetMessages(string start, string end, bool uid, bool uidsonly, bool headersonly, bool setseen, Func<System.IO.Stream, int, NameValueCollection, MailMessage> action)
        {
            CheckMailboxSelected();
            IdlePause();

            string tag = GetTag();
            string command = tag + (uid ? "UID " : null)
                    + "FETCH " + start + ":" + end + " ("
                    + _fetchHeaders + "UID FLAGS"
                    + (uidsonly ? null : (setseen ? " BODY[" : " BODY.PEEK[") + (headersonly ? "HEADER]" : "]"))
                    + ")";

            SendCommand(command);
            while (true)
            {
                string response = GetResponse();
                if (string.IsNullOrEmpty(response) || response.Contains(tag + "OK"))
                    break;

                if (response[0] != '*' || !response.Contains("FETCH ("))
                    continue;

                var imapHeaders = Utilities.ParseImapHeader(response.Substring(response.IndexOf('(') + 1));
                String body = (imapHeaders["BODY[HEADER]"] ?? imapHeaders["BODY[]"]);
                if (body == null && !uidsonly)
                {
                    RaiseWarning(null, "Expected BODY[] in stream, but received \"" + response + "\"");
                    break;
                }
                var size = (uidsonly ? 0 : body.Trim('{', '}').ToInt());
                MailMessage msg;
                using (StreamInterceptor streamInterceptor = new StreamInterceptor(Stream, size))
                {
                    msg = action(streamInterceptor.Stream, size, imapHeaders);
                    //msg = action(_Stream, size, imapHeaders);
                    if (!headersonly)
                    {
                        OnRfc822Readed(new EmailReadedEventArgs(streamInterceptor));
                    }
                }

                // with only uids we have no body and the closing bracket is on the same line
                if (!uidsonly)
                {
                    response = GetResponse();
                    if (response == null)
                    {
                        RaiseWarning(null, "Expected \")\" in stream, but received nothing");
                        break;
                    }
                }
                var n = response.Trim().LastOrDefault();
                if (n != ')')
                {
                    RaiseWarning(null, "Expected \")\" in stream, but received \"" + response + "\"");
                }
            }

            IdleResume();
        }

        public Quota GetQuota(string mailbox)
        {
            if (!Supports("NAMESPACE"))
                throw new Exception("This command is not supported by the server!");
            IdlePause();

            Quota quota = null;
            string command = GetTag() + "GETQUOTAROOT " + _utf7.Encode(mailbox).QuoteString();
            string response = SendCommandGetResponse(command);
            string reg = "\\* QUOTA (.*?) \\((.*?) (.*?) (.*?)\\)";
            while (response.StartsWith("*"))
            {
                Match m = Regex.Match(response, reg);
                if (m.Groups.Count > 1)
                {
                    quota = new Quota(m.Groups[1].ToString(),
                                                                                                    m.Groups[2].ToString(),
                                                                                                    Int32.Parse(m.Groups[3].ToString()),
                                                                                                    Int32.Parse(m.Groups[4].ToString())
                                                                                    );
                    break;
                }
                response = GetResponse();
            }

            IdleResume();
            return quota;
        }

        public Mailbox[] ListMailboxes(string reference, string pattern)
        {
            IdlePause();

            var x = new List<Mailbox>();
            string command = GetTag() + "LIST " + reference.QuoteString() + " " + pattern.QuoteString();
            const string reg = "\\* LIST \\(([^\\)]*)\\) \\\"([^\\\"]+)\\\" \\\"?([^\\\"]+)\\\"?";
            string response = SendCommandGetResponse(command);
            Match m = Regex.Match(response, reg);
            while (m.Groups.Count > 1)
            {
                Mailbox mailbox = new Mailbox(_utf7.Decode(m.Groups[3].Value));
                mailbox.SetFlags(m.Groups[1].Value);
                x.Add(mailbox);
                response = GetResponse();
                m = Regex.Match(response, reg);
            }
            IdleResume();
            return x.ToArray();
        }

        public Mailbox[] ListSuscribesMailboxes(string reference, string pattern)
        {
            IdlePause();

            var x = new List<Mailbox>();
            string command = GetTag() + "LSUB " + reference.QuoteString() + " " + pattern.QuoteString();
            string reg = "\\* LSUB \\(([^\\)]*)\\) \\\"([^\\\"]+)\\\" \\\"([^\\\"]+)\\\"";
            string response = SendCommandGetResponse(command);
            Match m = Regex.Match(response, reg);
            while (m.Groups.Count > 1)
            {
                Mailbox mailbox = new Mailbox(_utf7.Decode(m.Groups[3].Value));
                x.Add(mailbox);
                response = GetResponse();
                m = Regex.Match(response, reg);
            }
            IdleResume();
            return x.ToArray();
        }

        internal override void OnLogin(string login, string password)
        {
            string command = String.Empty;
            string result = String.Empty;
            string tag = GetTag();

            switch (AuthMethod)
            {
                case AuthMethods.CRAMMD5:
                    command = tag + "AUTHENTICATE CRAM-MD5";
                    result = SendCommandGetResponse(command);
                    // retrieve server key
                    string key = result.Replace("+ ", "");
                    key = System.Text.Encoding.Default.GetString(Convert.FromBase64String(key));
                    // calcul hash
                    using (var kMd5 = new HMACMD5(System.Text.Encoding.ASCII.GetBytes(password)))
                    {
                        byte[] hash1 = kMd5.ComputeHash(System.Text.Encoding.ASCII.GetBytes(key));
                        key = BitConverter.ToString(hash1).ToLower().Replace("-", "");
                        result = Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes(login + " " + key));
                        result = SendCommandGetResponse(result);
                    }
                    break;

                case AuthMethods.Login:
                    command = tag + "LOGIN " + login.QuoteString() + " " + password.QuoteString();
                    result = SendCommandGetResponse(command);
                    break;

                case AuthMethods.SaslOAuth:
                    string sasl = "user=" + login + "\x01" + "auth=Bearer " + password + "\x01" + "\x01";
                    string base64 = Convert.ToBase64String(Encoding.GetBytes(sasl));
                    command = tag + "AUTHENTICATE XOAUTH2 " + base64;
                    result = SendCommandGetResponse(command);
                    break;

                default:
                    throw new NotSupportedException();
            }

            if (!result.StartsWith(tag + "OK"))
            {
                if (result.StartsWith("+ ") && result.EndsWith("=="))
                {
                    string jsonErr = Utilities.DecodeBase64(result.Substring(2), System.Text.Encoding.UTF7);
                    throw new Exception(jsonErr);
                }
                else
                    throw new Exception(result);
            }

            //if (Supports("COMPRESS=DEFLATE")) {
            //  SendCommandCheckOK(GetTag() + "compress deflate");
            //  _Stream0 = _Stream;
            // // _Reader = new System.IO.StreamReader(new System.IO.Compression.DeflateStream(_Stream0, System.IO.Compression.CompressionMode.Decompress, true), System.Text.Encoding.Default);
            // // _Stream = new System.IO.Compression.DeflateStream(_Stream0, System.IO.Compression.CompressionMode.Compress, true);
            //}

            if (Supports("X-GM-EXT-1"))
            {
                _fetchHeaders = "X-GM-MSGID X-GM-THRID X-GM-LABELS ";
            }
        }

        internal override void OnLogout()
        {
            if (IsConnected)
            {
                if (_idleTask != null && _idling)
                {
                    IdleStop();
                }
                SendCommand(GetTag() + "LOGOUT");
            }
        }

        public Namespaces Namespace()
        {
            if (!Supports("NAMESPACE"))
                throw new NotSupportedException("This command is not supported by the server!");
            IdlePause();

            string command = GetTag() + "NAMESPACE";
            string response = SendCommandGetResponse(command);

            if (!response.StartsWith("* NAMESPACE"))
            {
                throw new Exception("Unknow server response !");
            }

            response = response.Substring(12);
            Namespaces n = new Namespaces();
            //[TODO] be sure to parse correctly namespace when not all namespaces are present. NIL character
            string reg = @"\((.*?)\) \((.*?)\) \((.*?)\)$";
            Match m = Regex.Match(response, reg);
            if (m.Groups.Count != 4)
                throw new Exception("En error occure, this command is not fully supported !");
            string reg2 = "\\(\\\"(.*?)\\\" \\\"(.*?)\\\"\\)";
            Match m2 = Regex.Match(m.Groups[1].ToString(), reg2);
            while (m2.Groups.Count > 1)
            {
                n.ServerNamespace.Add(new Namespace(m2.Groups[1].Value, m2.Groups[2].Value));
                m2 = m2.NextMatch();
            }
            m2 = Regex.Match(m.Groups[2].ToString(), reg2);
            while (m2.Groups.Count > 1)
            {
                n.UserNamespace.Add(new Namespace(m2.Groups[1].Value, m2.Groups[2].Value));
                m2 = m2.NextMatch();
            }
            m2 = Regex.Match(m.Groups[3].ToString(), reg2);
            while (m2.Groups.Count > 1)
            {
                n.SharedNamespace.Add(new Namespace(m2.Groups[1].Value, m2.Groups[2].Value));
                m2 = m2.NextMatch();
            }
            GetResponse();
            IdleResume();
            return n;
        }

        public int GetMessageCount()
        {
            CheckMailboxSelected();
            return GetMessageCount(null);
        }
        public int GetMessageCount(string mailbox)
        {
            IdlePause();

            string command = GetTag() + "STATUS " + Utilities.QuoteString(_utf7.Encode(mailbox) ?? _selectedMailbox) + " (MESSAGES)";
            string response = SendCommandGetResponse(command);
            string reg = @"\* STATUS.*MESSAGES (\d+)";
            int result = 0;
            while (response.StartsWith("*"))
            {
                Match m = Regex.Match(response, reg);
                if (m.Groups.Count > 1)
                    result = Convert.ToInt32(m.Groups[1].ToString());
                response = GetResponse();
                m = Regex.Match(response, reg);
            }
            IdleResume();
            return result;
        }

        public void RenameMailbox(string frommailbox, string tomailbox)
        {
            IdlePause();

            string command = GetTag() + "RENAME " + frommailbox.QuoteString() + " " + tomailbox.QuoteString();
            SendCommandCheckOk(command);
            IdleResume();
        }

        public string[] Search(SearchCondition criteria, bool uid = true)
        {
            return Search(criteria.ToString(), uid);
        }

        public string[] Search(string criteria, bool uid = true)
        {
            CheckMailboxSelected();

            string isuid = uid ? "UID " : "";
            string tag = GetTag();
            string command = tag + isuid + "SEARCH " + criteria;
            string response = SendCommandGetResponse(command);

            if (!response.StartsWith("* SEARCH", StringComparison.InvariantCultureIgnoreCase) && !IsResultOk(response))
            {
                throw new Exception(response);
            }

            string temp;
            while (!(temp = GetResponse()).StartsWith(tag))
            {
                response += Environment.NewLine + temp;
            }

            var m = Regex.Match(response, @"^\* SEARCH (.*)");
            return m.Groups[1].Value.Trim().Split(' ').Where(x => !string.IsNullOrEmpty(x)).ToArray();
        }

        public Lazy<MailMessage>[] SearchMessages(SearchCondition criteria, bool headersonly = false, bool setseen = false)
        {
            return Search(criteria, true)
                            .Select(x => new Lazy<MailMessage>(() => GetMessage(x, headersonly, setseen)))
                            .ToArray();
        }

        public Mailbox SelectMailbox(string mailboxName)
        {
            IdlePause();

            var tag = GetTag();
            var command = tag + "SELECT " + _utf7.Encode(mailboxName).QuoteString();
            _mailbox = new Mailbox(mailboxName);
            _selectedMailbox = mailboxName;
            var response = SendCommandGetResponse(command);

            CheckResultOk(response);
            _mailbox.IsWritable = Regex.IsMatch(response, "READ.WRITE", RegexOptions.IgnoreCase);

            IdleResume();
            return _mailbox;
        }

        protected override string SendCommandGetResponse(string command)
        {
            return SendCommandGetResponse(command, _mailbox);
        }

        private string SendCommandGetResponse(string command, Mailbox mailbox)
        {
            var response = base.SendCommandGetResponse(command);
            response = HandleUntaggedResponse(response, Mailbox);
            return response;
        }

        private string HandleUntaggedResponse(string response, Mailbox mailbox)
        {
            while (response.StartsWith("*"))
            {
                if (mailbox != null)
                {
                    Match match;
                    if ((match = Regex.Match(response, @"\d+(?=\s+EXISTS)")).Success)
                        mailbox.NumMsg = match.Value.ToInt();

                    else if ((match = Regex.Match(response, @"\d+(?=\s+RECENT)")).Success)
                        mailbox.NumNewMsg = match.Value.ToInt();

                    else if ((match = Regex.Match(response, @"(?<=UNSEEN\s+)\d+")).Success)
                        mailbox.NumUnSeen = match.Value.ToInt();

                    else if ((match = Regex.Match(response, @"(?<=\sFLAGS\s+\().*?(?=\))")).Success)
                        mailbox.SetFlags(match.Value);

                    else if ((match = Regex.Match(response, @"UIDVALIDITY (\d+)")).Success)
                        mailbox.UidValidity = match.Groups[1].Value.ToInt();


                    else if (response.StartsWith("* CAPABILITY "))
                    {
                        response = response.Substring(13);
                        _capability = response.Trim().Split(' ');
                    }

                    else if (response.StartsWith("* OK"))
                    {

                    }

                    else return response;
                }
                response = GetResponse();
            }

            return response;
        }

        public void SetFlags(Flags flags, params MailMessage[] msgs)
        {
            SetFlags(FlagsToFlagString(flags), msgs);
        }

        public void SetFlags(string flags, params MailMessage[] msgs)
        {
            Store("UID " + string.Join(" ", msgs.Select(x => x.Uid)), true, flags);
            foreach (var msg in msgs)
            {
                msg.SetFlags(flags);
            }
        }

        private string FlagsToFlagString(Flags flags)
        {
            return string.Join(" ", flags.ToString().Split(',').Select(x => "\\" + x.Trim()));
        }


        public void AddFlags(Flags flags, params MailMessage[] msgs)
        {
            AddFlags(FlagsToFlagString(flags), msgs);
        }

        public void AddFlags(string flags, params MailMessage[] msgs)
        {
            Store("UID " + string.Join(" ", msgs.Select(x => x.Uid)), false, flags);
            foreach (var msg in msgs)
            {
                msg.SetFlags(FlagsToFlagString(msg.Flags) + " " + flags);
            }
        }

        public void Store(string messageset, bool replace, string flags)
        {
            CheckMailboxSelected();
            IdlePause();
            string prefix = null;
            if (messageset.StartsWith("UID ", StringComparison.OrdinalIgnoreCase))
            {
                messageset = messageset.Substring(4);
                prefix = "UID ";
            }

            string command = string.Concat(GetTag(), prefix, "STORE ", messageset, " ", replace ? "" : "+", "FLAGS.SILENT (" + flags + ")");
            string response = SendCommandGetResponse(command);
            CheckResultOk(response);
            IdleResume();
        }

        public void SuscribeMailbox(string mailbox)
        {
            IdlePause();

            string command = GetTag() + "SUBSCRIBE " + _utf7.Encode(mailbox).QuoteString();
            SendCommandCheckOk(command);
            IdleResume();
        }

        public void UnSuscribeMailbox(string mailbox)
        {
            IdlePause();

            string command = GetTag() + "UNSUBSCRIBE " + _utf7.Encode(mailbox).QuoteString();
            SendCommandCheckOk(command);
            IdleResume();
        }

        internal override void CheckResultOk(string response)
        {
            if (!IsResultOk(response))
            {
                response = response.Substring(response.IndexOf(" ", StringComparison.CurrentCulture)).Trim();
                throw new Exception(response);
            }
        }

        internal bool IsResultOk(string response)
        {
            response = response.Substring(response.IndexOf(" ", StringComparison.CurrentCulture)).Trim();
            return response.ToUpper().StartsWith("OK");
        }

        private void OnRfc822Readed(EmailReadedEventArgs e)
        {
            var handler = Rfc822Readed;
            if (handler != null) handler(this, e);
        }
    }
}
