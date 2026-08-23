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
    public class EmitterRuleDictionary : ResourceSystemBlock
    {
        public override long Length => 0x40;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public uint Unknown_10h;
        public uint Unknown_14h;
        public uint Unknown_18h;
        public uint Unknown_1Ch;
        public ulong HashesPointer;
        public ushort HashesCount1;
        public ushort HashesCount2;
        public uint Unknown_2Ch;
        public ulong EffectRulesPointer;
        public ushort EffectRulesCount1;
        public ushort EffectRulesCount2;
        public uint Unknown_3Ch;

        public ResourceSimpleArray<uint_r> Hashes;
        public ResourcePointerArray64<EmitterRule> EmitterRules;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.HashesPointer = reader.ReadUInt64();
            this.HashesCount1 = reader.ReadUInt16();
            this.HashesCount2 = reader.ReadUInt16();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.EffectRulesPointer = reader.ReadUInt64();
            this.EffectRulesCount1 = reader.ReadUInt16();
            this.EffectRulesCount2 = reader.ReadUInt16();
            this.Unknown_3Ch = reader.ReadUInt32();

            this.Hashes = reader.ReadBlockAt<ResourceSimpleArray<uint_r>>(
                this.HashesPointer,
                this.HashesCount1
            );
            this.EmitterRules = reader.ReadBlockAt<ResourcePointerArray64<EmitterRule>>(
                this.EffectRulesPointer,
                this.EffectRulesCount1
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.HashesPointer = (ulong)(this.Hashes != null ? this.Hashes.Position : 0);
            this.EffectRulesPointer = (ulong)(this.EmitterRules != null ? this.EmitterRules.Position : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.HashesPointer);
            writer.Write(this.HashesCount1);
            writer.Write(this.HashesCount2);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.EffectRulesPointer);
            writer.Write(this.EffectRulesCount1);
            writer.Write(this.EffectRulesCount2);
            writer.Write(this.Unknown_3Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Hashes != null) list.Add(Hashes);
            if (EmitterRules != null) list.Add(EmitterRules);
            return list.ToArray();
        }
    }
}
