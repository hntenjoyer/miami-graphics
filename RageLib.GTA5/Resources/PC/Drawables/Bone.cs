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
    public class Bone : ResourceSystemBlock
    {
        public override long Length => 0x50;

        public float RotationX;
        public float RotationY;
        public float RotationZ;
        public float RotationW;
        public float TranslationX;
        public float TranslationY;
        public float TranslationZ;
        public uint Unknown_1Ch;
        public float Unknown_20h;
        public float Unknown_24h;
        public float Unknown_28h;
        public float Unknown_2Ch;
        public uint Unknown_30h;
        public uint Unknown_34h;
        public ulong NamePointer;
        public ushort Unknown_40h;
        public ushort Unknown_42h;
        public ushort Id;
        public ushort Unknown_46h;
        public uint Unknown_48h;
        public uint Unknown_4Ch;

        public string_r Name;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.RotationX = reader.ReadSingle();
            this.RotationY = reader.ReadSingle();
            this.RotationZ = reader.ReadSingle();
            this.RotationW = reader.ReadSingle();
            this.TranslationX = reader.ReadSingle();
            this.TranslationY = reader.ReadSingle();
            this.TranslationZ = reader.ReadSingle();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.Unknown_20h = reader.ReadSingle();
            this.Unknown_24h = reader.ReadSingle();
            this.Unknown_28h = reader.ReadSingle();
            this.Unknown_2Ch = reader.ReadSingle();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.NamePointer = reader.ReadUInt64();
            this.Unknown_40h = reader.ReadUInt16();
            this.Unknown_42h = reader.ReadUInt16();
            this.Id = reader.ReadUInt16();
            this.Unknown_46h = reader.ReadUInt16();
            this.Unknown_48h = reader.ReadUInt32();
            this.Unknown_4Ch = reader.ReadUInt32();

            this.Name = reader.ReadBlockAt<string_r>(
                this.NamePointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.NamePointer = (ulong)(this.Name != null ? this.Name.Position : 0);

            writer.Write(this.RotationX);
            writer.Write(this.RotationY);
            writer.Write(this.RotationZ);
            writer.Write(this.RotationW);
            writer.Write(this.TranslationX);
            writer.Write(this.TranslationY);
            writer.Write(this.TranslationZ);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.Unknown_30h);
            writer.Write(this.Unknown_34h);
            writer.Write(this.NamePointer);
            writer.Write(this.Unknown_40h);
            writer.Write(this.Unknown_42h);
            writer.Write(this.Id);
            writer.Write(this.Unknown_46h);
            writer.Write(this.Unknown_48h);
            writer.Write(this.Unknown_4Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Name != null) list.Add(Name);
            return list.ToArray();
        }
    }
}
