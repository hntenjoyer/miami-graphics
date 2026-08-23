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

namespace RageLib.Resources.GTA5.PC.Fragments
{
    public class FragPhysicsLODGroup : ResourceSystemBlock
    {
        public override long Length => 0x30;

        public uint VFT;
        public uint Unknown_4h;
        public uint Unknown_8h;
        public uint Unknown_Ch;
        public ulong PhysicsLOD1Pointer;
        public ulong PhysicsLOD2Pointer;
        public ulong PhysicsLOD3Pointer;
        public uint Unknown_28h;
        public uint Unknown_2Ch;

        public FragPhysicsLOD PhysicsLOD1;
        public FragPhysicsLOD PhysicsLOD2;
        public FragPhysicsLOD PhysicsLOD3;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_4h = reader.ReadUInt32();
            this.Unknown_8h = reader.ReadUInt32();
            this.Unknown_Ch = reader.ReadUInt32();
            this.PhysicsLOD1Pointer = reader.ReadUInt64();
            this.PhysicsLOD2Pointer = reader.ReadUInt64();
            this.PhysicsLOD3Pointer = reader.ReadUInt64();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();

            this.PhysicsLOD1 = reader.ReadBlockAt<FragPhysicsLOD>(
                this.PhysicsLOD1Pointer
            );
            this.PhysicsLOD2 = reader.ReadBlockAt<FragPhysicsLOD>(
                this.PhysicsLOD2Pointer
            );
            this.PhysicsLOD3 = reader.ReadBlockAt<FragPhysicsLOD>(
                this.PhysicsLOD3Pointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.PhysicsLOD1Pointer = (ulong)(this.PhysicsLOD1 != null ? this.PhysicsLOD1.Position : 0);
            this.PhysicsLOD2Pointer = (ulong)(this.PhysicsLOD2 != null ? this.PhysicsLOD2.Position : 0);
            this.PhysicsLOD3Pointer = (ulong)(this.PhysicsLOD3 != null ? this.PhysicsLOD3.Position : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_4h);
            writer.Write(this.Unknown_8h);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.PhysicsLOD1Pointer);
            writer.Write(this.PhysicsLOD2Pointer);
            writer.Write(this.PhysicsLOD3Pointer);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (PhysicsLOD1 != null) list.Add(PhysicsLOD1);
            if (PhysicsLOD2 != null) list.Add(PhysicsLOD2);
            if (PhysicsLOD3 != null) list.Add(PhysicsLOD3);
            return list.ToArray();
        }
    }
}
