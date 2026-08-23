using System;
using System.Collections.Generic;
using System.Text;
using TC = System.ComponentModel.TypeConverterAttribute;
using EXP = System.ComponentModel.ExpandableObjectConverter;
using System.IO;
using System.Xml;
using SharpDX;

namespace CodeWalker.GameFiles
{
    [TC(typeof(EXP))]
    public class WatermapFile : GameFile, PackedFile
    {
        public byte[] RawFileData { get; set; }

        public uint Magic { get; set; } = 0x574D4150;
        public uint Version { get; set; } = 100;
        public uint DataLength { get; set; }
        public float CornerX { get; set; }
        public float CornerY { get; set; }
        public float TileX { get; set; }
        public float TileY { get; set; }
        public ushort Width { get; set; }
        public ushort Height { get; set; }
        public uint WatermapIndsCount { get; set; }
        public uint WatermapRefsCount { get; set; }
        public ushort RiverVecsCount { get; set; }
        public ushort RiverCount { get; set; }
        public ushort LakeVecsCount { get; set; }
        public ushort LakeCount { get; set; }
        public ushort PoolCount { get; set; }
        public ushort ColoursOffset { get; set; }
        public byte[] Unks1 { get; set; }

        public CompHeader[] CompHeaders { get; set; }
        public short[] CompWatermapInds { get; set; }
        public WaterItemRef[] CompWatermapRefs { get; set; }
        public byte[] Zeros1 { get; set; }
        public Vector4[] RiverVecs { get; set; }
        public WaterFlow[] Rivers { get; set; }
        public Vector4[] LakeVecs { get; set; }
        public WaterFlow[] Lakes { get; set; }
        public WaterPool[] Pools { get; set; }
        public Color[] Colours { get; set; }
        public uint ColourCount { get; set; }

        public short[] GridWatermapInds { get; set; }
        public WaterItemRef[][] GridWatermapRefs { get; set; }

        public WatermapFile() : base(null, GameFileType.Watermap)
        {
        }
        public WatermapFile(RpfFileEntry entry) : base(entry, GameFileType.Watermap)
        {
            RpfFileEntry = entry;
        }

        public void Load(byte[] data, RpfFileEntry entry)
        {
            RawFileData = data;
            if (entry != null)
            {
                RpfFileEntry = entry;
                Name = entry.Name;
            }

            using (MemoryStream ms = new MemoryStream(data))
            {
                DataReader r = new DataReader(ms, Endianess.BigEndian);

                Read(r);
            }
        }

        public byte[] Save()
        {
            MemoryStream s = new MemoryStream();
            DataWriter w = new DataWriter(s, Endianess.BigEndian);

            Write(w);

            var buf = new byte[s.Length];
            s.Position = 0;
            s.Read(buf, 0, buf.Length);
            return buf;
        }

        private void Read(DataReader r)
        {
            Magic = r.ReadUInt32();
            Version = r.ReadUInt32();
            DataLength = r.ReadUInt32();
            CornerX = r.ReadSingle();
            CornerY = r.ReadSingle();
            TileX = r.ReadSingle();
            TileY = r.ReadSingle();
            Width = r.ReadUInt16();
            Height = r.ReadUInt16();
            WatermapIndsCount = r.ReadUInt32();
            WatermapRefsCount = r.ReadUInt32();
            RiverVecsCount = r.ReadUInt16();
            RiverCount = r.ReadUInt16();
            LakeVecsCount = r.ReadUInt16();
            LakeCount = r.ReadUInt16();
            PoolCount = r.ReadUInt16();
            ColoursOffset = r.ReadUInt16();
            Unks1 = r.ReadBytes(8);

            var shortslen = (int)((WatermapIndsCount + WatermapRefsCount) * 2) + (Height * 4);
            var padcount = (16 - (shortslen % 16)) % 16;
            var strucslen = ((RiverVecsCount + LakeVecsCount) * 16) + ((RiverCount + LakeCount) * 48) + (PoolCount * 32);
            var datalen = shortslen + padcount + strucslen;
            var extoffs = padcount + strucslen - 60 - 60;

            CompHeaders = new CompHeader[Height];
            for (int i = 0; i < Height; i++) CompHeaders[i].Read(r);

            CompWatermapInds = new short[WatermapIndsCount];
            for (int i = 0; i < WatermapIndsCount; i++) CompWatermapInds[i] = r.ReadInt16();

            CompWatermapRefs = new WaterItemRef[WatermapRefsCount];
            for (int i = 0; i < WatermapRefsCount; i++) CompWatermapRefs[i] = new WaterItemRef(r.ReadUInt16());

            Zeros1 = r.ReadBytes(padcount);

            RiverVecs = new Vector4[RiverVecsCount];
            for (int i = 0; i < RiverVecsCount; i++) RiverVecs[i] = r.ReadVector4();

            Rivers = new WaterFlow[RiverCount];
            for (int i = 0; i < RiverCount; i++) Rivers[i] = new WaterFlow(WaterItemType.River, r, RiverVecs);

            LakeVecs = new Vector4[LakeVecsCount];
            for (int i = 0; i < LakeVecsCount; i++) LakeVecs[i] = r.ReadVector4();

            Lakes = new WaterFlow[LakeCount];
            for (int i = 0; i < LakeCount; i++) Lakes[i] = new WaterFlow(WaterItemType.Lake, r, LakeVecs);

            Pools = new WaterPool[PoolCount];
            for (int i = 0; i < PoolCount; i++) Pools[i] = new WaterPool(r);

            ColourCount = (uint)(RiverCount + LakeCount + PoolCount);
            Colours = new Color[ColourCount];
            for (int i = 0; i < 342; i++) Colours[i] = Color.FromAbgr(r.ReadUInt32());

            var flagoff = 0;
            for (int i = 0; i < Rivers.Length; i++)
            {
                var river = Rivers[i];
                river.Colour = Colours[flagoff++];
            }
            for (int i = 0; i < Lakes.Length; i++)
            {
                var lake = Lakes[i];
                lake.Colour = Colours[flagoff++];
            }
            for (int i = 0; i < Pools.Length; i++)
            {
                var pool = Pools[i];
                pool.Colour = Colours[flagoff++];
            }

            for (int i = 0; i < CompWatermapRefs.Length; i++)
            {
                var ir = CompWatermapRefs[i];
                switch (ir.Type)
                {
                    case WaterItemType.River: CompWatermapRefs[i].Item = Rivers[ir.ItemIndex]; break;
                    case WaterItemType.Lake: CompWatermapRefs[i].Item = Lakes[ir.ItemIndex]; break;
                    case WaterItemType.Pool: CompWatermapRefs[i].Item = Pools[ir.ItemIndex]; break;
                }
            }

            GridWatermapInds = new short[Width * Height];
            GridWatermapRefs = new WaterItemRef[Width * Height][];
            var reflist = new List<WaterItemRef>();
            for (int y = 0; y < Height; y++)
            {
                var ch = CompHeaders[y];
                for (int i = 0; i < ch.Count; i++)
                {
                    var x = ch.Start + i;
                    var n = CompWatermapInds[ch.Offset + i];
                    var o = y * Width + x;

                    reflist.Clear();
                    WaterItemRef[] refarr = null;
                    if (n >= 0)
                    {
                        var h = CompWatermapRefs[n];
                        reflist.Add(h);
                        var cn = n;
                        while (h.EndOfList == false)
                        {
                            cn++;
                            h = CompWatermapRefs[cn];
                            reflist.Add(h);
                        }

                        refarr = reflist.ToArray();
                    }

                    GridWatermapInds[o] = n;
                    GridWatermapRefs[o] = refarr;
                }
            }

            var rem = r.Length - r.Position;
            if (rem != 0)
            { }

        }
        private void Write(DataWriter w)
        {

            w.Write(Magic);

        }

        public void WriteXml(StringBuilder sb, int indent)
        {
        }
        public void ReadXml(XmlNode node)
        {
        }

        public struct CompHeader
        {
            public byte Start { get; set; }
            public byte Count { get; set; }
            public ushort Offset { get; set; }

            public void Read(DataReader r)
            {
                Start = r.ReadByte();
                Count = r.ReadByte();
                Offset = r.ReadUInt16();
            }

            public override string ToString()
            {
                return string.Format("{0}, {1}, {2}",
                    Start, Count, Offset);
            }
        }

        public struct WaterItemRef
        {
            public ushort RawValue { get; set; }

            public bool EndOfList { get { return ((RawValue >> 15) & 0x1) == 1; } }
            public WaterItemType Type { get { return (WaterItemType)((RawValue >> 13) & 0x3); } }
            public ushort ItemIndex
            {
                get
                {
                    switch (Type)
                    {
                        case WaterItemType.River:
                        case WaterItemType.Lake:
                            return (ushort)((RawValue >> 7) & 0x3F);
                        case WaterItemType.Pool:
                        default:
                            return (ushort)(RawValue & 0x7FF);
                    }
                }
            }
            public ushort VectorIndex
            {
                get
                {
                    switch (Type)
                    {
                        case WaterItemType.River:
                        case WaterItemType.Lake:
                            return (ushort)(RawValue & 0x7F);
                        case WaterItemType.Pool:
                        default:
                            return 0;
                    }
                }
            }

            public WaterItem Item { get; set; }
            public Vector4 Vector
            {
                get
                {
                    if (Item?.Vectors == null) return Vector4.Zero;
                    if (VectorIndex >= Item.Vectors.Length) return Vector4.Zero;
                    return Item.Vectors[VectorIndex];
                }
            }

            public WaterItemRef(ushort rawval) { RawValue = rawval; Item = null; }

            public override string ToString()
            {
                if (Item != null) return Item.ToString() + ": " + Vector.ToString();
                return Type.ToString() + ": " + ItemIndex.ToString() + ": " + VectorIndex.ToString();
            }
        }
        public enum WaterItemType
        {
            None = 0,
            River = 1,
            Lake = 2,
            Pool = 3,
        }
        public abstract class WaterItem
        {
            public Vector3 Position { get; set; }
            public uint Unk04 { get; set; }
            public Vector3 Size { get; set; }
            public uint Unk09 { get; set; }

            public WaterItemType Type { get; private set; }

            public Vector4[] Vectors { get; set; }
            public Color Colour { get; set; }

            public WaterItem(WaterItemType type)
            {
                Type = type;
            }

            public virtual void Read(DataReader r)
            {
                Position = r.ReadVector3();
                Unk04 = r.ReadUInt32();
                Size = r.ReadVector3();
                Unk09 = r.ReadUInt32();

                if (Unk04 != 0)
                { }
                if (Unk09 != 0)
                { }
            }

            public override string ToString()
            {
                return string.Format("{0} - Size: {1},  Pos: {2}", Type, Size, Position);
            }
        }
        public class WaterFlow : WaterItem
        {
            public byte VectorCount { get; set; }
            public byte Unk11 { get; set; }
            public ushort VectorOffset { get; set; }
            public uint Unk13 { get; set; }
            public uint Unk14 { get; set; }
            public uint Unk15 { get; set; }

            public WaterFlow(WaterItemType type) : base(type) { }
            public WaterFlow(WaterItemType type, DataReader r, Vector4[] vecs) : base(type)
            {
                Read(r);

                if (VectorCount > 0)
                {
                    Vectors = new Vector4[VectorCount];
                    for (int i = 0; i < VectorCount; i++)
                    {
                        Vectors[i] = vecs[VectorOffset + i];
                    }
                }
            }

            public override void Read(DataReader r)
            {
                base.Read(r);
                VectorCount = r.ReadByte();
                Unk11 = r.ReadByte();
                VectorOffset = r.ReadUInt16();
                Unk13 = r.ReadUInt32();
                Unk14 = r.ReadUInt32();
                Unk15 = r.ReadUInt32();

            }

            public override string ToString()
            {
                return base.ToString() + " : " + VectorCount.ToString();
            }
        }
        public class WaterPool : WaterItem
        {

            public WaterPool() : base(WaterItemType.Pool) { }
            public WaterPool(DataReader r) : base(WaterItemType.Pool) { Read(r); }

            public override void Read(DataReader r)
            {
                base.Read(r);
            }

            public override string ToString()
            {
                return base.ToString();
            }
        }

        public string GetPGM()
        {
            if (GridWatermapInds == null) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendFormat("P2\n{0} {1}\n65535\n", Width, Height);

            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var h = GridWatermapInds[y * Width + x];
                    sb.Append(h.ToString());
                    sb.Append(" ");
                }
                sb.Append("\n");
            }

            return sb.ToString();
        }

    }

    public class WatermapXml : MetaXmlBase
    {

        public static string GetXml(WatermapFile wmf)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(XmlHeader);

            if ((wmf != null))
            {
                var name = "Watermap";

                OpenTag(sb, 0, name);

                wmf.WriteXml(sb, 1);

                CloseTag(sb, 0, name);
            }

            return sb.ToString();
        }

    }

    public class XmlWatermap
    {

        public static WatermapFile GetWatermap(string xml)
        {
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(xml);
            return GetWatermap(doc);
        }

        public static WatermapFile GetWatermap(XmlDocument doc)
        {
            WatermapFile wmf = new WatermapFile();
            wmf.ReadXml(doc.DocumentElement);
            return wmf;
        }

    }

}
