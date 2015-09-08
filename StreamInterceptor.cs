using System;
using System.IO;
using ADODB;
using Stream = System.IO.Stream;

namespace AE.Net.Mail
{
    public class StreamInterceptor : IDisposable
    {
        private readonly MemoryStream _memoryStream;
        private readonly bool _firstLf;
        private readonly bool _lastCr;

        public StreamInterceptor(Stream stream, int size)
        {
            _memoryStream = new MemoryStream();
            byte[] bytes = stream.Read(size);
            _firstLf = bytes.Length > 0 && bytes[0] == '\n';
            _lastCr = bytes.Length > 0 && bytes[bytes.Length-1] == '\r';
            _memoryStream.Write(bytes,0, bytes.Length);
            _memoryStream.Position = 0;
        }

        public MemoryStream Stream
        {
            get { return _memoryStream; }
        }

        public void SaveTo(Stream stream)
        {
            long savedPos = _memoryStream.Position;
            _memoryStream.Position = 0;
            if (_firstLf) _memoryStream.Position = 1;
            _memoryStream.CopyTo(stream);
            if (_lastCr) stream.Write(new byte[]{(byte) '\n'},0,1);
            _memoryStream.Position = savedPos;
        }

        public void SaveTo(string fileName)
        {
            using (var fs = new FileStream(fileName, FileMode.Create))
            {
                SaveTo(fs);
            }
        }

        public void Dispose()
        {
            _memoryStream.Dispose();
        }
    }
}