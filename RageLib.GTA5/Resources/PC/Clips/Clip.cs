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
using System;

namespace RageLib.Resources.GTA5.PC.Clips
{
    public class Clip : ResourceSystemBlock, IResourceXXSystemBlock
    {
        public override long Length => 0x70;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public uint Unknown_10h;
        public uint Unknown_14h;
        public ulong NamePointer;
        public ushort NameLength1;
        public ushort NameLength2;
        public uint Unknown_24h;
        public uint Unknown_28h;
        public uint Unknown_2Ch;
        public uint Unknown_30h;
        public uint Unknown_34h;
        public ulong TagsPointer;
        public ulong PropertiesPointer;
        public uint Unknown_48h;
        public uint Unknown_4Ch;

        public string_r Name;
        public Tags Tags;
        public PropertyMap Properties;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.NamePointer = reader.ReadUInt64();
            this.NameLength1 = reader.ReadUInt16();
            this.NameLength2 = reader.ReadUInt16();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.TagsPointer = reader.ReadUInt64();
            this.PropertiesPointer = reader.ReadUInt64();
            this.Unknown_48h = reader.ReadUInt32();
            this.Unknown_4Ch = reader.ReadUInt32();

            this.Name = reader.ReadBlockAt<string_r>(
                this.NamePointer
            );
            this.Tags = reader.ReadBlockAt<Tags>(
                this.TagsPointer
            );
            this.Properties = reader.ReadBlockAt<PropertyMap>(
                this.PropertiesPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.NamePointer = (ulong)(this.Name != null ? this.Name.Position : 0);
            this.NameLength1 = (ushort)(this.Name != null ? this.Name.Value.Length : 0);
            this.NameLength2 = (ushort)(this.Name != null ? this.Name.Value.Length + 1 : 0);
            this.TagsPointer = (ulong)(this.Tags != null ? this.Tags.Position : 0);
            this.PropertiesPointer = (ulong)(this.Properties != null ? this.Properties.Position : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.NamePointer);
            writer.Write(this.NameLength1);
            writer.Write(this.NameLength2);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.Unknown_30h);
            writer.Write(this.Unknown_34h);
            writer.Write(this.TagsPointer);
            writer.Write(this.PropertiesPointer);
            writer.Write(this.Unknown_48h);
            writer.Write(this.Unknown_4Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Name != null) list.Add(Name);
            if (Tags != null) list.Add(Tags);
            if (Properties != null) list.Add(Properties);
            return list.ToArray();
        }

        public IResourceSystemBlock GetType(ResourceDataReader reader, params object[] parameters)
        {
            reader.Position += 16;
            var type = reader.ReadByte();
            reader.Position -= 17;

            switch (type)
            {
                case 1: return new ClipAnimation();
                case 2: return new ClipAnimations();
                default: throw new Exception("Unknown clip type");
            }
        }
    }
}
