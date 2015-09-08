using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections;
using System.Collections.Specialized;
using System.Runtime.Serialization;
using System.Collections.ObjectModel;

namespace AE.Net.Mail.Imap {
    public class Namespaces {
        private readonly Collection<Namespace> _servernamespace = new Collection<Namespace>();
        private readonly Collection<Namespace> _usernamespace = new Collection<Namespace>();
        private readonly Collection<Namespace> _sharednamespace = new Collection<Namespace>();

        public virtual Collection<Namespace> ServerNamespace {
            get { return _servernamespace; }
        }
        public virtual Collection<Namespace> UserNamespace {
            get { return _usernamespace; }
        }
        public virtual Collection<Namespace> SharedNamespace {
            get { return _sharednamespace; }
        }
    }

    public sealed class Namespace {
        public Namespace(string prefix, string delimiter) {
            Prefix = prefix;
            Delimiter = delimiter;
        }
        public Namespace() { }
        public string Prefix { get; internal set; }
        public string Delimiter { get; internal set; }
    }
}