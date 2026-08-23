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

namespace RageLib.Resources.GTA5.PC.Particles
{
    public class EventEmitter : ResourceSystemBlock
    {
        public override long Length => 0x70;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public uint Unknown_10h;
        public uint Unknown_14h;
        public ulong p1;
        public uint Unknown_20h;
        public uint Unknown_24h;
        public uint Unknown_28h;
        public uint Unknown_2Ch;
        public ulong p2;
        public ulong p3;
        public ulong p4;
        public ulong p5;
        public uint Unknown_50h;
        public uint Unknown_54h;
        public uint Unknown_58h;
        public uint Unknown_5Ch;
        public uint Unknown_60h;
        public uint Unknown_64h;
        public uint Unknown_68h;
        public uint Unknown_6Ch;

        public Unknown_P_005 p1data;
        public string_r p2data;
        public string_r p3data;
        public EmitterRule EmitterRule;
        public ParticleRule ParticleRule;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.p1 = reader.ReadUInt64();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.p2 = reader.ReadUInt64();
            this.p3 = reader.ReadUInt64();
            this.p4 = reader.ReadUInt64();
            this.p5 = reader.ReadUInt64();
            this.Unknown_50h = reader.ReadUInt32();
            this.Unknown_54h = reader.ReadUInt32();
            this.Unknown_58h = reader.ReadUInt32();
            this.Unknown_5Ch = reader.ReadUInt32();
            this.Unknown_60h = reader.ReadUInt32();
            this.Unknown_64h = reader.ReadUInt32();
            this.Unknown_68h = reader.ReadUInt32();
            this.Unknown_6Ch = reader.ReadUInt32();

            this.p1data = reader.ReadBlockAt<Unknown_P_005>(
                this.p1
            );
            this.p2data = reader.ReadBlockAt<string_r>(
                this.p2
            );
            this.p3data = reader.ReadBlockAt<string_r>(
                this.p3
            );
            this.EmitterRule = reader.ReadBlockAt<EmitterRule>(
                this.p4
            );
            this.ParticleRule = reader.ReadBlockAt<ParticleRule>(
                this.p5
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.p1 = (ulong)(this.p1data != null ? this.p1data.Position : 0);
            this.p2 = (ulong)(this.p2data != null ? this.p2data.Position : 0);
            this.p3 = (ulong)(this.p3data != null ? this.p3data.Position : 0);
            this.p4 = (ulong)(this.EmitterRule != null ? this.EmitterRule.Position : 0);
            this.p5 = (ulong)(this.ParticleRule != null ? this.ParticleRule.Position : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.p1);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.p2);
            writer.Write(this.p3);
            writer.Write(this.p4);
            writer.Write(this.p5);
            writer.Write(this.Unknown_50h);
            writer.Write(this.Unknown_54h);
            writer.Write(this.Unknown_58h);
            writer.Write(this.Unknown_5Ch);
            writer.Write(this.Unknown_60h);
            writer.Write(this.Unknown_64h);
            writer.Write(this.Unknown_68h);
            writer.Write(this.Unknown_6Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (p1data != null) list.Add(p1data);
            if (p2data != null) list.Add(p2data);
            if (p3data != null) list.Add(p3data);
            if (EmitterRule != null) list.Add(EmitterRule);
            if (ParticleRule != null) list.Add(ParticleRule);
            return list.ToArray();
        }
    }
}
