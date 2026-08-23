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

using System.Collections.Generic;

namespace RageLib.Resources.GTA5.PC.Navigations
{
    public class Sector : ResourceSystemBlock
    {
        public override long Length => 0x60;

        public float Unknown_0h;
        public float Unknown_4h;
        public float Unknown_8h;
        public float Unknown_Ch;
        public float Unknown_10h;
        public float Unknown_14h;
        public float Unknown_18h;
        public float Unknown_1Ch;
        public uint Unknown_20h;
        public uint Unknown_24h;
        public uint Unknown_28h;
        public ulong DataPointer;
        public ulong SubTree1Pointer;
        public ulong SubTree2Pointer;
        public ulong SubTree3Pointer;
        public ulong SubTree4Pointer;
        public uint Unknown_54h;
        public uint Unknown_58h;
        public uint Unknown_5Ch;

        public SectorData Data;
        public Sector SubTree1;
        public Sector SubTree2;
        public Sector SubTree3;
        public Sector SubTree4;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Unknown_0h = reader.ReadSingle();
            this.Unknown_4h = reader.ReadSingle();
            this.Unknown_8h = reader.ReadSingle();
            this.Unknown_Ch = reader.ReadSingle();
            this.Unknown_10h = reader.ReadSingle();
            this.Unknown_14h = reader.ReadSingle();
            this.Unknown_18h = reader.ReadSingle();
            this.Unknown_1Ch = reader.ReadSingle();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.DataPointer = reader.ReadUInt64();
            this.SubTree1Pointer = reader.ReadUInt64();
            this.SubTree2Pointer = reader.ReadUInt64();
            this.SubTree3Pointer = reader.ReadUInt64();
            this.SubTree4Pointer = reader.ReadUInt64();
            this.Unknown_54h = reader.ReadUInt32();
            this.Unknown_58h = reader.ReadUInt32();
            this.Unknown_5Ch = reader.ReadUInt32();

            this.Data = reader.ReadBlockAt<SectorData>(
                this.DataPointer
            );
            this.SubTree1 = reader.ReadBlockAt<Sector>(
                this.SubTree1Pointer
            );
            this.SubTree2 = reader.ReadBlockAt<Sector>(
                this.SubTree2Pointer
            );
            this.SubTree3 = reader.ReadBlockAt<Sector>(
                this.SubTree3Pointer
            );
            this.SubTree4 = reader.ReadBlockAt<Sector>(
                this.SubTree4Pointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.DataPointer = (ulong)(this.Data != null ? this.Data.Position : 0);
            this.SubTree1Pointer = (ulong)(this.SubTree1 != null ? this.SubTree1.Position : 0);
            this.SubTree2Pointer = (ulong)(this.SubTree2 != null ? this.SubTree2.Position : 0);
            this.SubTree3Pointer = (ulong)(this.SubTree3 != null ? this.SubTree3.Position : 0);
            this.SubTree4Pointer = (ulong)(this.SubTree4 != null ? this.SubTree4.Position : 0);

            writer.Write(this.Unknown_0h);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.DataPointer);
            writer.Write(this.SubTree1Pointer);
            writer.Write(this.SubTree2Pointer);
            writer.Write(this.SubTree3Pointer);
            writer.Write(this.SubTree4Pointer);
            writer.Write(this.Unknown_54h);
            writer.Write(this.Unknown_58h);
            writer.Write(this.Unknown_5Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Data != null) list.Add(Data);
            if (SubTree1 != null) list.Add(SubTree1);
            if (SubTree2 != null) list.Add(SubTree2);
            if (SubTree3 != null) list.Add(SubTree3);
            if (SubTree4 != null) list.Add(SubTree4);
            return list.ToArray();
        }
    }
}
