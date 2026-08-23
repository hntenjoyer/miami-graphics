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

namespace RageLib.Resources.GTA5.PC.Navigations
{
    public class PolysListPart : ResourceSystemBlock
    {
        public override long Length => 0x10;

        public ulong PolysPointer;
        public uint PolysCount;
        public uint Unknown_Ch;

        public ResourceSimpleArray<Poly> Polys;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.PolysPointer = reader.ReadUInt64();
            this.PolysCount = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();

            this.Polys = reader.ReadBlockAt<ResourceSimpleArray<Poly>>(
                this.PolysPointer,
                this.PolysCount
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.PolysPointer = (ulong)(this.Polys != null ? this.Polys.Position : 0);
            this.PolysCount = (uint)(this.Polys != null ? this.Polys.Count : 0);

            writer.Write(this.PolysPointer);
            writer.Write(this.PolysCount);
            writer.Write(this.Unknown_Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Polys != null) list.Add(Polys);
            return list.ToArray();
        }
    }
}
