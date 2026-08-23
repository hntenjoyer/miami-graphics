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
using RageLib.Resources.GTA5.PC.Bounds;
using RageLib.Resources.GTA5.PC.Drawables;
using System.Collections.Generic;

namespace RageLib.Resources.GTA5.PC.Fragments
{
    public class FragDrawable : Drawable
    {
        public override long Length => 0x150;

        public uint Unknown_A8h;
        public uint Unknown_ACh;
        public RAGE_Matrix4 Unknown_B0h;
        public ulong BoundPointer;
        public ulong Unknown_F8h_Pointer;
        public ushort Count1;
        public ushort Count2;
        public uint Unknown_104h;
        public ulong Unknown_108h_Pointer;
        public ushort Count3;
        public ushort Count4;
        public uint Unknown_114h;
        public uint Unknown_118h;
        public uint Unknown_11Ch;
        public uint Unknown_120h;
        public uint Unknown_124h;
        public uint Unknown_128h;
        public uint Unknown_12Ch;
        public ulong NamePointer;
        public uint Unknown_138h;
        public uint Unknown_13Ch;
        public uint Unknown_140h;
        public uint Unknown_144h;
        public uint Unknown_148h;
        public uint Unknown_14Ch;

        public Bound Bound;
        public ResourceSimpleArray<ulong_r> Unknown_F8h_Data;
        public ResourceSimpleArray<RAGE_Matrix4> Unknown_108h_Data;
        public string_r Name;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Unknown_A8h = reader.ReadUInt32();
            this.Unknown_ACh = reader.ReadUInt32();
            this.Unknown_B0h = reader.ReadBlock<RAGE_Matrix4>();
            this.BoundPointer = reader.ReadUInt64();
            this.Unknown_F8h_Pointer = reader.ReadUInt64();
            this.Count1 = reader.ReadUInt16();
            this.Count2 = reader.ReadUInt16();
            this.Unknown_104h = reader.ReadUInt32();
            this.Unknown_108h_Pointer = reader.ReadUInt64();
            this.Count3 = reader.ReadUInt16();
            this.Count4 = reader.ReadUInt16();
            this.Unknown_114h = reader.ReadUInt32();
            this.Unknown_118h = reader.ReadUInt32();
            this.Unknown_11Ch = reader.ReadUInt32();
            this.Unknown_120h = reader.ReadUInt32();
            this.Unknown_124h = reader.ReadUInt32();
            this.Unknown_128h = reader.ReadUInt32();
            this.Unknown_12Ch = reader.ReadUInt32();
            this.NamePointer = reader.ReadUInt64();
            this.Unknown_138h = reader.ReadUInt32();
            this.Unknown_13Ch = reader.ReadUInt32();
            this.Unknown_140h = reader.ReadUInt32();
            this.Unknown_144h = reader.ReadUInt32();
            this.Unknown_148h = reader.ReadUInt32();
            this.Unknown_14Ch = reader.ReadUInt32();

            this.Bound = reader.ReadBlockAt<Bound>(
                this.BoundPointer
            );
            this.Unknown_F8h_Data = reader.ReadBlockAt<ResourceSimpleArray<ulong_r>>(
                this.Unknown_F8h_Pointer,
                this.Count1
            );
            this.Unknown_108h_Data = reader.ReadBlockAt<ResourceSimpleArray<RAGE_Matrix4>>(
                this.Unknown_108h_Pointer,
                this.Count2
            );
            this.Name = reader.ReadBlockAt<string_r>(
                this.NamePointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.BoundPointer = (ulong)(this.Bound != null ? this.Bound.Position : 0);
            this.Unknown_F8h_Pointer = (ulong)(this.Unknown_F8h_Data != null ? this.Unknown_F8h_Data.Position : 0);
            this.Unknown_108h_Pointer = (ulong)(this.Unknown_108h_Data != null ? this.Unknown_108h_Data.Position : 0);
            this.NamePointer = (ulong)(this.Name != null ? this.Name.Position : 0);

            writer.Write(this.Unknown_A8h);
            writer.Write(this.Unknown_ACh);
            writer.WriteBlock(this.Unknown_B0h);
            writer.Write(this.BoundPointer);
            writer.Write(this.Unknown_F8h_Pointer);
            writer.Write(this.Count1);
            writer.Write(this.Count2);
            writer.Write(this.Unknown_104h);
            writer.Write(this.Unknown_108h_Pointer);
            writer.Write(this.Count3);
            writer.Write(this.Count4);
            writer.Write(this.Unknown_114h);
            writer.Write(this.Unknown_118h);
            writer.Write(this.Unknown_11Ch);
            writer.Write(this.Unknown_120h);
            writer.Write(this.Unknown_124h);
            writer.Write(this.Unknown_128h);
            writer.Write(this.Unknown_12Ch);
            writer.Write(this.NamePointer);
            writer.Write(this.Unknown_138h);
            writer.Write(this.Unknown_13Ch);
            writer.Write(this.Unknown_140h);
            writer.Write(this.Unknown_144h);
            writer.Write(this.Unknown_148h);
            writer.Write(this.Unknown_14Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Bound != null) list.Add(Bound);
            if (Unknown_F8h_Data != null) list.Add(Unknown_F8h_Data);
            if (Unknown_108h_Data != null) list.Add(Unknown_108h_Data);
            if (Name != null) list.Add(Name);
            return list.ToArray();
        }
    }
}
