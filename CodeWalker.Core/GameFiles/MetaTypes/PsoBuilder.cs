using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeWalker.GameFiles
{
    public class PsoBuilder
    {

        public PsoBuilderPointer RootPointer { get; set; }

        List<string> STRFStrings = new List<string>();
        List<string> STRSStrings = new List<string>();

        Dictionary<MetaName, PsoStructureInfo> StructureInfos = new Dictionary<MetaName, PsoStructureInfo>();
        Dictionary<MetaName, PsoEnumInfo> EnumInfos = new Dictionary<MetaName, PsoEnumInfo>();

        List<PsoBuilderBlock> Blocks = new List<PsoBuilderBlock>();
        int MaxBlockLength = 0x100000;

        public PsoBuilderBlock EnsureBlock(MetaName type)
        {
            foreach (var block in Blocks)
            {
                if (block.StructureNameHash == type)
                {
                    if (block.TotalSize < MaxBlockLength)
                    {
                        return block;
                    }
                }
            }
            PsoBuilderBlock b = new PsoBuilderBlock();
            b.StructureNameHash = type;
            b.Index = Blocks.Count;
            Blocks.Add(b);
            return b;
        }

        public PsoBuilderPointer AddItem<T>(MetaName type, T item) where T : struct
        {
            byte[] data = MetaTypes.ConvertToBytes(item);
            return AddItem(type, data);
        }

        public PsoBuilderPointer AddItem(MetaName type, byte[] data)
        {
            PsoBuilderBlock block = EnsureBlock(type);
            int brem = data.Length % 16;
            int idx = block.AddItem(data);
            PsoBuilderPointer r = new PsoBuilderPointer();
            r.BlockID = block.Index + 1;
            r.Offset = (idx * data.Length);
            r.Length = data.Length;
            return r;
        }

        public PsoBuilderPointer AddItemArray<T>(MetaName type, T[] items) where T : struct
        {
            byte[] data = MetaTypes.ConvertArrayToBytes(items);
            return AddItemArray(type, data, items.Length);
        }

        public PsoBuilderPointer AddItemArray(MetaName type, byte[] data, int length)
        {
            PsoBuilderBlock block = EnsureBlock(type);
            int datalen = data.Length;
            int newlen = datalen;
            byte[] newdata = new byte[newlen];
            Buffer.BlockCopy(data, 0, newdata, 0, datalen);
            int offs = block.TotalSize;
            int idx = block.AddItem(newdata);
            PsoBuilderPointer r = new PsoBuilderPointer();
            r.BlockID = block.Index + 1;
            r.Offset = offs;
            r.Length = length;
            return r;
        }

        public PsoPOINTER AddItemPtr<T>(MetaName type, T item) where T : struct
        {
            var ptr = AddItem(type, item);
            return new PsoPOINTER(ptr.BlockID, ptr.Offset);
        }

        public PsoPOINTER AddItemPtr(MetaName type, byte[] data)
        {
            var ptr = AddItem(type, data);
            return new PsoPOINTER(ptr.BlockID, ptr.Offset);
        }

        public Array_Structure AddItemArrayPtr<T>(MetaName type, T[] items) where T : struct
        {
            if ((items == null) || (items.Length == 0)) return new Array_Structure();
            var ptr = AddItemArray(type, items);
            return new Array_Structure(ptr.Pointer, ptr.Length);
        }

        public Array_Structure AddItemArrayPtr(MetaName type, byte[][] data)
        {
            if ((data == null) || (data.Length == 0)) return new Array_Structure();

            int len = 0;

            for (int i = 0; i < data.Length; i++)
            {
                len += data[i].Length;
            }

            var newdata = new byte[len];

            int offset = 0;

            for (int i = 0; i < data.Length; i++)
            {
                Buffer.BlockCopy(data[i], 0, newdata, offset, data[i].Length);
                offset += data[i].Length;
            }

            var ptr = AddItemArray(type, newdata, data.Length);
            return new Array_Structure(ptr.Pointer, ptr.Length);
        }

        public Array_StructurePointer AddPointerArray(PsoPOINTER[] arr)
        {
            if ((arr == null) || (arr.Length == 0)) return new Array_StructurePointer();
            var ptr = AddItemArray((MetaName)MetaTypeName.PsoPOINTER, arr);
            Array_StructurePointer sp = new Array_StructurePointer();
            sp.Count1 = (ushort)arr.Length;
            sp.Count2 = sp.Count1;
            sp.Pointer = ptr.Pointer;
            return sp;
        }

        public PsoBuilderPointer AddString(string str)
        {
            PsoBuilderBlock block = EnsureBlock((MetaName)1);
            byte[] data = Encoding.ASCII.GetBytes(str + (char)0);
            int datalen = data.Length;
            int newlen = datalen;
            byte[] newdata = new byte[newlen];
            Buffer.BlockCopy(data, 0, newdata, 0, datalen);
            int offs = block.TotalSize;
            int idx = block.AddItem(newdata);
            PsoBuilderPointer r = new PsoBuilderPointer();
            r.BlockID = block.Index + 1;
            r.Offset = offs;
            r.Length = datalen;
            return r;
        }

        public Array_Vector3 AddPaddedVector3ArrayPtr(Vector4[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_Vector3();
            var ptr = AddItemArray((MetaName)1, items);
            return new Array_Vector3(ptr.Pointer, items.Length);
        }
        public Array_Vector3 AddVector2ArrayPtr(Vector2[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_Vector3();
            var ptr = AddItemArray((MetaName)1, items);
            return new Array_Vector3(ptr.Pointer, items.Length);
        }
        public Array_uint AddHashArrayPtr(MetaHash[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_uint();
            var ptr = AddItemArray((MetaName)6, items);
            return new Array_uint(ptr.Pointer, items.Length);
        }
        public Array_uint AddUIntArrayPtr(uint[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_uint();
            var ptr = AddItemArray((MetaName)6, items);
            return new Array_uint(ptr.Pointer, items.Length);
        }
        public Array_uint AddSIntArrayPtr(int[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_uint();
            var ptr = AddItemArray((MetaName)5, items);
            return new Array_uint(ptr.Pointer, items.Length);
        }
        public Array_ushort AddUShortArrayPtr(ushort[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_ushort();
            var ptr = AddItemArray((MetaName)4, items);
            return new Array_ushort(ptr.Pointer, items.Length);
        }
        public Array_byte AddByteArrayPtr(byte[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_byte();
            var ptr = AddItemArray((MetaName)2, items);
            return new Array_byte(ptr.Pointer, items.Length);
        }
        public Array_float AddFloatArrayPtr(float[] items)
        {
            if ((items == null) || (items.Length == 0)) return new Array_float();
            var ptr = AddItemArray((MetaName)7, items);
            return new Array_float(ptr.Pointer, items.Length);
        }

        public void AddStringToSTRF(string str)
        {
            STRFStrings.Add(str);
        }
        public void AddStringToSTRS(string str)
        {
            STRSStrings.Add(str);
        }

        public void AddStructureInfo(MetaName name)
        {
            if (!StructureInfos.ContainsKey(name))
            {
                PsoStructureInfo si = PsoTypes.GetStructureInfo(name);
                if (si != null)
                {
                    StructureInfos[name] = si;
                }
            }
        }
        public void AddEnumInfo(MetaName name)
        {
            if (!EnumInfos.ContainsKey(name))
            {
                PsoEnumInfo ei = PsoTypes.GetEnumInfo(name);
                if (ei != null)
                {
                    EnumInfos[name] = ei;
                }
            }
        }

        public PsoStructureInfo AddMapNodeStructureInfo(MetaName valType)
        {
            PsoStructureInfo inf = null;

            if (valType == 0)
            {
                inf = PsoTypes.GetStructureInfo((MetaName)MetaTypeName.ARRAYINFO);
                if (!StructureInfos.ContainsKey(inf.IndexInfo.NameHash))
                {
                    StructureInfos[inf.IndexInfo.NameHash] = inf;
                }
                return inf;
            }

            var structInfo = PsoTypes.GetStructureInfo(valType);
            if (structInfo == null)
            { }

            MetaName xName = (MetaName)MetaTypeName.ARRAYINFO + 1;
            bool nameOk = !StructureInfos.ContainsKey(xName);
            while (!nameOk)
            {
                var exInfo = StructureInfos[xName];
                var exInfoItem = exInfo.FindEntry(MetaName.Item);
                if (((MetaName)(exInfoItem?.ReferenceKey ?? 0) == valType))
                {
                    return exInfo;
                }
                xName++;
                nameOk = !StructureInfos.ContainsKey(xName);
            }

            inf = new PsoStructureInfo(xName, 0, 2, 8 + structInfo.StructureLength,
                new PsoStructureEntryInfo(MetaName.Key, PsoDataType.String, 0, 7, 0),
                new PsoStructureEntryInfo(MetaName.Item, PsoDataType.Structure, 8, 0, valType)
                );

            if (!StructureInfos.ContainsKey(xName))
            {
                StructureInfos[xName] = inf;
            }

            return inf;

        }

        public byte[] GetData()
        {
            int totlen = 16;
            for (int i = 0; i < Blocks.Count; i++)
            {
                totlen += Blocks[i].TotalSize;
            }
            byte[] data = new byte[totlen];
            int offset = 16;
            for (int i = 0; i < Blocks.Count; i++)
            {
                var block = Blocks[i];
                for (int j = 0; j < block.Items.Count; j++)
                {
                    var bdata = block.Items[j];
                    Buffer.BlockCopy(bdata, 0, data, offset, bdata.Length);
                    offset += bdata.Length;
                }
            }
            if (offset != data.Length)
            { }
            return data;
        }

        public PsoFile GetPso()
        {
            PsoFile pso = new PsoFile();
            pso.SchemaSection = new PsoSchemaSection();

            var schEntries = new List<PsoElementInfo>();
            foreach (var structInfo in StructureInfos.Values)
            {
                schEntries.Add(structInfo);
            }
            foreach (var enumInfo in EnumInfos.Values)
            {
                schEntries.Add(enumInfo);
            }
            pso.SchemaSection.Entries = schEntries.ToArray();
            pso.SchemaSection.EntriesIdx = new PsoElementIndexInfo[schEntries.Count];
            for (int i = 0; i < schEntries.Count; i++)
            {
                pso.SchemaSection.EntriesIdx[i] = new PsoElementIndexInfo();
                pso.SchemaSection.EntriesIdx[i].NameHash = schEntries[i].IndexInfo.NameHash;
            }

            if (STRFStrings.Count > 0)
            {
                pso.STRFSection = new PsoSTRFSection();
                pso.STRFSection.Strings = STRFStrings.ToArray();
            }
            if (STRSStrings.Count > 0)
            {
                pso.STRSSection = new PsoSTRSSection();
                pso.STRSSection.Strings = STRSStrings.ToArray();
            }

            pso.DataSection = new PsoDataSection();
            pso.DataSection.Data = GetData();

            pso.DataMapSection = new PsoDataMapSection();
            pso.DataMapSection.Entries = new PsoDataMappingEntry[Blocks.Count];
            pso.DataMapSection.RootId = RootPointer.BlockID;
            var offset = 16;
            for (int i = 0; i < Blocks.Count; i++)
            {
                var b = Blocks[i];
                var e = new PsoDataMappingEntry();
                e.NameHash = b.StructureNameHash;
                e.Length = b.TotalSize;
                e.Offset = offset;
                offset += b.TotalSize;
                pso.DataMapSection.Entries[i] = e;
            }

            return pso;
        }

    }

    public class PsoBuilderBlock
    {
        public MetaName StructureNameHash { get; set; }
        public List<byte[]> Items { get; set; } = new List<byte[]>();
        public int TotalSize { get; set; } = 0;
        public int Index { get; set; } = 0;

        public int AddItem(byte[] item)
        {
            int idx = Items.Count;
            Items.Add(item);
            TotalSize += item.Length;
            return idx;
        }

        public uint BasePointer
        {
            get
            {
                return (((uint)Index + 1) & 0xFFF);
            }
        }

    }

    public struct PsoBuilderPointer
    {
        public int BlockID { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
        public uint Pointer
        {
            get
            {
                uint bidx = (((uint)BlockID) & 0xFFF);
                uint offs = (((uint)Offset) & 0xFFFFF) << 12;
                return bidx + offs;
            }
        }
    }

}
