using SharpDX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

/*
    Copyright(c) 2016 Neodymium

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

//ruthlessly stolen

namespace CodeWalker.GameFiles
{

    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipDictionary : ResourceFileBase
    {
        public override long BlockLength
        {
            get { return 64; }
        }

        public uint Unknown_10h { get; set; }
        public uint Unknown_14h { get; set; }
        public ulong AnimationsPointer { get; set; }
        public uint Unknown_20h { get; set; } = 0x00000101;
        public uint Unknown_24h { get; set; }
        public ulong ClipsPointer { get; set; }
        public ushort ClipsMapCapacity { get; set; }
        public ushort ClipsMapEntries { get; set; }
        public uint Unknown_34h { get; set; } = 0x01000000;
        public uint Unknown_38h { get; set; }
        public uint Unknown_3Ch { get; set; }

        public AnimationMap Animations { get; set; }
        public ResourcePointerArray64<ClipMapEntry> Clips { get; set; }

        public Dictionary<MetaHash, ClipMapEntry> ClipMap { get; set; }
        public Dictionary<MetaHash, AnimationMapEntry> AnimMap { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.AnimationsPointer = reader.ReadUInt64();
            this.Unknown_20h = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.ClipsPointer = reader.ReadUInt64();
            this.ClipsMapCapacity = reader.ReadUInt16();
            this.ClipsMapEntries = reader.ReadUInt16();
            this.Unknown_34h = reader.ReadUInt32();
            this.Unknown_38h = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();

            this.Animations = reader.ReadBlockAt<AnimationMap>(
                this.AnimationsPointer
            );
            this.Clips = reader.ReadBlockAt<ResourcePointerArray64<ClipMapEntry>>(
                this.ClipsPointer,
                this.ClipsMapCapacity
            );

            BuildMaps();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.AnimationsPointer = (ulong)(this.Animations != null ? this.Animations.FilePosition : 0);
            this.ClipsPointer = (ulong)(this.Clips != null ? this.Clips.FilePosition : 0);
            this.ClipsMapCapacity = (ushort)((Clips != null) ? Clips.Count : 0);
            this.ClipsMapEntries = (ushort)((ClipMap != null) ? ClipMap.Count : 0);

            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.AnimationsPointer);
            writer.Write(this.Unknown_20h);
            writer.Write(this.Unknown_24h);
            writer.Write(this.ClipsPointer);
            writer.Write(this.ClipsMapCapacity);
            writer.Write(this.ClipsMapEntries);
            writer.Write(this.Unknown_34h);
            writer.Write(this.Unknown_38h);
            writer.Write(this.Unknown_3Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Animations != null)
            {
                list.Add(Animations);
                Animations.AnimationsMapEntries = (ushort)(AnimMap?.Count ?? 0);
            }
            if (Clips != null) list.Add(Clips);
            return list.ToArray();
        }

        public void BuildMaps()
        {
            ClipMap = new Dictionary<MetaHash, ClipMapEntry>();
            AnimMap = new Dictionary<MetaHash, AnimationMapEntry>();

            if ((Clips != null) && (Clips.data_items != null))
            {
                foreach (var cme in Clips.data_items)
                {
                    if (cme != null)
                    {
                        ClipMap[cme.Hash] = cme;
                        var nxt = cme.Next;
                        while (nxt != null)
                        {
                            ClipMap[nxt.Hash] = nxt;
                            nxt = nxt.Next;
                        }
                    }
                }
            }
            if ((Animations != null) && (Animations.Animations != null) && (Animations.Animations.data_items != null))
            {
                foreach (var ame in Animations.Animations.data_items)
                {
                    if (ame != null)
                    {
                        AnimMap[ame.Hash] = ame;
                        var nxt = ame.NextEntry;
                        while (nxt != null)
                        {
                            AnimMap[nxt.Hash] = nxt;
                            nxt = nxt.NextEntry;
                        }
                    }
                }
            }

            foreach (var cme in ClipMap.Values)
            {
                var clip = cme.Clip;
                if (clip == null) continue;

                var name = clip.ShortName;

            }
        }

        public void UpdateUsageCounts()
        {

            var usages = new Dictionary<MetaHash, uint>();

            void addUsage(MetaHash h)
            {
                uint u = 0;
                usages.TryGetValue(h, out u);
                u++;
                usages[h] = u;
            }

            if ((Animations != null) && (Animations.Animations != null) && (Animations.Animations.data_items != null))
            {
                foreach (var ame in Animations.Animations.data_items)
                {
                    if (ame != null)
                    {
                        addUsage(ame.Hash);
                        var nxt = ame.NextEntry;
                        while (nxt != null)
                        {
                            addUsage(nxt.Hash);
                            nxt = nxt.NextEntry;
                        }
                    }
                }
            }

            foreach (var cme in ClipMap.Values)
            {
                var ca = cme.Clip as ClipAnimation;
                var cal = cme.Clip as ClipAnimationList;
                if (ca?.Animation != null)
                {
                    addUsage(ca.Animation.Hash);
                }
                if (cal?.Animations != null)
                {
                    foreach (var cae in cal.Animations)
                    {
                        if (cae?.Animation != null)
                        {
                            addUsage(cae.Animation.Hash);
                        }
                    }
                }
            }

            foreach (var ame in AnimMap.Values)
            {
                if (ame.Animation != null)
                {
                    uint u = 0;
                    if (usages.TryGetValue(ame.Animation.Hash, out u))
                    {
                        if (ame.Animation.UsageCount != u)
                        { }
                        ame.Animation.UsageCount = u;
                    }
                    else
                    { }
                }
            }

        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            var clips = new List<ClipBase>();
            if (ClipMap != null)
            {
                foreach (var cme in ClipMap.Values)
                {
                    if (cme?.Clip == null) continue;
                    clips.Add(cme.Clip);
                }
            }
            var anims = new List<Animation>();
            if (AnimMap != null)
            {
                foreach (var ame in AnimMap.Values)
                {
                    if (ame?.Animation == null) continue;
                    anims.Add(ame.Animation);
                }
            }

            YcdXml.WriteItemArray(sb, clips.ToArray(), indent, "Clips");
            YcdXml.WriteItemArray(sb, anims.ToArray(), indent, "Animations");

        }
        public void ReadXml(XmlNode node)
        {

            var clipList = new List<ClipMapEntry>();
            var clipsNode = node.SelectSingleNode("Clips");
            if (clipsNode != null)
            {
                var inodes = clipsNode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    foreach (XmlNode inode in inodes)
                    {
                        var type = Xml.GetEnumValue<ClipType>(Xml.GetChildStringAttribute(inode, "Type", "value"));
                        var c = ClipBase.ConstructClip(type);
                        c.ReadXml(inode);

                        var cme = new ClipMapEntry();
                        cme.Hash = c.Hash;
                        cme.Clip = c;
                        clipList.Add(cme);
                    }
                }
            }

            var animDict = new Dictionary<MetaHash, Animation>();
            var animList = new List<AnimationMapEntry>();
            var anims = XmlMeta.ReadItemArrayNullable<Animation>(node, "Animations");
            if (anims != null)
            {
                foreach (var anim in anims)
                {
                    animDict[anim.Hash] = anim;

                    var ame = new AnimationMapEntry();
                    ame.Hash = anim.Hash;
                    ame.Animation = anim;
                    animList.Add(ame);
                }
            }

            foreach (var cme in clipList)
            {
                var cb = cme?.Clip;
                var clipanim = cb as ClipAnimation;
                if (clipanim != null)
                {
                    animDict.TryGetValue(clipanim.AnimationHash, out Animation a);
                    clipanim.Animation = a;
                }
                var clipanimlist = cb as ClipAnimationList;
                if (clipanimlist?.Animations?.Data != null)
                {
                    foreach (var cae in clipanimlist.Animations.Data)
                    {
                        animDict.TryGetValue(cae.AnimationHash, out Animation a);
                        cae.Animation = a;
                    }
                }
            }

            CreateAnimationsMap(animList.ToArray());
            CreateClipsMap(clipList.ToArray());

            BuildMaps();
            UpdateUsageCounts();
        }

        public void CreateClipsMap(ClipMapEntry[] clips)
        {
            var numClipBuckets = GetNumHashBuckets(clips?.Length ?? 0);
            var clipBuckets = new List<ClipMapEntry>[numClipBuckets];
            if (clips != null)
            {
                foreach (var cme in clips)
                {
                    var b = cme.Hash % numClipBuckets;
                    var bucket = clipBuckets[b];
                    if (bucket == null)
                    {
                        bucket = new List<ClipMapEntry>();
                        clipBuckets[b] = bucket;
                    }
                    bucket.Add(cme);
                }
            }

            var newClips = new List<ClipMapEntry>();
            foreach (var b in clipBuckets)
            {
                if ((b?.Count ?? 0) == 0) newClips.Add(null);
                else
                {
                    newClips.Add(b[0]);
                    var p = b[0];
                    for (int i = 1; i < b.Count; i++)
                    {
                        var c = b[i];
                        c.Next = null;
                        p.Next = c;
                        p = c;
                    }
                }
            }

            Clips = new ResourcePointerArray64<ClipMapEntry>();
            Clips.data_items = newClips.ToArray();

        }
        public void CreateAnimationsMap(AnimationMapEntry[] anims)
        {
            var numAnimBuckets = GetNumHashBuckets(anims?.Length ?? 0);
            var animBuckets = new List<AnimationMapEntry>[numAnimBuckets];
            if (anims != null)
            {
                foreach (var ame in anims)
                {
                    var b = ame.Hash % numAnimBuckets;
                    var bucket = animBuckets[b];
                    if (bucket == null)
                    {
                        bucket = new List<AnimationMapEntry>();
                        animBuckets[b] = bucket;
                    }
                    bucket.Add(ame);
                }
            }

            var newAnims = new List<AnimationMapEntry>();
            foreach (var b in animBuckets)
            {
                if ((b?.Count ?? 0) == 0) newAnims.Add(null);
                else
                {
                    newAnims.Add(b[0]);
                    var p = b[0];
                    for (int i = 1; i < b.Count; i++)
                    {
                        var c = b[i];
                        c.NextEntry = null;
                        p.NextEntry = c;
                        p = c;
                    }
                }
            }

            Animations = new AnimationMap();
            Animations.Animations = new ResourcePointerArray64<AnimationMapEntry>();
            Animations.Animations.data_items = newAnims.ToArray();

        }

        public static uint GetNumHashBuckets(int nHashes)
        {
            if (nHashes < 11) return 11;
            else if (nHashes < 29) return 29;
            else if (nHashes < 59) return 59;
            else if (nHashes < 107) return 107;
            else if (nHashes < 191) return 191;
            else if (nHashes < 331) return 331;
            else if (nHashes < 563) return 563;
            else if (nHashes < 953) return 953;
            else if (nHashes < 1609) return 1609;
            else if (nHashes < 2729) return 2729;
            else if (nHashes < 4621) return 4621;
            else if (nHashes < 7841) return 7841;
            else if (nHashes < 13297) return 13297;
            else if (nHashes < 22571) return 22571;
            else if (nHashes < 38351) return 38351;
            else if (nHashes < 65167) return 65167;
            else  return 65521;
        }

    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimationMap : ResourceSystemBlock
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public uint VFT { get; set; }
        public uint Unknown_04h { get; set; } = 1;
        public uint Unknown_08h { get; set; } = 0;
        public uint Unknown_0Ch { get; set; } = 0;
        public uint Unknown_10h { get; set; } = 0;
        public uint Unknown_14h { get; set; } = 0;
        public ulong AnimationsPointer { get; set; }
        public ushort AnimationsMapCapacity { get; set; }
        public ushort AnimationsMapEntries { get; set; }
        public uint Unknown_24h { get; set; } = 16777216;
        public uint Unknown_28h { get; set; } = 1;
        public uint Unknown_2Ch { get; set; } = 0;

        public ResourcePointerArray64<AnimationMapEntry> Animations { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_04h = reader.ReadUInt32();
            this.Unknown_08h = reader.ReadUInt32();
            this.Unknown_0Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.AnimationsPointer = reader.ReadUInt64();
            this.AnimationsMapCapacity = reader.ReadUInt16();
            this.AnimationsMapEntries = reader.ReadUInt16();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();

            this.Animations = reader.ReadBlockAt<ResourcePointerArray64<AnimationMapEntry>>(
                this.AnimationsPointer,
                this.AnimationsMapCapacity
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.AnimationsPointer = (ulong)(this.Animations != null ? this.Animations.FilePosition : 0);
            this.AnimationsMapCapacity = (ushort)(this.Animations != null ? this.Animations.Count : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_04h);
            writer.Write(this.Unknown_08h);
            writer.Write(this.Unknown_0Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.AnimationsPointer);
            writer.Write(this.AnimationsMapCapacity);
            writer.Write(this.AnimationsMapEntries);
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
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimationMapEntry : ResourceSystemBlock
    {
        public override long BlockLength
        {
            get { return 32; }
        }

        public MetaHash Hash { get; set; }
        public uint Unknown_04h { get; set; }
        public ulong AnimationPtr { get; set; }
        public ulong NextEntryPtr { get; set; }
        public uint Unknown_18h { get; set; }
        public uint Unknown_1Ch { get; set; }

        public Animation Animation { get; set; }
        public AnimationMapEntry NextEntry { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Hash = new MetaHash(reader.ReadUInt32());
            this.Unknown_04h = reader.ReadUInt32();
            this.AnimationPtr = reader.ReadUInt64();
            this.NextEntryPtr = reader.ReadUInt64();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();

            this.Animation = reader.ReadBlockAt<Animation>(
                this.AnimationPtr
            );
            this.NextEntry = reader.ReadBlockAt<AnimationMapEntry>(
                this.NextEntryPtr
            );

            if (Animation != null)
            {
                if (Animation.Hash != 0)
                { }
                Animation.Hash = Hash;
            }
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.AnimationPtr = (ulong)(this.Animation != null ? this.Animation.FilePosition : 0);
            this.NextEntryPtr = (ulong)(this.NextEntry != null ? this.NextEntry.FilePosition : 0);

            writer.Write(this.Hash);
            writer.Write(this.Unknown_04h);
            writer.Write(this.AnimationPtr);
            writer.Write(this.NextEntryPtr);
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

        public override string ToString()
        {
            return Hash.ToString();
        }

    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class Animation : ResourceSystemBlock, IMetaXmlItem
    {
        public override long BlockLength
        {
            get { return 96; }
        }

        public uint VFT { get; set; }
        public uint Unknown_04h { get; set; } = 1;
        public uint Unused_08h { get; set; }
        public uint Unused_0Ch { get; set; }
        public byte Unknown_10h { get; set; }
        public byte Unknown_11h { get; set; } = 1;
        public ushort Unused_12h { get; set; }
        public ushort Frames { get; set; }
        public ushort SequenceFrameLimit { get; set; }
        public float Duration { get; set; }
        public MetaHash Unknown_1Ch { get; set; }
        public uint Unused_20h { get; set; }
        public uint Unused_24h { get; set; }
        public uint Unused_28h { get; set; }
        public uint Unused_2Ch { get; set; }
        public uint Unused_30h { get; set; }
        public uint Unused_34h { get; set; }
        public uint MaxSeqBlockLength { get; set; }
        public uint UsageCount { get; set; }
        public ResourcePointerList64<Sequence> Sequences { get; set; }
        public ResourceSimpleList64_s<AnimationBoneId> BoneIds { get; set; }

        public YcdFile Ycd { get; set; }

        public MetaHash Hash { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_04h = reader.ReadUInt32();
            this.Unused_08h = reader.ReadUInt32();
            this.Unused_0Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadByte();
            this.Unknown_11h = reader.ReadByte();
            this.Unused_12h = reader.ReadUInt16();
            this.Frames = reader.ReadUInt16();
            this.SequenceFrameLimit = reader.ReadUInt16();
            this.Duration = reader.ReadSingle();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.Unused_20h = reader.ReadUInt32();
            this.Unused_24h = reader.ReadUInt32();
            this.Unused_28h = reader.ReadUInt32();
            this.Unused_2Ch = reader.ReadUInt32();
            this.Unused_30h = reader.ReadUInt32();
            this.Unused_34h = reader.ReadUInt32();
            this.MaxSeqBlockLength = reader.ReadUInt32();
            this.UsageCount = reader.ReadUInt32();
            this.Sequences = reader.ReadBlock<ResourcePointerList64<Sequence>>();
            this.BoneIds = reader.ReadBlock<ResourceSimpleList64_s<AnimationBoneId>>();

            AssignSequenceBoneIds();

        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {

            writer.Write(this.VFT);
            writer.Write(this.Unknown_04h);
            writer.Write(this.Unused_08h);
            writer.Write(this.Unused_0Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_11h);
            writer.Write(this.Unused_12h);
            writer.Write(this.Frames);
            writer.Write(this.SequenceFrameLimit);
            writer.Write(this.Duration);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.Unused_20h);
            writer.Write(this.Unused_24h);
            writer.Write(this.Unused_28h);
            writer.Write(this.Unused_2Ch);
            writer.Write(this.Unused_30h);
            writer.Write(this.Unused_34h);
            writer.Write(this.MaxSeqBlockLength);
            writer.Write(this.UsageCount);
            writer.WriteBlock(this.Sequences);
            writer.WriteBlock(this.BoneIds);
        }

        public override Tuple<long, IResourceBlock>[] GetParts()
        {
            BuildSequencesData();

            return new Tuple<long, IResourceBlock>[] {
                new Tuple<long, IResourceBlock>(0x40, Sequences),
                new Tuple<long, IResourceBlock>(0x50, BoneIds)
            };
        }

        public void AssignSequenceBoneIds()
        {
            if (Sequences?.data_items != null)
            {
                foreach (var seq in Sequences.data_items)
                {
                    for (int i = 0; i < seq?.Sequences?.Length; i++)
                    {
                        if (i < BoneIds?.data_items?.Length)
                        {
                            seq.Sequences[i].BoneId = BoneIds.data_items[i];
                        }
                    }
                }
            }
        }

        public void CalculateMaxSeqBlockLength()
        {
            if (Sequences?.data_items != null)
            {
                uint maxSize = 0;
                foreach (var seq in Sequences.data_items)
                {
                    maxSize = Math.Max(maxSize, (uint)seq.BlockLength);
                }
                MaxSeqBlockLength = maxSize;
            }
        }

        public void BuildSequencesData()
        {
            AssignSequenceBoneIds();

            if (Sequences?.data_items != null)
            {
                foreach (var seq in Sequences.data_items)
                {
                    seq.BuildData();
                }
            }

            CalculateMaxSeqBlockLength();
        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.StringTag(sb, indent, "Hash", YcdXml.HashString(Hash));
            YcdXml.ValueTag(sb, indent, "Unknown10", Unknown_10h.ToString());
            YcdXml.ValueTag(sb, indent, "FrameCount", Frames.ToString());
            YcdXml.ValueTag(sb, indent, "SequenceFrameLimit", SequenceFrameLimit.ToString());
            YcdXml.ValueTag(sb, indent, "Duration", FloatUtil.ToString(Duration));
            YcdXml.StringTag(sb, indent, "Unknown1C", YcdXml.HashString(Unknown_1Ch));
            YcdXml.WriteItemArray(sb, BoneIds?.data_items, indent, "BoneIds");
            YcdXml.WriteItemArray(sb, Sequences?.data_items, indent, "Sequences");
        }
        public void ReadXml(XmlNode node)
        {
            Hash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Hash"));
            Unknown_10h = (byte)Xml.GetChildUIntAttribute(node, "Unknown10", "value");
            Frames = (ushort)Xml.GetChildUIntAttribute(node, "FrameCount", "value");
            SequenceFrameLimit = (ushort)Xml.GetChildUIntAttribute(node, "SequenceFrameLimit", "value");
            Duration = Xml.GetChildFloatAttribute(node, "Duration", "value");
            Unknown_1Ch = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Unknown1C"));

            BoneIds = new ResourceSimpleList64_s<AnimationBoneId>();
            BoneIds.data_items = XmlMeta.ReadItemArray<AnimationBoneId>(node, "BoneIds");

            Sequences = new ResourcePointerList64<Sequence>();
            Sequences.data_items = XmlMeta.ReadItemArrayNullable<Sequence>(node, "Sequences");

            AssignSequenceBoneIds();
        }

        public struct FramePosition
        {
            public int Frame0;
            public int Frame1;
            public float Alpha0;
            public float Alpha1;
        }
        public FramePosition GetFramePosition(float t)
        {
            bool ignoreLastFrame = true;

            FramePosition p = new FramePosition();
            var nframes = (ignoreLastFrame) ? (Frames - 1) : Frames;

            var curPos = (t / Duration) * nframes;
            p.Frame0 = ((ushort)curPos) % Frames;
            p.Frame1 = (p.Frame0 + 1);
            p.Alpha1 = (float)(curPos - Math.Floor(curPos));
            p.Alpha0 = 1.0f - p.Alpha1;

            return p;
        }
        public Vector4 EvaluateVector4(FramePosition frame, int boneIndex, bool interpolate)
        {
            var s = frame.Frame0 / SequenceFrameLimit;
            int f0 = frame.Frame0 % SequenceFrameLimit;
            int f1 = f0 + 1;
            var seq = Sequences.data_items[s];
            var aseq = seq.Sequences[boneIndex];
            var v0 = aseq.EvaluateVector(f0);
            var v1 = aseq.EvaluateVector(f1);
            var v = interpolate ? (v0 * frame.Alpha0) + (v1 * frame.Alpha1) : v0;
            return v;
        }
        public Quaternion EvaluateQuaternion(FramePosition frame, int boneIndex, bool interpolate)
        {
            var s = frame.Frame0 / SequenceFrameLimit;
            int f0 = frame.Frame0 % SequenceFrameLimit;
            int f1 = f0 + 1;
            var seq = Sequences.data_items[s];
            var aseq = seq.Sequences[boneIndex];
            var q0 = aseq.EvaluateQuaternion(f0);
            var q1 = aseq.EvaluateQuaternion(f1);
            var q = interpolate ? QuaternionExtension.FastLerp(q0, q1, frame.Alpha1) : q0;
            return q;
        }

        public int FindBoneIndex(ushort boneTag, byte track)
        {
            if (BoneIds?.data_items != null)
            {
                for (int i = 0; i < BoneIds.data_items.Length; i++)
                {
                    var b = BoneIds.data_items[i];
                    if ((b.BoneId == boneTag) && (b.Track == track)) return i;
                }
            }
            return -1;
        }

    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public struct AnimationBoneId : IMetaXmlItem
    {
        public ushort BoneId { get; set; }
        public byte Unk0 { get; set; }
        public byte Track { get; set; }

        public override string ToString()
        {
            return BoneId.ToString() + ": " + Unk0.ToString() + ", " + Track.ToString();
        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.ValueTag(sb, indent, "BoneId", BoneId.ToString());
            YcdXml.ValueTag(sb, indent, "Track", Track.ToString());
            YcdXml.ValueTag(sb, indent, "Unk0", Unk0.ToString());
        }
        public void ReadXml(XmlNode node)
        {
            BoneId = (ushort)Xml.GetChildUIntAttribute(node, "BoneId", "value");
            Track = (byte)Xml.GetChildUIntAttribute(node, "Track", "value");
            Unk0 = (byte)Xml.GetChildUIntAttribute(node, "Unk0", "value");
        }
    }

    public enum AnimChannelType : int
    {
        StaticQuaternion = 0,
        StaticVector3 = 1,
        StaticFloat = 2,
        RawFloat = 3,
        QuantizeFloat = 4,
        IndirectQuantizeFloat = 5,
        LinearFloat = 6,
        CachedQuaternion1 = 7,
        CachedQuaternion2 = 8,
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public abstract class AnimChannel : IMetaXmlItem
    {
        public AnimChannelType Type { get; set; }
        public int Sequence { get; set; }
        public int Index { get; set; }

        public int DataOffset { get; set; }
        public int FrameOffset { get; set; }

        public abstract void Read(AnimChannelDataReader reader);
        public virtual void Write(AnimChannelDataWriter writer)
        { }
        public virtual void ReadFrame(AnimChannelDataReader reader)
        { }
        public virtual void WriteFrame(AnimChannelDataWriter writer)
        { }

        public virtual int GetReferenceIndex()
        { return Index; }
        public virtual int GetFrameBits()
        { return 0; }

        public virtual float EvaluateFloat(int frame) => 0.0f;

        public void Associate(int sequence, int index)
        {
            Sequence = sequence;
            Index = index;
        }

        public virtual void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.ValueTag(sb, indent, "Type", Type.ToString());
        }
        public virtual void ReadXml(XmlNode node)
        {
        }

        public static AnimChannel ConstructChannel(AnimChannelType type)
        {
            switch (type)
            {
                case AnimChannelType.StaticQuaternion:
                    return new AnimChannelStaticQuaternion();
                case AnimChannelType.StaticVector3:
                    return new AnimChannelStaticVector3();
                case AnimChannelType.StaticFloat:
                    return new AnimChannelStaticFloat();
                case AnimChannelType.RawFloat:
                    return new AnimChannelRawFloat();
                case AnimChannelType.QuantizeFloat:
                    return new AnimChannelQuantizeFloat();
                case AnimChannelType.IndirectQuantizeFloat:
                    return new AnimChannelIndirectQuantizeFloat();
                case AnimChannelType.LinearFloat:
                    return new AnimChannelLinearFloat();
                case AnimChannelType.CachedQuaternion1:
                    return new AnimChannelCachedQuaternion(AnimChannelType.CachedQuaternion1);
                case AnimChannelType.CachedQuaternion2:
                    return new AnimChannelCachedQuaternion(AnimChannelType.CachedQuaternion2);
                default:
                    return null;
            }

        }

        public override string ToString()
        {
            return Sequence.ToString() + ": " + Index.ToString() + ": " + Type.ToString() + "   DataOffset: " + DataOffset.ToString() + "   FrameOffset: " + FrameOffset.ToString();
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelStaticFloat : AnimChannel
    {
        public float Value { get; set; }

        public AnimChannelStaticFloat()
        {
            Type = AnimChannelType.StaticFloat;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            Value = reader.ReadSingle();
        }
        public override void Write(AnimChannelDataWriter writer)
        {
            writer.Write(Value);
        }

        public override float EvaluateFloat(int frame)
        {
            return Value;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Value", FloatUtil.ToString(Value));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildFloatAttribute(node, "Value", "value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelStaticVector3 : AnimChannel
    {
        public Vector3 Value { get; set; }

        public AnimChannelStaticVector3()
        {
            Type = AnimChannelType.StaticVector3;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            Value = reader.ReadVector3();
        }
        public override void Write(AnimChannelDataWriter writer)
        {
            writer.Write(Value);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.SelfClosingTag(sb, indent, "Value " + FloatUtil.GetVector3XmlString(Value));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildVector3Attributes(node, "Value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelStaticQuaternion : AnimChannel
    {
        public Quaternion Value { get; set; }

        public AnimChannelStaticQuaternion()
        {
            Type = AnimChannelType.StaticQuaternion;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            var vec = reader.ReadVector3();

            Value = new Quaternion(
                vec,
                (float)Math.Sqrt(Math.Max(1.0f - vec.LengthSquared(), 0.0))
            );
        }
        public override void Write(AnimChannelDataWriter writer)
        {
            writer.Write(Value.ToVector4().XYZ());
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.SelfClosingTag(sb, indent, "Value " + FloatUtil.GetVector4XmlString(Value.ToVector4()));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = new Quaternion(Xml.GetChildVector4Attributes(node, "Value"));
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelIndirectQuantizeFloat : AnimChannel
    {
        public int FrameBits { get; set; }
        public int ValueBits { get; set; }
        public int NumInts { get; set; }
        public float Quantum { get; set; }
        public float Offset { get; set; }
        public float[] Values { get; set; }
        public uint[] ValueList { get; set; }
        public uint[] Frames { get; set; }

        public AnimChannelIndirectQuantizeFloat()
        {
            Type = AnimChannelType.IndirectQuantizeFloat;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            FrameBits = reader.ReadInt32();
            ValueBits = reader.ReadInt32();
            NumInts = reader.ReadInt32();
            Quantum = reader.ReadSingle();
            Offset = reader.ReadSingle();

            Frames = new uint[reader.NumFrames];

            var numValues0 = (NumInts * 32) / ValueBits;
            var numValues1 = (1u << FrameBits) - 1;
            var numValues = Math.Min(numValues0, numValues1);
            Values = new float[numValues];
            ValueList = new uint[numValues];
            reader.BitPosition = reader.Position * 8;
            for (int i = 0; i < numValues; i++)
            {
                uint bits = reader.ReadBits(ValueBits);
                Values[i] = (bits * Quantum) + Offset;
                ValueList[i] = bits;
            }
            reader.Position += NumInts * 4;

            if (FrameBits < 2)
            { }
            if (ValueBits < 3)
            { }

        }
        public override void Write(AnimChannelDataWriter writer)
        {
            var frameBits = Math.Max(writer.BitCount((uint)((Values?.Length ?? 1))), 2);
            FrameBits = frameBits;

            var valueCount = Values?.Length ?? 0;
            var valueList = new uint[valueCount];
            for (int i = 0; i < valueCount; i++)
            {
                var bits = GetQuanta(Values[i]);
                valueList[i] = bits;

            }
            var valueBits = Math.Max(writer.BitCount(valueList), 3);
            ValueBits = valueBits;

            writer.ResetBitstream();
            for (int i = 0; i < valueCount; i++)
            {
                var u = valueList[i];
                writer.WriteBits(u, ValueBits);
            }

            NumInts = writer.Bitstream.Count;

            writer.Write(FrameBits);
            writer.Write(ValueBits);
            writer.Write(NumInts);
            writer.Write(Quantum);
            writer.Write(Offset);
            writer.WriteBitstream();
        }

        public override void ReadFrame(AnimChannelDataReader reader)
        {
            Frames[reader.Frame] = reader.ReadFrameBits(FrameBits);
        }
        public override void WriteFrame(AnimChannelDataWriter writer)
        {
            writer.WriteFrameBits(Frames[writer.Frame], FrameBits);
        }

        public override int GetFrameBits()
        {
            return FrameBits;
        }

        private uint GetQuanta(float v)
        {
            var q = (v - Offset) / Quantum;
            return (uint)(q + 0.5f);
        }

        public override float EvaluateFloat(int frame)
        {
            if (Frames?.Length > 0) return Values[Frames[frame % Frames.Length]];
            return Offset;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Quantum", FloatUtil.ToString(Quantum));
            YcdXml.ValueTag(sb, indent, "Offset", FloatUtil.ToString(Offset));
            YcdXml.WriteRawArray(sb, Values, indent, "Values", "", FloatUtil.ToString, 10);
            YcdXml.WriteRawArray(sb, Frames, indent, "Frames", "", null, 10);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Quantum = Xml.GetChildFloatAttribute(node, "Quantum", "value");
            Offset = Xml.GetChildFloatAttribute(node, "Offset", "value");
            Values = Xml.GetChildRawFloatArray(node, "Values");
            Frames = Xml.GetChildRawUintArray(node, "Frames");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelQuantizeFloat : AnimChannel
    {
        public int ValueBits { get; set; }
        public float Quantum { get; set; }
        public float Offset { get; set; }
        public float[] Values { get; set; }
        public uint[] ValueList { get; set; }

        public AnimChannelQuantizeFloat()
        {
            Type = AnimChannelType.QuantizeFloat;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            ValueBits = reader.ReadInt32();
            Quantum = reader.ReadSingle();
            Offset = reader.ReadSingle();
            Values = new float[reader.NumFrames];
            ValueList = new uint[reader.NumFrames];

            if (ValueBits < 1)
            { }
        }
        public override void Write(AnimChannelDataWriter writer)
        {
            var valueCount = Values?.Length ?? 0;
            var valueList = new uint[valueCount];
            for (int i = 0; i < valueCount; i++)
            {
                var bits =  GetQuanta(Values[i]);
                valueList[i] = bits;

            }
            var valueBits = Math.Max(writer.BitCount(valueList), 1);
            ValueBits = valueBits;

            writer.Write(ValueBits);
            writer.Write(Quantum);
            writer.Write(Offset);
        }

        public override void ReadFrame(AnimChannelDataReader reader)
        {
            uint bits = reader.ReadFrameBits(ValueBits);
            float val = (bits * Quantum) + Offset;
            Values[reader.Frame] = val;
            ValueList[reader.Frame] = bits;
        }
        public override void WriteFrame(AnimChannelDataWriter writer)
        {
            uint bits = GetQuanta(Values[writer.Frame]);
            writer.WriteFrameBits(bits, ValueBits);
        }

        public override int GetFrameBits()
        {
            return ValueBits;
        }

        private uint GetQuanta(float v)
        {
            var q = (v - Offset) / Quantum;
            return (uint)(q + 0.5f);
        }

        public override float EvaluateFloat(int frame)
        {
            if (Values?.Length > 0) return Values[frame%Values.Length];
            return Offset;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Quantum", FloatUtil.ToString(Quantum));
            YcdXml.ValueTag(sb, indent, "Offset", FloatUtil.ToString(Offset));
            YcdXml.WriteRawArray(sb, Values, indent, "Values", "", FloatUtil.ToString, 10);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Quantum = Xml.GetChildFloatAttribute(node, "Quantum", "value");
            Offset = Xml.GetChildFloatAttribute(node, "Offset", "value");
            Values = Xml.GetChildRawFloatArray(node, "Values");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelLinearFloat : AnimChannel
    {
        private int NumInts { get; set; }
        private int Counts { get; set; }
        public float Quantum { get; set; }
        public float Offset { get; set; }

        private int Bit { get; set; }
        private int Count1 { get; set; }
        private int Count2 { get; set; }
        private int Count3 { get; set; }

        public float[] Values { get; set; }
        public int[] ValueList { get; set; }

        public AnimChannelLinearFloat()
        {
            Type = AnimChannelType.LinearFloat;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            NumInts = reader.ReadInt32();
            Counts = reader.ReadInt32();
            Quantum = reader.ReadSingle();
            Offset = reader.ReadSingle();

            Bit = (reader.Position * 8);
            Count1 = Counts & 0xFF;
            Count2 = (Counts >> 8) & 0xFF;
            Count3 = (Counts >> 16) & 0xFF;

            var streamLength = (reader.Data?.Length ?? 0) * 8;
            var numFrames = reader.NumFrames;
            var chunkSize = reader.ChunkSize;
            var numChunks = (ushort)((chunkSize + numFrames - 1) / chunkSize);
            var deltaOffset = Bit + (numChunks * (Count1 + Count2));

            reader.BitPosition = Bit;
            var chunkOffsets = new int[numChunks];
            var chunkValues = new int[numChunks];
            var frameValues = new float[numFrames];
            var frameBits = new int[numFrames];
            for (int i = 0; i < numChunks; i++)
            {
                chunkOffsets[i] = (Count1 > 0) ? (int)reader.ReadBits(Count1) : 0;
            }
            for (int i = 0; i < numChunks; i++)
            {
                chunkValues[i] = (Count2 > 0) ? (int)reader.ReadBits(Count2) : 0;
            }
            for (int i = 0; i < numChunks; i++)
            {
                var doffs = chunkOffsets[i] + deltaOffset;
                var value = chunkValues[i];
                var cframe = (i * chunkSize);
                reader.BitPosition = doffs;
                var inc = 0;
                for (int j = 0; j < chunkSize; j++)
                {
                    int frame = cframe + j;
                    if (frame >= numFrames) break;

                    frameValues[frame] = (value * Quantum) + Offset;
                    frameBits[frame] = value;

                    if ((j + 1) >= chunkSize) break;

                    var delta = (Count3 != 0) ? (int)reader.ReadBits(Count3) : 0;
                    var so = reader.BitPosition;
                    var maxso = streamLength;
                    uint b = 0;
                    while (b == 0)
                    {
                        b = reader.ReadBits(1);
                        if (reader.BitPosition >= maxso)
                        { break; }
                    }
                    delta |= ((reader.BitPosition - so - 1) << Count3);
                    if (delta != 0)
                    {
                        var sign = reader.ReadBits(1);
                        if (sign == 1)
                        {
                            delta = -delta;
                        }
                    }
                    inc += delta;
                    value += inc;
                }
            }
            Values = frameValues;
            ValueList = frameBits;

            reader.Position -= 16;
            reader.Position += NumInts * 4;
        }
        public override void Write(AnimChannelDataWriter writer)
        {
            var numFrames = writer.NumFrames;
            var numChunks = (ushort)((64 + numFrames - 1) / 64);
            byte chunkSize = 64;
            if (writer.ChunkSize != chunkSize)
            { writer.ChunkSize = chunkSize; }

            var valueCount = Values?.Length ?? 0;
            var valueList = new int[valueCount];
            for (int i = 0; i < valueCount; i++)
            {
                var bits = GetQuanta(Values[i]);
                valueList[i] = bits;

            }

            var chunkOffsets = new uint[numChunks];
            var chunkValues = new uint[numChunks];
            var chunkDeltas = new int[numChunks][];
            var chunkDeltaBits = new uint[numFrames];
            for (int i = 0; i < numChunks; i++)
            {
                var cframe = (i * chunkSize);
                var cvalue = (cframe < valueCount) ? valueList[cframe] : valueList[0];
                var cdeltas = new int[chunkSize];
                var cinc = 0;
                chunkValues[i] = (uint)cvalue;
                chunkDeltas[i] = cdeltas;
                for (int j = 1; j < chunkSize; j++)
                {
                    int frame = cframe + j;
                    if (frame >= numFrames) break;
                    var value = valueList[frame];
                    var inc = value - cvalue;
                    var delta = inc - cinc;
                    var deltaa = (uint)Math.Abs(delta);
                    cinc = inc;
                    cvalue = value;
                    cdeltas[j] = delta;
                    chunkDeltaBits[frame] = deltaa;
                }
            }
            Count3 = writer.BitCount(chunkDeltaBits);
            uint coffset = 0;
            for (int i = 0; i < numChunks; i++)
            {
                chunkOffsets[i] = coffset;
                var cdeltas = chunkDeltas[i];
                for (int j = 1; j < chunkSize; j++)
                {
                    var delta = cdeltas[j];
                    coffset += (uint)Count3 + ((delta < 0) ? 2u : 1u);
                }
            }
            Count1 = writer.BitCount(chunkOffsets);
            Count2 = writer.BitCount(chunkValues);

            writer.ResetBitstream();
            if (Count1 > 0)
            {
                for (int i = 0; i < numChunks; i++)
                {
                    writer.WriteBits(chunkOffsets[i], Count1);
                }
            }
            if (Count2 > 0)
            {
                for (int i = 0; i < numChunks; i++)
                {
                    writer.WriteBits(chunkValues[i], Count2);
                }
            }
            for (int i = 0; i < numChunks; i++)
            {
                var cdeltas = chunkDeltas[i];
                for (int j = 1; j < chunkSize; j++)
                {
                    var delta = cdeltas[j];
                    var deltaa = (uint)Math.Abs(delta);
                    writer.WriteBits(deltaa, Count3);
                    writer.WriteBits(1, 1);
                    if (delta < 0)
                    {
                        writer.WriteBits(1, 1);
                    }
                }
            }

            Counts = Count1 & 0xFF;
            Counts += (Count2 & 0xFF) << 8;
            Counts += (Count3 & 0xFF) << 16;
            NumInts = 4 + writer.Bitstream.Count;

            writer.Write(NumInts);
            writer.Write(Counts);
            writer.Write(Quantum);
            writer.Write(Offset);
            writer.WriteBitstream();
        }

        public override float EvaluateFloat(int frame)
        {
            if (Values?.Length > 0) return Values[frame % Values.Length];
            return Offset;
        }

        private int GetQuanta(float v)
        {
            var q = (v - Offset) / Quantum;
            return (int)(q + 0.5f);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {

            Type = AnimChannelType.QuantizeFloat;
            base.WriteXml(sb, indent);
            Type = AnimChannelType.LinearFloat;
            float minVal = float.MaxValue;
            for (int i = 0; i < Values.Length; i++)
            {
                minVal = Math.Min(minVal, Values[i]);
            }
            if (minVal != Offset)
            {
                Offset = minVal;
            }

            YcdXml.ValueTag(sb, indent, "Quantum", FloatUtil.ToString(Quantum));
            YcdXml.ValueTag(sb, indent, "Offset", FloatUtil.ToString(Offset));
            YcdXml.WriteRawArray(sb, Values, indent, "Values", "", FloatUtil.ToString, 10);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Quantum = Xml.GetChildFloatAttribute(node, "Quantum", "value");
            Offset = Xml.GetChildFloatAttribute(node, "Offset", "value");
            Values = Xml.GetChildRawFloatArray(node, "Values");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelRawFloat : AnimChannel
    {
        public float[] Values { get; set; }

        public AnimChannelRawFloat()
        {
            Type = AnimChannelType.RawFloat;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            Values = new float[reader.NumFrames];
        }
        public override void Write(AnimChannelDataWriter writer)
        {
        }

        public override void ReadFrame(AnimChannelDataReader reader)
        {
            uint bits = reader.ReadFrameBits(32);
            float v = MetaTypes.ConvertData<float>(MetaTypes.ConvertToBytes(bits));
            Values[reader.Frame] = v;
        }
        public override void WriteFrame(AnimChannelDataWriter writer)
        {
            float v = Values[writer.Frame];
            var b = BitConverter.GetBytes(v);
            var bits = BitConverter.ToUInt32(b, 0);
            writer.WriteFrameBits(bits, 32);
        }

        public override int GetFrameBits()
        {
            return 32;
        }

        public override float EvaluateFloat(int frame)
        {
            if (Values?.Length > 0) return Values[frame % Values.Length];
            return base.EvaluateFloat(frame);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.WriteRawArray(sb, Values, indent, "Values", "", FloatUtil.ToString, 10);
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Values = Xml.GetChildRawFloatArray(node, "Values");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimChannelCachedQuaternion : AnimChannel
    {
        private AnimChannelDataReader blockStream;

        private float[] valueCache;

        public float[] Values
        {
            get
            {
                if (valueCache != null)
                {
                    return valueCache;
                }

                valueCache = new float[blockStream.NumFrames];

                var channels = new AnimChannel[3];
                var ch = 0;

                for (int i = 0; i < 4; i++)
                {
                    if (i != 3)
                    {
                        channels[ch] = blockStream.Sequences[Sequence].Channels[i];
                        ch++;
                    }
                }

                for (int i = 0; i < valueCache.Length; i++)
                {
                    var vec = new Vector3(
                        channels[0].EvaluateFloat(i),
                        channels[1].EvaluateFloat(i),
                        channels[2].EvaluateFloat(i)
                    );

                    valueCache[i] = (float)Math.Sqrt(Math.Max(1.0f - vec.LengthSquared(), 0.0));
                }

                return valueCache;
            }
        }

        public int QuatIndex { get; set; }

        public AnimChannelCachedQuaternion(AnimChannelType type)
        {
            Type = type;
        }

        public override void Read(AnimChannelDataReader reader)
        {
            this.blockStream = reader;
        }

        public override int GetReferenceIndex()
        {
            return QuatIndex;
        }

        public override float EvaluateFloat(int frame)
        {
            if (Values?.Length > 0) return Values[frame % Values.Length];
            return 0.0f;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "QuatIndex", QuatIndex.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            QuatIndex = Xml.GetChildIntAttribute(node, "QuatIndex", "value");
        }
    }
    public class AnimChannelDataReader
    {
        public byte[] Data { get; set; }
        public ushort NumFrames { get; set; }
        public byte ChunkSize { get; set; }
        public int Position { get; set; }
        public int Frame { get; set; }
        public uint FrameOffset { get; set; }
        public ushort FrameLength { get; set; }
        public int ChannelListOffset { get; set; }
        public int ChannelDataOffset { get; set; }
        public int ChannelFrameOffset { get; set; }
        public AnimSequence[] Sequences { get; set; }
        public int BitPosition { get; set; }

        public AnimChannelDataReader(byte[] data, ushort numFrames, byte chunkSize, uint frameOffset, ushort frameLength)
        {
            Data = data;
            NumFrames = numFrames;
            ChunkSize = chunkSize;
            Position = 0;
            Frame = 0;
            FrameOffset = frameOffset;
            FrameLength = frameLength;
            ChannelListOffset = (int)FrameOffset + (FrameLength * NumFrames);
            ChannelDataOffset = ChannelListOffset + (9 * 2);
            ChannelFrameOffset = 0;
        }

        public int ReadInt32()
        {
            int i = BitConverter.ToInt32(Data, Position);
            Position += 4;
            return i;
        }
        public float ReadSingle()
        {
            float f = BitConverter.ToSingle(Data, Position);
            Position += 4;
            return f;
        }
        public Vector3 ReadVector3()
        {
            var v = new Vector3();
            v.X = BitConverter.ToSingle(Data, Position);
            v.Y = BitConverter.ToSingle(Data, Position + 4);
            v.Z = BitConverter.ToSingle(Data, Position + 8);
            Position += 12;
            return v;
        }

        public uint GetBits(int startBit, int length)
        {
            if (startBit < 0)
            { return 0; }
            int startByte = startBit / 8;
            int bitOffset = startBit % 8;
            uint result = 0;
            int shift = -bitOffset;
            int curByte = startByte;
            int bitsRemaining = length;
            while (bitsRemaining > 0)
            {
                var b = (curByte < Data.Length) ? (uint)Data[curByte++] : 0;
                var sb = (shift < 0) ? (b >> -shift) : (b << shift);
                var bm = ((1u << Math.Min(bitsRemaining, 8)) - 1u) << (Math.Max(shift, 0));
                var mb = (sb & bm);
                result += mb;
                bitsRemaining -= (8 + Math.Min(shift, 0));
                shift += 8;
            }
            return result;

        }
        public uint ReadBits(int length)
        {
            uint bits = GetBits(BitPosition, length);
            BitPosition += length;
            return bits;
        }

        public ushort ReadChannelCount()
        {
            ushort channelCount = BitConverter.ToUInt16(Data, ChannelListOffset);
            ChannelListOffset += 2;
            return channelCount;
        }
        public ushort ReadChannelDataBits()
        {
            ushort channelDataBit = BitConverter.ToUInt16(Data, ChannelDataOffset);
            ChannelDataOffset += 2;
            return channelDataBit;
        }
        public byte[] ReadChannelDataBytes(int n)
        {
            var r = new byte[n];
            Buffer.BlockCopy(Data, ChannelDataOffset, r, 0, n);
            ChannelDataOffset += n;
            return r;
        }

        public void AlignChannelDataOffset(int channelCount)
        {
            int remainder = channelCount % 4;
            if (remainder > 0)
            {
                int addamt = (4 - remainder) * 2;
                ChannelDataOffset += addamt;
            }
        }

        public void BeginFrame(int f)
        {
            Frame = f;
            ChannelFrameOffset = (int)((FrameOffset + (FrameLength * f)) * 8);
        }
        public uint ReadFrameBits(int n)
        {
            uint b = GetBits(ChannelFrameOffset, n);
            ChannelFrameOffset += n;
            return b;
        }

    }
    public class AnimChannelDataWriter
    {
        public int ChannelListOffset { get; set; }
        public int ChannelItemOffset { get; set; }
        public int ChannelFrameOffset { get; set; }
        public int Position { get; set; }
        public int Frame { get; set; }
        public ushort NumFrames { get; set; }
        public byte ChunkSize { get; set; }
        public ushort FrameLength { get; set; } = 0;

        MemoryStream ChannelListStream = new MemoryStream();
        MemoryStream ChannelItemStream = new MemoryStream();
        MemoryStream MainStream = new MemoryStream();
        BinaryWriter ChannelListWriter = null;
        BinaryWriter ChannelItemWriter = null;
        BinaryWriter MainWriter = null;
        public List<uint> ChannelFrameStream { get; private set; } = new List<uint>();
        public List<uint[]> ChannelFrames { get; private set; } = new List<uint[]>();

        public List<uint> Bitstream { get; private set; } = new List<uint>();
        public int BitstreamPos { get; set; } = 0;

        public AnimChannelDataWriter(ushort numFrames)
        {
            Position = 0;
            Frame = 0;
            NumFrames = numFrames;
            ChunkSize = 0;
            ChannelListWriter = new BinaryWriter(ChannelListStream);
            ChannelItemWriter = new BinaryWriter(ChannelItemStream);
            MainWriter = new BinaryWriter(MainStream);
        }

        public void WriteChannelListData(ushort c)
        {
            ChannelListWriter.Write(c);
            ChannelListOffset += 2;
        }
        public void WriteChannelItemData(ushort c)
        {
            ChannelItemWriter.Write(c);
            ChannelItemOffset += 2;
        }
        public void AlignChannelItemData(int channelCount, int sequenceCount)
        {
            int remainder = channelCount % 4;
            if (remainder > 0)
            {
                ushort writeval = (ushort)(sequenceCount << 2);
                int addamt = (4 - remainder);
                for (int i = 0; i < addamt; i++)
                {
                    WriteChannelItemData(writeval);
                }
            }
        }
        public void WriteChannelItemDataBytes(byte[] data)
        {
            if (data?.Length > 0)
            {
                ChannelItemWriter.Write(data);
                ChannelItemOffset += data.Length;
            }
        }

        public void Write(int i)
        {
            MainWriter.Write(i);
            Position += 4;
        }
        public void Write(float f)
        {
            MainWriter.Write(f);
            Position += 4;
        }
        public void Write(Vector3 v)
        {
            MainWriter.Write(v.X);
            MainWriter.Write(v.Y);
            MainWriter.Write(v.Z);
            Position += 12;
        }

        public void BeginFrame(int f)
        {
            Frame = f;
            ChannelFrameStream.Clear();
            ChannelFrameOffset = 0;
        }
        public void WriteFrameBits(uint bits, int n)
        {
            WriteToBitstream(ChannelFrameStream, ChannelFrameOffset, bits, n);
            ChannelFrameOffset += n;
        }
        public void EndFrame()
        {
            FrameLength = Math.Max(FrameLength, (ushort)(ChannelFrameStream.Count * 4));
            ChannelFrames.Add(ChannelFrameStream.ToArray());
            ChannelFrameStream.Clear();
        }

        public int BitCount(uint bits)
        {
            int bc = 0;
            for (int i = 0; i < 32; i++)
            {
                uint mask = 1u << i;
                if ((bits & mask) > 0) bc = (i + 1);
            }
            return bc;
        }
        public int BitCount(uint[] values)
        {
            uint maxValue = 0;
            for (int i = 0; i < values?.Length; i++)
            {
                maxValue = Math.Max(maxValue, values[i]);
            }
            return BitCount(maxValue);
        }

        public void ResetBitstream()
        {
            Bitstream.Clear();
            BitstreamPos = 0;
        }
        public void WriteBits(uint bits, int n)
        {
            WriteToBitstream(Bitstream, BitstreamPos, bits, n);
            BitstreamPos += n;
        }
        public void WriteBitstream()
        {
            for (int i = 0; i < Bitstream.Count; i++)
            {
                MainWriter.Write(Bitstream[i]);
            }
            Position += (Bitstream.Count * 4);
        }

        private void WriteToBitstream(List<uint> stream, int offset, uint bits, int n)
        {
            if (stream == null) return;

            uint mask = (uint)((1L << n) - 1);
            uint masked = bits & mask;
            if (bits != masked)
            { }

            int soffset = offset % 32;
            int sindex = offset / 32;
            while (sindex >= stream.Count) stream.Add(0);
            uint sval = stream[sindex];
            uint sbits = bits << soffset;
            stream[sindex] = sval + sbits;

            int endbit = (soffset + n) - 32;
            if (endbit > 0)
            {
                int eindex = sindex + 1;
                while (eindex >= stream.Count) stream.Add(0);
                int eoffset = 32 - soffset;
                uint eval = stream[eindex];
                uint ebits = bits >> eoffset;
                stream[eindex] = eval + ebits;
            }

        }

        public byte[] GetStreamData(MemoryStream ms)
        {
            var length = (int)ms.Length;
            var data = new byte[length];
            ms.Flush();
            ms.Position = 0;
            ms.Read(data, 0, length);
            return data;
        }
        public byte[] GetChannelListDataBytes()
        {
            return GetStreamData(ChannelListStream);
        }
        public byte[] GetChannelItemDataBytes()
        {
            return GetStreamData(ChannelItemStream);
        }
        public byte[] GetMainDataBytes()
        {
            return GetStreamData(MainStream);
        }
        public byte[] GetFrameDataBytes()
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            var frameUintCount = FrameLength / 4;
            for (int i = 0; i < ChannelFrames.Count; i++)
            {
                var frameData = ChannelFrames[i];
                for (int f = 0; f < frameUintCount; f++)
                {
                    bw.Write((f < frameData.Length) ? frameData[f] : 0);
                }
            }
            return GetStreamData(ms);
        }

    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class AnimSequence : IMetaXmlItem
    {
        public AnimChannel[] Channels { get; set; }
        public bool IsType7Quat { get; internal set; }

        public AnimationBoneId BoneId { get; set; }

        public Quaternion EvaluateQuaternionType7(int frame)
        {
            if (!IsType7Quat)
            {
                return new Quaternion(
                    Channels[0].EvaluateFloat(frame),
                    Channels[1].EvaluateFloat(frame),
                    Channels[2].EvaluateFloat(frame),
                    Channels[3].EvaluateFloat(frame)
                );
            }

            var t7 = Channels[3] as AnimChannelCachedQuaternion;
            if (t7 == null) t7 = Channels[4] as AnimChannelCachedQuaternion;

            var x = Channels[0].EvaluateFloat(frame);
            var y = Channels[1].EvaluateFloat(frame);
            var z = Channels[2].EvaluateFloat(frame);
            var normalized = t7.EvaluateFloat(frame);

            switch (t7.QuatIndex)
            {
                case 0:
                    return new Quaternion(normalized, x, y, z);
                case 1:
                    return new Quaternion(x, normalized, y, z);
                case 2:
                    return new Quaternion(x, y, normalized, z);
                case 3:
                    return new Quaternion(x, y, z, normalized);
                default:
                    return Quaternion.Identity;
            }
        }

        public Quaternion EvaluateQuaternion(int frame)
        {
            if (IsType7Quat) return EvaluateQuaternionType7(frame);
            return EvaluateVector(frame).ToQuaternion();
        }

        public Vector4 EvaluateVector(int frame)
        {
            if (Channels == null) return Vector4.Zero;
            if (IsType7Quat) return Quaternion.Normalize(EvaluateQuaternionType7(frame)).ToVector4();
            var v = Vector4.Zero;
            int c = 0;
            for (int i = 0; i < Channels.Length; i++)
            {
                if (c >= 4) break;
                var channel = Channels[i];
                var sv3c = channel as AnimChannelStaticVector3;
                var ssqc = channel as AnimChannelStaticQuaternion;
                if (sv3c != null)
                {
                    for (int n = 0; n < 3; n++)
                    {
                        if ((c + n) >= 4) break;
                        v[c + n] = sv3c.Value[n];
                    }
                    c += 3;
                }
                else if (ssqc != null)
                {
                    for (int n = 0; n < 4; n++)
                    {
                        if ((c + n) >= 4) break;
                        v[c + n] = ssqc.Value[n];
                    }
                    c += 4;
                }
                else
                {
                    v[c] = channel.EvaluateFloat(frame);
                    c++;
                }
            }
            return v;
        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.WriteItemArray(sb, Channels, indent, "Channels");
        }
        public void ReadXml(XmlNode node)
        {

            var chansNode = node.SelectSingleNode("Channels");
            if (chansNode != null)
            {
                var inodes = chansNode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var clist = new List<AnimChannel>();
                    foreach (XmlNode inode in inodes)
                    {
                        var type = Xml.GetEnumValue<AnimChannelType>(Xml.GetChildStringAttribute(inode, "Type", "value"));
                        var c = AnimChannel.ConstructChannel(type);
                        c.ReadXml(inode);
                        clist.Add(c);
                    }
                    Channels = clist.ToArray();
                }
            }

        }

        public override string ToString()
        {
            return "AnimSequence: " + (Channels?.Length??0).ToString() + " channels";
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class SequenceRootChannelRef : IMetaXmlItem
    {
        public byte[] Bytes { get; set; }
        public byte ChannelType { get { return Bytes[0]; } set { Bytes[0] = value; } }
        public byte ChannelIndex { get { return Bytes[1]; } set { Bytes[1] = value; } }
        public ushort DataIntOffset
        {
            get { return (ushort)(Bytes[2] + (Bytes[3] << 8)); }
            set
            {
                Bytes[2] = (byte)(value & 0xFF);
                Bytes[3] = (byte)((value >> 8) & 0xFF);
            }
        }
        public ushort FrameBitOffset
        {
            get { return (ushort)(Bytes[4] + (Bytes[5] << 8)); }
            set
            {
                Bytes[4] = (byte)(value & 0xFF);
                Bytes[5] = (byte)((value >> 8) & 0xFF);
            }
        }

        public SequenceRootChannelRef()
        {
            Bytes = new byte[6];
        }
        public SequenceRootChannelRef(AnimChannelType type, int channelIndex)
        {
            Bytes = new byte[6];
            ChannelType = (byte)type;
            ChannelIndex = (byte)channelIndex;
        }
        public SequenceRootChannelRef(byte[] bytes)
        {
            Bytes = bytes;
        }
        public override string ToString()
        {
            if (Bytes?.Length >= 6)
            {
                return ChannelType.ToString() + ", " + ChannelIndex.ToString() + ", " + DataIntOffset.ToString() + ", " + FrameBitOffset.ToString();
            }
            return "(empty)";
        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.WriteRawArray(sb, Bytes, indent, "Bytes", "", YcdXml.FormatHexByte, 6);
        }
        public void ReadXml(XmlNode node)
        {
            Bytes = Xml.GetChildRawByteArray(node, "Bytes");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class Sequence : ResourceSystemBlock, IMetaXmlItem
    {
        public override long BlockLength
        {
            get { return 32 + (Data?.Length ?? 0); }
        }

        public MetaHash Unknown_00h { get; set; }
        public uint DataLength { get; set; }
        public uint Unused_08h { get; set; }
        public uint FrameOffset { get; set; }
        public uint RootMotionRefsOffset { get; set; }
        public ushort Unused_14h { get; set; }
        public ushort NumFrames { get; set; }
        public ushort FrameLength { get; set; }
        public ushort IndirectQuantizeFloatNumInts { get; set; }
        public ushort QuantizeFloatValueBits { get; set; }
        public byte ChunkSize { get; set; }
        public byte RootMotionRefCounts { get; set; }
        public byte[] Data { get; set; }

        public AnimSequence[] Sequences { get; set; }

        public SequenceRootChannelRef[] RootPositionRefs { get; set; }
        public SequenceRootChannelRef[] RootRotationRefs { get; set; }
        public int RootPositionRefCount
        {
            get { return (RootMotionRefCounts >> 4) & 0xF; }
            set
            {
                var rrc = RootMotionRefCounts & 0xF;
                RootMotionRefCounts = (byte)(rrc + ((value & 0xF) << 4));
            }
        }
        public int RootRotationRefCount
        {
            get { return RootMotionRefCounts & 0xF; }
            set
            {
                var rpc = (RootMotionRefCounts >> 4) & 0xF;
                RootMotionRefCounts = (byte)(rpc + (value & 0xF));
            }
        }

        class AnimChannelListItem
        {
            public int Sequence;
            public int Index;
            public AnimChannel Channel;
            public AnimChannelListItem(int seq, int ind, AnimChannel channel)
            {
                Sequence = seq;
                Index = ind;
                Channel = channel;
            }
        }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Unknown_00h = reader.ReadUInt32();
            this.DataLength = reader.ReadUInt32();
            this.Unused_08h = reader.ReadUInt32();
            this.FrameOffset = reader.ReadUInt32();
            this.RootMotionRefsOffset = reader.ReadUInt32();
            this.Unused_14h = reader.ReadUInt16();
            this.NumFrames = reader.ReadUInt16();
            this.FrameLength = reader.ReadUInt16();
            this.IndirectQuantizeFloatNumInts = reader.ReadUInt16();
            this.QuantizeFloatValueBits = reader.ReadUInt16();
            this.ChunkSize = reader.ReadByte();
            this.RootMotionRefCounts = reader.ReadByte();

            this.Data = reader.ReadBytes((int)DataLength);

            ParseData();

        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {

            writer.Write(this.Unknown_00h);
            writer.Write(this.DataLength);
            writer.Write(this.Unused_08h);
            writer.Write(this.FrameOffset);
            writer.Write(this.RootMotionRefsOffset);
            writer.Write(this.Unused_14h);
            writer.Write(this.NumFrames);
            writer.Write(this.FrameLength);
            writer.Write(this.IndirectQuantizeFloatNumInts);
            writer.Write(this.QuantizeFloatValueBits);
            writer.Write(this.ChunkSize);
            writer.Write(this.RootMotionRefCounts);
            writer.Write(this.Data);
        }

        public override string ToString()
        {
            return Unknown_00h.ToString() + ": " + DataLength.ToString();
        }

        public void ParseData()
        {
            var reader = new AnimChannelDataReader(Data, NumFrames, ChunkSize, FrameOffset, FrameLength);
            var channelList = new List<AnimChannelListItem>();
            var channelLists = new AnimChannel[9][];
            var frameOffset = 0;
            for (int i = 0; i < 9; i++)
            {
                var ctype = (AnimChannelType)i;
                int channelCount = reader.ReadChannelCount();
                var channels = new AnimChannel[channelCount];
                for (int c = 0; c < channelCount; c++)
                {
                    var channel = AnimChannel.ConstructChannel(ctype);
                    var channelDataBit = reader.ReadChannelDataBits();
                    if (channel != null)
                    {
                        channel.DataOffset = reader.Position / 4;
                        channel.Read(reader);
                        channels[c] = channel;
                        var sequence = channelDataBit >> 2;
                        var index = channelDataBit & 3;
                        if (channel is AnimChannelCachedQuaternion t7)
                        {
                            t7.QuatIndex = index;
                            index = (channel.Type == AnimChannelType.CachedQuaternion1) ? 3 : 4;
                        }
                        channel.Associate(sequence, index);
                        channelList.Add(new AnimChannelListItem(sequence, index, channel));
                        channel.FrameOffset = frameOffset;
                        frameOffset += channel.GetFrameBits();
                    }
                }
                reader.AlignChannelDataOffset(channelCount);
                channelLists[i] = channels;
            }

            for (int f = 0; f < NumFrames; f++)
            {
                reader.BeginFrame(f);
                for (int i = 0; i < 9; i++)
                {
                    var channels = channelLists[i];
                    for (int c = 0; c < channels.Length; c++)
                    {
                        var channel = channels[c];
                        channel?.ReadFrame(reader);
                    }
                }
            }

            Sequences = new AnimSequence[channelList.Max(a => a.Sequence) + 1];
            for (int i = 0; i < Sequences.Length; i++)
            {
                Sequences[i] = new AnimSequence();

                var thisSeq = channelList.Where(a => a.Sequence == i);
                if (thisSeq.Count() == 0)
                { continue; }

                Sequences[i].Channels = new AnimChannel[thisSeq.Max(a => a.Index) + 1];

                for (int j = 0; j < Sequences[i].Channels.Length; j++)
                {
                    Sequences[i].Channels[j] = thisSeq.FirstOrDefault(a => a.Index == j)?.Channel;

                    if (Sequences[i].Channels[j].Type == AnimChannelType.CachedQuaternion1)
                    {
                        Sequences[i].IsType7Quat = true;
                    }
                }
            }

            reader.Sequences = Sequences;

            int numPosRefs = RootPositionRefCount;
            int numRotRefs = RootRotationRefCount;
            if (numPosRefs > 0)
            {
                RootPositionRefs = new SequenceRootChannelRef[numPosRefs];
                for (int i = 0; i < numPosRefs; i++)
                {
                    var pref = new SequenceRootChannelRef(reader.ReadChannelDataBytes(6));
                    RootPositionRefs[i] = pref;
                }
            }
            if (numRotRefs > 0)
            {
                RootRotationRefs = new SequenceRootChannelRef[numRotRefs];
                for (int i = 0; i < numRotRefs; i++)
                {
                    var rref = new SequenceRootChannelRef(reader.ReadChannelDataBytes(6));
                    RootRotationRefs[i] = rref;
                }
            }
            if (reader.ChannelDataOffset != Data.Length)
            {
                var brem = Data.Length - reader.ChannelDataOffset;
            }

        }

        public void BuildData()
        {

            if (Sequences == null) return;

            var writer = new AnimChannelDataWriter(NumFrames);

            var channelLists = new List<AnimChannel>[9];

            for (int s = 0; s < Sequences.Length; s++)
            {
                var seq = Sequences[s];
                if (seq?.Channels == null) continue;
                for (int c = 0; c < seq.Channels.Length; c++)
                {
                    var chan = seq.Channels[c];
                    if (chan == null) continue;
                    int typeid = (int)chan.Type;
                    if ((typeid < 0) || (typeid >= 9))
                    { continue; }
                    var chanList = channelLists[typeid];
                    if (chanList == null)
                    {
                        chanList = new List<AnimChannel>();
                        channelLists[typeid] = chanList;
                    }
                    if (chan is AnimChannelCachedQuaternion accq)
                    {
                        chan.Index = accq.QuatIndex;
                    }
                    chanList.Add(chan);
                }
            }

            for (int i = 0; i < 9; i++)
            {
                var channelList = channelLists[i];
                var channelCount = (ushort)(channelList?.Count ?? 0);
                writer.WriteChannelListData(channelCount);
                for (int c = 0; c < channelCount; c++)
                {
                    var channel = channelList[c];
                    var channelDataBit = (ushort)((channel?.Index ?? 0) + ((channel?.Sequence ?? 0) << 2));
                    writer.WriteChannelItemData(channelDataBit);
                    if (channel != null)
                    {
                        channel.DataOffset = writer.Position / 4;
                        channel?.Write(writer);
                    }
                }
                writer.AlignChannelItemData(channelCount, Sequences.Length);
            }

            for (int f = 0; f < NumFrames; f++)
            {
                writer.BeginFrame(f);
                for (int i = 0; i < 9; i++)
                {
                    var channelList = channelLists[i];
                    var channelCount = (ushort)(channelList?.Count ?? 0);
                    for (int c = 0; c < channelCount; c++)
                    {
                        var channel = channelList[c];
                        channel?.WriteFrame(writer);
                    }
                }
                writer.EndFrame();
            }

            var frameOffset = 0;
            for (int i = 0; i < channelLists.Length; i++)
            {
                var chanList = channelLists[i];
                if (chanList == null) continue;
                for (int c = 0; c < chanList.Count; c++)
                {
                    var chan = chanList[c];
                    if (chan == null) continue;
                    chan.FrameOffset = frameOffset;
                    frameOffset += chan.GetFrameBits();
                }
            }

            UpdateRootMotionRefs();
            if (RootPositionRefs != null)
            {
                for (int i = 0; i < RootPositionRefs.Length; i++)
                {
                    writer.WriteChannelItemDataBytes(RootPositionRefs[i].Bytes);
                }
            }
            if (RootRotationRefs != null)
            {
                for (int i = 0; i < RootRotationRefs.Length; i++)
                {
                    writer.WriteChannelItemDataBytes(RootRotationRefs[i].Bytes);
                }
            }

            var mainData = writer.GetMainDataBytes();
            var frameData = writer.GetFrameDataBytes();
            var channelListData = writer.GetChannelListDataBytes();
            var channelItemData = writer.GetChannelItemDataBytes();

            var dataLen = mainData.Length + frameData.Length + channelListData.Length + channelItemData.Length;
            var data = new byte[dataLen];
            var curpos = 0;
            Buffer.BlockCopy(mainData, 0, data, 0, mainData.Length); curpos += mainData.Length;
            Buffer.BlockCopy(frameData, 0, data, curpos, frameData.Length); curpos += frameData.Length;
            Buffer.BlockCopy(channelListData, 0, data, curpos, channelListData.Length); curpos += channelListData.Length;
            Buffer.BlockCopy(channelItemData, 0, data, curpos, channelItemData.Length);

            Data = data;
            DataLength = (uint)data.Length;
            FrameOffset = (uint)mainData.Length;
            FrameLength = writer.FrameLength;
            ChunkSize = (writer.ChunkSize > 0) ? writer.ChunkSize : (byte)255;
            QuantizeFloatValueBits = GetQuantizeFloatValueBits();
            IndirectQuantizeFloatNumInts = GetIndirectQuantizeFloatNumInts();
            RootMotionRefCounts = (byte)((((uint)(RootPositionRefs?.Length??0))<<4) | ((uint)(RootRotationRefs?.Length ?? 0)));
            RootMotionRefsOffset = (uint)(BlockLength - ((RootPositionRefCount + RootRotationRefCount) * 6));
        }

        public void AssociateSequenceChannels()
        {
            if (Sequences == null) return;
            for (int s = 0; s < Sequences.Length; s++)
            {
                var seq = Sequences[s];
                if (seq?.Channels == null) continue;
                for (int c = 0; c < seq.Channels.Length; c++)
                {
                    var chan = seq.Channels[c];
                    chan?.Associate(s, c);
                }
            }
        }

        public ushort GetQuantizeFloatValueBits()
        {
            int b = 0;
            foreach (var seq in Sequences)
            {
                foreach (var chan in seq.Channels)
                {
                    if (chan.Type == AnimChannelType.QuantizeFloat)
                    {
                        var acqf = chan as AnimChannelQuantizeFloat;
                        b += acqf.ValueBits;
                    }
                }
            }
            return (ushort)b;
        }
        public ushort GetIndirectQuantizeFloatNumInts()
        {
            int b = 0;
            foreach (var seq in Sequences)
            {
                foreach (var chan in seq.Channels)
                {
                    if (chan.Type == AnimChannelType.IndirectQuantizeFloat)
                    {
                        var acif = chan as AnimChannelIndirectQuantizeFloat;
                        b += acif.NumInts+5;
                    }
                }
            }
            return (ushort)b;
        }

        public void UpdateRootMotionRefs()
        {

            var newPosRefs = new List<SequenceRootChannelRef>();
            var newRotRefs = new List<SequenceRootChannelRef>();

            for (int i = 0; i < Sequences.Length; i++)
            {
                var seq = Sequences[i];
                if (seq == null) continue;
                if (seq.BoneId.Track == 5)
                {
                    for (var c = 0; c < seq.Channels?.Length; c++)
                    {
                        var chan = seq.Channels[c];
                        var newPosRef = new SequenceRootChannelRef(chan.Type, chan.GetReferenceIndex());
                        newPosRef.DataIntOffset = (ushort)chan.DataOffset;
                        newPosRef.FrameBitOffset = (ushort)chan.FrameOffset;
                        newPosRefs.Add(newPosRef);
                    }
                }
                if (seq.BoneId.Track == 6)
                {
                    for (var c = 0; c < seq.Channels?.Length; c++)
                    {
                        var chan = seq.Channels[c];
                        var newRotRef = new SequenceRootChannelRef(chan.Type, chan.GetReferenceIndex());
                        newRotRef.DataIntOffset = (ushort)chan.DataOffset;
                        newRotRef.FrameBitOffset = (ushort)chan.FrameOffset;
                        newRotRefs.Add(newRotRef);
                    }
                }
            }

            int compare(SequenceRootChannelRef a, SequenceRootChannelRef b)
            {
                var v1 = a.ChannelType.CompareTo(b.ChannelType);
                if (v1 != 0) return v1;
                var v2 = a.ChannelIndex.CompareTo(b.ChannelIndex);
                if (v2 != 0) return v2;
                return 0;
            }
            newPosRefs.Sort((a, b) => { return compare(a, b); });
            newRotRefs.Sort((a, b) => { return compare(a, b); });

            RootPositionRefs = (newPosRefs.Count > 0) ? newPosRefs.ToArray() : null;
            RootRotationRefs = (newRotRefs.Count > 0) ? newRotRefs.ToArray() : null;

        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.StringTag(sb, indent, "Hash", YcdXml.HashString(Unknown_00h));
            YcdXml.ValueTag(sb, indent, "FrameCount", NumFrames.ToString());
            YcdXml.WriteItemArray(sb, Sequences, indent, "SequenceData");
        }
        public void ReadXml(XmlNode node)
        {
            Unknown_00h = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Hash"));
            NumFrames = (ushort)Xml.GetChildUIntAttribute(node, "FrameCount", "value");
            Sequences = XmlMeta.ReadItemArray<AnimSequence>(node, "SequenceData");

            AssociateSequenceChannels();
        }

    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipMapEntry : ResourceSystemBlock
    {
        public override long BlockLength
        {
            get { return 32; }
        }

        public MetaHash Hash { get; set; }
        public uint Unknown_04h { get; set; }
        public ulong ClipPointer { get; set; }
        public ulong NextPointer { get; set; }
        public uint Unknown_18h { get; set; }
        public uint Unknown_1Ch { get; set; }

        public ClipBase Clip { get; set; }
        public ClipMapEntry Next { get; set; }

        public bool EnableRootMotion { get; set; } = false;
        public bool OverridePlayTime { get; set; } = false;
        public float PlayTime { get; set; } = 0.0f;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.Hash = new MetaHash(reader.ReadUInt32());
            this.Unknown_04h = reader.ReadUInt32();
            this.ClipPointer = reader.ReadUInt64();
            this.NextPointer = reader.ReadUInt64();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();

            this.Clip = reader.ReadBlockAt<ClipBase>(
                this.ClipPointer
            );
            this.Next = reader.ReadBlockAt<ClipMapEntry>(
                this.NextPointer
            );

            if (Clip != null)
            {
                Clip.Hash = Hash;
            }
            else
            { }
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.ClipPointer = (ulong)(this.Clip != null ? this.Clip.FilePosition : 0);
            this.NextPointer = (ulong)(this.Next != null ? this.Next.FilePosition : 0);

            writer.Write(this.Hash);
            writer.Write(this.Unknown_04h);
            writer.Write(this.ClipPointer);
            writer.Write(this.NextPointer);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Clip != null) list.Add(Clip);
            if (Next != null) list.Add(Next);
            return list.ToArray();
        }

        public override string ToString()
        {
            return Clip?.Name ?? Hash.ToString();
        }

    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipBase : ResourceSystemBlock, IResourceXXSystemBlock, IMetaXmlItem
    {
        public override long BlockLength
        {
            get { return 112; }
        }

        public uint VFT { get; set; }
        public uint Unknown_04h { get; set; } = 1;
        public uint Unknown_08h { get; set; }
        public uint Unknown_0Ch { get; set; }
        public ClipType Type { get; set; }
        public uint Unknown_14h { get; set; }
        public ulong NamePointer { get; set; }
        public ushort NameLength { get; set; }
        public ushort NameCapacity { get; set; }
        public uint Unknown_24h { get; set; }
        public ulong Unknown_28hPtr { get; set; } = 0x50000000;
        public uint Unknown_30h { get; set; }
        public uint Unknown_34h { get; set; }
        public ulong TagsPointer { get; set; }
        public ulong PropertiesPointer { get; set; }
        public uint Unknown_48h { get; set; } = 1;
        public uint Unknown_4Ch { get; set; }

        public string Name { get; set; }
        public ClipTagList Tags { get; set; }
        public ClipPropertyMap Properties { get; set; }

        private string_r NameBlock = null;

        public YcdFile Ycd { get; set; }
        public string ShortName
        {
            get
            {
                if (!string.IsNullOrEmpty(_ShortName)) return _ShortName;
                if (string.IsNullOrEmpty(Name)) return null;

                string name = Name.Replace('\\', '/');
                var slidx = name.LastIndexOf('/');
                if ((slidx >= 0) && (slidx < name.Length - 1))
                {
                    name = name.Substring(slidx + 1);
                }
                var didx = name.IndexOf('.');
                if ((didx > 0) && (didx < name.Length))
                {
                    name = name.Substring(0, didx);
                }
                _ShortName = name.ToLowerInvariant();
                JenkIndex.Ensure(_ShortName);
                return _ShortName;
            }
        }
        private string _ShortName;

        public MetaHash Hash { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_04h = reader.ReadUInt32();
            this.Unknown_08h = reader.ReadUInt32();
            this.Unknown_0Ch = reader.ReadUInt32();
            this.Type = (ClipType)reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.NamePointer = reader.ReadUInt64();
            this.NameLength = reader.ReadUInt16();
            this.NameCapacity = reader.ReadUInt16();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28hPtr = reader.ReadUInt64();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.TagsPointer = reader.ReadUInt64();
            this.PropertiesPointer = reader.ReadUInt64();
            this.Unknown_48h = reader.ReadUInt32();
            this.Unknown_4Ch = reader.ReadUInt32();

            this.Name = reader.ReadStringAt(this.NamePointer);
            this.Tags = reader.ReadBlockAt<ClipTagList>(
                this.TagsPointer
            );
            this.Properties = reader.ReadBlockAt<ClipPropertyMap>(
                this.PropertiesPointer
            );

            if (Unknown_28hPtr != 0x50000000)
            { }

            switch (VFT)
            {
                case 1079664808:
                case 1079656584:
                case 1079607128:
                    break;
                default:
                    break;
            }
            switch (Unknown_30h)
            {
                case 0:
                case 1:
                    break;
                default:
                    break;
            }

            if (Tags?.Tags?.data_items == null)
            { }
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.NamePointer = (ulong)(this.NameBlock != null ? this.NameBlock.FilePosition : 0);
            this.NameLength = (ushort)(Name?.Length ?? 0);
            this.NameCapacity = (ushort)((Name != null) ? Name.Length + 1 : 0);
            this.TagsPointer = (ulong)(this.Tags != null ? this.Tags.FilePosition : 0);
            this.PropertiesPointer = (ulong)(this.Properties != null ? this.Properties.FilePosition : 0);

            writer.Write(this.VFT);
            writer.Write(this.Unknown_04h);
            writer.Write(this.Unknown_08h);
            writer.Write(this.Unknown_0Ch);
            writer.Write((uint)this.Type);
            writer.Write(this.Unknown_14h);
            writer.Write(this.NamePointer);
            writer.Write(this.NameLength);
            writer.Write(this.NameCapacity);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28hPtr);
            writer.Write(this.Unknown_30h);
            writer.Write(this.Unknown_34h);
            writer.Write(this.TagsPointer);
            writer.Write(this.PropertiesPointer);
            writer.Write(this.Unknown_48h);
            writer.Write(this.Unknown_4Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Name != null)
            {
                NameBlock = (string_r)Name;
                list.Add(NameBlock);
            }
            if (Tags != null) list.Add(Tags);
            if (Properties != null) list.Add(Properties);
            return list.ToArray();
        }

        public IResourceSystemBlock GetType(ResourceDataReader reader, params object[] parameters)
        {
            reader.Position += 16;
            var type = reader.ReadByte();
            reader.Position -= 17;

            return ConstructClip((ClipType)type);
        }

        public static ClipBase ConstructClip(ClipType type)
        {
            switch (type)
            {
                case ClipType.Animation: return new ClipAnimation();
                case ClipType.AnimationList: return new ClipAnimationList();
                default: return null;
            }
        }

        public override string ToString()
        {
            return Name;
        }

        public virtual void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.StringTag(sb, indent, "Hash", MetaXml.XmlEscape(YcdXml.HashString(Hash)));
            YcdXml.StringTag(sb, indent, "Name", MetaXml.XmlEscape(Name));
            YcdXml.ValueTag(sb, indent, "Type", Type.ToString());
            YcdXml.ValueTag(sb, indent, "Unknown30", Unknown_30h.ToString());
            YcdXml.WriteItemArray(sb, Tags?.Tags?.data_items, indent, "Tags");
            YcdXml.WriteItemArray(sb, Properties?.AllProperties, indent, "Properties");
        }
        public virtual void ReadXml(XmlNode node)
        {
            Hash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Hash"));
            Name = Xml.GetChildInnerText(node, "Name");
            Unknown_30h = Xml.GetChildUIntAttribute(node, "Unknown30", "value");

            var tags = XmlMeta.ReadItemArrayNullable<ClipTag>(node, "Tags");
            Tags = new ClipTagList();
            if (tags != null)
            {
                Tags.Tags = new ResourcePointerArray64<ClipTag>();
                Tags.Tags.data_items = tags;
                Tags.BuildAllTags();
                Tags.AssignTagOwners();
            }

            var props = XmlMeta.ReadItemArrayNullable<ClipProperty>(node, "Properties");
            Properties = new ClipPropertyMap();
            Properties.CreatePropertyMap(props);
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipAnimation : ClipBase
    {
        public override long BlockLength
        {
            get { return 112; }
        }

        public ulong AnimationPointer { get; set; }
        public float StartTime { get; set; }
        public float EndTime { get; set; }
        public float Rate { get; set; }
        public uint Unknown_64h { get; set; }
        public uint Unknown_68h { get; set; }
        public uint Unknown_6Ch { get; set; }

        public Animation Animation { get; set; }
        public MetaHash AnimationHash { get; set; }

        public ClipAnimation()
        {
            Type = ClipType.Animation;
        }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);
            this.AnimationPointer = reader.ReadUInt64();
            this.StartTime = reader.ReadSingle();
            this.EndTime = reader.ReadSingle();
            this.Rate = reader.ReadSingle();
            this.Unknown_64h = reader.ReadUInt32();
            this.Unknown_68h = reader.ReadUInt32();
            this.Unknown_6Ch = reader.ReadUInt32();

            this.Animation = reader.ReadBlockAt<Animation>(
                this.AnimationPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.AnimationPointer = (ulong)(this.Animation != null ? this.Animation.FilePosition : 0);

            writer.Write(this.AnimationPointer);
            writer.Write(this.StartTime);
            writer.Write(this.EndTime);
            writer.Write(this.Rate);
            writer.Write(this.Unknown_64h);
            writer.Write(this.Unknown_68h);
            writer.Write(this.Unknown_6Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            list.AddRange(base.GetReferences());
            if (Animation != null) list.Add(Animation);
            return list.ToArray();
        }

        public float GetPlaybackTime(double currentTime)
        {
            double scaledTime = currentTime * Rate;
            double duration = EndTime - StartTime;
            double curpos = scaledTime % duration;
            return StartTime + (float)curpos;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.StringTag(sb, indent, "AnimationHash", YcdXml.HashString(Animation?.Hash ?? 0));
            YcdXml.ValueTag(sb, indent, "StartTime", FloatUtil.ToString(StartTime));
            YcdXml.ValueTag(sb, indent, "EndTime", FloatUtil.ToString(EndTime));
            YcdXml.ValueTag(sb, indent, "Rate", FloatUtil.ToString(Rate));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AnimationHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "AnimationHash"));
            StartTime = Xml.GetChildFloatAttribute(node, "StartTime", "value");
            EndTime = Xml.GetChildFloatAttribute(node, "EndTime", "value");
            Rate = Xml.GetChildFloatAttribute(node, "Rate", "value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipAnimationList : ClipBase
    {
        public override long BlockLength
        {
            get { return 112; }
        }

        public ulong AnimationsPointer { get; set; }
        public ushort AnimationsCount1 { get; set; }
        public ushort AnimationsCount2 { get; set; }
        public uint Unknown_5Ch { get; set; }
        public float Duration { get; set; }
        public uint Unknown_64h { get; set; } = 1;
        public uint Unknown_68h { get; set; }
        public uint Unknown_6Ch { get; set; }

        public ResourceSimpleArray<ClipAnimationsEntry> Animations { get; set; }

        public ClipAnimationList()
        {
            Type = ClipType.AnimationList;
        }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);
            this.AnimationsPointer = reader.ReadUInt64();
            this.AnimationsCount1 = reader.ReadUInt16();
            this.AnimationsCount2 = reader.ReadUInt16();
            this.Unknown_5Ch = reader.ReadUInt32();
            this.Duration = reader.ReadSingle();
            this.Unknown_64h = reader.ReadUInt32();
            this.Unknown_68h = reader.ReadUInt32();
            this.Unknown_6Ch = reader.ReadUInt32();

            this.Animations = reader.ReadBlockAt<ResourceSimpleArray<ClipAnimationsEntry>>(
                this.AnimationsPointer,
                this.AnimationsCount1
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.AnimationsPointer = (ulong)(this.Animations != null ? this.Animations.FilePosition : 0);
            this.AnimationsCount1 = (ushort)(this.Animations != null ? this.Animations.Count : 0);
            this.AnimationsCount2 = this.AnimationsCount1;

            writer.Write(this.AnimationsPointer);
            writer.Write(this.AnimationsCount1);
            writer.Write(this.AnimationsCount2);
            writer.Write(this.Unknown_5Ch);
            writer.Write(this.Duration);
            writer.Write(this.Unknown_64h);
            writer.Write(this.Unknown_68h);
            writer.Write(this.Unknown_6Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            list.AddRange(base.GetReferences());
            if (Animations != null) list.Add(Animations);
            return list.ToArray();
        }

        public float GetPlaybackTime(double currentTime)
        {
            double scaledTime = currentTime;
            double duration = Duration;
            double curpos = scaledTime % duration;
            return  (float)curpos;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Duration", FloatUtil.ToString(Duration));
            YcdXml.WriteItemArray(sb, Animations?.Data.ToArray(), indent, "Animations");
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Duration = Xml.GetChildFloatAttribute(node, "Duration", "value");

            Animations = new ResourceSimpleArray<ClipAnimationsEntry>();
            Animations.Data = new List<ClipAnimationsEntry>();
            var anims = XmlMeta.ReadItemArrayNullable<ClipAnimationsEntry>(node, "Animations");
            if (anims != null) Animations.Data.AddRange(anims);
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipAnimationsEntry : ResourceSystemBlock, IMetaXmlItem
    {
        public override long BlockLength
        {
            get { return 24; }
        }

        public float StartTime { get; set; }
        public float EndTime { get; set; }
        public float Rate { get; set; }
        public uint Unknown_0Ch { get; set; }
        public ulong AnimationPointer { get; set; }

        public Animation Animation { get; set; }
        public MetaHash AnimationHash { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.StartTime = reader.ReadSingle();
            this.EndTime = reader.ReadSingle();
            this.Rate = reader.ReadSingle();
            this.Unknown_0Ch = reader.ReadUInt32();
            this.AnimationPointer = reader.ReadUInt64();

            this.Animation = reader.ReadBlockAt<Animation>(
                this.AnimationPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.AnimationPointer = (ulong)(this.Animation != null ? this.Animation.FilePosition : 0);

            writer.Write(this.StartTime);
            writer.Write(this.EndTime);
            writer.Write(this.Rate);
            writer.Write(this.Unknown_0Ch);
            writer.Write(this.AnimationPointer);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Animation != null) list.Add(Animation);
            return list.ToArray();
        }

        public float GetPlaybackTime(double currentTime)
        {
            double scaledTime = currentTime * Rate;
            double duration = EndTime - StartTime;
            double curpos = scaledTime % duration;
            return StartTime + (float)curpos;
        }

        public void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.StringTag(sb, indent, "AnimationHash", YcdXml.HashString(Animation?.Hash ?? 0));
            YcdXml.ValueTag(sb, indent, "StartTime", FloatUtil.ToString(StartTime));
            YcdXml.ValueTag(sb, indent, "EndTime", FloatUtil.ToString(EndTime));
            YcdXml.ValueTag(sb, indent, "Rate", FloatUtil.ToString(Rate));
        }
        public void ReadXml(XmlNode node)
        {
            AnimationHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "AnimationHash"));
            StartTime = Xml.GetChildFloatAttribute(node, "StartTime", "value");
            EndTime = Xml.GetChildFloatAttribute(node, "EndTime", "value");
            Rate = Xml.GetChildFloatAttribute(node, "Rate", "value");
        }
    }
    public enum ClipType : uint
    {
        Animation = 1,
        AnimationList = 2,
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyMap : ResourceSystemBlock
    {
        public override long BlockLength
        {
            get { return 16; }
        }

        public ulong PropertyEntriesPointer { get; set; }
        public ushort PropertyEntriesCapacity { get; set; }
        public ushort PropertyEntriesCount { get; set; }
        public uint Unknown_0Ch { get; set; } = 0x01000000;

        public ResourcePointerArray64<ClipPropertyMapEntry> Properties { get; set; }

        public ClipProperty[] AllProperties { get; set; }
        public Dictionary<MetaHash, ClipProperty> PropertyMap { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.PropertyEntriesPointer = reader.ReadUInt64();
            this.PropertyEntriesCapacity = reader.ReadUInt16();
            this.PropertyEntriesCount = reader.ReadUInt16();
            this.Unknown_0Ch = reader.ReadUInt32();

            this.Properties = reader.ReadBlockAt<ResourcePointerArray64<ClipPropertyMapEntry>>(
                this.PropertyEntriesPointer,
                this.PropertyEntriesCapacity
            );

            BuildPropertyMap();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.PropertyEntriesPointer = (ulong)(this.Properties != null ? this.Properties.FilePosition : 0);
            this.PropertyEntriesCapacity = (ushort)(this.Properties != null ? this.Properties.Count : 0);
            this.PropertyEntriesCount = (ushort)(this.AllProperties?.Length ?? 0);

            writer.Write(this.PropertyEntriesPointer);
            writer.Write(this.PropertyEntriesCapacity);
            writer.Write(this.PropertyEntriesCount);
            writer.Write(this.Unknown_0Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Properties != null) list.Add(Properties);
            return list.ToArray();
        }

        public override string ToString()
        {
            return "Count: " + (AllProperties?.Length ?? 0).ToString();
        }

        public void BuildPropertyMap()
        {
            if (Properties?.data_items != null)
            {
                List<ClipProperty> pl = new List<ClipProperty>();
                foreach (var pme in Properties.data_items)
                {
                    ClipPropertyMapEntry cpme = pme;
                    while (cpme?.Data != null)
                    {
                        pl.Add(cpme.Data);
                        cpme = cpme.Next;
                    }
                }
                AllProperties = pl.ToArray();

                PropertyMap = new Dictionary<MetaHash, ClipProperty>();
                foreach (var cp in AllProperties)
                {
                    PropertyMap[cp.NameHash] = cp;
                }
            }
        }

        public void CreatePropertyMap(ClipProperty[] properties)
        {
            var numBuckets = ClipDictionary.GetNumHashBuckets(properties?.Length ?? 0);
            var buckets = new List<ClipPropertyMapEntry>[numBuckets];
            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    var b = prop.NameHash % numBuckets;
                    var bucket = buckets[b];
                    if (bucket == null)
                    {
                        bucket = new List<ClipPropertyMapEntry>();
                        buckets[b] = bucket;
                    }
                    var pme = new ClipPropertyMapEntry();
                    pme.PropertyNameHash = prop.NameHash;
                    pme.Data = prop;
                    bucket.Add(pme);
                }
            }

            var newProperties = new List<ClipPropertyMapEntry>();
            foreach (var b in buckets)
            {
                if ((b?.Count ?? 0) == 0) newProperties.Add(null);
                else
                {
                    newProperties.Add(b[0]);
                    var p = b[0];
                    for (int i = 1; i < b.Count; i++)
                    {
                        var c = b[i];
                        c.Next = null;
                        p.Next = c;
                        p = c;
                    }
                }
            }

            Properties = new ResourcePointerArray64<ClipPropertyMapEntry>();
            Properties.data_items = newProperties.ToArray();

            AllProperties = properties;
        }

    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyMapEntry : ResourceSystemBlock
    {
        public override long BlockLength
        {
            get { return 32; }
        }

        public MetaHash PropertyNameHash { get; set; }
        public uint Unknown_04h { get; set; }
        public ulong DataPointer { get; set; }
        public ulong NextPointer { get; set; }
        public uint Unknown_18h { get; set; }
        public uint Unknown_1Ch { get; set; }

        public ClipProperty Data { get; set; }
        public ClipPropertyMapEntry Next { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.PropertyNameHash = reader.ReadUInt32();
            this.Unknown_04h = reader.ReadUInt32();
            this.DataPointer = reader.ReadUInt64();
            this.NextPointer = reader.ReadUInt64();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();

            this.Data = reader.ReadBlockAt<ClipProperty>(
                this.DataPointer
            );
            this.Next = reader.ReadBlockAt<ClipPropertyMapEntry>(
                this.NextPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.DataPointer = (ulong)(this.Data != null ? this.Data.FilePosition : 0);
            this.NextPointer = (ulong)(this.Next != null ? this.Next.FilePosition : 0);

            writer.Write(this.PropertyNameHash);
            writer.Write(this.Unknown_04h);
            writer.Write(this.DataPointer);
            writer.Write(this.NextPointer);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Data != null) list.Add(Data);
            if (Next != null) list.Add(Next);
            return list.ToArray();
        }

    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipProperty : ResourceSystemBlock, IMetaXmlItem
    {
        public override long BlockLength
        {
            get { return 64; }
        }

        public uint VFT { get; set; }
        public uint Unknown_04h { get; set; } = 1;
        public uint Unknown_08h { get; set; }
        public uint Unknown_0Ch { get; set; }
        public uint Unknown_10h { get; set; }
        public uint Unknown_14h { get; set; }
        public MetaHash NameHash { get; set; }
        public uint Unknown_1Ch { get; set; }
        public ulong AttributesPointer { get; set; }
        public ushort AttributesCount { get; set; }
        public ushort AttributesCapacity { get; set; }
        public uint Unknown_2Ch { get; set; }
        public uint Unknown_30h { get; set; }
        public uint Unknown_34h { get; set; }
        public MetaHash UnkHash { get; set; }
        public uint Unknown_3Ch { get; set; }

        public ResourcePointerArray64<ClipPropertyAttribute> Attributes { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_04h = reader.ReadUInt32();
            this.Unknown_08h = reader.ReadUInt32();
            this.Unknown_0Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.NameHash = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();
            this.AttributesPointer = reader.ReadUInt64();
            this.AttributesCount = reader.ReadUInt16();
            this.AttributesCapacity = reader.ReadUInt16();
            this.Unknown_2Ch = reader.ReadUInt32();
            this.Unknown_30h = reader.ReadUInt32();
            this.Unknown_34h = reader.ReadUInt32();
            this.UnkHash = reader.ReadUInt32();
            this.Unknown_3Ch = reader.ReadUInt32();

            this.Attributes = reader.ReadBlockAt<ResourcePointerArray64<ClipPropertyAttribute>>(
                this.AttributesPointer,
                this.AttributesCount
            );

            switch (VFT)
            {
                case 1080111464:
                case 1080103160:
                case 1080119200:
                case 1080069168:
                case 1080053176:
                    break;
                default:
                    break;
            }
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.AttributesPointer = (ulong)(this.Attributes != null ? this.Attributes.FilePosition : 0);
            this.AttributesCount = (ushort)(this.Attributes != null ? this.Attributes.Count : 0);
            this.AttributesCapacity = this.AttributesCount;

            writer.Write(this.VFT);
            writer.Write(this.Unknown_04h);
            writer.Write(this.Unknown_08h);
            writer.Write(this.Unknown_0Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.NameHash);
            writer.Write(this.Unknown_1Ch);
            writer.Write(this.AttributesPointer);
            writer.Write(this.AttributesCount);
            writer.Write(this.AttributesCapacity);
            writer.Write(this.Unknown_2Ch);
            writer.Write(this.Unknown_30h);
            writer.Write(this.Unknown_34h);
            writer.Write(this.UnkHash);
            writer.Write(this.Unknown_3Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Attributes != null) list.Add(Attributes);
            return list.ToArray();
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            if ((Attributes != null) && (Attributes.data_items != null))
            {
                foreach (var item in Attributes.data_items)
                {
                    if (sb.Length > 0) sb.Append(", ");
                    sb.Append(item.ToString());
                }
            }
            return NameHash.ToString() + ": " + UnkHash.ToString() + ": " + sb.ToString();
        }

        public virtual void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.StringTag(sb, indent, "NameHash", YcdXml.HashString(NameHash));
            YcdXml.StringTag(sb, indent, "UnkHash", YcdXml.HashString(UnkHash));
            YcdXml.WriteItemArray(sb, Attributes?.data_items, indent, "Attributes");
        }
        public virtual void ReadXml(XmlNode node)
        {
            NameHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "NameHash"));
            UnkHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "UnkHash"));

            var attrsNode = node.SelectSingleNode("Attributes");
            if (attrsNode != null)
            {
                var inodes = attrsNode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var alist = new List<ClipPropertyAttribute>();
                    foreach (XmlNode inode in inodes)
                    {
                        var item = new ClipPropertyAttribute();
                        item.ReadXml(inode);
                        var v = ClipPropertyAttribute.ConstructItem(item.Type);
                        v.ReadXml(inode);
                        alist.Add(v);
                    }
                    Attributes = new ResourcePointerArray64<ClipPropertyAttribute>();
                    Attributes.data_items = alist.ToArray();
                }
            }
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttribute : ResourceSystemBlock, IResourceXXSystemBlock, IMetaXmlItem
    {
        public override long BlockLength
        {
            get { return 16; }
        }

        public uint VFT { get; set; }
        public uint Unknown_04h { get; set; } = 1;
        public ClipPropertyAttributeType Type { get; set; }
        public byte Unknown_09h { get; set; }
        public ushort Unknown_Ah { get; set; }
        public uint Unknown_Ch { get; set; }
        public uint Unknown_10h { get; set; }
        public uint Unknown_14h { get; set; }
        public MetaHash NameHash { get; set; }
        public uint Unknown_1Ch { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.VFT = reader.ReadUInt32();
            this.Unknown_04h = reader.ReadUInt32();
            this.Type = (ClipPropertyAttributeType)reader.ReadByte();
            this.Unknown_09h = reader.ReadByte();
            this.Unknown_Ah = reader.ReadUInt16();
            this.Unknown_Ch = reader.ReadUInt32();
            this.Unknown_10h = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.NameHash = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();

            switch (VFT)
            {
                case 1080119416:
                case 1080119528:
                case 1080119640:
                case 1080119752:
                case 1080119832:
                case 1080120088:
                case 1080120472:
                case 1080069384:
                case 1080069496:
                case 1080127976:
                case 1080069800:
                    break;
                default:
                    break;
            }
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            writer.Write(this.VFT);
            writer.Write(this.Unknown_04h);
            writer.Write((byte)this.Type);
            writer.Write(this.Unknown_09h);
            writer.Write(this.Unknown_Ah);
            writer.Write(this.Unknown_Ch);
            writer.Write(this.Unknown_10h);
            writer.Write(this.Unknown_14h);
            writer.Write(this.NameHash);
            writer.Write(this.Unknown_1Ch);
        }

        public IResourceSystemBlock GetType(ResourceDataReader reader, params object[] parameters)
        {
            reader.Position += 8;
            var type = (ClipPropertyAttributeType)reader.ReadByte();
            reader.Position -= 9;
            return ConstructItem(type);
        }

        public static ClipPropertyAttribute ConstructItem(ClipPropertyAttributeType type)
        {
            switch (type)
            {
                case ClipPropertyAttributeType.Float: return new ClipPropertyAttributeFloat();
                case ClipPropertyAttributeType.Int: return new ClipPropertyAttributeInt();
                case ClipPropertyAttributeType.Bool: return new ClipPropertyAttributeBool();
                case ClipPropertyAttributeType.String: return new ClipPropertyAttributeString();
                case ClipPropertyAttributeType.Vector3: return new ClipPropertyAttributeVector3();
                case ClipPropertyAttributeType.Vector4: return new ClipPropertyAttributeVector4();
                case ClipPropertyAttributeType.HashString: return new ClipPropertyAttributeHashString();
                default: return null;
            }
        }

        public virtual void WriteXml(StringBuilder sb, int indent)
        {
            YcdXml.StringTag(sb, indent, "NameHash", YcdXml.HashString(NameHash));
            YcdXml.ValueTag(sb, indent, "Type", Type.ToString());
        }
        public virtual void ReadXml(XmlNode node)
        {
            NameHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "NameHash"));
            Type = Xml.GetEnumValue<ClipPropertyAttributeType>(Xml.GetChildStringAttribute(node, "Type", "value"));
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeFloat : ClipPropertyAttribute
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public float Value { get; set; }
        public uint Unknown_24h { get; set; }
        public uint Unknown_28h { get; set; }
        public uint Unknown_2Ch { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Value = reader.ReadSingle();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            writer.Write(this.Value);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override string ToString()
        {
            return "Float:" + FloatUtil.ToString(Value);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Value", FloatUtil.ToString(Value));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildFloatAttribute(node, "Value", "value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeInt : ClipPropertyAttribute
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public int Value { get; set; }
        public uint Unknown_24h { get; set; }
        public uint Unknown_28h { get; set; }
        public uint Unknown_2Ch { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Value = reader.ReadInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            writer.Write(this.Value);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override string ToString()
        {
            return "Int:" + Value.ToString();
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Value", Value.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildIntAttribute(node, "Value", "value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeBool : ClipPropertyAttribute
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public uint Value { get; set; }
        public uint Unknown_24h { get; set; }
        public uint Unknown_28h { get; set; }
        public uint Unknown_2Ch { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Value = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            writer.Write(this.Value);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override string ToString()
        {
            return "Uint:" + Value.ToString();
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "Value", Value.ToString());
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildUIntAttribute(node, "Value", "value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeString : ClipPropertyAttribute
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public ulong ValuePointer { get; set; }
        public ushort ValueLength { get; set; }
        public ushort ValueCapacity { get; set; }
        public uint Unknown_02Ch { get; set; }

        public string Value;
        private string_r ValueBlock = null;

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.ValuePointer = reader.ReadUInt64();
            this.ValueLength = reader.ReadUInt16();
            this.ValueCapacity = reader.ReadUInt16();
            this.Unknown_02Ch = reader.ReadUInt32();

            Value = reader.ReadStringAt(ValuePointer);
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.ValuePointer = (ulong)(this.ValueBlock != null ? this.ValueBlock.FilePosition : 0);
            this.ValueLength = (ushort)(Value?.Length ?? 0);
            this.ValueCapacity = (ushort)((Value != null) ? Value.Length + 1 : 0);

            writer.Write(this.ValuePointer);
            writer.Write(this.ValueLength);
            writer.Write(this.ValueCapacity);
            writer.Write(this.Unknown_02Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Value != null)
            {
                ValueBlock = (string_r)Value;
                list.Add(ValueBlock);
            }
            return list.ToArray();
        }

        public override string ToString()
        {
            return "String:" + Value;
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.StringTag(sb, indent, "Value", MetaXml.XmlEscape(Value));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildInnerText(node, "Value");
            ValueLength = (ushort)(Value?.Length??0);
            ValueCapacity = ValueLength;
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeVector3 : ClipPropertyAttribute
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public Vector3 Value { get; set; }
        public float Unknown_02Ch { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            Value = reader.ReadVector3();
            this.Unknown_02Ch = reader.ReadSingle();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            writer.Write(this.Value);
            writer.Write(this.Unknown_02Ch);
        }

        public override string ToString()
        {
            return "Vector3:" + FloatUtil.GetVector3String(Value);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.SelfClosingTag(sb, indent, "Value " + FloatUtil.GetVector3XmlString(Value));
            YcdXml.ValueTag(sb, indent, "Unknown2C", FloatUtil.ToString(Unknown_02Ch));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildVector3Attributes(node, "Value");
            Unknown_02Ch = Xml.GetChildFloatAttribute(node, "Unknown2C", "value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeVector4 : ClipPropertyAttribute
    {
        public override long BlockLength
        {
            get { return 48; }
        }

        public Vector4 Value { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Value = reader.ReadVector4();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            writer.Write(this.Value);
        }

        public override string ToString()
        {
            return "Vector4:" + FloatUtil.GetVector4String(Value);
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.SelfClosingTag(sb, indent, "Value " + FloatUtil.GetVector4XmlString(Value));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = Xml.GetChildVector4Attributes(node, "Value");
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipPropertyAttributeHashString : ClipPropertyAttribute
    {
        public override long BlockLength => 0x30;

        public MetaHash Value { get; set; }
        public uint Unknown_24h { get; set; }
        public uint Unknown_28h { get; set; }
        public uint Unknown_2Ch { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.Value = reader.ReadUInt32();
            this.Unknown_24h = reader.ReadUInt32();
            this.Unknown_28h = reader.ReadUInt32();
            this.Unknown_2Ch = reader.ReadUInt32();
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            writer.Write(this.Value);
            writer.Write(this.Unknown_24h);
            writer.Write(this.Unknown_28h);
            writer.Write(this.Unknown_2Ch);
        }

        public override string ToString()
        {
            return "Hash:" + Value.ToString();
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.StringTag(sb, indent, "Value", YcdXml.HashString(Value));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            Value = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Value"));
        }
    }
    public enum ClipPropertyAttributeType : byte
    {
        Float = 1,
        Int = 2,
        Bool = 3,
        String = 4,
        Vector3 = 6,
        Vector4 = 8,
        HashString = 12,
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipTagList : ResourceSystemBlock
    {
        public override long BlockLength
        {
            get { return 32; }
        }

        public ulong TagsPointer { get; set; }
        public ushort TagCount1 { get; set; }
        public ushort TagCount2 { get; set; }
        public uint Unknown_0Ch { get; set; }
        public uint HasBlockTag { get; set; }
        public uint Unknown_14h { get; set; }
        public uint Unknown_18h { get; set; }
        public uint Unknown_1Ch { get; set; }

        public ResourcePointerArray64<ClipTag> Tags { get; set; }

        public ClipTag[] AllTags { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            this.TagsPointer = reader.ReadUInt64();
            this.TagCount1 = reader.ReadUInt16();
            this.TagCount2 = reader.ReadUInt16();
            this.Unknown_0Ch = reader.ReadUInt32();
            this.HasBlockTag = reader.ReadUInt32();
            this.Unknown_14h = reader.ReadUInt32();
            this.Unknown_18h = reader.ReadUInt32();
            this.Unknown_1Ch = reader.ReadUInt32();

            this.Tags = reader.ReadBlockAt<ResourcePointerArray64<ClipTag>>(
                this.TagsPointer,
                this.TagCount1
            );

            BuildAllTags();

            if (TagCount1 != TagCount2)
            { }
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            this.TagsPointer = (ulong)(this.Tags != null ? this.Tags.FilePosition : 0);
            this.TagCount1 = (ushort)(this.Tags != null ? this.Tags.Count : 0);
            this.TagCount2 = this.TagCount1;

            BuildAllTags();

            writer.Write(this.TagsPointer);
            writer.Write(this.TagCount1);
            writer.Write(this.TagCount2);
            writer.Write(this.Unknown_0Ch);
            writer.Write(this.HasBlockTag);
            writer.Write(this.Unknown_14h);
            writer.Write(this.Unknown_18h);
            writer.Write(this.Unknown_1Ch);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>();
            if (Tags != null) list.Add(Tags);
            return list.ToArray();
        }

        public override string ToString()
        {
            return "Count: " + (AllTags?.Length ?? 0).ToString();
        }

        public void BuildAllTags()
        {

            if ((Tags != null) && (Tags.data_items != null))
            {
                List<ClipTag> tl = new List<ClipTag>();
                foreach (var te in Tags.data_items)
                {
                    if (te.Tags != this)
                    { }
                    if (te != null)
                    {
                        tl.Add(te);
                    }
                }
                AllTags = tl.ToArray();
            }

            uint hasBlock = 0;
            if (AllTags != null)
            {
                foreach (var tag in AllTags)
                {
                    if (tag.NameHash == (uint)MetaName.block)
                    { hasBlock = 1; break; }
                }
            }
            if (HasBlockTag != hasBlock)
            { }
            HasBlockTag = hasBlock;

        }

        public void AssignTagOwners()
        {
            if (Tags?.data_items == null) return;
            foreach (var tag in Tags.data_items)
            {
                tag.Tags = this;
            }
        }
    }
    [TypeConverter(typeof(ExpandableObjectConverter))] public class ClipTag : ClipProperty
    {
        public override long BlockLength
        {
            get { return 80; }
        }

        public float StartPhase { get; set; }
        public float EndPhase { get; set; }
        public ulong TagsPointer { get; set; }

        public ClipTagList Tags { get; set; }

        public override void Read(ResourceDataReader reader, params object[] parameters)
        {
            base.Read(reader, parameters);

            this.StartPhase = reader.ReadSingle();
            this.EndPhase = reader.ReadSingle();
            this.TagsPointer = reader.ReadUInt64();

            this.Tags = reader.ReadBlockAt<ClipTagList>(
                this.TagsPointer
            );
        }

        public override void Write(ResourceDataWriter writer, params object[] parameters)
        {
            base.Write(writer, parameters);

            this.TagsPointer = (ulong)(this.Tags != null ? this.Tags.FilePosition : 0);

            writer.Write(this.StartPhase);
            writer.Write(this.EndPhase);
            writer.Write(this.TagsPointer);
        }

        public override IResourceBlock[] GetReferences()
        {
            var list = new List<IResourceBlock>(base.GetReferences());
            if (Tags != null) list.Add(Tags);
            return list.ToArray();
        }

        public override string ToString()
        {
            return base.ToString() + ": " + StartPhase.ToString() + ", " + EndPhase.ToString();
        }

        public override void WriteXml(StringBuilder sb, int indent)
        {
            base.WriteXml(sb, indent);
            YcdXml.ValueTag(sb, indent, "StartPhase", FloatUtil.ToString(StartPhase));
            YcdXml.ValueTag(sb, indent, "EndPhase", FloatUtil.ToString(EndPhase));
        }
        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StartPhase = Xml.GetChildFloatAttribute(node, "StartPhase", "value");
            EndPhase = Xml.GetChildFloatAttribute(node, "EndPhase", "value");
        }
    }

}
