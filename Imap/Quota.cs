using System;
using System.Collections;

namespace AE.Net.Mail.Imap {
    public class Quota {
        private string _resource;
        private string _usage;
        private readonly int _used;
        private readonly int _max;
        public Quota(string resourceName, string usage, int used, int max) {
            _resource = resourceName;
            _usage = usage;
            _used = used;
            _max = max;
        }
        public virtual int Used {
            get { return _used; }
        }
        public virtual int Max {
            get { return _max; }
        }
    }
}