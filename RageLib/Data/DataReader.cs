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
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RageLib.Data
{
    public class DataReader
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

        public DataReader(Stream stream, Endianess endianess = Endianess.LittleEndian)
        {
            this.baseStream = stream;
            this.Endianess = endianess;
        }

        protected virtual byte[] ReadFromStream(int count, bool ignoreEndianess = false)
        {
            var buffer = new byte[count];
            baseStream.Read(buffer, 0, count);

            if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
            {
                Array.Reverse(buffer);
            }

            return buffer;
        }

        public byte ReadByte()
        {
            return ReadFromStream(1)[0];
        }

        public byte[] ReadBytes(int count)
        {
            return ReadFromStream(count, true);
        }

        public short ReadInt16()
        {
            return BitConverter.ToInt16(ReadFromStream(2), 0);
        }

        public int ReadInt32()
        {
            return BitConverter.ToInt32(ReadFromStream(4), 0);
        }

        public long ReadInt64()
        {
            return BitConverter.ToInt64(ReadFromStream(8), 0);
        }

        public ushort ReadUInt16()
        {
            return BitConverter.ToUInt16(ReadFromStream(2), 0);
        }

        public uint ReadUInt32()
        {
            return BitConverter.ToUInt32(ReadFromStream(4), 0);
        }

        public ulong ReadUInt64()
        {
            return BitConverter.ToUInt64(ReadFromStream(8), 0);
        }

        public float ReadSingle()
        {
            return BitConverter.ToSingle(ReadFromStream(4), 0);
        }

        public double ReadDouble()
        {
            return BitConverter.ToDouble(ReadFromStream(8), 0);
        }

        public string ReadString()
        {
            var bytes = new List<byte>();
            var temp = ReadFromStream(1)[0];
            while (temp != 0)
            {
                bytes.Add(temp);
                temp = ReadFromStream(1)[0];
            }

            return Encoding.UTF8.GetString(bytes.ToArray());
        }
    }
}
