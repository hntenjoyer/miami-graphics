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
using RageLib.Resources.GTA5.PC.Fragments;
using System.Collections.Generic;

namespace RageLib.Resources.GTA5.PC.Clothes
{
    public class EnvironmentCloth : ResourceSystemBlock
    {
        public override long Length => 0x80;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public ulong InstanceTuningPointer;
        public ulong DrawablePointer;
        public uint Unknown_20h;
        public uint Unknown_24h;
        public ulong ControllerPointer;
        public uint Unknown_30h;
        public uint Unknown_34h;
        public uint Unknown_38h;
        public uint Unknown_3Ch;
        public uint Unknown_40h;
        public uint Unknown_44h;
        public uint Unknown_48h;
        public uint Unknown_4Ch;
        public uint Unknown_50h;
        public uint Unknown_54h;
        public uint Unknown_58h;
        public uint Unknown_5Ch;
        public ulong pxxxxx_2;
        public ushort cntxx51a;
        public ushort cntxx51b;
        public uint Unknown_6Ch;
        public uint Unknown_70h;
        public uint Unknown_74h;
        public uint Unknown_78h;
        public uint Unknown_7Ch;

        public ClothInstanceTuning InstanceTuning;
        public FragDrawable Drawable;
        public ClothController Controller;
        public ResourceSimpleArray<uint_r> pxxxxx_2data;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.InstanceTuningPointer = reader.ReadUInt64();
            this.DrawablePointer = reader.ReadUInt64();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.ControllerPointer = reader.ReadUInt64();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();
            this.Unknown_40h = reader.ReadUInt32();
            this.Unknown_44h = reader.ReadUInt32();
            this.Unknown_48h = reader.ReadUInt32();
            this.Unknown_4Ch = reader.ReadUInt32();
            this.Unknown_50h = reader.ReadUInt32();
            this.Unknown_54h = reader.ReadUInt32();
            this.Unknown_58h = reader.ReadUInt32();
            this.Unknown_5Ch = reader.ReadUInt32();
            this.pxxxxx_2 = reader.ReadUInt64();
            this.cntxx51a = reader.ReadUInt16();
            this.cntxx51b = reader.ReadUInt16();
            this.Unknown_6Ch = reader.ReadUInt32();
            this.Unknown_70h = reader.ReadUInt32();
            this.Unknown_74h = reader.ReadUInt32();
            this.Unknown_78h = reader.ReadUInt32();
            this.Unknown_7Ch = reader.ReadUInt32();

            this.InstanceTuning = reader.ReadBlockAt<ClothInstanceTuning>(
                this.InstanceTuningPointer
            );
            this.Drawable = reader.ReadBlockAt<FragDrawable>(
                this.DrawablePointer
            );
            this.Controller = reader.ReadBlockAt<ClothController>(
                this.ControllerPointer
            );
            this.pxxxxx_2data = reader.ReadBlockAt<ResourceSimpleArray<uint_r>>(
                this.pxxxxx_2,
                this.cntxx51a
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.InstanceTuningPointer = (ulong)(this.InstanceTuning != null ? this.InstanceTuning.Position : 0);
            this.DrawablePointer = (ulong)(this.Drawable != null ? this.Drawable.Position : 0);
            this.ControllerPointer = (ulong)(this.Controller != null ? this.Controller.Position : 0);
            this.pxxxxx_2 = (ulong)(this.pxxxxx_2data != null ? this.pxxxxx_2data.Position : 0);
            this.cntxx51a = (ushort)(this.pxxxxx_2data != null ? this.pxxxxx_2data.Count : 0);
            this.cntxx51b = (ushort)(this.pxxxxx_2data != null ? this.pxxxxx_2data.Count : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.InstanceTuningPointer);
            writer.Write(this.DrawablePointer);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.ControllerPointer);
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
            writer.Write(this.pxxxxx_2);
            writer.Write(this.cntxx51a);
            writer.Write(this.cntxx51b);
            writer.Write(this.Unknown_6Ch);
            writer.Write(this.Unknown_70h);
            writer.Write(this.Unknown_74h);
            writer.Write(this.Unknown_78h);
            writer.Write(this.Unknown_7Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (InstanceTuning != null) list.Add(InstanceTuning);
            if (Drawable != null) list.Add(Drawable);
            if (Controller != null) list.Add(Controller);
            if (pxxxxx_2data != null) list.Add(pxxxxx_2data);
            return list.ToArray();
        }
    }
}
