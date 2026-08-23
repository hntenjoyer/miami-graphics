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
    public class DataWriter
    {
        private Stream baseStream;

        public Endianess Endianess
        {
            get;
            set;
        }

        public virtual long Length
        {
            get
            {
                return baseStream.Length;
            }
        }

        public virtual long Position
        {
            get
            {
                return baseStream.Position;
            }
            set
            {
                baseStream.Position = value;
            }
        }

        public DataWriter(Stream stream, Endianess endianess = Endianess.LittleEndian)
        {
            this.baseStream = stream;
            this.Endianess = endianess;
        }

        protected virtual void WriteToStream(byte[] value, bool ignoreEndianess = false)
        {
            if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
            {
                var buffer = (byte[])value.Clone();
                Array.Reverse(buffer);
                baseStream.Write(buffer, 0, buffer.Length);
            }
            else
            {
                baseStream.Write(value, 0, value.Length);
            }
        }

        public void Write(byte value)
        {
            WriteToStream(new byte[] { value });
        }

        public void Write(byte[] value)
        {
            WriteToStream(value, true);
        }

        public void Write(short value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(int value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(long value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(ushort value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(uint value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(ulong value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(float value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(double value)
        {
            WriteToStream(BitConverter.GetBytes(value));
        }

        public void Write(string value)
        {
            foreach (var c in value)
                Write((byte)c);
            Write((byte)0);
        }
    }
}
