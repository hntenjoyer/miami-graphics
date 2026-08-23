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

namespace RageLib.Resources.GTA5.PC.Drawables
{
    public class GtaDrawableDictionary : FileBase64_GTA5_pc
    {
        public override long Length => 0x40;

        public uint Unknown_10h;
        public uint Unknown_14h;
        public uint Unknown_18h;
        public uint Unknown_1Ch;
        public ulong HashesPointer;
        public ushort HashesCount1;
        public ushort HashesCount2;
        public uint Unknown_2Ch;
        public ulong DrawablesPointer;
        public ushort DrawablesCount1;
        public ushort DrawablesCount2;
        public uint Unknown_3Ch;

        public ResourceSimpleArray<uint_r> Hashes;
        public ResourcePointerArray64<GtaDrawable> Drawables;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.HashesPointer = reader.ReadUInt64();
            this.HashesCount1 = reader.ReadUInt16();
            this.HashesCount2 = reader.ReadUInt16();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.DrawablesPointer = reader.ReadUInt64();
            this.DrawablesCount1 = reader.ReadUInt16();
            this.DrawablesCount2 = reader.ReadUInt16();
            this.Unknown_3Ch = reader.ReadUInt32();

            this.Hashes = reader.ReadBlockAt<ResourceSimpleArray<uint_r>>(
                this.HashesPointer,
                this.HashesCount1
            );
            this.Drawables = reader.ReadBlockAt<ResourcePointerArray64<GtaDrawable>>(
                this.DrawablesPointer,
                this.DrawablesCount1
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.HashesPointer = (ulong)(this.Hashes != null ? this.Hashes.Position : 0);
            this.DrawablesPointer = (ulong)(this.Drawables != null ? this.Drawables.Position : 0);

            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.HashesPointer);
            writer.Write(this.HashesCount1);
            writer.Write(this.HashesCount2);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.DrawablesPointer);
            writer.Write(this.DrawablesCount1);
            writer.Write(this.DrawablesCount2);
            writer.Write(this.Unknown_3Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Hashes != null) list.Add(Hashes);
            if (Drawables != null) list.Add(Drawables);
            return list.ToArray();
        }
    }
}
