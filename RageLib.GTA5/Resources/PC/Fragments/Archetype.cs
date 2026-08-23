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
using System.Collections.Generic;

namespace RageLib.Resources.GTA5.PC.Fragments
{
    public class Archetype : ResourceSystemBlock
    {
        public override long Length => 0xE0;

        public float Unknown_0h;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public uint Unknown_10h;
        public uint Unknown_14h;
        public ulong NamePointer;
        public ulong BoundPointer;
        public uint Unknown_28h;
        public uint Unknown_2Ch;
        public uint Unknown_30h;
        public uint Unknown_34h;
        public uint Unknown_38h;
        public uint Unknown_3Ch;
        public float Unknown_40h;
        public float Unknown_44h;
        public float Unknown_48h;
        public float Unknown_4Ch;
        public float Unknown_50h;
        public float Unknown_54h;
        public uint Unknown_58h;
        public uint Unknown_5Ch;
        public RAGE_Vector4 Unknown_60h;
        public RAGE_Vector4 Unknown_70h;
        public RAGE_Vector4 Unknown_80h;
        public RAGE_Vector4 Unknown_90h;
        public RAGE_Vector4 Unknown_A0h;
        public RAGE_Vector4 Unknown_B0h;
        public RAGE_Vector4 Unknown_C0h;
        public RAGE_Vector4 Unknown_D0h;

        public string_r Name;
        public Bound Bound;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Unknown_0h = reader.ReadSingle();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.NamePointer = reader.ReadUInt64();
            this.BoundPointer = reader.ReadUInt64();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();
            this.Unknown_40h = reader.ReadSingle();
            this.Unknown_44h = reader.ReadSingle();
            this.Unknown_48h = reader.ReadSingle();
            this.Unknown_4Ch = reader.ReadSingle();
            this.Unknown_50h = reader.ReadSingle();
            this.Unknown_54h = reader.ReadSingle();
            this.Unknown_58h = reader.ReadUInt32();
            this.Unknown_5Ch = reader.ReadUInt32();
            this.Unknown_60h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_70h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_80h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_90h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_A0h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_B0h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_C0h = reader.ReadBlock<RAGE_Vector4>();
            this.Unknown_D0h = reader.ReadBlock<RAGE_Vector4>();

            this.Name = reader.ReadBlockAt<string_r>(
                this.NamePointer
            );
            this.Bound = reader.ReadBlockAt<Bound>(
                this.BoundPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.NamePointer = (ulong)(this.Name != null ? this.Name.Position : 0);
            this.BoundPointer = (ulong)(this.Bound != null ? this.Bound.Position : 0);

            writer.Write(this.Unknown_0h);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.NamePointer);
            writer.Write(this.BoundPointer);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.Unknown_30h);
            writer.Write(this.Unknown_34h);
            writer.Write(this.Unknown_38h);
            writer.Write(this.Unknown_3Ch);
            writer.Write(this.Unknown_40h);
            writer.Write(this.Unknown_44h);
            writer.Write(this.Unknown_48h);
            writer.Write(this.Unknown_4Ch);
            writer.Write(this.Unknown_50h);
            writer.Write(this.Unknown_54h);
            writer.Write(this.Unknown_58h);
            writer.Write(this.Unknown_5Ch);
            writer.WriteBlock(this.Unknown_60h);
            writer.WriteBlock(this.Unknown_70h);
            writer.WriteBlock(this.Unknown_80h);
            writer.WriteBlock(this.Unknown_90h);
            writer.WriteBlock(this.Unknown_A0h);
            writer.WriteBlock(this.Unknown_B0h);
            writer.WriteBlock(this.Unknown_C0h);
            writer.WriteBlock(this.Unknown_D0h);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Name != null) list.Add(Name);
            if (Bound != null) list.Add(Bound);
            return list.ToArray();
        }
    }
}
