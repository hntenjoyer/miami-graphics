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
using RageLib.Resources.GTA5.PC.Textures;
using System;
using System.Collections.Generic;

namespace RageLib.Resources.GTA5.PC.Drawables
{
    public class ShaderFX : ResourceSystemBlock
    {
        public override long Length => 0x30;

        public ulong ParametersPointer;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public byte ParameterCount;
        public byte Unknown_11h;
        public ushort Unknown_12h;
        public uint Unknown_14h;
        public uint Unknown_18h;
        public uint Unknown_1Ch;
        public uint Unknown_20h;
        public ushort Unknown_24h;
        public byte Unknown_26h;
        public byte TextureParametersCount;
        public uint Unknown_28h;
        public uint Unknown_2Ch;

        public ShaderParametersBlock_GTA5_pc ParametersList;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.ParametersPointer = reader.ReadUInt64();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.ParameterCount = reader.ReadByte();
            this.Unknown_11h = reader.ReadByte();
            this.Unknown_12h = reader.ReadUInt16();
            this.Unknown_14h = reader.ReadUInt32();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt16();
            this.Unknown_26h = reader.ReadByte();
            this.TextureParametersCount = reader.ReadByte();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();

            this.ParametersList = reader.ReadBlockAt<ShaderParametersBlock_GTA5_pc>(
                this.ParametersPointer,
                this.ParameterCount
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.ParametersPointer = (ulong)(this.ParametersList != null ? this.ParametersList.Position : 0);

            writer.Write(this.ParametersPointer);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.ParameterCount);
            writer.Write(this.Unknown_11h);
            writer.Write(this.Unknown_12h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_26h);
            writer.Write(this.TextureParametersCount);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (ParametersList != null) list.Add(ParametersList);
            return list.ToArray();
        }

    }

    public class ShaderParametersBlock_GTA5_pc : ResourceSystemBlock
    {

        public override long Length
        {
            get
            {
                long offset = 0;
                foreach (var x in Parameters)
                {
                    offset += 16;
                }

                foreach (var x in Parameters)
                {
                    offset += 16 * x.DataType;
                }

                offset += Parameters.Count * 4;

                return offset;
            }
        }

        public List<ShaderParameter> Parameters = new List<ShaderParameter>();
        public List<uint> Hashes = new List<uint>();

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            int cnt = Convert.ToInt32(parameters[0]);

            Parameters = new List<ShaderParameter>();
            for (int i = 0; i < cnt; i++)
            {
                Parameters.Add(reader.ReadBlock<ShaderParameter>());
            }

            int offset = 0;
            for (int i = 0; i < cnt; i++)
            {
                var p = Parameters[i];

                switch (p.DataType)
                {
                    case 0:
                        offset += 0;
                        p.Data = reader.ReadBlockAt<Texture>(
                            p.DataPointer
                        );
                        break;
                    case 1:
                        offset += 16;
                        p.Data = reader.ReadBlockAt<RAGE_Vector4>(
                            p.DataPointer
                        );
                        break;

                    default:
                        offset += 16 * p.DataType;
                        p.Data = reader.ReadBlockAt<ResourceSimpleArray<RAGE_Vector4>>(
                             p.DataPointer,
                              p.DataType
                         );
                        break;
                }
            }

            reader.Position += offset;
            Hashes = new List<uint>();
            for (int i = 0; i < cnt; i++)
            {
                Hashes.Add(reader.ReadUInt32());
            }

        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            foreach (var f in Parameters)
                if (f.Data != null)
                    f.DataPointer = (ulong)f.Data.Position;
                else
                    f.DataPointer = 0;

            foreach (var f in Parameters)
                writer.WriteBlock(f);

            foreach (var f in Parameters)
            {
                if (f.DataType != 0)
                    writer.WriteBlock(f.Data);
            }

            foreach (var h in Hashes)
                writer.Write(h);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            list.AddRange(base.GetReferences());

            foreach (var x in Parameters)
                if (x.DataType == 0)
                    list.Add(x.Data);

            return list.ToArray();
        }

        public override Tuple<long, IResourceBlock>[] GetParts()
        {
            var list = new List<Tuple<long, IResourceBlock>>();
            list.AddRange(base.GetParts());

            long offset = 0;
            foreach (var x in Parameters)
            {
                list.Add(new Tuple<long, IResourceBlock>(offset, x));
                offset += 16;
            }

            foreach (var x in Parameters)
            {
                if (x.DataType != 0)
                    list.Add(new Tuple<long, IResourceBlock>(offset, x.Data));
                offset += 16 * x.DataType;
            }

            return list.ToArray();
        }
    }
}
