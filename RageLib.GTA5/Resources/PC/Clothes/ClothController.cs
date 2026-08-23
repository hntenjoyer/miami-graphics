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

namespace RageLib.Resources.GTA5.PC.Clothes
{
    public class ClothController : ResourceSystemBlock
    {
        public override long Length => 0x80;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public ulong BridgeSimGfxPointer;
        public ulong MorphControllerPointer;
        public ulong VerletCloth1Pointer;
        public ulong VerletCloth2Pointer;
        public ulong VerletCloth3Pointer;
        public uint Unknown_38h;
        public uint Unknown_3Ch;
        public uint Unknown_40h;
        public uint Unknown_44h;
        public uint Unknown_48h;
        public uint Unknown_4Ch;
        public uint Type;
        public uint Unknown_54h;
        public uint Unknown_58h;
        public uint Unknown_5Ch;
        public uint Unknown_60h;
        public uint Unknown_64h;
        public uint Unknown_68h;
        public uint Unknown_6Ch;
        public uint Unknown_70h;
        public uint Unknown_74h;
        public uint Unknown_78h;
        public uint Unknown_7Ch;

        public ClothBridgeSimGfx BridgeSimGfx;
        public MorphController MorphController;
        public VerletCloth VerletCloth1;
        public VerletCloth VerletCloth2;
        public VerletCloth VerletCloth3;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.BridgeSimGfxPointer = reader.ReadUInt64();
            this.MorphControllerPointer = reader.ReadUInt64();
            this.VerletCloth1Pointer = reader.ReadUInt64();
            this.VerletCloth2Pointer = reader.ReadUInt64();
            this.VerletCloth3Pointer = reader.ReadUInt64();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();
            this.Unknown_40h = reader.ReadUInt32();
            this.Unknown_44h = reader.ReadUInt32();
            this.Unknown_48h = reader.ReadUInt32();
            this.Unknown_4Ch = reader.ReadUInt32();
            this.Type = reader.ReadUInt32();
            this.Unknown_54h = reader.ReadUInt32();
            this.Unknown_58h = reader.ReadUInt32();
            this.Unknown_5Ch = reader.ReadUInt32();
            this.Unknown_60h = reader.ReadUInt32();
            this.Unknown_64h = reader.ReadUInt32();
            this.Unknown_68h = reader.ReadUInt32();
            this.Unknown_6Ch = reader.ReadUInt32();
            this.Unknown_70h = reader.ReadUInt32();
            this.Unknown_74h = reader.ReadUInt32();
            this.Unknown_78h = reader.ReadUInt32();
            this.Unknown_7Ch = reader.ReadUInt32();

            this.BridgeSimGfx = reader.ReadBlockAt<ClothBridgeSimGfx>(
                this.BridgeSimGfxPointer
            );
            this.MorphController = reader.ReadBlockAt<MorphController>(
                this.MorphControllerPointer
            );
            this.VerletCloth1 = reader.ReadBlockAt<VerletCloth>(
                this.VerletCloth1Pointer
            );
            this.VerletCloth2 = reader.ReadBlockAt<VerletCloth>(
                this.VerletCloth2Pointer
            );
            this.VerletCloth3 = reader.ReadBlockAt<VerletCloth>(
                this.VerletCloth3Pointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.BridgeSimGfxPointer = (ulong)(this.BridgeSimGfx != null ? this.BridgeSimGfx.Position : 0);
            this.MorphControllerPointer = (ulong)(this.MorphController != null ? this.MorphController.Position : 0);
            this.VerletCloth1Pointer = (ulong)(this.VerletCloth1 != null ? this.VerletCloth1.Position : 0);
            this.VerletCloth2Pointer = (ulong)(this.VerletCloth2 != null ? this.VerletCloth2.Position : 0);
            this.VerletCloth3Pointer = (ulong)(this.VerletCloth3 != null ? this.VerletCloth3.Position : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.BridgeSimGfxPointer);
            writer.Write(this.MorphControllerPointer);
            writer.Write(this.VerletCloth1Pointer);
            writer.Write(this.VerletCloth2Pointer);
            writer.Write(this.VerletCloth3Pointer);
            writer.Write(this.Unknown_38h);
            writer.Write(this.Unknown_3Ch);
            writer.Write(this.Unknown_40h);
            writer.Write(this.Unknown_44h);
            writer.Write(this.Unknown_48h);
            writer.Write(this.Unknown_4Ch);
            writer.Write(this.Type);
            writer.Write(this.Unknown_54h);
            writer.Write(this.Unknown_58h);
            writer.Write(this.Unknown_5Ch);
            writer.Write(this.Unknown_60h);
            writer.Write(this.Unknown_64h);
            writer.Write(this.Unknown_68h);
            writer.Write(this.Unknown_6Ch);
            writer.Write(this.Unknown_70h);
            writer.Write(this.Unknown_74h);
            writer.Write(this.Unknown_78h);
            writer.Write(this.Unknown_7Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (BridgeSimGfx != null) list.Add(BridgeSimGfx);
            if (MorphController != null) list.Add(MorphController);
            if (VerletCloth1 != null) list.Add(VerletCloth1);
            if (VerletCloth2 != null) list.Add(VerletCloth2);
            if (VerletCloth3 != null) list.Add(VerletCloth3);
            return list.ToArray();
        }
    }
}
