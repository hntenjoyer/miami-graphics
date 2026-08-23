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

namespace RageLib.Resources.GTA5.PC.Drawables
{
    public class GtaDrawable : Drawable
    {
        public override long Length => 0xD0;

        public ulong NamePointer;
        public ulong LightAttributesPointer;
        public ushort LightAttributesCount1;
        public ushort LightAttributesCount2;
        public uint Unknown_BCh;
        public uint Unknown_C0h;
        public uint Unknown_C4h;
        public ulong BoundPointer;

        public string_r Name;
        public ResourceSimpleArray<LightAttributes> LightAttributes;
        public Bound Bound;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.NamePointer = reader.ReadUInt64();
            this.LightAttributesPointer = reader.ReadUInt64();
            this.LightAttributesCount1 = reader.ReadUInt16();
            this.LightAttributesCount2 = reader.ReadUInt16();
            this.Unknown_BCh = reader.ReadUInt32();
            this.Unknown_C0h = reader.ReadUInt32();
            this.Unknown_C4h = reader.ReadUInt32();
            this.BoundPointer = reader.ReadUInt64();

            this.Name = reader.ReadBlockAt<string_r>(
                this.NamePointer
            );
            this.LightAttributes = reader.ReadBlockAt<ResourceSimpleArray<LightAttributes>>(
                this.LightAttributesPointer,
                this.LightAttributesCount1
            );
            this.Bound = reader.ReadBlockAt<Bound>(
                this.BoundPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.NamePointer = (ulong)(this.Name != null ? this.Name.Position : 0);
            this.LightAttributesPointer = (ulong)(this.LightAttributes != null ? this.LightAttributes.Position : 0);
            this.BoundPointer = (ulong)(this.Bound != null ? this.Bound.Position : 0);

            writer.Write(this.NamePointer);
            writer.Write(this.LightAttributesPointer);
            writer.Write(this.LightAttributesCount1);
            writer.Write(this.LightAttributesCount2);
            writer.Write(this.Unknown_BCh);
            writer.Write(this.Unknown_C0h);
            writer.Write(this.Unknown_C4h);
            writer.Write(this.BoundPointer);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Name != null) list.Add(Name);
            if (LightAttributes != null) list.Add(LightAttributes);
            if (Bound != null) list.Add(Bound);
            return list.ToArray();
        }
    }
}
