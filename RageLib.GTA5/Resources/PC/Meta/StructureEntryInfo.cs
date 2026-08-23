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

namespace RageLib.Resources.GTA5.PC.Meta
{
    public enum StructureEntryDataType : byte
    {
        Boolean = 0x01,
        SignedByte = 0x10,
        UnsignedByte = 0x11,
        SignedShort = 0x12,
        UnsignedShort = 0x13,
        SignedInt = 0x14,
        UnsignedInt = 0x15,
        Float = 0x21,
        Float_XYZ = 0x33,
        Float_XYZW = 0x34,
        ByteEnum = 0x60,
        IntEnum = 0x62,
        ShortFlags = 0x64,
        IntFlags1 = 0x63,
        IntFlags2 = 0x65,
        Hash = 0x4A,
        Array = 0x52,
        ArrayOfChars = 0x40,
        ArrayOfBytes = 0x50,
        DataBlockPointer = 0x59,
        CharPointer = 0x44,
        StructurePointer = 0x07,
        Structure = 0x05
    }

    public class StructureEntryInfo : ResourceSystemBlock
    {
        public override long Length => 0x10;

        public int EntryNameHash { get; set; }
        public int DataOffset { get; set; }
        public StructureEntryDataType DataType { get; set; }
        public byte Unknown_9h { get; set; }
        public short ReferenceTypeIndex { get; set; }
        public int ReferenceKey { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.EntryNameHash = reader.ReadInt32();
            this.DataOffset = reader.ReadInt32();
            this.DataType = (StructureEntryDataType)reader.ReadByte();
            this.Unknown_9h = reader.ReadByte();
            this.ReferenceTypeIndex = reader.ReadInt16();
            this.ReferenceKey = reader.ReadInt32();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            writer.Write(this.EntryNameHash);
            writer.Write(this.DataOffset);
            writer.Write((byte)this.DataType);
            writer.Write(this.Unknown_9h);
            writer.Write(this.ReferenceTypeIndex);
            writer.Write(this.ReferenceKey);
        }
    }
}
