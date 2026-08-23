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
    public class AnimationMap : ResourceSystemBlock
    {
        public override long Length => 0x30;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public uint Unknown_10h;
        public uint Unknown_14h;
        public ulong AnimationsPointer;
        public ushort AnimationEntriesCount;
        public ushort AnimationEntriesTotalCount;
        public uint Unknown_24h;
        public uint Unknown_28h;
        public uint Unknown_2Ch;

        public ResourcePointerArray64<AnimationMapEntry> Animations;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.AnimationsPointer = reader.ReadUInt64();
            this.AnimationEntriesCount = reader.ReadUInt16();
            this.AnimationEntriesTotalCount = reader.ReadUInt16();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();

            this.Animations = reader.ReadBlockAt<ResourcePointerArray64<AnimationMapEntry>>(
                this.AnimationsPointer,
                this.AnimationEntriesCount
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.AnimationsPointer = (ulong)(this.Animations != null ? this.Animations.Position : 0);
            this.AnimationEntriesCount = (ushort)(this.Animations != null ? this.Animations.Count : 0);
            if (this.Animations != null)
            {
                int i = 0;
                foreach (var x in this.Animations.data_items)
                {
                    if (x != null)
                    {
                        var y = x;
                        do
                        {
                            if (y.Animation != null)
                            {
                                i++;
                            }
                            if (y.NextEntry != null)
                            {
                                y = y.NextEntry;
                            }
                            else
                            {
                                break;
                            }
                        } while (true);
                    }
                }
                this.AnimationEntriesTotalCount = (ushort)i;
            }
            else
            {
                this.AnimationEntriesTotalCount = 0;
            }

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.AnimationsPointer);
            writer.Write(this.AnimationEntriesCount);
            writer.Write(this.AnimationEntriesTotalCount);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Animations != null) list.Add(Animations);
            return list.ToArray();
        }
    }
}
