/*
    Copyright(c) 2015 Neodymium

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in
    all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
    THE SOFTWARE.
*/

using System;
using System.IO;

namespace RageLib.Data
{
    public delegate long GetOffsetDelegate();
    public delegate long GetLengthDelegate();
    public delegate void SetLengthDelegate(long length);

    public class PartialStream : Stream
    {
        private Stream baseStream;
        private GetOffsetDelegate getOffsetDelegate;
        private GetLengthDelegate getLengthDelegate;
        private SetLengthDelegate setLengthDelegate;
        private long relativePosiiton;

        public override bool CanSeek
        {
            get
            {
                return true;
            }
        }

        public override bool CanRead
        {
            get
            {
                return baseStream.CanRead;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return baseStream.CanWrite;
            }
        }

        public override long Length
        {
            get
            {
                return getLengthDelegate();
            }
        }

        public override long Position
        {
            get
            {
                return relativePosiiton;
            }
            set
            {
                if (Position > Length)
                    SetLength(Position);
                relativePosiiton = value;
            }
        }

        public PartialStream(
            Stream baseStream,
            GetOffsetDelegate getOffsetDelegate,
            GetLengthDelegate getLengthDelegate,
            SetLengthDelegate setLengthDelegate = null)
        {
            this.baseStream = baseStream;
            this.getOffsetDelegate = getOffsetDelegate;
            this.getLengthDelegate = getLengthDelegate;
            this.setLengthDelegate = setLengthDelegate;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var positionBackup = baseStream.Position;

            int maxCount = (int)(getLengthDelegate() - relativePosiiton);
            int newcount = Math.Min(count, maxCount);

            baseStream.Position = getOffsetDelegate() + relativePosiiton;
            int r = baseStream.Read(buffer, offset, newcount);
            relativePosiiton += r;

            baseStream.Position = positionBackup;

            return r;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var positionBackup = baseStream.Position;

            var newlen = relativePosiiton + count;
            if (newlen > Length)
                setLengthDelegate(newlen);

            int maxCount = (int)(getLengthDelegate() - relativePosiiton);
            var newcount = Math.Min(count, maxCount);

            baseStream.Position = getOffsetDelegate() + relativePosiiton;
            baseStream.Write(buffer, offset, count);
            relativePosiiton += count;

            baseStream.Position = positionBackup;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin:
                    {
                        relativePosiiton = offset;
                        break;
                    }
                case SeekOrigin.Current:
                    {
                        relativePosiiton += offset;
                        break;
                    }
                case SeekOrigin.End:
                    {
                        relativePosiiton = getLengthDelegate() + offset;
                        break;
                    }
            }

            return relativePosiiton;
        }

        public override void SetLength(long value)
        {
            setLengthDelegate(value);
        }

        public override void Flush()
        {
            baseStream.Flush();
        }
    }
}
