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

using RageLib.Data;
using System;
using System.IO;

namespace RageLib.Resources
{
    public class ResourceDataWriter : DataWriter
    {
        private const long SYSTEM_BASE = 0x50000000;
        private const long GRAPHICS_BASE = 0x60000000;

        private Stream systemStream;
        private Stream graphicsStream;

        public override long Length
        {
            get
            {
                return -1;
            }
        }

        public override long Position
        {
            get;
            set;
        }

        public ResourceDataWriter(Stream systemStream, Stream graphicsStream, Endianess endianess = Endianess.LittleEndian)
            : base((Stream)null, endianess)
        {
            this.systemStream = systemStream;
            this.graphicsStream = graphicsStream;
        }

        protected override void WriteToStream(byte[] value, bool ignoreEndianess = true)
        {
            if ((Position & SYSTEM_BASE) == SYSTEM_BASE)
            {

                systemStream.Position = Position & ~SYSTEM_BASE;

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    var buf = (byte[])value.Clone();
                    Array.Reverse(buf);
                    systemStream.Write(buf, 0, buf.Length);
                }
                else
                {
                    systemStream.Write(value, 0, value.Length);
                }

                Position = systemStream.Position | 0x50000000;
                return;

            }
            if ((Position & GRAPHICS_BASE) == GRAPHICS_BASE)
            {

                graphicsStream.Position = Position & ~GRAPHICS_BASE;

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    var buf = (byte[])value.Clone();
                    Array.Reverse(buf);
                    graphicsStream.Write(buf, 0, buf.Length);
                }
                else
                {
                    graphicsStream.Write(value, 0, value.Length);
                }

                Position = graphicsStream.Position | 0x60000000;
                return;
            }

            throw new Exception("illegal position!");
        }

        public void WriteBlock(IResourceBlock value)
        {
            value.Write(this);
        }
    }
}
