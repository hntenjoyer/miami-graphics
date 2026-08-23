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

namespace RageLib.Resources.GTA5.PC.Clips
{
    public class AnimationMapEntry : ResourceSystemBlock
    {
        public override long Length => 0x20;

        public uint Hash;
        public uint Unknown_4h;
        public ulong AnimationPointer;
        public ulong NextEntryPointer;
        public uint Unknown_18h;
        public uint Unknown_1Ch;

        public Animation Animation;
        public AnimationMapEntry NextEntry;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Hash = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.AnimationPointer = reader.ReadUInt64();
            this.NextEntryPointer = reader.ReadUInt64();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();

            this.Animation = reader.ReadBlockAt<Animation>(
                this.AnimationPointer
            );
            this.NextEntry = reader.ReadBlockAt<AnimationMapEntry>(
                this.NextEntryPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.AnimationPointer = (ulong)(this.Animation != null ? this.Animation.Position : 0);
            this.NextEntryPointer = (ulong)(this.NextEntry != null ? this.NextEntry.Position : 0);

            writer.Write(this.Hash);
            writer.Write(this.Unknown_4h);
            writer.Write(this.AnimationPointer);
            writer.Write(this.NextEntryPointer);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Animation != null) list.Add(Animation);
            if (NextEntry != null) list.Add(NextEntry);
            return list.ToArray();
        }
    }
}
