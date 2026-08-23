/*
    Copyright(c) 2017 Neodymium

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

using RageLib.Resources.Common;
using System.Collections.Generic;

namespace RageLib.Resources.GTA5.PC.Meta
{
    public class DataBlock : ResourceSystemBlock
    {
        public override long Length => 0x10;

        public int StructureNameHash { get; set; }
        public int DataLength { get; private set; }
        public long DataPointer { get; private set; }

        public ResourceSimpleArray<byte_r> Data { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.StructureNameHash = reader.ReadInt32();
            this.DataLength = reader.ReadInt32();
            this.DataPointer = reader.ReadInt64();

            this.Data = reader.ReadBlockAt<ResourceSimpleArray<byte_r>>(
                (ulong)this.DataPointer,
                this.DataLength
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.DataLength = this.Data?.Count ?? 0;
            this.DataPointer = this.Data?.Position ?? 0;

            writer.Write(this.StructureNameHash);
            writer.Write(this.DataLength);
            writer.Write(this.DataPointer);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Data != null) list.Add(Data);
            return list.ToArray();
        }
    }
}
