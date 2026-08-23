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
    public class Joints : ResourceSystemBlock
    {
        public override long Length => 0x40;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public ulong RotationLimitsPointer;
        public ulong TranslationLimitsPointer;
        public uint Unknown_20h;
        public uint Unknown_24h;
        public uint Unknown_28h;
        public uint Unknown_2Ch;
        public ushort RotationLimitsCount;
        public ushort TranslationLimitsCount;
        public ushort Unknown_34h;
        public ushort Unknown_36h;
        public uint Unknown_38h;
        public uint Unknown_3Ch;

        public ResourceSimpleArray<JointRotationLimit> RotationLimits;
        public ResourceSimpleArray<JointTranslationLimit> TranslationLimits;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.RotationLimitsPointer = reader.ReadUInt64();
            this.TranslationLimitsPointer = reader.ReadUInt64();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.RotationLimitsCount = reader.ReadUInt16();
            this.TranslationLimitsCount = reader.ReadUInt16();
            this.Unknown_34h = reader.ReadUInt16();
            this.Unknown_36h = reader.ReadUInt16();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();

            this.RotationLimits = reader.ReadBlockAt<ResourceSimpleArray<JointRotationLimit>>(
                this.RotationLimitsPointer,
                this.RotationLimitsCount
            );
            this.TranslationLimits = reader.ReadBlockAt<ResourceSimpleArray<JointTranslationLimit>>(
                this.TranslationLimitsPointer,
                this.TranslationLimitsCount
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.RotationLimitsPointer = (ulong)(this.RotationLimits != null ? this.RotationLimits.Position : 0);
            this.TranslationLimitsPointer = (ulong)(this.TranslationLimits != null ? this.TranslationLimits.Position : 0);
            this.RotationLimitsCount = (ushort)(this.RotationLimits != null ? this.RotationLimits.Count : 0);
            this.TranslationLimitsCount = (ushort)(this.TranslationLimits != null ? this.TranslationLimits.Count : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.RotationLimitsPointer);
            writer.Write(this.TranslationLimitsPointer);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.RotationLimitsCount);
            writer.Write(this.TranslationLimitsCount);
            writer.Write(this.Unknown_34h);
            writer.Write(this.Unknown_36h);
            writer.Write(this.Unknown_38h);
            writer.Write(this.Unknown_3Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (RotationLimits != null) list.Add(RotationLimits);
            if (TranslationLimits != null) list.Add(TranslationLimits);
            return list.ToArray();
        }
    }
}
