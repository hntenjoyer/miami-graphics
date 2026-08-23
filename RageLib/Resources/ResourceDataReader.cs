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
using System.Collections.Generic;
using System.IO;

namespace RageLib.Resources
{
    public class ResourceDataReader : DataReader
    {
        private const long SYSTEM_BASE = 0x50000000;
        private const long GRAPHICS_BASE = 0x60000000;

        private Stream systemStream;
        private Stream graphicsStream;

        private Dictionary<long, List<IResourceBlock>> blockPool;

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

        public ResourceDataReader(Stream systemStream, Stream graphicsStream, Endianess endianess = Endianess.LittleEndian)
            : base((Stream)null, endianess)
        {
            this.systemStream = systemStream;
            this.graphicsStream = graphicsStream;
            this.blockPool = new Dictionary<long, List<IResourceBlock>>();
        }

        protected override byte[] ReadFromStream(int count, bool ignoreEndianess = false)
        {
            if ((Position & SYSTEM_BASE) == SYSTEM_BASE)
            {

                systemStream.Position = Position & ~0x50000000;

                var buffer = new byte[count];
                systemStream.Read(buffer, 0, count);

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    Array.Reverse(buffer);
                }

                Position = systemStream.Position | 0x50000000;
                return buffer;

            }
            if ((Position & GRAPHICS_BASE) == GRAPHICS_BASE)
            {

                graphicsStream.Position = Position & ~0x60000000;

                var buffer = new byte[count];
                graphicsStream.Read(buffer, 0, count);

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    Array.Reverse(buffer);
                }

                Position = graphicsStream.Position | 0x60000000;
                return buffer;
            }
            throw new Exception("illegal position!");
        }

        public T ReadBlock<T>(params object[] parameters) where T : IResourceBlock, new()
        {
            if (blockPool.ContainsKey(Position))
            {
                var blocks = blockPool[Position];
                foreach (var block in blocks)
                    if (block is T)
                    {
                        Position += block.Length;

                        return (T)block;
                    }
            }

            var result = new T();

            if (result is IResourceXXSystemBlock)
                result = (T)((IResourceXXSystemBlock)result).GetType(this, parameters);

            if (blockPool.ContainsKey(Position))
            {
                blockPool[Position].Add(result);
            }
            else
            {
                var blocks = new List<IResourceBlock>();
                blocks.Add(result);
                blockPool.Add(Position, blocks);
            }

            var classPosition = Position;
            result.Read(this, parameters);
            result.Position = classPosition;
            return (T)result;
        }

        public T ReadBlockAt<T>(ulong position, params object[] parameters) where T : IResourceBlock, new()
        {
            if (position != 0)
            {
                var positionBackup = Position;

                Position = (long)position;
                var result = ReadBlock<T>(parameters);
                Position = positionBackup;

                return result;
            }
            else
            {
                return default(T);
            }
        }
    }
}
