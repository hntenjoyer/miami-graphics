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

namespace RageLib.Resources.GTA5.PC.Drawables
{
    public class JointRotationLimit : ResourceSystemBlock
    {
        public override long Length => 0xC0;

        public uint Unknown_0h;
        public uint Unknown_4h;
        public ushort BoneId;
        public ushort Unknown_Ah;
        public uint Unknown_Ch;
        public uint Unknown_10h;
        public uint Unknown_14h;
        public uint Unknown_18h;
        public uint Unknown_1Ch;
        public uint Unknown_20h;
        public uint Unknown_24h;
        public uint Unknown_28h;
        public float Unknown_2Ch;
        public uint Unknown_30h;
        public uint Unknown_34h;
        public uint Unknown_38h;
        public uint Unknown_3Ch;
        public float Unknown_40h;
        public uint Unknown_44h;
        public uint Unknown_48h;
        public uint Unknown_4Ch;
        public float Unknown_50h;
        public float Unknown_54h;
        public float Unknown_58h;
        public float Unknown_5Ch;
        public float Unknown_60h;
        public float Unknown_64h;
        public float Unknown_68h;
        public float Unknown_6Ch;
        public float Unknown_70h;
        public float Unknown_74h;
        public float Unknown_78h;
        public float Unknown_7Ch;
        public float Unknown_80h;
        public float Unknown_84h;
        public float Unknown_88h;
        public float Unknown_8Ch;
        public float Unknown_90h;
        public float Unknown_94h;
        public float Unknown_98h;
        public float Unknown_9Ch;
        public float Unknown_A0h;
        public float Unknown_A4h;
        public float Unknown_A8h;
        public float Unknown_ACh;
        public float Unknown_B0h;
        public float Unknown_B4h;
        public float Unknown_B8h;
        public uint Unknown_BCh;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Unknown_0h = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.BoneId = reader.ReadUInt16();
            this.Unknown_Ah = reader.ReadUInt16();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadSingle();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();
            this.Unknown_40h = reader.ReadSingle();
            this.Unknown_44h = reader.ReadUInt32();
            this.Unknown_48h = reader.ReadUInt32();
            this.Unknown_4Ch = reader.ReadUInt32();
            this.Unknown_50h = reader.ReadSingle();
            this.Unknown_54h = reader.ReadSingle();
            this.Unknown_58h = reader.ReadSingle();
            this.Unknown_5Ch = reader.ReadSingle();
            this.Unknown_60h = reader.ReadSingle();
            this.Unknown_64h = reader.ReadSingle();
            this.Unknown_68h = reader.ReadSingle();
            this.Unknown_6Ch = reader.ReadSingle();
            this.Unknown_70h = reader.ReadSingle();
            this.Unknown_74h = reader.ReadSingle();
            this.Unknown_78h = reader.ReadSingle();
            this.Unknown_7Ch = reader.ReadSingle();
            this.Unknown_80h = reader.ReadSingle();
            this.Unknown_84h = reader.ReadSingle();
            this.Unknown_88h = reader.ReadSingle();
            this.Unknown_8Ch = reader.ReadSingle();
            this.Unknown_90h = reader.ReadSingle();
            this.Unknown_94h = reader.ReadSingle();
            this.Unknown_98h = reader.ReadSingle();
            this.Unknown_9Ch = reader.ReadSingle();
            this.Unknown_A0h = reader.ReadSingle();
            this.Unknown_A4h = reader.ReadSingle();
            this.Unknown_A8h = reader.ReadSingle();
            this.Unknown_ACh = reader.ReadSingle();
            this.Unknown_B0h = reader.ReadSingle();
            this.Unknown_B4h = reader.ReadSingle();
            this.Unknown_B8h = reader.ReadSingle();
            this.Unknown_BCh = reader.ReadUInt32();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            writer.Write(this.Unknown_0h);
            writer.Write(this.Unknown_4h);
            writer.Write(this.BoneId);
            writer.Write(this.Unknown_Ah);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
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
            writer.Write(this.Unknown_60h);
            writer.Write(this.Unknown_64h);
            writer.Write(this.Unknown_68h);
            writer.Write(this.Unknown_6Ch);
            writer.Write(this.Unknown_70h);
            writer.Write(this.Unknown_74h);
            writer.Write(this.Unknown_78h);
            writer.Write(this.Unknown_7Ch);
            writer.Write(this.Unknown_80h);
            writer.Write(this.Unknown_84h);
            writer.Write(this.Unknown_88h);
            writer.Write(this.Unknown_8Ch);
            writer.Write(this.Unknown_90h);
            writer.Write(this.Unknown_94h);
            writer.Write(this.Unknown_98h);
            writer.Write(this.Unknown_9Ch);
            writer.Write(this.Unknown_A0h);
            writer.Write(this.Unknown_A4h);
            writer.Write(this.Unknown_A8h);
            writer.Write(this.Unknown_ACh);
            writer.Write(this.Unknown_B0h);
            writer.Write(this.Unknown_B4h);
            writer.Write(this.Unknown_B8h);
            writer.Write(this.Unknown_BCh);
        }
    }
}
