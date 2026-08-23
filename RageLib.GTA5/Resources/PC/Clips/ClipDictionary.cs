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

namespace RageLib.Resources.GTA5.PC.Clips
{
    public class ClipDictionary : FileBase64_GTA5_pc
    {
        public override long Length => 0x40;

        public uint Unknown_10h;
        public uint Unknown_14h;
        public ulong AnimationsPointer;
        public uint Unknown_20h;
        public uint Unknown_24h;
        public ulong ClipsPointer;
        public ushort ClipEntriesCount;
        public ushort ClipEntriesTotalCount;
        public uint Unknown_34h;
        public uint Unknown_38h;
        public uint Unknown_3Ch;

        public AnimationMap Animations;
        public ResourcePointerArray64<ClipMapEntry> Clips;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.AnimationsPointer = reader.ReadUInt64();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.ClipsPointer = reader.ReadUInt64();
            this.ClipEntriesCount = reader.ReadUInt16();
            this.ClipEntriesTotalCount = reader.ReadUInt16();
            this.Unknown_34h = reader.ReadUInt32();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();

            this.Animations = reader.ReadBlockAt<AnimationMap>(
                this.AnimationsPointer
            );
            this.Clips = reader.ReadBlockAt<ResourcePointerArray64<ClipMapEntry>>(
                this.ClipsPointer,
                this.ClipEntriesCount
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.AnimationsPointer = (ulong)(this.Animations != null ? this.Animations.Position : 0);
            this.ClipsPointer = (ulong)(this.Clips != null ? this.Clips.Position : 0);
            this.ClipEntriesCount = (ushort)(this.Clips != null ? this.Clips.Count : 0);
            if (this.Clips != null)
            {
                int i = 0;
                foreach (var x in this.Clips.data_items)
                {
                    if (x != null)
                    {
                        var y = x;
                        do
                        {
                            if (y.Clip != null)
                            {
                                i++;
                            }
                            if (y.Next != null)
                            {
                                y = y.Next;
                            }
                            else
                            {
                                break;
                            }
                        } while (true);
                    }
                }
                this.ClipEntriesTotalCount = (ushort)i;
            }
            else
            {
                this.ClipEntriesTotalCount = 0;
            }

            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.AnimationsPointer);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.ClipsPointer);
            writer.Write(this.ClipEntriesCount);
            writer.Write(this.ClipEntriesTotalCount);
            writer.Write(this.Unknown_34h);
            writer.Write(this.Unknown_38h);
            writer.Write(this.Unknown_3Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Animations != null) list.Add(Animations);
            if (Clips != null) list.Add(Clips);
            return list.ToArray();
        }
    }
}
