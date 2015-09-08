using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace AE.Net.Mail {
	public class SafeDictionary<TKey, TValue> : Dictionary<TKey, TValue> {
		public SafeDictionary() { }
		public SafeDictionary(IEqualityComparer<TKey> comparer) : base(comparer) { }

		public virtual new TValue this[TKey key] {
			get {
				return this.Get(key);
			}
			set {
				this.Set(key, value);
			}
		}
	}
}
