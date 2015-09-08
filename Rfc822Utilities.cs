using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Reflection;
using System.Text;

namespace AE.Net.Mail
{
    public static class Rfc822Utilities
    {
        private static readonly bool UseQuotedEncoding = true;
        private static MethodInfo _encodeHeaderValueMethod;

        private static MethodInfo EncodeHeaderValueMethod
        {
            get
            {
                if (_encodeHeaderValueMethod == null)
                {
                    //System.Net.Mail.Attachment
                    Type type = typeof (System.Net.Mime.ContentType);
                    Assembly assembly = type.Assembly;
                    Type mimeBasePartType = assembly.GetType("System.Net.Mime.MimeBasePart");
                    Console.WriteLine(mimeBasePartType);
                    //internal static string EncodeHeaderValue(string value, Encoding encoding, bool base64Encoding, int headerLength)
                    _encodeHeaderValueMethod = mimeBasePartType.GetMethod("EncodeHeaderValue", BindingFlags.Static | BindingFlags.NonPublic, null,
                        new[] {typeof (string), typeof (Encoding), typeof (bool), typeof (int)}, null);
                }
                return _encodeHeaderValueMethod;
            }
        }

        public static string ToQuotedPrintable(this string str, int headerLength = 0)
        {
            /*
            Type type = typeof (System.Net.Mime.ContentType);
            Assembly assembly = type.Assembly;
            Type mimeBasePartType = assembly.GetType("System.Net.Mime.MimeBasePart");
            Console.WriteLine(mimeBasePartType);
            //internal static string EncodeHeaderValue(string value, Encoding encoding, bool base64Encoding, int headerLength)
            MethodInfo method = mimeBasePartType.GetMethod("EncodeHeaderValue", BindingFlags.Static | BindingFlags.NonPublic, null,
                new Type[] {typeof (string), typeof (Encoding), typeof (bool), typeof (int)}, null);
            Console.WriteLine(method);
            object result = method.Invoke(null, new object[] {"X-FUROGEP: \"Nem esik messze az árvíztûrõ tükörfúrógép a fájától.\" 1 alma", null, false, 20});
            Encoding.ASCII.GetBytes()
            Console.WriteLine(result);
            Console.WriteLine(result.GetType());
            */
            if (str == null) return null;
            return EncodeHeaderValueMethod.Invoke(null, new object[] {str, null, false, headerLength}) as string;
        }

        public static string ToRfc822Header(this string value, string headerName)
        {
            if (headerName == null) throw new ArgumentNullException("headerName");
            if (headerName == string.Empty) throw new ArgumentException("headerName can not be empty!");
            string header = string.Format("{0}: ", headerName);
            return !UseQuotedEncoding ? value.ToRawHeader(header) : string.Format("{0}{1}", header, value.ToQuotedPrintable(header.Length));
        }

        public static string ToRawHeader(this string value, string headerName)
        {
            if (headerName == null) throw new ArgumentNullException("headerName");
            if (headerName == string.Empty) throw new ArgumentException("headerName can not be empty!");
            return string.Format("{0}: {1}", headerName, value);
        }

        public static string ToQuotedPrintable(this MailAddress address)
        {
            if (!UseQuotedEncoding) return address.ToString();
            if (String.IsNullOrEmpty(address.DisplayName)) {
                return address.Address;
            }
            else {
                return String.Format("\"{0}\" {1}", address.DisplayName.ToQuotedPrintable(0), String.Format(CultureInfo.InvariantCulture, "<{0}>", address.Address));
            } 
        }

        public static string ToRfc822Header(this MailAddress value, string headerName)
        {
            return value.ToQuotedPrintable().ToRawHeader(headerName);
        }

        public static string ToRfc822Header(this IEnumerable<MailAddress> addresses, string headerName)
        {
            return string.Join("; ", addresses.Select(x => x.ToQuotedPrintable())).ToRawHeader(headerName);
            //return string.Join("; ", addresses.Select(x => x.ToString())).ToRfc822Header(headerName);
        }
    }
}