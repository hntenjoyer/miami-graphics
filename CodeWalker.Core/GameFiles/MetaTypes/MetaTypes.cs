using SharpDX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

using TC = System.ComponentModel.TypeConverterAttribute;
using EXP = System.ComponentModel.ExpandableObjectConverter;
using CodeWalker.World;

namespace CodeWalker.GameFiles
{

    public static class MetaTypes
    {

        public static Dictionary<uint, MetaEnumInfo> EnumDict = new Dictionary<uint, MetaEnumInfo>();
        public static Dictionary<uint, MetaStructureInfo> StructDict = new Dictionary<uint, MetaStructureInfo>();

        public static void Clear()
        {
            EnumDict.Clear();
            StructDict.Clear();
        }

        public static void EnsureMetaTypes(Meta meta)
        {

            if (meta.EnumInfos != null)
            {
                foreach (MetaEnumInfo mei in meta.EnumInfos)
                {
                    if (!EnumDict.ContainsKey(mei.EnumKey))
                    {
                        EnumDict.Add(mei.EnumKey, mei);
                    }
                    else
                    {
                        MetaEnumInfo oldei = EnumDict[mei.EnumKey];
                        if (!CompareMetaEnumInfos(oldei, mei))
                        {
                        }
                    }
                }
            }

            if (meta.StructureInfos != null)
            {
                foreach (MetaStructureInfo msi in meta.StructureInfos)
                {
                    if (!StructDict.ContainsKey(msi.StructureKey))
                    {
                        StructDict.Add(msi.StructureKey, msi);
                    }
                    else
                    {
                        MetaStructureInfo oldsi = StructDict[msi.StructureKey];
                        if (!CompareMetaStructureInfos(oldsi, msi))
                        {
                        }
                    }
                }
            }

        }

        public static bool CompareMetaEnumInfos(MetaEnumInfo a, MetaEnumInfo b)
        {

            if (a.Entries.Length != b.Entries.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Entries.Length; i++)
            {
                if ((a.Entries[i].EntryNameHash != b.Entries[i].EntryNameHash) ||
                    (a.Entries[i].EntryValue != b.Entries[i].EntryValue))
                {
                    return false;
                }
            }

            return true;
        }
        public static bool CompareMetaStructureInfos(MetaStructureInfo a, MetaStructureInfo b)
        {

            if (a.Entries.Length != b.Entries.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Entries.Length; i++)
            {
                if ((a.Entries[i].EntryNameHash != b.Entries[i].EntryNameHash) ||
                    (a.Entries[i].DataOffset != b.Entries[i].DataOffset) ||
                    (a.Entries[i].DataType != b.Entries[i].DataType))
                {
                    return false;
                }
            }

            return true;
        }

        public static string GetTypesString()
        {
            StringBuilder sbe = new StringBuilder();
            StringBuilder sbs = new StringBuilder();

            sbe.AppendLine("//Enum infos");
            sbs.AppendLine("//Struct infos");

            foreach (var kvp in EnumDict)
            {
                var mei = kvp.Value;
                string name = GetSafeName(mei.EnumNameHash, mei.EnumKey);
                sbe.AppendLine("public enum " + name + " //Key:" + mei.EnumKey.ToString());
                sbe.AppendLine("{");
                foreach (var entry in mei.Entries)
                {
                    string eename = GetSafeName(entry.EntryNameHash, (uint)entry.EntryValue);
                    sbe.AppendFormat("   {0} = {1},", eename, entry.EntryValue);
                    sbe.AppendLine();
                }
                sbe.AppendLine("}");
                sbe.AppendLine();
            }

            foreach (var kvp in StructDict)
            {
                var msi = kvp.Value;
                string name = GetSafeName(msi.StructureNameHash, msi.StructureKey);
                sbs.AppendLine("public struct " + name + " //" + msi.StructureSize.ToString() + " bytes, Key:" + msi.StructureKey.ToString());
                sbs.AppendLine("{");
                for (int i = 0; i < msi.Entries.Length; i++)
                {
                    var entry = msi.Entries[i];

                    if ((entry.DataOffset == 0) && (entry.EntryNameHash == (MetaName)MetaTypeName.ARRAYINFO))
                    {
                    }
                    else
                    {
                        string sename = GetSafeName(entry.EntryNameHash, (uint)entry.ReferenceKey);
                        string fmt = "   public {0} {1}; //{2}   {3}";

                        if (entry.DataType == MetaStructureEntryDataType.Array)
                        {
                            if (entry.ReferenceTypeIndex >= msi.Entries.Length)
                            {
                            }
                            else
                            {
                                var structentry = msi.Entries[entry.ReferenceTypeIndex];
                                var typename = "Array_" + MetaStructureEntryDataTypes.GetCSharpTypeName(structentry.DataType);
                                sbs.AppendFormat(fmt, typename, sename, entry.DataOffset, entry.ToString() + "  {" + structentry.ToString() + "}");
                                sbs.AppendLine();
                            }
                        }
                        else if (entry.DataType == MetaStructureEntryDataType.Structure)
                        {
                            var typename = GetSafeName(entry.ReferenceKey, (uint)entry.ReferenceTypeIndex);
                            sbs.AppendFormat(fmt, typename, sename, entry.DataOffset, entry.ToString());
                            sbs.AppendLine();
                        }
                        else
                        {
                            var typename = MetaStructureEntryDataTypes.GetCSharpTypeName(entry.DataType);
                            sbs.AppendFormat(fmt, typename, sename, entry.DataOffset, entry);
                            sbs.AppendLine();
                        }

                    }

                }
                sbs.AppendLine("}");
                sbs.AppendLine();
            }

            sbe.AppendLine();
            sbe.AppendLine();
            sbe.AppendLine();
            sbe.AppendLine();
            sbe.AppendLine();
            sbe.Append(sbs.ToString());

            string result = sbe.ToString();

            return result;
        }

        public static string GetTypesInitString(Meta meta)
        {
            StringBuilder sb = new StringBuilder();

            foreach (var si in meta.StructureInfos)
            {
                AddStructureInfoString(si, sb);
            }

            sb.AppendLine();

            foreach (var ei in meta.EnumInfos)
            {
                AddEnumInfoString(ei, sb);
            }

            string str = sb.ToString();
            return str;
        }
        public static string GetTypesInitString()
        {
            StringBuilder sb = new StringBuilder();

            foreach (var si in StructDict.Values)
            {
                AddStructureInfoString(si, sb);
            }

            sb.AppendLine();

            foreach (var ei in EnumDict.Values)
            {
                AddEnumInfoString(ei, sb);
            }

            string str = sb.ToString();
            return str;
        }
        private static void AddStructureInfoString(MetaStructureInfo si, StringBuilder sb)
        {
            var ns = GetMetaNameString(si.StructureNameHash);
            sb.AppendFormat("case " + ns + ":");
            sb.AppendLine();
            sb.AppendFormat("return new MetaStructureInfo({0}, {1}, {2}, {3},", ns, si.StructureKey, si.Unknown_8h, si.StructureSize);
            sb.AppendLine();
            for (int i = 0; i < si.Entries.Length; i++)
            {
                var e = si.Entries[i];
                string refkey = "0";
                if (e.ReferenceKey != 0)
                {
                    refkey = GetMetaNameString(e.ReferenceKey);
                }
                sb.AppendFormat(" new MetaStructureEntryInfo_s({0}, {1}, MetaStructureEntryDataType.{2}, {3}, {4}, {5})", GetMetaNameString(e.EntryNameHash), e.DataOffset, e.DataType, e.Unknown_9h, e.ReferenceTypeIndex, refkey);
                if (i < si.Entries.Length - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendFormat(");");
            sb.AppendLine();
        }
        private static void AddEnumInfoString(MetaEnumInfo ei, StringBuilder sb)
        {
            var ns = GetMetaNameString(ei.EnumNameHash);
            sb.AppendFormat("case " + ns + ":");
            sb.AppendLine();
            sb.AppendFormat("return new MetaEnumInfo({0}, {1},", ns, ei.EnumKey);
            sb.AppendLine();
            for (int i = 0; i < ei.Entries.Length; i++)
            {
                var e = ei.Entries[i];
                sb.AppendFormat(" new MetaEnumEntryInfo_s({0}, {1})", GetMetaNameString(e.EntryNameHash), e.EntryValue);
                if (i < ei.Entries.Length - 1) sb.Append(",");
                sb.AppendLine();
            }
            sb.AppendFormat(");");
            sb.AppendLine();
        }
        private static string GetMetaNameString(MetaName n)
        {
            if (Enum.IsDefined(typeof(MetaName), n))
            {
                return "MetaName." + n.ToString();
            }
            else
            {
                return "(MetaName)" + n.ToString();
            }
        }

        public static MetaStructureInfo GetStructureInfo(MetaName name)
        {
            switch (name)
            {
                case MetaName.CScenarioPointContainer:
                    return new MetaStructureInfo(MetaName.CScenarioPointContainer, 2489654897, 768, 48,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CExtensionDefSpawnPoint),
                     new MetaStructureEntryInfo_s(MetaName.LoadSavePoints, 0, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioPoint),
                     new MetaStructureEntryInfo_s(MetaName.MyPoints, 16, MetaStructureEntryDataType.Array, 0, 2, 0)
                    );
                case MetaName.CScenarioChainingGraph:
                    return new MetaStructureInfo(MetaName.CScenarioChainingGraph, 88255871, 768, 88,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioChainingNode),
                     new MetaStructureEntryInfo_s(MetaName.Nodes, 0, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioChainingEdge),
                     new MetaStructureEntryInfo_s(MetaName.Edges, 16, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioChain),
                     new MetaStructureEntryInfo_s(MetaName.Chains, 32, MetaStructureEntryDataType.Array, 0, 4, 0)
                    );
                case MetaName.rage__spdGrid2D:
                    return new MetaStructureInfo(MetaName.rage__spdGrid2D, 894636096, 768, 64,
                     new MetaStructureEntryInfo_s(MetaName.MinCellX, 12, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.MaxCellX, 16, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.MinCellY, 20, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.MaxCellY, 24, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CellDimX, 44, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CellDimY, 48, MetaStructureEntryDataType.Float, 0, 0, 0)
                    );
                case MetaName.CScenarioPointLookUps:
                    return new MetaStructureInfo(MetaName.CScenarioPointLookUps, 2669361587, 768, 96,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.TypeNames, 0, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.PedModelSetNames, 16, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.VehicleModelSetNames, 32, MetaStructureEntryDataType.Array, 0, 4, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.GroupNames, 48, MetaStructureEntryDataType.Array, 0, 6, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.InteriorNames, 64, MetaStructureEntryDataType.Array, 0, 8, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.RequiredIMapNames, 80, MetaStructureEntryDataType.Array, 0, 10, 0)
                    );
                case MetaName.CScenarioPointRegion:
                    return new MetaStructureInfo(MetaName.CScenarioPointRegion, 3501351821, 768, 376,
                     new MetaStructureEntryInfo_s(MetaName.VersionNumber, 0, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Points, 8, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioPointContainer),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioEntityOverride),
                     new MetaStructureEntryInfo_s(MetaName.EntityOverrides, 72, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s(MetaName.ChainingGraph, 96, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioChainingGraph),
                     new MetaStructureEntryInfo_s(MetaName.AccelGrid, 184, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__spdGrid2D),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)3844724227, 248, MetaStructureEntryDataType.Array, 0, 6, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioPointCluster),
                     new MetaStructureEntryInfo_s(MetaName.Clusters, 264, MetaStructureEntryDataType.Array, 0, 8, 0),
                     new MetaStructureEntryInfo_s(MetaName.LookUps, 280, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioPointLookUps)
                    );
                case MetaName.CScenarioPoint:
                    return new MetaStructureInfo(MetaName.CScenarioPoint, 402442150, 1024, 64,
                     new MetaStructureEntryInfo_s(MetaName.iType, 21, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ModelSetId, 22, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iInterior, 23, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iRequiredIMapId, 24, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iProbability, 25, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.uAvailableInMpSp, 26, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iTimeStartOverride, 27, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iTimeEndOverride, 28, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iRadius, 29, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iTimeTillPedLeaves, 30, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iScenarioGroup, 32, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Flags, 36, MetaStructureEntryDataType.IntFlags2, 0, 32, MetaName.CScenarioPointFlags__Flags),
                     new MetaStructureEntryInfo_s(MetaName.vPositionAndDirection, 48, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0)
                    );
                case MetaName.CScenarioEntityOverride:
                    return new MetaStructureInfo(MetaName.CScenarioEntityOverride, 1271200492, 1024, 80,
                     new MetaStructureEntryInfo_s(MetaName.EntityPosition, 0, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.EntityType, 16, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CExtensionDefSpawnPoint),
                     new MetaStructureEntryInfo_s(MetaName.ScenarioPoints, 24, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s(MetaName.EntityMayNotAlwaysExist, 64, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.SpecificallyPreventArtPoints, 65, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CExtensionDefSpawnPoint:
                    return new MetaStructureInfo(MetaName.CExtensionDefSpawnPoint, 3077340721, 1024, 96,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetRotation, 32, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.spawnType, 48, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedType, 52, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.group, 56, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.interior, 60, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.requiredImap, 64, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.availableInMpSp, 68, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.CSpawnPoint__AvailabilityMpSp),
                     new MetaStructureEntryInfo_s(MetaName.probability, 72, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.timeTillPedLeaves, 76, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.radius, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.start, 84, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.end, 85, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 88, MetaStructureEntryDataType.IntFlags2, 0, 32, MetaName.CScenarioPointFlags__Flags),
                     new MetaStructureEntryInfo_s(MetaName.highPri, 92, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extendedRange, 93, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.shortRange, 94, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CScenarioChainingNode:
                    return new MetaStructureInfo(MetaName.CScenarioChainingNode, 1811784424, 1024, 32,
                     new MetaStructureEntryInfo_s(MetaName.Position, 0, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)2602393771, 16, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ScenarioType, 20, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.HasIncomingEdges, 24, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.HasOutgoingEdges, 25, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CScenarioChainingEdge:
                    return new MetaStructureInfo(MetaName.CScenarioChainingEdge, 2004985940, 256, 8,
                     new MetaStructureEntryInfo_s(MetaName.NodeIndexFrom, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.NodeIndexTo, 2, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Action, 4, MetaStructureEntryDataType.ByteEnum, 0, 0, MetaName.CScenarioChainingEdge__eAction),
                     new MetaStructureEntryInfo_s(MetaName.NavMode, 5, MetaStructureEntryDataType.ByteEnum, 0, 0, MetaName.CScenarioChainingEdge__eNavMode),
                     new MetaStructureEntryInfo_s(MetaName.NavSpeed, 6, MetaStructureEntryDataType.ByteEnum, 0, 0, MetaName.CScenarioChainingEdge__eNavSpeed)
                    );
                case MetaName.CScenarioChain:
                    return new MetaStructureInfo(MetaName.CScenarioChain, 2751910366, 768, 40,
                     new MetaStructureEntryInfo_s((MetaName)1156691834, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.EdgeIds, 8, MetaStructureEntryDataType.Array, 0, 1, 0)
                    );
                case MetaName.rage__spdSphere:
                    return new MetaStructureInfo(MetaName.rage__spdSphere, 1189037266, 1024, 16,
                     new MetaStructureEntryInfo_s(MetaName.centerAndRadius, 0, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0)
                    );
                case MetaName.CScenarioPointCluster:
                    return new MetaStructureInfo(MetaName.CScenarioPointCluster, 3622480419, 1024, 80,
                     new MetaStructureEntryInfo_s(MetaName.Points, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CScenarioPointContainer),
                     new MetaStructureEntryInfo_s(MetaName.ClusterSphere, 48, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__spdSphere),
                     new MetaStructureEntryInfo_s((MetaName)1095875445, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)3129415068, 68, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CStreamingRequestRecord:
                    return new MetaStructureInfo(MetaName.CStreamingRequestRecord, 3825587854, 768, 40,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CStreamingRequestFrame),
                     new MetaStructureEntryInfo_s(MetaName.Frames, 0, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CStreamingRequestCommonSet),
                     new MetaStructureEntryInfo_s(MetaName.CommonSets, 16, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s(MetaName.NewStyle, 32, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CStreamingRequestFrame:
                    return new MetaStructureInfo(MetaName.CStreamingRequestFrame, 1112444512, 1024, 112,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.AddList, 0, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.RemoveList, 16, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.PromoteToHDList, 32, MetaStructureEntryDataType.Array, 0, 4, 0),
                     new MetaStructureEntryInfo_s(MetaName.CamPos, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CamDir, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CommonAddSets, 80, MetaStructureEntryDataType.Array, 0, 8, 0),
                     new MetaStructureEntryInfo_s(MetaName.Flags, 96, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CStreamingRequestCommonSet:
                    return new MetaStructureInfo(MetaName.CStreamingRequestCommonSet, 3710200606, 768, 16,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Requests, 0, MetaStructureEntryDataType.Array, 0, 0, 0)
                    );
                case MetaName.CMapTypes:
                    return new MetaStructureInfo(MetaName.CMapTypes, 2608875220, 768, 80,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extensions, 8, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.archetypes, 24, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s(MetaName.name, 40, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.dependencies, 48, MetaStructureEntryDataType.Array, 0, 5, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CCompositeEntityType),
                     new MetaStructureEntryInfo_s(MetaName.compositeEntityTypes, 64, MetaStructureEntryDataType.Array, 0, 7, 0)
                    );
                case MetaName.CBaseArchetypeDef:
                    return new MetaStructureInfo(MetaName.CBaseArchetypeDef, 2411387556, 1024, 144,
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 8, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.specialAttribute, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMin, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMax, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsCentre, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsRadius, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.hdTextureDist, 84, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.name, 88, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.textureDictionary, 92, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.clipDictionary, 96, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.drawableDictionary, 100, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.physicsDictionary, 104, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.assetType, 108, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__fwArchetypeDef__eAssetType),
                     new MetaStructureEntryInfo_s(MetaName.assetName, 112, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extensions, 120, MetaStructureEntryDataType.Array, 0, 15, 0)
                    );
                case MetaName.CCreatureMetaData:
                    return new MetaStructureInfo(MetaName.CCreatureMetaData, 2181653572, 768, 56,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CShaderVariableComponent),
                     new MetaStructureEntryInfo_s(MetaName.shaderVariableComponents, 8, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPedPropExpressionData),
                     new MetaStructureEntryInfo_s(MetaName.pedPropExpressions, 24, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPedCompExpressionData),
                     new MetaStructureEntryInfo_s(MetaName.pedCompExpressions, 40, MetaStructureEntryDataType.Array, 0, 4, 0)
                    );
                case MetaName.CShaderVariableComponent:
                    return new MetaStructureInfo(MetaName.CShaderVariableComponent, 3085831725, 768, 72,
                     new MetaStructureEntryInfo_s(MetaName.pedcompID, 8, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.maskID, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.shaderVariableHashString, 16, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.tracks, 24, MetaStructureEntryDataType.Array, 0, 3, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ids, 40, MetaStructureEntryDataType.Array, 0, 5, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.components, 56, MetaStructureEntryDataType.Array, 0, 7, 0)
                    );
                case MetaName.CPedPropExpressionData:
                    return new MetaStructureInfo(MetaName.CPedPropExpressionData, 1355135810, 768, 88,
                     new MetaStructureEntryInfo_s(MetaName.pedPropID, 8, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedPropVarIndex, 12, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedPropExpressionIndex, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.tracks, 24, MetaStructureEntryDataType.Array, 0, 3, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ids, 40, MetaStructureEntryDataType.Array, 0, 5, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.types, 56, MetaStructureEntryDataType.Array, 0, 7, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.components, 72, MetaStructureEntryDataType.Array, 0, 9, 0)
                    );
                case MetaName.CPedCompExpressionData:
                    return new MetaStructureInfo(MetaName.CPedCompExpressionData, 3458164745, 768, 88,
                     new MetaStructureEntryInfo_s(MetaName.pedCompID, 8, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedCompVarIndex, 12, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedCompExpressionIndex, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.tracks, 24, MetaStructureEntryDataType.Array, 0, 3, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ids, 40, MetaStructureEntryDataType.Array, 0, 5, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.types, 56, MetaStructureEntryDataType.Array, 0, 7, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.components, 72, MetaStructureEntryDataType.Array, 0, 9, 0)
                    );
                case MetaName.rage__fwInstancedMapData:
                    return new MetaStructureInfo(MetaName.rage__fwInstancedMapData, 1836780118, 768, 48,
                     new MetaStructureEntryInfo_s(MetaName.ImapLink, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__fwPropInstanceListDef),
                     new MetaStructureEntryInfo_s(MetaName.PropInstanceList, 16, MetaStructureEntryDataType.Array, 0, 1, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__fwGrassInstanceListDef),
                     new MetaStructureEntryInfo_s(MetaName.GrassInstanceList, 32, MetaStructureEntryDataType.Array, 0, 3, 0)
                    );
                case MetaName.CLODLight:
                    return new MetaStructureInfo(MetaName.CLODLight, 2325189228, 768, 136,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.FloatXYZ),
                     new MetaStructureEntryInfo_s(MetaName.direction, 8, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.falloff, 24, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.falloffExponent, 40, MetaStructureEntryDataType.Array, 0, 4, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.timeAndStateFlags, 56, MetaStructureEntryDataType.Array, 0, 6, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.hash, 72, MetaStructureEntryDataType.Array, 0, 8, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coneInnerAngle, 88, MetaStructureEntryDataType.Array, 0, 10, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coneOuterAngleOrCapExt, 104, MetaStructureEntryDataType.Array, 0, 12, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coronaIntensity, 120, MetaStructureEntryDataType.Array, 0, 14, 0)
                    );
                case MetaName.CDistantLODLight:
                    return new MetaStructureInfo(MetaName.CDistantLODLight, 2820908419, 768, 48,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.FloatXYZ),
                     new MetaStructureEntryInfo_s(MetaName.position, 8, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.RGBI, 24, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s(MetaName.numStreetLights, 40, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.category, 42, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0)
                    );
                case MetaName.CBlockDesc:
                    return new MetaStructureInfo(MetaName.CBlockDesc, 2015795449, 768, 72,
                     new MetaStructureEntryInfo_s(MetaName.version, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 4, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.exportedBy, 24, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.owner, 40, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.time, 56, MetaStructureEntryDataType.CharPointer, 0, 0, 0)
                    );
                case MetaName.CMapData:
                    return new MetaStructureInfo(MetaName.CMapData, 3448101671, 1024, 512,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.parent, 12, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.contentFlags, 20, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.streamingExtentsMin, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.streamingExtentsMax, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.entitiesExtentsMin, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.entitiesExtentsMax, 80, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.entities, 96, MetaStructureEntryDataType.Array, 0, 8, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__fwContainerLodDef),
                     new MetaStructureEntryInfo_s(MetaName.containerLods, 112, MetaStructureEntryDataType.Array, 0, 10, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.BoxOccluder),
                     new MetaStructureEntryInfo_s(MetaName.boxOccluders, 128, MetaStructureEntryDataType.Array, 4, 12, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.OccludeModel),
                     new MetaStructureEntryInfo_s(MetaName.occludeModels, 144, MetaStructureEntryDataType.Array, 4, 14, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.physicsDictionaries, 160, MetaStructureEntryDataType.Array, 0, 16, 0),
                     new MetaStructureEntryInfo_s(MetaName.instancedData, 176, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__fwInstancedMapData),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CTimeCycleModifier),
                     new MetaStructureEntryInfo_s(MetaName.timeCycleModifiers, 224, MetaStructureEntryDataType.Array, 0, 19, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CCarGen),
                     new MetaStructureEntryInfo_s(MetaName.carGenerators, 240, MetaStructureEntryDataType.Array, 0, 21, 0),
                     new MetaStructureEntryInfo_s(MetaName.LODLightsSOA, 256, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CLODLight),
                     new MetaStructureEntryInfo_s(MetaName.DistantLODLightsSOA, 392, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CDistantLODLight),
                     new MetaStructureEntryInfo_s(MetaName.block, 440, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CBlockDesc)
                    );
                case MetaName.CEntityDef:
                    return new MetaStructureInfo(MetaName.CEntityDef, 1825799514, 1024, 128,
                     new MetaStructureEntryInfo_s(MetaName.archetypeName, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.guid, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.position, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.rotation, 48, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.scaleXY, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.scaleZ, 68, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.parentIndex, 72, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 76, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.childLodDist, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lodLevel, 84, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__eLodType),
                     new MetaStructureEntryInfo_s(MetaName.numChildren, 88, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.priorityLevel, 92, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__ePriorityLevel),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extensions, 96, MetaStructureEntryDataType.Array, 0, 13, 0),
                     new MetaStructureEntryInfo_s(MetaName.ambientOcclusionMultiplier, 112, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.artificialAmbientOcclusion, 116, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.tintValue, 120, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CTimeCycleModifier:
                    return new MetaStructureInfo(MetaName.CTimeCycleModifier, 2683420777, 1024, 64,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.minExtents, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.maxExtents, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.percentage, 48, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.range, 52, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.startHour, 56, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.endHour, 60, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CTimeArchetypeDef:
                    return new MetaStructureInfo(MetaName.CTimeArchetypeDef, 2520619910, 1024, 160,
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 8, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.specialAttribute, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMin, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMax, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsCentre, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsRadius, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.hdTextureDist, 84, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.name, 88, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.textureDictionary, 92, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.clipDictionary, 96, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.drawableDictionary, 100, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.physicsDictionary, 104, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.assetType, 108, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__fwArchetypeDef__eAssetType),
                     new MetaStructureEntryInfo_s(MetaName.assetName, 112, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extensions, 120, MetaStructureEntryDataType.Array, 0, 15, 0),
                     new MetaStructureEntryInfo_s(MetaName.timeFlags, 144, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CExtensionDefLightEffect:
                    return new MetaStructureInfo(MetaName.CExtensionDefLightEffect, 2436199897, 1024, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CLightAttrDef),
                     new MetaStructureEntryInfo_s(MetaName.instances, 32, MetaStructureEntryDataType.Array, 0, 2, 0)
                    );
                case MetaName.CLightAttrDef:
                    return new MetaStructureInfo(MetaName.CLightAttrDef, 2363260268, 768, 160,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.posn, 8, MetaStructureEntryDataType.ArrayOfBytes, 0, 0, (MetaName)3),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.colour, 20, MetaStructureEntryDataType.ArrayOfBytes, 0, 2, (MetaName)3),
                     new MetaStructureEntryInfo_s(MetaName.flashiness, 23, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.intensity, 24, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 28, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.boneTag, 32, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lightType, 34, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.groupId, 35, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.timeFlags, 36, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.falloff, 40, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.falloffExponent, 44, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.cullingPlane, 48, MetaStructureEntryDataType.ArrayOfBytes, 0, 13, (MetaName)4),
                     new MetaStructureEntryInfo_s(MetaName.shadowBlur, 64, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.padding1, 65, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.padding2, 66, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.padding3, 68, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.volIntensity, 72, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.volSizeScale, 76, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.volOuterColour, 80, MetaStructureEntryDataType.ArrayOfBytes, 0, 21, (MetaName)3),
                     new MetaStructureEntryInfo_s(MetaName.lightHash, 83, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.volOuterIntensity, 84, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coronaSize, 88, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.volOuterExponent, 92, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lightFadeDistance, 96, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.shadowFadeDistance, 97, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.specularFadeDistance, 98, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.volumetricFadeDistance, 99, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.shadowNearClip, 100, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coronaIntensity, 104, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coronaZBias, 108, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.direction, 112, MetaStructureEntryDataType.ArrayOfBytes, 0, 34, (MetaName)3),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.tangent, 124, MetaStructureEntryDataType.ArrayOfBytes, 0, 36, (MetaName)3),
                     new MetaStructureEntryInfo_s(MetaName.coneInnerAngle, 136, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.coneOuterAngle, 140, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extents, 144, MetaStructureEntryDataType.ArrayOfBytes, 0, 40, (MetaName)3),
                     new MetaStructureEntryInfo_s(MetaName.projectedTextureKey, 156, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CMloInstanceDef:
                    return new MetaStructureInfo(MetaName.CMloInstanceDef, 2151576752, 1024, 160,
                     new MetaStructureEntryInfo_s(MetaName.archetypeName, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.guid, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.position, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.rotation, 48, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.scaleXY, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.scaleZ, 68, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.parentIndex, 72, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 76, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.childLodDist, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lodLevel, 84, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__eLodType),
                     new MetaStructureEntryInfo_s(MetaName.numChildren, 88, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.priorityLevel, 92, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__ePriorityLevel),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extensions, 96, MetaStructureEntryDataType.Array, 0, 13, 0),
                     new MetaStructureEntryInfo_s(MetaName.ambientOcclusionMultiplier, 112, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.artificialAmbientOcclusion, 116, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.tintValue, 120, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.groupId, 128, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.floorId, 132, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.defaultEntitySets, 136, MetaStructureEntryDataType.Array, 0, 20, 0),
                     new MetaStructureEntryInfo_s(MetaName.numExitPortals, 152, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.MLOInstflags, 156, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.BoxOccluder:
                    return new MetaStructureInfo(MetaName.BoxOccluder, 1831736438, 256, 16,
                     new MetaStructureEntryInfo_s(MetaName.iCenterX, 0, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iCenterY, 2, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iCenterZ, 4, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iCosZ, 6, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iLength, 8, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iWidth, 10, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iHeight, 12, MetaStructureEntryDataType.SignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iSinZ, 14, MetaStructureEntryDataType.SignedShort, 0, 0, 0)
                    );
                case MetaName.OccludeModel:
                    return new MetaStructureInfo(MetaName.OccludeModel, 1172796107, 1024, 64,
                     new MetaStructureEntryInfo_s(MetaName.bmin, 0, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bmax, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.dataSize, 32, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.verts, 40, MetaStructureEntryDataType.DataBlockPointer, 4, 3, (MetaName)2),
                     new MetaStructureEntryInfo_s(MetaName.numVertsInBytes, 48, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.numTris, 50, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 52, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CMloArchetypeDef:
                    return new MetaStructureInfo(MetaName.CMloArchetypeDef, 937664754, 1024, 240,
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 8, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.specialAttribute, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMin, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMax, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsCentre, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsRadius, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.hdTextureDist, 84, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.name, 88, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.textureDictionary, 92, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.clipDictionary, 96, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.drawableDictionary, 100, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.physicsDictionary, 104, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.assetType, 108, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.rage__fwArchetypeDef__eAssetType),
                     new MetaStructureEntryInfo_s(MetaName.assetName, 112, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.extensions, 120, MetaStructureEntryDataType.Array, 0, 15, 0),
                     new MetaStructureEntryInfo_s(MetaName.mloFlags, 144, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.entities, 152, MetaStructureEntryDataType.Array, 0, 18, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CMloRoomDef),
                     new MetaStructureEntryInfo_s(MetaName.rooms, 168, MetaStructureEntryDataType.Array, 0, 20, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CMloPortalDef),
                     new MetaStructureEntryInfo_s(MetaName.portals, 184, MetaStructureEntryDataType.Array, 0, 22, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CMloEntitySet),
                     new MetaStructureEntryInfo_s(MetaName.entitySets, 200, MetaStructureEntryDataType.Array, 0, 24, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CMloTimeCycleModifier),
                     new MetaStructureEntryInfo_s(MetaName.timeCycleModifiers, 216, MetaStructureEntryDataType.Array, 0, 26, 0)
                    );
                case MetaName.CMloRoomDef:
                    return new MetaStructureInfo(MetaName.CMloRoomDef, 3885428245, 1024, 112,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMin, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMax, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.blend, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.timecycleName, 68, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.secondaryTimecycleName, 72, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 76, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.portalCount, 80, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.floorId, 84, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.exteriorVisibiltyDepth, 88, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.attachedObjects, 96, MetaStructureEntryDataType.Array, 0, 10, 0)
                    );
                case MetaName.CMloPortalDef:
                    return new MetaStructureInfo(MetaName.CMloPortalDef, 1110221513, 768, 64,
                     new MetaStructureEntryInfo_s(MetaName.roomFrom, 8, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.roomTo, 12, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 16, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.mirrorPriority, 20, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.opacity, 24, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.audioOcclusion, 28, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.corners, 32, MetaStructureEntryDataType.Array, 0, 6, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.attachedObjects, 48, MetaStructureEntryDataType.Array, 0, 8, 0)
                    );
                case MetaName.CMloTimeCycleModifier:
                    return new MetaStructureInfo(MetaName.CMloTimeCycleModifier, 838874674, 1024, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.sphere, 16, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.percentage, 32, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.range, 36, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.startHour, 40, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.endHour, 44, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CExtensionDefParticleEffect:
                    return new MetaStructureInfo(MetaName.CExtensionDefParticleEffect, 466596385, 1024, 96,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetRotation, 32, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fxName, 48, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fxType, 64, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.boneTag, 68, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.scale, 72, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.probability, 76, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 80, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.color, 84, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CCompositeEntityType:
                    return new MetaStructureInfo(MetaName.CCompositeEntityType, 659539004, 1024, 304,
                     new MetaStructureEntryInfo_s(MetaName.Name, 0, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 68, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.specialAttribute, 72, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMin, 80, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bbMax, 96, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsCentre, 112, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bsRadius, 128, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.StartModel, 136, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.EndModel, 200, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.StartImapFile, 264, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.EndImapFile, 268, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.PtFxAssetName, 272, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CCompEntityAnims),
                     new MetaStructureEntryInfo_s(MetaName.Animations, 280, MetaStructureEntryDataType.Array, 0, 13, 0)
                    );
                case MetaName.CCompEntityAnims:
                    return new MetaStructureInfo(MetaName.CCompEntityAnims, 4110496011, 768, 216,
                     new MetaStructureEntryInfo_s(MetaName.AnimDict, 0, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.AnimName, 64, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.AnimatedModel, 128, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.punchInPhase, 192, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.punchOutPhase, 196, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CCompEntityEffectsData),
                     new MetaStructureEntryInfo_s(MetaName.effectsData, 200, MetaStructureEntryDataType.Array, 0, 5, 0)
                    );
                case MetaName.CCompEntityEffectsData:
                    return new MetaStructureInfo(MetaName.CCompEntityEffectsData, 1724963966, 1024, 160,
                     new MetaStructureEntryInfo_s(MetaName.fxType, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fxOffsetPos, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fxOffsetRot, 32, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.boneTag, 48, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.startPhase, 52, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.endPhase, 56, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxIsTriggered, 60, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxTag, 61, MetaStructureEntryDataType.ArrayOfChars, 0, 0, (MetaName)64),
                     new MetaStructureEntryInfo_s(MetaName.ptFxScale, 128, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxProbability, 132, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxHasTint, 136, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxTintR, 137, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxTintG, 138, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxTintB, 139, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ptFxSize, 144, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0)
                    );
                case MetaName.CExtensionDefAudioCollisionSettings:
                    return new MetaStructureInfo(MetaName.CExtensionDefAudioCollisionSettings, 2701897500, 1024, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.settings, 32, MetaStructureEntryDataType.Hash, 0, 0, 0)
                    );
                case MetaName.CExtensionDefAudioEmitter:
                    return new MetaStructureInfo(MetaName.CExtensionDefAudioEmitter, 15929839, 1024, 64,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetRotation, 32, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.effectHash, 48, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CExtensionDefExplosionEffect:
                    return new MetaStructureInfo(MetaName.CExtensionDefExplosionEffect, 2840366784, 1024, 80,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetRotation, 32, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.explosionName, 48, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.boneTag, 64, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.explosionTag, 68, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.explosionType, 72, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 76, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.CExtensionDefLadder:
                    return new MetaStructureInfo(MetaName.CExtensionDefLadder, 1978210597, 1024, 96,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bottom, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.top, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.normal, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.materialType, 80, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.CExtensionDefLadderMaterialType),
                     new MetaStructureEntryInfo_s(MetaName.template, 84, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.canGetOffAtTop, 88, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.canGetOffAtBottom, 89, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CExtensionDefBuoyancy:
                    return new MetaStructureInfo(MetaName.CExtensionDefBuoyancy, 2383039928, 1024, 32,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0)
                    );
                case MetaName.CExtensionDefExpression:
                    return new MetaStructureInfo(MetaName.CExtensionDefExpression, 24441706, 1024, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.expressionDictionaryName, 32, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.expressionName, 36, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.creatureMetadataName, 40, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.initialiseOnCollision, 44, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CExtensionDefLightShaft:
                    return new MetaStructureInfo(MetaName.CExtensionDefLightShaft, 2526429398, 1024, 176,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.cornerA, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.cornerB, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.cornerC, 64, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.cornerD, 80, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.direction, 96, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.directionAmount, 112, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.length, 116, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fadeInTimeStart, 120, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fadeInTimeEnd, 124, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fadeOutTimeStart, 128, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fadeOutTimeEnd, 132, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fadeDistanceStart, 136, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.fadeDistanceEnd, 140, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.color, 144, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.intensity, 148, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flashiness, 152, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 156, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.densityType, 160, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.CExtensionDefLightShaftDensityType),
                     new MetaStructureEntryInfo_s(MetaName.volumeType, 164, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.CExtensionDefLightShaftVolumeType),
                     new MetaStructureEntryInfo_s(MetaName.softness, 168, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.scaleBySunIntensity, 172, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.FloatXYZ:
                    return new MetaStructureInfo(MetaName.FloatXYZ, 2751397072, 512, 12,
                     new MetaStructureEntryInfo_s(MetaName.x, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.y, 4, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.z, 8, MetaStructureEntryDataType.Float, 0, 0, 0)
                    );
                case MetaName.CPedPropInfo:
                    return new MetaStructureInfo(MetaName.CPedPropInfo, 1792487819, 768, 40,
                     new MetaStructureEntryInfo_s(MetaName.numAvailProps, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPedPropMetaData),
                     new MetaStructureEntryInfo_s(MetaName.aPropMetaData, 8, MetaStructureEntryDataType.Array, 0, 1, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CAnchorProps),
                     new MetaStructureEntryInfo_s(MetaName.aAnchors, 24, MetaStructureEntryDataType.Array, 0, 3, 0)
                    );
                case MetaName.CPedVariationInfo:
                    return new MetaStructureInfo(MetaName.CPedVariationInfo, 4030871161, 768, 112,
                     new MetaStructureEntryInfo_s(MetaName.bHasTexVariations, 0, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bHasDrawblVariations, 1, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bHasLowLODs, 2, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bIsSuperLOD, 3, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.availComp, 4, MetaStructureEntryDataType.ArrayOfBytes, 0, 4, (MetaName)MetaTypeName.PsoPOINTER),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPVComponentData),
                     new MetaStructureEntryInfo_s(MetaName.aComponentData3, 16, MetaStructureEntryDataType.Array, 0, 6, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPedSelectionSet),
                     new MetaStructureEntryInfo_s(MetaName.aSelectionSets, 32, MetaStructureEntryDataType.Array, 0, 8, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CComponentInfo),
                     new MetaStructureEntryInfo_s(MetaName.compInfos, 48, MetaStructureEntryDataType.Array, 0, 10, 0),
                     new MetaStructureEntryInfo_s(MetaName.propInfo, 64, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPedPropInfo),
                     new MetaStructureEntryInfo_s(MetaName.dlcName, 104, MetaStructureEntryDataType.Hash, 0, 0, 0)
                    );
                case MetaName.CPVComponentData:
                    return new MetaStructureInfo(MetaName.CPVComponentData, 2024084511, 768, 24,
                     new MetaStructureEntryInfo_s(MetaName.numAvailTex, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPVDrawblData),
                     new MetaStructureEntryInfo_s(MetaName.aDrawblData3, 8, MetaStructureEntryDataType.Array, 0, 1, 0)
                    );
                case MetaName.CPVDrawblData__CPVClothComponentData:
                    return new MetaStructureInfo(MetaName.CPVDrawblData__CPVClothComponentData, 508935687, 0, 24,
                     new MetaStructureEntryInfo_s(MetaName.ownsCloth, 0, MetaStructureEntryDataType.Boolean, 0, 0, 0)
                    );
                case MetaName.CPVDrawblData:
                    return new MetaStructureInfo(MetaName.CPVDrawblData, 124073662, 768, 48,
                     new MetaStructureEntryInfo_s(MetaName.propMask, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.numAlternatives, 1, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPVTextureData),
                     new MetaStructureEntryInfo_s(MetaName.aTexData, 8, MetaStructureEntryDataType.Array, 0, 2, 0),
                     new MetaStructureEntryInfo_s(MetaName.clothData, 24, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPVDrawblData__CPVClothComponentData)
                    );
                case MetaName.CPVTextureData:
                    return new MetaStructureInfo(MetaName.CPVTextureData, 4272717794, 0, 3,
                     new MetaStructureEntryInfo_s(MetaName.texId, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.distribution, 1, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0)
                    );
                case MetaName.CComponentInfo:
                    return new MetaStructureInfo(MetaName.CComponentInfo, 3693847250, 512, 48,
                     new MetaStructureEntryInfo_s(MetaName.pedXml_audioID, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedXml_audioID2, 4, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedXml_expressionMods, 8, MetaStructureEntryDataType.ArrayOfBytes, 0, 2, (MetaName)5),
                     new MetaStructureEntryInfo_s(MetaName.flags, 28, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.inclusions, 32, MetaStructureEntryDataType.IntFlags2, 0, 32, 0),
                     new MetaStructureEntryInfo_s(MetaName.exclusions, 36, MetaStructureEntryDataType.IntFlags2, 0, 32, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedXml_vfxComps, 40, MetaStructureEntryDataType.ShortFlags, 0, 16, MetaName.ePedVarComp),
                     new MetaStructureEntryInfo_s(MetaName.pedXml_flags, 42, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedXml_compIdx, 44, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.pedXml_drawblIdx, 45, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0)
                    );
                case MetaName.CPedPropMetaData:
                    return new MetaStructureInfo(MetaName.CPedPropMetaData, 2029738350, 768, 56,
                     new MetaStructureEntryInfo_s(MetaName.audioId, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.expressionMods, 4, MetaStructureEntryDataType.ArrayOfBytes, 0, 1, (MetaName)5),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.CPedPropTexData),
                     new MetaStructureEntryInfo_s(MetaName.texData, 24, MetaStructureEntryDataType.Array, 0, 3, 0),
                     new MetaStructureEntryInfo_s(MetaName.renderFlags, 40, MetaStructureEntryDataType.IntFlags1, 0, 3, MetaName.ePropRenderFlags),
                     new MetaStructureEntryInfo_s(MetaName.propFlags, 44, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 48, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.anchorId, 50, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.propId, 51, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.stickyness, 52, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0)
                    );
                case MetaName.CPedPropTexData:
                    return new MetaStructureInfo(MetaName.CPedPropTexData, 2767296137, 512, 12,
                     new MetaStructureEntryInfo_s(MetaName.inclusions, 0, MetaStructureEntryDataType.IntFlags2, 0, 32, 0),
                     new MetaStructureEntryInfo_s(MetaName.exclusions, 4, MetaStructureEntryDataType.IntFlags2, 0, 32, 0),
                     new MetaStructureEntryInfo_s(MetaName.texId, 8, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.inclusionId, 9, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.exclusionId, 10, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.distribution, 11, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0)
                    );
                case MetaName.CAnchorProps:
                    return new MetaStructureInfo(MetaName.CAnchorProps, 403574180, 768, 24,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.props, 0, MetaStructureEntryDataType.Array, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.anchor, 16, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.eAnchorPoints)
                    );
                case MetaName.CPedSelectionSet:
                    return new MetaStructureInfo(MetaName.CPedSelectionSet, 3120284999, 512, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 0, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.compDrawableId, 4, MetaStructureEntryDataType.ArrayOfBytes, 0, 1, (MetaName)MetaTypeName.PsoPOINTER),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.compTexId, 16, MetaStructureEntryDataType.ArrayOfBytes, 0, 3, (MetaName)MetaTypeName.PsoPOINTER),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.propAnchorId, 28, MetaStructureEntryDataType.ArrayOfBytes, 0, 5, (MetaName)6),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.propDrawableId, 34, MetaStructureEntryDataType.ArrayOfBytes, 0, 7, (MetaName)6),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.propTexId, 40, MetaStructureEntryDataType.ArrayOfBytes, 0, 9, (MetaName)6)
                    );
                case MetaName.CExtensionDefDoor:
                    return new MetaStructureInfo(MetaName.CExtensionDefDoor, 2671601385, 1024, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.enableLimitAngle, 32, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.startsLocked, 33, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.canBreak, 34, MetaStructureEntryDataType.Boolean, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.limitAngle, 36, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.doorTargetRatio, 40, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.audioHash, 44, MetaStructureEntryDataType.Hash, 0, 0, 0)
                    );
                case MetaName.CMloEntitySet:
                    return new MetaStructureInfo(MetaName.CMloEntitySet, 4180211587, 768, 48,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.locations, 16, MetaStructureEntryDataType.Array, 0, 1, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.StructurePointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.entities, 32, MetaStructureEntryDataType.Array, 0, 3, 0)
                    );
                case MetaName.CExtensionDefSpawnPointOverride:
                    return new MetaStructureInfo(MetaName.CExtensionDefSpawnPointOverride, 2551875873, 1024, 64,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ScenarioType, 32, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iTimeStartOverride, 36, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.iTimeEndOverride, 37, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Group, 40, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.ModelSet, 44, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.AvailabilityInMpSp, 48, MetaStructureEntryDataType.IntEnum, 0, 0, MetaName.CSpawnPoint__AvailabilityMpSp),
                     new MetaStructureEntryInfo_s(MetaName.Flags, 52, MetaStructureEntryDataType.IntFlags2, 0, 32, MetaName.CScenarioPointFlags__Flags),
                     new MetaStructureEntryInfo_s(MetaName.Radius, 56, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.TimeTillPedLeaves, 60, MetaStructureEntryDataType.Float, 0, 0, 0)
                    );
                case MetaName.CExtensionDefWindDisturbance:
                    return new MetaStructureInfo(MetaName.CExtensionDefWindDisturbance, 3971538917, 1024, 96,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetRotation, 32, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.disturbanceType, 48, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.boneTag, 52, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.size, 64, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.strength, 80, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 84, MetaStructureEntryDataType.SignedInt, 0, 0, 0)
                    );
                case MetaName.CCarGen:
                    return new MetaStructureInfo(MetaName.CCarGen, 2345238261, 1024, 80,
                     new MetaStructureEntryInfo_s(MetaName.position, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.orientX, 32, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.orientY, 36, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.perpendicularLength, 40, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.carModel, 44, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 48, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bodyColorRemap1, 52, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bodyColorRemap2, 56, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bodyColorRemap3, 60, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.bodyColorRemap4, 64, MetaStructureEntryDataType.SignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.popGroup, 68, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.livery, 72, MetaStructureEntryDataType.SignedByte, 0, 0, 0)
                    );
                case MetaName.rage__spdAABB:
                    return new MetaStructureInfo(MetaName.rage__spdAABB, 1158138379, 1024, 32,
                     new MetaStructureEntryInfo_s(MetaName.min, 0, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.max, 16, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0)
                    );
                case MetaName.rage__fwGrassInstanceListDef:
                    return new MetaStructureInfo(MetaName.rage__fwGrassInstanceListDef, 941808164, 1024, 96,
                     new MetaStructureEntryInfo_s(MetaName.BatchAABB, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__spdAABB),
                     new MetaStructureEntryInfo_s(MetaName.ScaleRange, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.archetypeName, 48, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.lodDist, 52, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.LodFadeStartDist, 56, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.LodInstFadeRange, 60, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.OrientToTerrain, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__fwGrassInstanceListDef__InstanceData),
                     new MetaStructureEntryInfo_s(MetaName.InstanceList, 72, MetaStructureEntryDataType.Array, 36, 7, 0)
                    );
                case MetaName.rage__fwGrassInstanceListDef__InstanceData:
                    return new MetaStructureInfo(MetaName.rage__fwGrassInstanceListDef__InstanceData, 2740378365, 256, 16,
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedShort, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Position, 0, MetaStructureEntryDataType.ArrayOfBytes, 0, 0, (MetaName)3),
                     new MetaStructureEntryInfo_s(MetaName.NormalX, 6, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.NormalY, 7, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Color, 8, MetaStructureEntryDataType.ArrayOfBytes, 0, 4, (MetaName)3),
                     new MetaStructureEntryInfo_s(MetaName.Scale, 11, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Ao, 12, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.UnsignedByte, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Pad, 13, MetaStructureEntryDataType.ArrayOfBytes, 0, 8, (MetaName)3)
                    );
                case MetaName.CExtensionDefProcObject:
                    return new MetaStructureInfo(MetaName.CExtensionDefProcObject, 3965391891, 1024, 80,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.offsetPosition, 16, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.radiusInner, 32, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.radiusOuter, 36, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.spacing, 40, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.minScale, 44, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.maxScale, 48, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.minScaleZ, 52, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.maxScaleZ, 56, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.minZOffset, 60, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.maxZOffset, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.objectHash, 68, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.flags, 72, MetaStructureEntryDataType.UnsignedInt, 0, 0, 0)
                    );
                case MetaName.rage__phVerletClothCustomBounds:
                    return new MetaStructureInfo(MetaName.rage__phVerletClothCustomBounds, 2075461750, 768, 32,
                     new MetaStructureEntryInfo_s(MetaName.name, 8, MetaStructureEntryDataType.Hash, 0, 0, 0),
                     new MetaStructureEntryInfo_s((MetaName)MetaTypeName.ARRAYINFO, 0, MetaStructureEntryDataType.Structure, 0, 0, MetaName.rage__phCapsuleBoundDef),
                     new MetaStructureEntryInfo_s(MetaName.CollisionData, 16, MetaStructureEntryDataType.Array, 0, 1, 0)
                    );
                case MetaName.rage__phCapsuleBoundDef:
                    return new MetaStructureInfo(MetaName.rage__phCapsuleBoundDef, 2859775340, 1024, 96,
                     new MetaStructureEntryInfo_s(MetaName.OwnerName, 0, MetaStructureEntryDataType.CharPointer, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Rotation, 16, MetaStructureEntryDataType.Float_XYZW, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Position, 32, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Normal, 48, MetaStructureEntryDataType.Float_XYZ, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CapsuleRadius, 64, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CapsuleLen, 68, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CapsuleHalfHeight, 72, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.CapsuleHalfWidth, 76, MetaStructureEntryDataType.Float, 0, 0, 0),
                     new MetaStructureEntryInfo_s(MetaName.Flags, 80, MetaStructureEntryDataType.IntFlags2, 0, 32, MetaName.rage__phCapsuleBoundDef__enCollisionBoundDef)
                    );

                default:
                    return null;
            }
        }
        public static MetaEnumInfo GetEnumInfo(MetaName name)
        {
            switch (name)
            {
                case MetaName.CScenarioPointFlags__Flags:
                    return new MetaEnumInfo(MetaName.CScenarioPointFlags__Flags, 2814596095,
                     new MetaEnumEntryInfo_s(MetaName.IgnoreMaxInRange, 0),
                     new MetaEnumEntryInfo_s(MetaName.NoSpawn, 1),
                     new MetaEnumEntryInfo_s(MetaName.StationaryReactions, 2),
                     new MetaEnumEntryInfo_s(MetaName.OnlySpawnInSameInterior, 3),
                     new MetaEnumEntryInfo_s(MetaName.SpawnedPedIsArrestable, 4),
                     new MetaEnumEntryInfo_s(MetaName.ActivateVehicleSiren, 5),
                     new MetaEnumEntryInfo_s(MetaName.AggressiveVehicleDriving, 6),
                     new MetaEnumEntryInfo_s(MetaName.LandVehicleOnArrival, 7),
                     new MetaEnumEntryInfo_s(MetaName.IgnoreThreatsIfLosNotClear, 8),
                     new MetaEnumEntryInfo_s(MetaName.EventsInRadiusTriggerDisputes, 9),
                     new MetaEnumEntryInfo_s(MetaName.AerialVehiclePoint, 10),
                     new MetaEnumEntryInfo_s(MetaName.TerritorialScenario, 11),
                     new MetaEnumEntryInfo_s(MetaName.EndScenarioIfPlayerWithinRadius, 12),
                     new MetaEnumEntryInfo_s(MetaName.EventsInRadiusTriggerThreatResponse, 13),
                     new MetaEnumEntryInfo_s(MetaName.TaxiPlaneOnGround, 14),
                     new MetaEnumEntryInfo_s(MetaName.FlyOffToOblivion, 15),
                     new MetaEnumEntryInfo_s(MetaName.InWater, 16),
                     new MetaEnumEntryInfo_s(MetaName.AllowInvestigation, 17),
                     new MetaEnumEntryInfo_s(MetaName.OpenDoor, 18),
                     new MetaEnumEntryInfo_s(MetaName.PreciseUseTime, 19),
                     new MetaEnumEntryInfo_s(MetaName.NoRespawnUntilStreamedOut, 20),
                     new MetaEnumEntryInfo_s(MetaName.NoVehicleSpawnMaxDistance, 21),
                     new MetaEnumEntryInfo_s(MetaName.ExtendedRange, 22),
                     new MetaEnumEntryInfo_s(MetaName.ShortRange, 23),
                     new MetaEnumEntryInfo_s(MetaName.HighPriority, 24),
                     new MetaEnumEntryInfo_s(MetaName.IgnoreLoitering, 25),
                     new MetaEnumEntryInfo_s(MetaName.UseSearchlight, 26),
                     new MetaEnumEntryInfo_s(MetaName.ResetNoCollisionOnCleanUp, 27),
                     new MetaEnumEntryInfo_s(MetaName.CheckCrossedArrivalPlane, 28),
                     new MetaEnumEntryInfo_s(MetaName.UseVehicleFrontForArrival, 29),
                     new MetaEnumEntryInfo_s(MetaName.IgnoreWeatherRestrictions, 30)
                    );
                case MetaName.CSpawnPoint__AvailabilityMpSp:
                    return new MetaEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp, 671739257,
                     new MetaEnumEntryInfo_s(MetaName.kBoth, 0),
                     new MetaEnumEntryInfo_s(MetaName.kOnlySp, 1),
                     new MetaEnumEntryInfo_s(MetaName.kOnlyMp, 2)
                    );
                case MetaName.CScenarioChainingEdge__eAction:
                    return new MetaEnumInfo(MetaName.CScenarioChainingEdge__eAction, 3326075799,
                     new MetaEnumEntryInfo_s(MetaName.Move, 0),
                     new MetaEnumEntryInfo_s((MetaName)7865678, 1),
                     new MetaEnumEntryInfo_s(MetaName.MoveFollowMaster, 2)
                    );
                case MetaName.CScenarioChainingEdge__eNavMode:
                    return new MetaEnumInfo(MetaName.CScenarioChainingEdge__eNavMode, 3016128742,
                     new MetaEnumEntryInfo_s(MetaName.Direct, 0),
                     new MetaEnumEntryInfo_s(MetaName.NavMesh, 1),
                     new MetaEnumEntryInfo_s(MetaName.Roads, 2)
                    );
                case MetaName.CScenarioChainingEdge__eNavSpeed:
                    return new MetaEnumInfo(MetaName.CScenarioChainingEdge__eNavSpeed, 1112851290,
                     new MetaEnumEntryInfo_s((MetaName)3279574318, 0),
                     new MetaEnumEntryInfo_s((MetaName)2212923970, 1),
                     new MetaEnumEntryInfo_s((MetaName)4022799658, 2),
                     new MetaEnumEntryInfo_s((MetaName)1425672334, 3),
                     new MetaEnumEntryInfo_s((MetaName)957720931, 4),
                     new MetaEnumEntryInfo_s((MetaName)3795195414, 5),
                     new MetaEnumEntryInfo_s((MetaName)2834622009, 6),
                     new MetaEnumEntryInfo_s((MetaName)1876554076, 7),
                     new MetaEnumEntryInfo_s((MetaName)698543797, 8),
                     new MetaEnumEntryInfo_s((MetaName)1544199634, 9),
                     new MetaEnumEntryInfo_s((MetaName)2725613303, 10),
                     new MetaEnumEntryInfo_s((MetaName)4033265820, 11),
                     new MetaEnumEntryInfo_s((MetaName)3054809929, 12),
                     new MetaEnumEntryInfo_s((MetaName)3911005380, 13),
                     new MetaEnumEntryInfo_s((MetaName)3717649022, 14),
                     new MetaEnumEntryInfo_s((MetaName)3356026130, 15)
                    );
                case MetaName.rage__fwArchetypeDef__eAssetType:
                    return new MetaEnumInfo(MetaName.rage__fwArchetypeDef__eAssetType, 1866031916,
                     new MetaEnumEntryInfo_s(MetaName.ASSET_TYPE_UNINITIALIZED, 0),
                     new MetaEnumEntryInfo_s(MetaName.ASSET_TYPE_FRAGMENT, 1),
                     new MetaEnumEntryInfo_s(MetaName.ASSET_TYPE_DRAWABLE, 2),
                     new MetaEnumEntryInfo_s(MetaName.ASSET_TYPE_DRAWABLEDICTIONARY, 3),
                     new MetaEnumEntryInfo_s(MetaName.ASSET_TYPE_ASSETLESS, 4)
                    );
                case MetaName.rage__eLodType:
                    return new MetaEnumInfo(MetaName.rage__eLodType, 1856311430,
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_HD, 0),
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_LOD, 1),
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_SLOD1, 2),
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_SLOD2, 3),
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_SLOD3, 4),
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_ORPHANHD, 5),
                     new MetaEnumEntryInfo_s(MetaName.LODTYPES_DEPTH_SLOD4, 6)
                    );
                case MetaName.rage__ePriorityLevel:
                    return new MetaEnumInfo(MetaName.rage__ePriorityLevel, 2200357711,
                     new MetaEnumEntryInfo_s(MetaName.PRI_REQUIRED, 0),
                     new MetaEnumEntryInfo_s(MetaName.PRI_OPTIONAL_HIGH, 1),
                     new MetaEnumEntryInfo_s(MetaName.PRI_OPTIONAL_MEDIUM, 2),
                     new MetaEnumEntryInfo_s(MetaName.PRI_OPTIONAL_LOW, 3)
                    );
                case MetaName.CExtensionDefLadderMaterialType:
                    return new MetaEnumInfo(MetaName.CExtensionDefLadderMaterialType, 3514570158,
                     new MetaEnumEntryInfo_s(MetaName.METAL_SOLID_LADDER, 0),
                     new MetaEnumEntryInfo_s(MetaName.METAL_LIGHT_LADDER, 1),
                     new MetaEnumEntryInfo_s(MetaName.WOODEN_LADDER, 2)
                    );
                case MetaName.CExtensionDefLightShaftDensityType:
                    return new MetaEnumInfo(MetaName.CExtensionDefLightShaftDensityType, 3539601182,
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_CONSTANT, 0),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_SOFT, 1),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_SOFT_SHADOW, 2),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_SOFT_SHADOW_HD, 3),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_LINEAR, 4),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_LINEAR_GRADIENT, 5),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_QUADRATIC, 6),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_DENSITYTYPE_QUADRATIC_GRADIENT, 7)
                    );
                case MetaName.CExtensionDefLightShaftVolumeType:
                    return new MetaEnumInfo(MetaName.CExtensionDefLightShaftVolumeType, 4287472345,
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_VOLUMETYPE_SHAFT, 0),
                     new MetaEnumEntryInfo_s(MetaName.LIGHTSHAFT_VOLUMETYPE_CYLINDER, 1)
                    );
                case MetaName.ePedVarComp:
                    return new MetaEnumInfo(MetaName.ePedVarComp, 3472084374,
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_INVALID, -1),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_HEAD, 0),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_BERD, 1),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_HAIR, 2),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_UPPR, 3),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_LOWR, 4),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_HAND, 5),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_FEET, 6),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_TEEF, 7),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_ACCS, 8),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_TASK, 9),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_DECL, 10),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_JBIB, 11),
                     new MetaEnumEntryInfo_s(MetaName.PV_COMP_MAX, 12)
                    );
                case MetaName.ePropRenderFlags:
                    return new MetaEnumInfo(MetaName.ePropRenderFlags, 1551913633,
                     new MetaEnumEntryInfo_s(MetaName.PRF_ALPHA, 0),
                     new MetaEnumEntryInfo_s(MetaName.PRF_DECAL, 1),
                     new MetaEnumEntryInfo_s(MetaName.PRF_CUTOUT, 2)
                    );
                case MetaName.eAnchorPoints:
                    return new MetaEnumInfo(MetaName.eAnchorPoints, 1309372691,
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_HEAD, 0),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_EYES, 1),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_EARS, 2),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_MOUTH, 3),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_LEFT_HAND, 4),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_RIGHT_HAND, 5),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_LEFT_WRIST, 6),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_RIGHT_WRIST, 7),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_HIP, 8),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_LEFT_FOOT, 9),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_RIGHT_FOOT, 10),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_PH_L_HAND, 11),
                     new MetaEnumEntryInfo_s(MetaName.ANCHOR_PH_R_HAND, 12),
                     new MetaEnumEntryInfo_s(MetaName.NUM_ANCHORS, 13)
                    );
                case MetaName.rage__phCapsuleBoundDef__enCollisionBoundDef:
                    return new MetaEnumInfo(MetaName.rage__phCapsuleBoundDef__enCollisionBoundDef, 1585854303,
                     new MetaEnumEntryInfo_s(MetaName.BOUND_DEF_IS_PLANE, 0)
                    );

                default:
                    return null;
            }
        }

        private static string GetSafeName(MetaName namehash, uint key)
        {
            string name = namehash.ToString();
            if (string.IsNullOrEmpty(name))
            {
                name = "Unk_" + key;
            }
            if (!char.IsLetter(name[0]))
            {
                name = "Unk_" + name;
            }
            return name;
        }

        public static byte[] ConvertToBytes<T>(T item) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            int offset = 0;
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(item, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            offset += size;
            return arr;
        }
        public static byte[] ConvertArrayToBytes<T>(params T[] items) where T : struct
        {
            if (items == null) return null;

            var size = Marshal.SizeOf(typeof(T)) * items.Length;
            var b = new byte[size];
            GCHandle handle = GCHandle.Alloc(items, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            Marshal.Copy(h, b, 0, size);
            handle.Free();
            return b;

        }

        public static T ConvertData<T>(byte[] data) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            var r = Marshal.PtrToStructure<T>(h);
            handle.Free();
            return r;
        }
        public static T ConvertData<T>(byte[] data, int offset) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            var r = Marshal.PtrToStructure<T>(h + offset);
            handle.Free();
            return r;
        }
        public static T[] ConvertDataArray<T>(byte[] data, int offset, int count) where T : struct
        {
            T[] items = new T[count];
            int itemsize = Marshal.SizeOf(typeof(T));
            GCHandle handle = GCHandle.Alloc(items, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            Marshal.Copy(data, offset, h, itemsize * count);
            handle.Free();

            return items;
        }
        public static T[] ConvertDataArray<T>(Meta meta, MetaName name, Array_StructurePointer array) where T : struct
        {
            uint count = array.Count1;
            if (count == 0) return null;
            MetaPOINTER[] ptrs = GetPointerArray(meta, array);
            if (ptrs == null) return null;
            if (ptrs.Length < count)
            { return null; }

            T[] items = new T[count];
            int itemsize = Marshal.SizeOf(typeof(T));
            int itemsleft = (int)count;

            for (int i = 0; i < count; i++)
            {
                var ptr = ptrs[i];
                var offset = ptr.Offset;
                var block = meta.GetBlock(ptr.BlockID);
                if (block == null) continue;

                if (block.StructureNameHash != name)
                { return null; }
                if ((offset < 0) || (block.Data == null) || (offset >= block.Data.Length))
                { continue; }
                items[i] = ConvertData<T>(block.Data, offset);
            }

            return items;
        }
        public static T[] ConvertDataArray<T>(Meta meta, MetaName name, Array_Structure array) where T : struct
        {
            return ConvertDataArray<T>(meta, name, array.Pointer, array.Count1);
        }
        public static T[] ConvertDataArray<T>(Meta meta, MetaName name, ulong pointer, uint count) where T : struct
        {
            if (count == 0) return null;

            T[] items = new T[count];
            int itemsize = Marshal.SizeOf(typeof(T));
            int itemsleft = (int)count;

            uint ptrindex = (uint)(pointer & 0xFFF) - 1;
            uint ptroffset = (uint)((pointer >> 12) & 0xFFFFF);
            var ptrblock = (ptrindex < meta.DataBlocks.Count) ? meta.DataBlocks[(int)ptrindex] : null;
            if ((ptrblock == null) || (ptrblock.Data == null) || (ptrblock.StructureNameHash != name))
            { return null; }

            int byteoffset = (int)ptroffset;
            int itemoffset = byteoffset / itemsize;

            int curi = 0;
            while (itemsleft > 0)
            {
                int blockcount = ptrblock.DataLength / itemsize;
                int itemcount = blockcount - itemoffset;
                if (itemcount > itemsleft)
                { itemcount = itemsleft; }
                for (int i = 0; i < itemcount; i++)
                {
                    int offset = (itemoffset + i) * itemsize;
                    int index = curi + i;
                    items[index] = ConvertData<T>(ptrblock.Data, offset);
                }
                itemoffset = 0;
                curi += itemcount;
                itemsleft -= itemcount;
                if (itemsleft <= 0)
                { return items; }
                ptrindex++;
                ptrblock = (ptrindex < meta.DataBlocks.Count) ? meta.DataBlocks[(int)ptrindex] : null;
                if ((ptrblock == null) || (ptrblock.Data == null))
                { break; }
                if (ptrblock.StructureNameHash != name)
                { break; }
            }

            return null;

            #region old version

            #endregion
        }

        public static MetaPOINTER[] GetPointerArray(Meta meta, Array_StructurePointer array)
        {
            uint count = array.Count1;
            if (count == 0) return null;

            MetaPOINTER[] ptrs = new MetaPOINTER[count];
            int ptrsize = Marshal.SizeOf(typeof(MetaPOINTER));
            int ptroffset = (int)array.PointerDataOffset;
            var ptrblock = meta.GetBlock((int)array.PointerDataId);
            if ((ptrblock == null) || (ptrblock.Data == null) || (ptrblock.StructureNameHash != (MetaName)MetaTypeName.POINTER))
            { return null; }

            for (int i = 0; i < count; i++)
            {
                int offset = ptroffset + (i * ptrsize);
                if (offset >= ptrblock.Data.Length)
                { break; }
                ptrs[i] = ConvertData<MetaPOINTER>(ptrblock.Data, offset);
            }

            return ptrs;
        }

        public static MetaHash[] GetHashArray(Meta meta, Array_uint array)
        {
            return ConvertDataArray<MetaHash>(meta, (MetaName)MetaTypeName.HASH, array.Pointer, array.Count1);
        }
        public static Vector4[] GetPaddedVector3Array(Meta meta, Array_Vector3 array)
        {
            return ConvertDataArray<Vector4>(meta, (MetaName)MetaTypeName.VECTOR4, array.Pointer, array.Count1);
        }
        public static uint[] GetUintArray(Meta meta, Array_uint array)
        {
            return ConvertDataArray<uint>(meta, (MetaName)MetaTypeName.UINT, array.Pointer, array.Count1);
        }
        public static ushort[] GetUshortArray(Meta meta, Array_ushort array)
        {
            return ConvertDataArray<ushort>(meta, (MetaName)MetaTypeName.USHORT, array.Pointer, array.Count1);
        }
        public static short[] GetShortArray(Meta meta, Array_ushort array)
        {
            return ConvertDataArray<short>(meta, (MetaName)MetaTypeName.USHORT, array.Pointer, array.Count1);
        }
        public static float[] GetFloatArray(Meta meta, Array_float array)
        {
            return ConvertDataArray<float>(meta, (MetaName)MetaTypeName.FLOAT, array.Pointer, array.Count1);
        }
        public static byte[] GetByteArray(Meta meta, Array_byte array)
        {
            uint ptrindex = array.PointerDataIndex;
            uint ptroffset = array.PointerDataOffset;
            var ptrblock = (ptrindex < meta.DataBlocks.Count) ? meta.DataBlocks[(int)ptrindex] : null;
            if ((ptrblock == null) || (ptrblock.Data == null))
            { return null; }
            var count = array.Count1;
            if ((ptroffset + count) > ptrblock.Data.Length)
            { return null; }
            byte[] data = new byte[count];
            Buffer.BlockCopy(ptrblock.Data, (int)ptroffset, data, 0, count);
            return data;
        }
        public static byte[] GetByteArray(Meta meta, DataBlockPointer ptr, uint count)
        {
            uint ptrindex = ptr.PointerDataIndex;
            uint ptroffset = ptr.PointerDataOffset;
            var ptrblock = (ptrindex < meta.DataBlocks.Count) ? meta.DataBlocks[(int)ptrindex] : null;
            if ((ptrblock == null) || (ptrblock.Data == null))
            { return null; }
            if ((ptroffset + count) > ptrblock.Data.Length)
            { return null; }
            byte[] data = new byte[count];
            Buffer.BlockCopy(ptrblock.Data, (int)ptroffset, data, 0, (int)count);
            return data;
        }

        public static T[] GetTypedPointerArray<T>(Meta meta, MetaName name, MetaPOINTER[] arr) where T : struct
        {
            if (arr == null) return null;
            var datablocks = meta.DataBlocks.Data;
            if (datablocks == null) return null;
            int tsize = Marshal.SizeOf(typeof(T));
            var list = new List<T>();
            for (int i = 0; i < arr.Length; i++)
            {
                ref var ptr = ref arr[i];
                var blockind = ptr.BlockIndex;
                if (blockind < 0) continue;
                if (blockind >= datablocks.Count) continue;
                var block = datablocks[blockind];
                if (block == null) continue;
                if (block.Data == null) continue;
                if (block.StructureNameHash != name) continue;
                var offset = ptr.Offset;
                if (offset < 0) continue;
                if (offset + tsize > block.Data.Length) continue;
                var item = ConvertData<T>(block.Data, offset);
                list.Add(item);
            }
            if (list.Count == 0) return null;
            return list.ToArray();
        }
        public static T GetTypedData<T>(Meta meta, MetaName name) where T : struct
        {
            foreach (var block in meta.DataBlocks)
            {
                if (block.StructureNameHash == name)
                {
                    return MetaTypes.ConvertData<T>(block.Data);
                }
            }
            throw new Exception("Couldn't find " + name.ToString() + " block.");
        }
        public static string[] GetStrings(Meta meta)
        {

            if ((meta == null) || (meta.DataBlocks == null)) return null;

            var datablocks = meta.DataBlocks.Data;

            MetaDataBlock startblock = null;
            int startblockind = -1;
            for (int i = 0; i < datablocks.Count; i++)
            {
                var block = datablocks[i];
                if (block.StructureNameHash == (MetaName)MetaTypeName.STRING)
                {
                    startblock = block;
                    startblockind = i;
                    break;
                }
            }
            if (startblock == null)
            {
                return null;
            }

            List<string> strings = new List<string>();
            StringBuilder sb = new StringBuilder();
            var currentblock = startblock;
            int currentblockind = startblockind;
            while (currentblock != null)
            {

                sb.Clear();
                int startindex = 0;
                int endindex = 0;
                var data = currentblock.Data;
                for (int b = 0; b < data.Length; b++)
                {
                    if (data[b] == 0)
                    {
                        startindex = endindex;
                        endindex = b;
                        if (endindex > startindex)
                        {
                            string str = Encoding.ASCII.GetString(data, startindex, endindex - startindex);
                            strings.Add(str);
                            endindex++;
                        }
                    }
                }
                if (endindex != data.Length - 1)
                {
                    startindex = endindex;
                    endindex = data.Length - 1;
                    if (endindex > startindex)
                    {
                        string str = Encoding.ASCII.GetString(data, startindex, endindex - startindex);
                        strings.Add(str);
                    }
                }

                currentblockind++;
                if (currentblockind >= datablocks.Count) break;
                currentblock = datablocks[currentblockind];
                if (currentblock.StructureNameHash != (MetaName)MetaTypeName.STRING) break;
            }

            if (strings.Count <= 0)
            {
                return null;
            }
            return strings.ToArray();
        }
        public static string GetString(Meta meta, CharPointer ptr)
        {
            var blocki = (int)ptr.PointerDataIndex;
            var offset = (int)ptr.PointerDataOffset;
            if ((blocki < 0) || (blocki >= meta.DataBlocks.BlockLength))
            { return null; }
            var block = meta.DataBlocks[blocki];
            if (block.StructureNameHash != (MetaName)MetaTypeName.STRING)
            { return null; }
            var length = ptr.Count1;
            var lastbyte = offset + length;
            if (lastbyte >= block.DataLength)
            { return null; }
            string s = Encoding.ASCII.GetString(block.Data, offset, length);

            return s;
        }

        public static MetaWrapper[] GetExtensions(Meta meta, Array_StructurePointer ptr)
        {
            if (ptr.Count1 == 0) return null;
            var result = new MetaWrapper[ptr.Count1];
            var extptrs = GetPointerArray(meta, ptr);
            if (extptrs != null)
            {
                for (int i = 0; i < extptrs.Length; i++)
                {
                    var extptr = extptrs[i];
                    MetaWrapper ext = null;
                    var block = meta.GetBlock(extptr.BlockID);
                    var h = block.StructureNameHash;
                    switch (h)
                    {
                        case MetaName.CExtensionDefParticleEffect:
                            ext = new MCExtensionDefParticleEffect();
                            break;
                        case MetaName.CExtensionDefAudioCollisionSettings:
                            ext = new MCExtensionDefAudioCollisionSettings();
                            break;
                        case MetaName.CExtensionDefAudioEmitter:
                            ext = new MCExtensionDefAudioEmitter();
                            break;
                        case MetaName.CExtensionDefSpawnPoint:
                            ext = new MCExtensionDefSpawnPoint();
                            break;
                        case MetaName.CExtensionDefExplosionEffect:
                            ext = new MCExtensionDefExplosionEffect();
                            break;
                        case MetaName.CExtensionDefLadder:
                            ext = new MCExtensionDefLadder();
                            break;
                        case MetaName.CExtensionDefBuoyancy:
                            ext = new MCExtensionDefBuoyancy();
                            break;
                        case MetaName.CExtensionDefExpression:
                            ext = new MCExtensionDefExpression();
                            break;
                        case MetaName.CExtensionDefLightShaft:
                            ext = new MCExtensionDefLightShaft();
                            break;
                        case MetaName.CExtensionDefWindDisturbance:
                            ext = new MCExtensionDefWindDisturbance();
                            break;
                        case MetaName.CExtensionDefProcObject:
                            ext = new MCExtensionDefProcObject();
                            break;

                        case MetaName.CExtensionDefLightEffect:
                            ext = new MCExtensionDefLightEffect();
                            break;
                        case MetaName.CExtensionDefSpawnPointOverride:
                            ext = new MCExtensionDefSpawnPointOverride();
                            break;
                        case MetaName.CExtensionDefDoor:
                            ext = new MCExtensionDefDoor();
                            break;
                        case MetaName.rage__phVerletClothCustomBounds:
                            ext = new Mrage__phVerletClothCustomBounds();
                            break;

                        default:
                            break;
                    }

                    if (ext != null)
                    {
                        ext.Load(meta, extptr);
                    }
                    if (i < result.Length)
                    {
                        result[i] = ext;
                    }
                }
            }
            return result;
        }

        public static int GetDataOffset(MetaDataBlock block, MetaPOINTER ptr)
        {
            if (block == null) return -1;
            var offset = ptr.Offset;
            if ((offset < 0) || (block.Data == null) || (offset >= block.Data.Length))
            { return -1; }
            return offset;
        }
        public static T GetData<T>(Meta meta, MetaPOINTER ptr) where T : struct
        {
            var block = meta.GetBlock(ptr.BlockID);
            var offset = GetDataOffset(block, ptr);
            if (offset < 0) return new T();
            return ConvertData<T>(block.Data, offset);
        }

        public static ushort SwapBytes(ushort x)
        {
            return (ushort)(((x & 0xFF00) >> 8) | ((x & 0x00FF) << 8));
        }
        public static short SwapBytes(short x)
        {
            return (short)SwapBytes((ushort)x);
        }
        public static uint SwapBytes(uint x)
        {
            x = (x >> 16) | (x << 16);
            return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
        }
        public static int SwapBytes(int x)
        {
            return (int)SwapBytes((uint)x);
        }
        public static ulong SwapBytes(ulong x)
        {
            x = ((x & 0xFFFF0000FFFF0000) >> 16) | ((x & 0x0000FFFF0000FFFF) << 16);
            return ((x & 0xFF00FF00FF00FF00) >> 8) | ((x & 0x00FF00FF00FF00FF) << 8);
        }
        public static float SwapBytes(float f)
        {
            var a = BitConverter.GetBytes(f);
            Array.Reverse(a);
            return BitConverter.ToSingle(a, 0);
        }
        public static Vector2 SwapBytes(Vector2 v)
        {
            var x = SwapBytes(v.X);
            var y = SwapBytes(v.Y);
            return new Vector2(x, y);
        }
        public static Vector3 SwapBytes(Vector3 v)
        {
            var x = SwapBytes(v.X);
            var y = SwapBytes(v.Y);
            var z = SwapBytes(v.Z);
            return new Vector3(x, y, z);
        }
        public static Vector4 SwapBytes(Vector4 v)
        {
            var x = SwapBytes(v.X);
            var y = SwapBytes(v.Y);
            var z = SwapBytes(v.Z);
            var w = SwapBytes(v.W);
            return new Vector4(x, y, z, w);
        }
    }

    public enum MetaTypeName : uint
    {

        VECTOR4 = 0x33,
        HASH = 0x4a,
        STRING = 0x10,
        POINTER = 0x7,
        USHORT = 0x13,
        UINT = 0x15,
        ARRAYINFO = 0x100,
        BYTE = 17,
        FLOAT = 33,

        PsoPOINTER = 12,

    }

    [TC(typeof(EXP))] public abstract class MetaWrapper
    {
        public virtual string Name { get { return ToString(); } }
        public abstract void Load(Meta meta, MetaPOINTER ptr);
        public abstract MetaPOINTER Save(MetaBuilder mb);
    }

    [Flags] public enum CScenarioPointFlags__Flags
        : int
    {
        IgnoreMaxInRange = 1,
        NoSpawn = 2,
        StationaryReactions = 4,
        OnlySpawnInSameInterior = 8,
        SpawnedPedIsArrestable = 16,
        ActivateVehicleSiren = 32,
        AggressiveVehicleDriving = 64,
        LandVehicleOnArrival = 128,
        IgnoreThreatsIfLosNotClear = 256,
        EventsInRadiusTriggerDisputes = 512,
        AerialVehiclePoint = 1024,
        TerritorialScenario = 2048,
        EndScenarioIfPlayerWithinRadius = 4096,
        EventsInRadiusTriggerThreatResponse = 8192,
        TaxiPlaneOnGround = 16384,
        FlyOffToOblivion = 32768,
        InWater = 65536,
        AllowInvestigation = 131072,
        OpenDoor = 262144,
        PreciseUseTime = 524288,
        NoRespawnUntilStreamedOut = 1048576,
        NoVehicleSpawnMaxDistance = 2097152,
        ExtendedRange = 4194304,
        ShortRange = 8388608,
        HighPriority = 16777216,
        IgnoreLoitering = 33554432,
        UseSearchlight = 67108864,
        ResetNoCollisionOnCleanUp = 134217728,
        CheckCrossedArrivalPlane = 268435456,
        UseVehicleFrontForArrival = 536870912,
        IgnoreWeatherRestrictions = 1073741824,
    }

    public enum CSpawnPoint__AvailabilityMpSp
        : int
    {
        kBoth = 0,
        kOnlySp = 1,
        kOnlyMp = 2,
    }

    public enum CScenarioChainingEdge__eAction
        : byte
    {
        Move = 0,
        MoveIntoVehicleAsPassenger = 1,
        MoveFollowMaster = 2,
    }

    public enum CScenarioChainingEdge__eNavMode
        : byte
    {
        Direct = 0,
        NavMesh = 1,
        Roads = 2,
    }

    public enum CScenarioChainingEdge__eNavSpeed
        : byte
    {
        kSpeed5Mph = 0,
        kSpeed10Mph = 1,
        kSpeed15Mph = 2,
        kSpeed25Mph = 3,
        kSpeed35Mph = 4,
        kSpeed45Mph = 5,
        kSpeed55Mph = 6,
        kSpeed65Mph = 7,
        kSpeed80Mph = 8,
        kSpeed100Mph = 9,
        kSpeed125Mph = 10,
        kSpeed150Mph = 11,
        kSpeed200Mph = 12,
        kSpeedWalk = 13,
        kSpeedRun = 14,
        kSpeedSprint = 15,
    }

    public enum rage__fwArchetypeDef__eAssetType
        : int
    {
        ASSET_TYPE_UNINITIALIZED = 0,
        ASSET_TYPE_FRAGMENT = 1,
        ASSET_TYPE_DRAWABLE = 2,
        ASSET_TYPE_DRAWABLEDICTIONARY = 3,
        ASSET_TYPE_ASSETLESS = 4,
    }

    public enum rage__eLodType
        : int
    {
        LODTYPES_DEPTH_HD = 0,
        LODTYPES_DEPTH_LOD = 1,
        LODTYPES_DEPTH_SLOD1 = 2,
        LODTYPES_DEPTH_SLOD2 = 3,
        LODTYPES_DEPTH_SLOD3 = 4,
        LODTYPES_DEPTH_ORPHANHD = 5,
        LODTYPES_DEPTH_SLOD4 = 6,
    }

    public enum rage__ePriorityLevel
        : int
    {
        PRI_REQUIRED = 0,
        PRI_OPTIONAL_HIGH = 1,
        PRI_OPTIONAL_MEDIUM = 2,
        PRI_OPTIONAL_LOW = 3,
    }

    public enum CExtensionDefLadderMaterialType
        : int
    {
        METAL_SOLID_LADDER = 0,
        METAL_LIGHT_LADDER = 1,
        WOODEN_LADDER = 2,
    }

    public enum CExtensionDefLightShaftDensityType
        : int
    {
        LIGHTSHAFT_DENSITYTYPE_CONSTANT = 0,
        LIGHTSHAFT_DENSITYTYPE_SOFT = 1,
        LIGHTSHAFT_DENSITYTYPE_SOFT_SHADOW = 2,
        LIGHTSHAFT_DENSITYTYPE_SOFT_SHADOW_HD = 3,
        LIGHTSHAFT_DENSITYTYPE_LINEAR = 4,
        LIGHTSHAFT_DENSITYTYPE_LINEAR_GRADIENT = 5,
        LIGHTSHAFT_DENSITYTYPE_QUADRATIC = 6,
        LIGHTSHAFT_DENSITYTYPE_QUADRATIC_GRADIENT = 7,
    }

    public enum CExtensionDefLightShaftVolumeType
        : int
    {
        LIGHTSHAFT_VOLUMETYPE_SHAFT = 0,
        LIGHTSHAFT_VOLUMETYPE_CYLINDER = 1,
    }

    public enum ePedVarComp
        : short
    {
        PV_COMP_INVALID = -1,
        PV_COMP_HEAD = 0,
        PV_COMP_BERD = 1,
        PV_COMP_HAIR = 2,
        PV_COMP_UPPR = 4,
        PV_COMP_LOWR = 8,
        PV_COMP_HAND = 16,
        PV_COMP_FEET = 32,
        PV_COMP_TEEF = 64,
        PV_COMP_ACCS = 128,
        PV_COMP_TASK = 256,
        PV_COMP_DECL = 512,
        PV_COMP_JBIB = 1024,
        PV_COMP_MAX = 2048,
    }

    public enum ePropRenderFlags
        : int
    {
        PRF_ALPHA = 0,
        PRF_DECAL = 1,
        PRF_CUTOUT = 2,
    }

    public enum eAnchorPoints
        : int
    {
        ANCHOR_HEAD = 0,
        ANCHOR_EYES = 1,
        ANCHOR_EARS = 2,
        ANCHOR_MOUTH = 3,
        ANCHOR_LEFT_HAND = 4,
        ANCHOR_RIGHT_HAND = 5,
        ANCHOR_LEFT_WRIST = 6,
        ANCHOR_RIGHT_WRIST = 7,
        ANCHOR_HIP = 8,
        ANCHOR_LEFT_FOOT = 9,
        ANCHOR_RIGHT_FOOT = 10,
        ANCHOR_PH_L_HAND = 11,
        ANCHOR_PH_R_HAND = 12,
        NUM_ANCHORS = 13,
    }

    public enum rage__phCapsuleBoundDef__enCollisionBoundDef
        : int
    {
        BOUND_DEF_IS_PLANE = 0,
    }

    [TC(typeof(EXP))] public struct CMapTypes
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public Array_StructurePointer extensions { get; set; }
        public Array_StructurePointer archetypes { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Array_uint dependencies { get; set; }
        public Array_Structure compositeEntityTypes { get; set; }

        public override string ToString()
        {
            return name.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CBaseArchetypeDef
    {
        public uint Unused00 { get; set; }
        public uint Unused01 { get; set; }
        public float lodDist { get; set; }
        public uint flags { get; set; }
        public uint specialAttribute { get; set; }
        public uint Unused02 { get; set; }
        public uint Unused03 { get; set; }
        public uint Unused04 { get; set; }
        public Vector3 bbMin { get; set; }
        public float Unused05 { get; set; }
        public Vector3 bbMax { get; set; }
        public float Unused06 { get; set; }
        public Vector3 bsCentre { get; set; }
        public float Unused07 { get; set; }
        public float bsRadius { get; set; }
        public float hdTextureDist { get; set; }
        public MetaHash name { get; set; }
        public MetaHash textureDictionary { get; set; }
        public MetaHash clipDictionary { get; set; }
        public MetaHash drawableDictionary { get; set; }
        public MetaHash physicsDictionary { get; set; }
        public rage__fwArchetypeDef__eAssetType assetType { get; set; }
        public MetaHash assetName { get; set; }
        public uint Unused08 { get; set; }
        public Array_StructurePointer extensions { get; set; }
        public uint Unused09 { get; set; }
        public uint Unused10 { get; set; }

        public override string ToString()
        {
            return name.ToString() + ", " +
                   assetName.ToString() + ", " +
                   drawableDictionary.ToString() + ", " +
                   textureDictionary.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CBaseArchetypeDef_v2
    {
        public uint Unused00 { get; set; }
        public uint Unused01 { get; set; }
        public float lodDist { get; set; }
        public uint flags { get; set; }
        public uint specialAttribute { get; set; }
        public uint Unused02 { get; set; }
        public uint Unused03 { get; set; }
        public uint Unused04 { get; set; }
        public Vector3 bbMin { get; set; }
        public float Unused05 { get; set; }
        public Vector3 bbMax { get; set; }
        public float Unused06 { get; set; }
        public Vector3 bsCentre { get; set; }
        public float Unused07 { get; set; }
        public float bsRadius { get; set; }
        public float hdTextureDist { get; set; }
        public MetaHash name { get; set; }
        public MetaHash textureDictionary { get; set; }
        public MetaHash clipDictionary { get; set; }
        public MetaHash drawableDictionary { get; set; }
        public MetaHash physicsDictionary { get; set; }
        public uint Unused08 { get; set; }
        public Array_StructurePointer extensions { get; set; }
    }

    [TC(typeof(EXP))] public struct CTimeArchetypeDefData
    {
        public uint timeFlags { get; set; }
        public uint Unused11 { get; set; }
        public uint Unused12 { get; set; }
        public uint Unused13 { get; set; }
    }
    [TC(typeof(EXP))] public struct CTimeArchetypeDef
    {
        public CBaseArchetypeDef _BaseArchetypeDef;
        public CTimeArchetypeDefData _TimeArchetypeDef;
        public CBaseArchetypeDef BaseArchetypeDef { get { return _BaseArchetypeDef; } set { _BaseArchetypeDef = value; } }
        public CTimeArchetypeDefData TimeArchetypeDef { get { return _TimeArchetypeDef; } set { _TimeArchetypeDef = value; } }

        public override string ToString()
        {
            return _BaseArchetypeDef.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CMloArchetypeDefData
    {
        public uint mloFlags { get; set; }
        public uint Unused11 { get; set; }
        public Array_StructurePointer entities { get; set; }
        public Array_Structure rooms { get; set; }
        public Array_Structure portals { get; set; }
        public Array_Structure entitySets { get; set; }
        public Array_Structure timeCycleModifiers { get; set; }
        public uint Unused12 { get; set; }
        public uint Unused13 { get; set; }
    }
    [TC(typeof(EXP))] public struct CMloArchetypeDef
    {
        public CBaseArchetypeDef _BaseArchetypeDef;
        public CMloArchetypeDefData _MloArchetypeDef;
        public CBaseArchetypeDef BaseArchetypeDef { get { return _BaseArchetypeDef; } set { _BaseArchetypeDef = value; } }
        public CMloArchetypeDefData MloArchetypeDef { get { return _MloArchetypeDef; } set { _MloArchetypeDef = value; } }

        public override string ToString()
        {
            return _BaseArchetypeDef.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CMloInstanceDef
    {
        public CEntityDef CEntityDef { get; set; }
        public uint groupId { get; set; }
        public uint floorId { get; set; }
        public Array_uint defaultEntitySets { get; set; }
        public uint numExitPortals { get; set; }
        public uint MLOInstflags { get; set; }
    }

    [TC(typeof(EXP))] public struct CMloRoomDef
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public CharPointer name { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public Vector3 bbMin { get; set; }
        public float Unused4 { get; set; }
        public Vector3 bbMax { get; set; }
        public float Unused5 { get; set; }
        public float blend { get; set; }
        public MetaHash timecycleName { get; set; }
        public MetaHash secondaryTimecycleName { get; set; }
        public uint flags { get; set; }
        public uint portalCount { get; set; }
        public int floorId { get; set; }
        public int exteriorVisibiltyDepth { get; set; }
        public uint Unused6 { get; set; }
        public Array_uint attachedObjects { get; set; }
    }
    [TC(typeof(EXP))] public class MCMloRoomDef : MetaWrapper
    {
        public CMloRoomDef _Data;
        public CMloRoomDef Data { get { return _Data; } }
        public string RoomName { get; set; }
        public uint[] AttachedObjects { get; set; }

        public Vector3 BBCenter { get { return (_Data.bbMax + _Data.bbMin) * 0.5f; } }
        public Vector3 BBSize { get { return (_Data.bbMax - _Data.bbMin); } }
        public Vector3 BBMin { get { return (_Data.bbMin); } }
        public Vector3 BBMax { get { return (_Data.bbMax); } }
        public Vector3 BBMin_CW { get; set; }
        public Vector3 BBMax_CW { get; set; }

        public MloArchetype OwnerMlo { get; set; }
        public int Index { get; set; }

        public MCMloRoomDef() { }
        public MCMloRoomDef(Meta meta, CMloRoomDef data)
        {
            _Data = data;
            RoomName = MetaTypes.GetString(meta, _Data.name);
            AttachedObjects = MetaTypes.GetUintArray(meta, _Data.attachedObjects);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CMloRoomDef>(meta, ptr);
            RoomName = MetaTypes.GetString(meta, _Data.name);
            AttachedObjects = MetaTypes.GetUintArray(meta, _Data.attachedObjects);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (!string.IsNullOrEmpty(RoomName))
            {
                _Data.name = mb.AddStringPtr(RoomName);
            }
            else
            {
                _Data.name = new CharPointer();
            }

            if (AttachedObjects != null)
            {
                _Data.attachedObjects = mb.AddUintArrayPtr(AttachedObjects);
            }
            else
            {
                _Data.attachedObjects = new Array_uint();
            }

            mb.AddStructureInfo(MetaName.CMloRoomDef);
            return mb.AddItemPtr(MetaName.CMloRoomDef, _Data);
        }

        public override string Name
        {
            get
            {
                return RoomName;
            }
        }

        public override string ToString()
        {
            return RoomName;
        }
    }

    [TC(typeof(EXP))] public struct CMloPortalDef
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint roomFrom { get; set; }
        public uint roomTo { get; set; }
        public uint flags { get; set; }
        public uint mirrorPriority { get; set; }
        public uint opacity { get; set; }
        public uint audioOcclusion { get; set; }
        public Array_Vector3 corners { get; set; }
        public Array_uint attachedObjects { get; set; }
    }
    [TC(typeof(EXP))] public class MCMloPortalDef : MetaWrapper
    {
        public CMloPortalDef _Data;
        public CMloPortalDef Data { get { return _Data; } }
        public Vector4[] Corners { get; set; }
        public uint[] AttachedObjects { get; set; }

        public Vector3 Center
        {
            get
            {
                if ((Corners == null)||(Corners.Length==0)) return Vector3.Zero;
                var v = Vector3.Zero;
                for (int i = 0; i < Corners.Length; i++)
                {
                    v += Corners[i].XYZ();
                }
                v *= (1.0f / Corners.Length);
                return v;
            }
        }

        public MloArchetype OwnerMlo { get; set; }
        public int Index { get; set; }

        public MCMloPortalDef() { }
        public MCMloPortalDef(Meta meta, CMloPortalDef data)
        {
            _Data = data;
            Corners = MetaTypes.GetPaddedVector3Array(meta, _Data.corners);
            AttachedObjects = MetaTypes.GetUintArray(meta, _Data.attachedObjects);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CMloPortalDef>(meta, ptr);
            Corners = MetaTypes.GetPaddedVector3Array(meta, _Data.corners);
            AttachedObjects = MetaTypes.GetUintArray(meta, _Data.attachedObjects);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (Corners!=null)
            {
                _Data.corners = mb.AddPaddedVector3ArrayPtr(Corners);
            }
            else
            {
                _Data.corners = new Array_Vector3();
            }

            if (AttachedObjects != null)
            {
                _Data.attachedObjects = mb.AddUintArrayPtr(AttachedObjects);
            }
            else
            {
                _Data.attachedObjects = new Array_uint();
            }

            mb.AddStructureInfo(MetaName.CMloPortalDef);
            return mb.AddItemPtr(MetaName.CMloPortalDef, _Data);
        }

        public override string Name
        {
            get
            {
                return Index.ToString() + ": " + _Data.roomFrom.ToString() + " to " + _Data.roomTo.ToString();
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    [TC(typeof(EXP))] public struct CMloEntitySet
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Array_uint locations { get; set; }
        public Array_StructurePointer entities { get; set; }
    }
    [TC(typeof(EXP))] public class MCMloEntitySet : MetaWrapper
    {
        public CMloEntitySet _Data;
        public CMloEntitySet Data { get { return _Data; } }
        public uint[] Locations { get; set; }
        public MCEntityDef[] Entities { get; set; }

        public MloArchetype OwnerMlo { get; set; }
        public int Index { get; set; }

        public bool ForceVisible { get; set; } = false;

        public MCMloEntitySet() { }
        public MCMloEntitySet(Meta meta, CMloEntitySet data, MloArchetype owner)
        {
            _Data = data;
            OwnerMlo = owner;
            Load(meta);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CMloEntitySet>(meta, ptr);
            Load(meta);
        }

        private void Load(Meta meta)
        {
            Locations = MetaTypes.GetUintArray(meta, _Data.locations);

            var ents = MetaTypes.ConvertDataArray<CEntityDef>(meta, MetaName.CEntityDef, _Data.entities);
            if (ents != null)
            {
                Entities = new MCEntityDef[ents.Length];
                for (int i = 0; i < ents.Length; i++)
                {
                    Entities[i] = new MCEntityDef(meta, ref ents[i]) { OwnerMlo = OwnerMlo };
                }
            }
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (Locations != null)
            {
                _Data.locations = mb.AddUintArrayPtr(Locations);
            }
            else
            {
                _Data.locations = new Array_uint();
            }

            if (Entities!=null)
            {
                _Data.entities = mb.AddWrapperArrayPtr(Entities);
            }
            else
            {
                _Data.entities = new Array_StructurePointer();
            }

            mb.AddStructureInfo(MetaName.CMloEntitySet);
            return mb.AddItemPtr(MetaName.CMloEntitySet, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return Name + ": " + (Entities?.Length ?? 0).ToString() + " entities";
        }
    }

    [TC(typeof(EXP))] public struct CMloTimeCycleModifier
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector4 sphere { get; set; }
        public float percentage { get; set; }
        public float range { get; set; }
        public uint startHour { get; set; }
        public uint endHour { get; set; }
    }

    [TC(typeof(EXP))] public struct CMapData
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public MetaHash parent { get; set; }
        public uint flags { get; set; }
        public uint contentFlags { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public Vector3 streamingExtentsMin { get; set; }
        public float Unused4 { get; set; }
        public Vector3 streamingExtentsMax { get; set; }
        public float Unused5 { get; set; }
        public Vector3 entitiesExtentsMin { get; set; }
        public float Unused6 { get; set; }
        public Vector3 entitiesExtentsMax { get; set; }
        public float Unused7 { get; set; }
        public Array_StructurePointer entities { get; set; }
        public Array_Structure containerLods { get; set; }
        public Array_Structure boxOccluders { get; set; }
        public Array_Structure occludeModels { get; set; }
        public Array_uint physicsDictionaries { get; set; }
        public rage__fwInstancedMapData instancedData { get; set; }
        public Array_Structure timeCycleModifiers { get; set; }
        public Array_Structure carGenerators { get; set; }
        public CLODLight LODLightsSOA { get; set; }
        public CDistantLODLight DistantLODLightsSOA { get; set; }
        public CBlockDesc block { get; set; }

        public override string ToString()
        {
            return name.ToString() + ": " + parent.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CEntityDef
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash archetypeName { get; set; }
        public uint flags { get; set; }
        public uint guid { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public Vector3 position { get; set; }
        public float Unused5 { get; set; }
        public Vector4 rotation { get; set; }
        public float scaleXY { get; set; }
        public float scaleZ { get; set; }
        public int parentIndex { get; set; }
        public float lodDist { get; set; }
        public float childLodDist { get; set; }
        public rage__eLodType lodLevel { get; set; }
        public uint numChildren { get; set; }
        public rage__ePriorityLevel priorityLevel { get; set; }
        public Array_StructurePointer extensions { get; set; }
        public int ambientOcclusionMultiplier { get; set; }
        public int artificialAmbientOcclusion { get; set; }
        public uint tintValue { get; set; }
        public uint Unused6 { get; set; }

        public override string ToString()
        {
            return JenkIndex.GetString(archetypeName) + ": " + JenkIndex.GetString(guid) + ": " + position.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCEntityDef : MetaWrapper
    {
        public CEntityDef _Data;
        public CEntityDef Data { get { return _Data; } set { _Data = value; } }
        public MetaWrapper[] Extensions { get; set; }

        public MloArchetype OwnerMlo { get; set; }
        public int Index { get; set; }

        public MCEntityDef(MCEntityDef copy)
        {
            if (copy != null) _Data = copy.Data;
        }
        public MCEntityDef(Meta meta, ref CEntityDef d)
        {
            _Data = d;
            Extensions = MetaTypes.GetExtensions(meta, _Data.extensions);
        }
        public MCEntityDef(ref CEntityDef d, MloArchetype arch)
        {
            _Data = d;
            OwnerMlo = arch;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CEntityDef>(meta, ptr);
            Extensions = MetaTypes.GetExtensions(meta, _Data.extensions);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (Extensions != null)
            {
                _Data.extensions = mb.AddWrapperArrayPtr(Extensions);
            }
            else
            {
                _Data.extensions = new Array_StructurePointer();
            }

            mb.AddStructureInfo(MetaName.CEntityDef);
            return mb.AddItemPtr(MetaName.CEntityDef, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.archetypeName.ToString();
            }
        }

        public override string ToString()
        {
            return Name;
        }
    }

    [TC(typeof(EXP))] public struct BoxOccluder
    {
        public short iCenterX { get; set; }
        public short iCenterY { get; set; }
        public short iCenterZ { get; set; }
        public short iCosZ { get; set; }
        public short iLength { get; set; }
        public short iWidth { get; set; }
        public short iHeight { get; set; }
        public short iSinZ { get; set; }
    }

    [TC(typeof(EXP))] public struct OccludeModel
    {
        public Vector3 bmin { get; set; }
        public float Unused0 { get; set; }
        public Vector3 bmax { get; set; }
        public float Unused1 { get; set; }
        public uint dataSize { get; set; }
        public uint Unused2 { get; set; }
        public DataBlockPointer verts { get; set; }
        public ushort numVertsInBytes { get; set; }
        public ushort numTris { get; set; }
        public uint flags { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
    }

    [TC(typeof(EXP))] public struct rage__fwInstancedMapData
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash ImapLink { get; set; }
        public uint Unused2 { get; set; }
        public Array_Structure PropInstanceList { get; set; }
        public Array_Structure GrassInstanceList { get; set; }

        public override string ToString()
        {
            return ImapLink.ToString() + ", " + PropInstanceList.Count1.ToString() + " props, " + GrassInstanceList.Count1.ToString() + " grasses";
        }
    }

    [TC(typeof(EXP))] public struct rage__fwGrassInstanceListDef
    {
        public rage__spdAABB BatchAABB { get; set; }
        public Vector3 ScaleRange { get; set; }
        public float Unused0 { get; set; }
        public MetaHash archetypeName { get; set; }
        public uint lodDist { get; set; }
        public float LodFadeStartDist { get; set; }
        public float LodInstFadeRange { get; set; }
        public float OrientToTerrain { get; set; }
        public uint Unused1 { get; set; }
        public Array_Structure InstanceList { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }

        public override string ToString()
        {
            return archetypeName.ToString() + " (" + InstanceList.Count1.ToString() + " instances)";
        }
    }

    [TC(typeof(EXP))] public struct rage__fwGrassInstanceListDef__InstanceData
    {
        public ArrayOfUshorts3 Position { get; set; }
        public byte NormalX { get; set; }
        public byte NormalY { get; set; }
        public ArrayOfBytes3 Color { get; set; }
        public byte Scale { get; set; }
        public byte Ao { get; set; }
        public ArrayOfBytes3 Pad { get; set; }

        public override string ToString()
        {
            return Position.ToString() + " : " + Color.ToString() + " : " + Scale.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CTimeCycleModifier
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 minExtents { get; set; }
        public float Unused3 { get; set; }
        public Vector3 maxExtents { get; set; }
        public float Unused4 { get; set; }
        public float percentage { get; set; }
        public float range { get; set; }
        public uint startHour { get; set; }
        public uint endHour { get; set; }

        public override string ToString()
        {
            return name.ToString() + ": startHour " + startHour.ToString() + ", endHour " + endHour.ToString() + ", range " + range.ToString() + ", percentage " + percentage.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CCarGen
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public Vector3 position { get; set; }
        public float Unused4 { get; set; }
        public float orientX { get; set; }
        public float orientY { get; set; }
        public float perpendicularLength { get; set; }
        public MetaHash carModel { get; set; }
        public uint flags { get; set; }
        public int bodyColorRemap1 { get; set; }
        public int bodyColorRemap2 { get; set; }
        public int bodyColorRemap3 { get; set; }
        public int bodyColorRemap4 { get; set; }
        public MetaHash popGroup { get; set; }
        public sbyte livery { get; set; }
        public byte Unused5 { get; set; }
        public ushort Unused6 { get; set; }
        public uint Unused7 { get; set; }

        public override string ToString()
        {
            return carModel.ToString() + ", " + position.ToString() + ", " + popGroup.ToString() + ", " + livery.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CLODLight
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public Array_Structure direction { get; set; }
        public Array_float falloff { get; set; }
        public Array_float falloffExponent { get; set; }
        public Array_uint timeAndStateFlags { get; set; }
        public Array_uint hash { get; set; }
        public Array_byte coneInnerAngle { get; set; }
        public Array_byte coneOuterAngleOrCapExt { get; set; }
        public Array_byte coronaIntensity { get; set; }
    }

    [TC(typeof(EXP))] public struct CDistantLODLight
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public Array_Structure position { get; set; }
        public Array_uint RGBI { get; set; }
        public ushort numStreetLights { get; set; }
        public ushort category { get; set; }
        public uint Unused2 { get; set; }
    }

    [TC(typeof(EXP))] public struct CBlockDesc
    {
        public uint version { get; set; }
        public uint flags { get; set; }
        public CharPointer name { get; set; }
        public CharPointer exportedBy { get; set; }
        public CharPointer owner { get; set; }
        public CharPointer time { get; set; }
    }

    [TC(typeof(EXP))] public struct CExtensionDefParticleEffect
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public uint Unused3 { get; set; }
        public Vector4 offsetRotation { get; set; }
        public CharPointer fxName { get; set; }
        public int fxType { get; set; }
        public int boneTag { get; set; }
        public float scale { get; set; }
        public int probability { get; set; }
        public int flags { get; set; }
        public uint color { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefParticleEffect - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefParticleEffect : MetaWrapper
    {
        public CExtensionDefParticleEffect _Data;
        public CExtensionDefParticleEffect Data { get { return _Data; } }

        public string fxName { get; set; }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefParticleEffect>(meta, ptr);
            fxName = MetaTypes.GetString(meta, _Data.fxName);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (fxName != null)
            {
                _Data.fxName = mb.AddStringPtr(fxName);
            }

            mb.AddStructureInfo(MetaName.CExtensionDefParticleEffect);
            return mb.AddItemPtr(MetaName.CExtensionDefParticleEffect, _Data);
        }

        public override string Name
        {
            get
            {
                return fxName;
            }
        }

        public override string ToString()
        {
            return _Data.ToString() + " - " + fxName;
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefLightEffect
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public Array_Structure instances { get; set; }

        public override string ToString()
        {
            return "CExtensionDefLightEffect - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefLightEffect : MetaWrapper
    {
        public CExtensionDefLightEffect _Data;
        public CExtensionDefLightEffect Data { get { return _Data; } }

        public CLightAttrDef[] instances { get; set; }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefLightEffect>(meta, ptr);
            instances = MetaTypes.ConvertDataArray<CLightAttrDef>(meta, MetaName.CLightAttrDef, _Data.instances);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (instances != null)
            {
                _Data.instances = mb.AddItemArrayPtr(MetaName.CLightAttrDef, instances);
            }

            mb.AddStructureInfo(MetaName.CLightAttrDef);
            mb.AddStructureInfo(MetaName.CExtensionDefLightEffect);
            return mb.AddItemPtr(MetaName.CExtensionDefLightEffect, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString() + " (" + (instances?.Length ?? 0).ToString() + " Attributes)";
        }
    }

    [TC(typeof(EXP))] public struct CLightAttrDef
    {
        public uint Unused00 { get; set; }
        public uint Unused01 { get; set; }
        public Vector3 posn { get; set; }
        public ArrayOfBytes3 colour { get; set; }
        public byte flashiness { get; set; }
        public float intensity { get; set; }
        public uint flags { get; set; }
        public short boneTag { get; set; }
        public byte lightType { get; set; }
        public byte groupId { get; set; }
        public uint timeFlags { get; set; }
        public float falloff { get; set; }
        public float falloffExponent { get; set; }
        public Vector4 cullingPlane { get; set; }
        public byte shadowBlur { get; set; }
        public byte padding1 { get; set; }
        public short padding2 { get; set; }
        public uint padding3 { get; set; }
        public float volIntensity { get; set; }
        public float volSizeScale { get; set; }
        public ArrayOfBytes3 volOuterColour { get; set; }
        public byte lightHash { get; set; }
        public float volOuterIntensity { get; set; }
        public float coronaSize { get; set; }
        public float volOuterExponent { get; set; }
        public byte lightFadeDistance { get; set; }
        public byte shadowFadeDistance { get; set; }
        public byte specularFadeDistance { get; set; }
        public byte volumetricFadeDistance { get; set; }
        public float shadowNearClip { get; set; }
        public float coronaIntensity { get; set; }
        public float coronaZBias { get; set; }
        public Vector3 direction { get; set; }
        public Vector3 tangent { get; set; }
        public float coneInnerAngle { get; set; }
        public float coneOuterAngle { get; set; }
        public Vector3 extents { get; set; }
        public uint projectedTextureKey { get; set; }
    }

    [TC(typeof(EXP))] public struct CExtensionDefAudioCollisionSettings
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public MetaHash settings { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefAudioCollisionSettings - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefAudioCollisionSettings : MetaWrapper
    {
        public CExtensionDefAudioCollisionSettings _Data;
        public CExtensionDefAudioCollisionSettings Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefAudioCollisionSettings>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefAudioCollisionSettings);
            return mb.AddItemPtr(MetaName.CExtensionDefAudioCollisionSettings, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefAudioEmitter
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public Vector4 offsetRotation { get; set; }
        public MetaHash effectHash { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefAudioEmitter - " + name.ToString() + ": " + effectHash.ToString() + ": " + offsetPosition.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefAudioEmitter : MetaWrapper
    {
        public CExtensionDefAudioEmitter _Data;
        public CExtensionDefAudioEmitter Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefAudioEmitter>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefAudioEmitter);
            return mb.AddItemPtr(MetaName.CExtensionDefAudioEmitter, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefExplosionEffect
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public Vector4 offsetRotation { get; set; }
        public CharPointer explosionName { get; set; }
        public int boneTag { get; set; }
        public int explosionTag { get; set; }
        public int explosionType { get; set; }
        public uint flags { get; set; }

        public override string ToString()
        {
            return "CExtensionDefExplosionEffect - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefExplosionEffect : MetaWrapper
    {
        public CExtensionDefExplosionEffect _Data;
        public CExtensionDefExplosionEffect Data { get { return _Data; } }

        public string explosionName { get; set; }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefExplosionEffect>(meta, ptr);
            explosionName = MetaTypes.GetString(meta, _Data.explosionName);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (explosionName != null)
            {
                _Data.explosionName = mb.AddStringPtr(explosionName);
            }
            mb.AddStructureInfo(MetaName.CExtensionDefExplosionEffect);
            return mb.AddItemPtr(MetaName.CExtensionDefExplosionEffect, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString() + " - " + explosionName;
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefLadder
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public Vector3 bottom { get; set; }
        public float Unused4 { get; set; }
        public Vector3 top { get; set; }
        public float Unused5 { get; set; }
        public Vector3 normal { get; set; }
        public float Unused6 { get; set; }
        public CExtensionDefLadderMaterialType materialType { get; set; }
        public MetaHash template { get; set; }
        public byte canGetOffAtTop { get; set; }
        public byte canGetOffAtBottom { get; set; }
        public ushort Unused7 { get; set; }
        public uint Unused8 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefLadder - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefLadder : MetaWrapper
    {
        public CExtensionDefLadder _Data;
        public CExtensionDefLadder Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefLadder>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddEnumInfo(MetaName.CExtensionDefLadderMaterialType);
            mb.AddStructureInfo(MetaName.CExtensionDefLadder);
            return mb.AddItemPtr(MetaName.CExtensionDefLadder, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefBuoyancy
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefBuoyancy - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefBuoyancy : MetaWrapper
    {
        public CExtensionDefBuoyancy _Data;
        public CExtensionDefBuoyancy Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefBuoyancy>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefBuoyancy);
            return mb.AddItemPtr(MetaName.CExtensionDefBuoyancy, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefExpression
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public MetaHash expressionDictionaryName { get; set; }
        public MetaHash expressionName { get; set; }
        public MetaHash creatureMetadataName { get; set; }
        public byte initialiseOnCollision { get; set; }
        public byte Unused4 { get; set; }
        public ushort Unused5 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefExpression - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefExpression : MetaWrapper
    {
        public CExtensionDefExpression _Data;
        public CExtensionDefExpression Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefExpression>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefExpression);
            return mb.AddItemPtr(MetaName.CExtensionDefExpression, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefLightShaft
    {
        public uint Unused00 { get; set; }
        public uint Unused01 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused02 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused03 { get; set; }
        public Vector3 cornerA { get; set; }
        public float Unused04 { get; set; }
        public Vector3 cornerB { get; set; }
        public float Unused05 { get; set; }
        public Vector3 cornerC { get; set; }
        public float Unused06 { get; set; }
        public Vector3 cornerD { get; set; }
        public float Unused07 { get; set; }
        public Vector3 direction { get; set; }
        public float Unused08 { get; set; }
        public float directionAmount { get; set; }
        public float length { get; set; }
        public float fadeInTimeStart { get; set; }
        public float fadeInTimeEnd { get; set; }
        public float fadeOutTimeStart { get; set; }
        public float fadeOutTimeEnd { get; set; }
        public float fadeDistanceStart { get; set; }
        public float fadeDistanceEnd { get; set; }
        public uint color { get; set; }
        public float intensity { get; set; }
        public byte flashiness { get; set; }
        public byte Unused09 { get; set; }
        public ushort Unused10 { get; set; }
        public uint flags { get; set; }
        public CExtensionDefLightShaftDensityType densityType { get; set; }
        public CExtensionDefLightShaftVolumeType volumeType { get; set; }
        public float softness { get; set; }
        public byte scaleBySunIntensity { get; set; }
        public byte Unused11 { get; set; }
        public ushort Unused12 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefLightShaft - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefLightShaft : MetaWrapper
    {
        public CExtensionDefLightShaft _Data;
        public CExtensionDefLightShaft Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefLightShaft>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddEnumInfo(MetaName.CExtensionDefLightShaftDensityType);
            mb.AddEnumInfo(MetaName.CExtensionDefLightShaftVolumeType);
            mb.AddStructureInfo(MetaName.CExtensionDefLightShaft);
            return mb.AddItemPtr(MetaName.CExtensionDefLightShaft, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefDoor
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public byte enableLimitAngle { get; set; }
        public byte startsLocked { get; set; }
        public byte canBreak { get; set; }
        public byte Unused4 { get; set; }
        public float limitAngle { get; set; }
        public float doorTargetRatio { get; set; }
        public MetaHash audioHash { get; set; }

        public override string ToString()
        {
            return "CExtensionDefDoor - " + name.ToString() + ", " + audioHash.ToString() + ", " + offsetPosition.ToString() + ", " + limitAngle.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefDoor : MetaWrapper
    {
        public CExtensionDefDoor _Data;
        public CExtensionDefDoor Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefDoor>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefDoor);
            return mb.AddItemPtr(MetaName.CExtensionDefDoor, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefSpawnPoint
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public Vector4 offsetRotation { get; set; }
        public MetaHash spawnType { get; set; }
        public MetaHash pedType { get; set; }
        public MetaHash group { get; set; }
        public MetaHash interior { get; set; }
        public MetaHash requiredImap { get; set; }
        public CSpawnPoint__AvailabilityMpSp availableInMpSp { get; set; }
        public float probability { get; set; }
        public float timeTillPedLeaves { get; set; }
        public float radius { get; set; }
        public byte start { get; set; }
        public byte end { get; set; }
        public ushort Unused4 { get; set; }
        public CScenarioPointFlags__Flags flags { get; set; }
        public byte highPri { get; set; }
        public byte extendedRange { get; set; }
        public byte shortRange { get; set; }
        public byte Unused5 { get; set; }

        public override string ToString()
        {
            return spawnType.ToString() + ": " + name.ToString() + ", " + pedType.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefSpawnPoint : MetaWrapper
    {
        [TC(typeof(EXP))] public object Parent { get; set; }
        public MCScenarioPointRegion ScenarioRegion { get; private set; }

        public CExtensionDefSpawnPoint _Data;
        public CExtensionDefSpawnPoint Data { get { return _Data; } }

        public Vector3 ParentPosition { get; set; } = Vector3.Zero;
        public Vector3 OffsetPosition { get { return _Data.offsetPosition; } set { _Data.offsetPosition = value; } }
        public Vector4 OffsetRotation { get { return _Data.offsetRotation; } set { _Data.offsetRotation = value; } }
        public MetaHash NameHash { get { return _Data.name; } set { _Data.name = value; } }
        public MetaHash SpawnType { get { return _Data.spawnType; } set { _Data.spawnType = value; } }
        public MetaHash PedType { get { return _Data.pedType; } set { _Data.pedType = value; } }
        public MetaHash Group { get { return _Data.group; } set { _Data.group = value; } }
        public MetaHash Interior { get { return _Data.interior; } set { _Data.interior = value; } }
        public MetaHash RequiredImap { get { return _Data.requiredImap; } set { _Data.requiredImap = value; } }
        public CSpawnPoint__AvailabilityMpSp AvailableInMpSp { get { return _Data.availableInMpSp; } set { _Data.availableInMpSp = value; } }
        public float Probability { get { return _Data.probability; } set { _Data.probability = value; } }
        public float TimeTillPedLeaves { get { return _Data.timeTillPedLeaves; } set { _Data.timeTillPedLeaves = value; } }
        public float Radius { get { return _Data.radius; } set { _Data.radius = value; } }
        public byte StartTime { get { return _Data.start; } set { _Data.start = value; } }
        public byte EndTime { get { return _Data.end; } set { _Data.end = value; } }
        public CScenarioPointFlags__Flags Flags { get { return _Data.flags; } set { _Data.flags = value; } }
        public bool HighPri { get { return _Data.highPri == 1; } set { _Data.highPri = (byte)(value ? 1 : 0); } }
        public bool ExtendedRange { get { return _Data.extendedRange == 1; } set { _Data.extendedRange = (byte)(value ? 1 : 0); } }
        public bool ShortRange { get { return _Data.shortRange == 1; } set { _Data.shortRange = (byte)(value ? 1 : 0); } }

        public Vector3 Position { get { return _Data.offsetPosition + ParentPosition; } set { _Data.offsetPosition = value - ParentPosition; } }
        public Quaternion Orientation { get { return new Quaternion(_Data.offsetRotation);  } set { _Data.offsetRotation = value.ToVector4(); } }

        public MCExtensionDefSpawnPoint() { }
        public MCExtensionDefSpawnPoint(MCScenarioPointRegion region, Meta meta, CExtensionDefSpawnPoint data, object parent)
        {
            ScenarioRegion = region;
            Parent = parent;
            _Data = data;
        }
        public MCExtensionDefSpawnPoint(MCScenarioPointRegion region, MCExtensionDefSpawnPoint copy)
        {
            ScenarioRegion = region;
            if (copy != null)
            {
                _Data = copy.Data;
                Parent = copy.Parent;
                ParentPosition = copy.ParentPosition;
            }
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefSpawnPoint>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp);
            mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
            mb.AddStructureInfo(MetaName.CExtensionDefSpawnPoint);
            return mb.AddItemPtr(MetaName.CExtensionDefSpawnPoint, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefSpawnPointOverride
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public MetaHash ScenarioType { get; set; }
        public byte iTimeStartOverride { get; set; }
        public byte iTimeEndOverride { get; set; }
        public ushort Unused4 { get; set; }
        public MetaHash Group { get; set; }
        public MetaHash ModelSet { get; set; }
        public CSpawnPoint__AvailabilityMpSp AvailabilityInMpSp { get; set; }
        public CScenarioPointFlags__Flags Flags { get; set; }
        public float Radius { get; set; }
        public float TimeTillPedLeaves { get; set; }

        public override string ToString()
        {
            return "CExtensionDefSpawnPointOverride - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefSpawnPointOverride : MetaWrapper
    {
        public CExtensionDefSpawnPointOverride _Data;
        public CExtensionDefSpawnPointOverride Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefSpawnPointOverride>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp);
            mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
            mb.AddStructureInfo(MetaName.CExtensionDefSpawnPointOverride);
            return mb.AddItemPtr(MetaName.CExtensionDefSpawnPointOverride, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefWindDisturbance
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public Vector4 offsetRotation { get; set; }
        public int disturbanceType { get; set; }
        public int boneTag { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public Vector4 size { get; set; }
        public float strength { get; set; }
        public int flags { get; set; }
        public uint Unused6 { get; set; }
        public uint Unused7 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefWindDisturbance - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefWindDisturbance : MetaWrapper
    {
        public CExtensionDefWindDisturbance _Data;
        public CExtensionDefWindDisturbance Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefWindDisturbance>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefWindDisturbance);
            return mb.AddItemPtr(MetaName.CExtensionDefWindDisturbance, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CExtensionDefProcObject
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 offsetPosition { get; set; }
        public float Unused3 { get; set; }
        public float radiusInner { get; set; }
        public float radiusOuter { get; set; }
        public float spacing { get; set; }
        public float minScale { get; set; }
        public float maxScale { get; set; }
        public float minScaleZ { get; set; }
        public float maxScaleZ { get; set; }
        public float minZOffset { get; set; }
        public float maxZOffset { get; set; }
        public uint objectHash { get; set; }
        public uint flags { get; set; }
        public uint Unused4 { get; set; }

        public override string ToString()
        {
            return "CExtensionDefProcObject - " + name.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCExtensionDefProcObject : MetaWrapper
    {
        public CExtensionDefProcObject _Data;
        public CExtensionDefProcObject Data { get { return _Data; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CExtensionDefProcObject>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CExtensionDefProcObject);
            return mb.AddItemPtr(MetaName.CExtensionDefProcObject, _Data);
        }

        public override string Name
        {
            get
            {
                return _Data.name.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct rage__phVerletClothCustomBounds
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public MetaHash name { get; set; }
        public uint Unused2 { get; set; }
        public Array_Structure CollisionData { get; set; }
    }
    [TC(typeof(EXP))] public class Mrage__phVerletClothCustomBounds : MetaWrapper
    {
        public rage__phVerletClothCustomBounds _Data;
        public rage__phVerletClothCustomBounds Data { get { return _Data; } }

        public Mrage__phCapsuleBoundDef[] CollisionData { get; set; }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<rage__phVerletClothCustomBounds>(meta, ptr);

            var cdata = MetaTypes.ConvertDataArray<rage__phCapsuleBoundDef>(meta, MetaName.rage__phCapsuleBoundDef, _Data.CollisionData);
            if (cdata != null)
            {
                CollisionData = new Mrage__phCapsuleBoundDef[cdata.Length];
                for (int i = 0; i < cdata.Length; i++)
                {
                    CollisionData[i] = new Mrage__phCapsuleBoundDef(meta, cdata[i]);
                }
            }
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (CollisionData != null)
            {
                _Data.CollisionData = mb.AddWrapperArray(CollisionData);
            }

            mb.AddStructureInfo(MetaName.rage__phVerletClothCustomBounds);
            return mb.AddItemPtr(MetaName.rage__phVerletClothCustomBounds, _Data);
        }

        public override string ToString()
        {
            return "rage__phVerletClothCustomBounds - " + _Data.name.ToString() + " (" + (CollisionData?.Length ?? 0).ToString() + " CollisionData)";
        }
    }

    [TC(typeof(EXP))] public struct rage__phCapsuleBoundDef
    {
        public CharPointer OwnerName { get; set; }
        public Vector4 Rotation { get; set; }
        public Vector3 Position { get; set; }
        public float Unused0 { get; set; }
        public Vector3 Normal { get; set; }
        public float Unused1 { get; set; }
        public float CapsuleRadius { get; set; }
        public float CapsuleLen { get; set; }
        public float CapsuleHalfHeight { get; set; }
        public float CapsuleHalfWidth { get; set; }
        public rage__phCapsuleBoundDef__enCollisionBoundDef Flags { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
    }
    [TC(typeof(EXP))] public class Mrage__phCapsuleBoundDef : MetaWrapper
    {
        public rage__phCapsuleBoundDef _Data;
        public rage__phCapsuleBoundDef Data { get { return _Data; } }

        public string OwnerName { get; set; }

        public Mrage__phCapsuleBoundDef() { }
        public Mrage__phCapsuleBoundDef(Meta meta, rage__phCapsuleBoundDef s)
        {
            _Data = s;
            OwnerName = MetaTypes.GetString(meta, _Data.OwnerName);
        }
        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<rage__phCapsuleBoundDef>(meta, ptr);
            OwnerName = MetaTypes.GetString(meta, _Data.OwnerName);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            if (OwnerName != null)
            {
                _Data.OwnerName = mb.AddStringPtr(OwnerName);
            }

            mb.AddEnumInfo(MetaName.rage__phCapsuleBoundDef__enCollisionBoundDef);
            mb.AddStructureInfo(MetaName.rage__phCapsuleBoundDef);
            return mb.AddItemPtr(MetaName.rage__phCapsuleBoundDef, _Data);
        }

        public override string ToString()
        {
            return "rage__phCapsuleBoundDef - " + OwnerName;
        }
    }

    [TC(typeof(EXP))] public struct rage__spdGrid2D
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public int MinCellX { get; set; }
        public int MaxCellX { get; set; }
        public int MinCellY { get; set; }
        public int MaxCellY { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }
        public float CellDimX { get; set; }
        public float CellDimY { get; set; }
        public uint Unused7 { get; set; }
        public uint Unused8 { get; set; }
        public uint Unused9 { get; set; }

        public Vector2I Dimensions
        {
            get
            {
                return new Vector2I((MaxCellX - MinCellX)+1, (MaxCellY - MinCellY)+1);
            }
        }
        public Vector2 Scale
        {
            get
            {
                return new Vector2(CellDimX, CellDimY);
            }
            set
            {
                CellDimX = value.X;
                CellDimY = value.Y;
            }
        }
        public Vector2 Min
        {
            get
            {
                return new Vector2(MinCellX, MinCellY) * Scale;
            }
            set
            {
                var gv = value / Scale;
                MinCellX = (int)Math.Floor(gv.X);
                MinCellY = (int)Math.Floor(gv.Y);
            }
        }
        public Vector2 Max
        {
            get
            {
                return new Vector2(MaxCellX, MaxCellY) * Scale;
            }
            set
            {
                var gv = value / Scale;
                MaxCellX = (int)Math.Floor(gv.X);
                MaxCellY = (int)Math.Floor(gv.Y);
            }
        }
    }

    [TC(typeof(EXP))] public struct rage__spdAABB
    {
        public Vector4 min { get; set; }
        public Vector4 max { get; set; }

        public override string ToString()
        {
            return "min: " + min.ToString() + ", max: " + max.ToString();
        }
        public void SwapEnd()
        {
            min = MetaTypes.SwapBytes(min);
            max = MetaTypes.SwapBytes(max);
        }
    }

    [TC(typeof(EXP))] public struct rage__spdSphere
    {
        public Vector4 centerAndRadius { get; set; }

        public override string ToString()
        {
            return centerAndRadius.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CScenarioPointRegion
    {
        public int VersionNumber { get; set; }
        public uint Unused0 { get; set; }
        public CScenarioPointContainer Points { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public Array_Structure EntityOverrides { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }
        public CScenarioChainingGraph ChainingGraph { get; set; }
        public rage__spdGrid2D AccelGrid { get; set; }
        public Array_ushort Unk_3844724227 { get; set; }
        public Array_Structure Clusters { get; set; }
        public CScenarioPointLookUps LookUps { get; set; }
    }
    [TC(typeof(EXP))] public class MCScenarioPointRegion : MetaWrapper
    {
        public YmtFile Ymt { get; set; }

        public CScenarioPointRegion _Data;
        public CScenarioPointRegion Data { get { return _Data; } }

        public MCScenarioPointContainer Points { get; set; }
        public MCScenarioEntityOverride[] EntityOverrides { get; set; }
        public MCScenarioChainingGraph Paths { get; set; }
        public ushort[] Unk_3844724227 { get; set; }
        public MCScenarioPointCluster[] Clusters { get; set; }
        public MCScenarioPointLookUps LookUps { get; set; }

        public int VersionNumber { get { return _Data.VersionNumber; } set { _Data.VersionNumber = value; } }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            var data = MetaTypes.GetData<CScenarioPointRegion>(meta, ptr);
            Load(meta, data);
        }
        public void Load(Meta meta, CScenarioPointRegion data)
        {
            _Data = data;

            Points = new MCScenarioPointContainer(this, meta, _Data.Points);

            var entOverrides = MetaTypes.ConvertDataArray<CScenarioEntityOverride>(meta, MetaName.CScenarioEntityOverride, _Data.EntityOverrides);
            if (entOverrides != null)
            {
                EntityOverrides = new MCScenarioEntityOverride[entOverrides.Length];
                for (int i = 0; i < entOverrides.Length; i++)
                {
                    EntityOverrides[i] = new MCScenarioEntityOverride(this, meta, entOverrides[i]);
                }
            }

            Paths = new MCScenarioChainingGraph(this, meta, _Data.ChainingGraph);

            var clusters = MetaTypes.ConvertDataArray<CScenarioPointCluster>(meta, MetaName.CScenarioPointCluster, _Data.Clusters);
            if (clusters != null)
            {
                Clusters = new MCScenarioPointCluster[clusters.Length];
                for (int i = 0; i < clusters.Length; i++)
                {
                    Clusters[i] = new MCScenarioPointCluster(this, meta, clusters[i]);
                }
            }

            Unk_3844724227 = MetaTypes.GetUshortArray(meta, _Data.Unk_3844724227);

            LookUps = new MCScenarioPointLookUps(this, meta, _Data.LookUps);

            #region data analysis
            #endregion
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {

            var sprb = mb.EnsureBlock(MetaName.CScenarioPointRegion);

            mb.AddStructureInfo(MetaName.CScenarioPointContainer);
            mb.AddStructureInfo(MetaName.CScenarioChainingGraph);
            mb.AddStructureInfo(MetaName.rage__spdGrid2D);
            mb.AddStructureInfo(MetaName.CScenarioPointLookUps);
            mb.AddStructureInfo(MetaName.CScenarioPointRegion);

            if (Points != null)
            {
                var scp = new CScenarioPointContainer();

                var loadSavePoints = Points.GetCLoadSavePoints();
                if (loadSavePoints != null)
                {
                    mb.AddStructureInfo(MetaName.CExtensionDefSpawnPoint);
                    mb.AddEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp);
                    mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
                    scp.LoadSavePoints = mb.AddItemArrayPtr(MetaName.CExtensionDefSpawnPoint, loadSavePoints);
                }
                var myPoints = Points.GetCMyPoints();
                if (myPoints != null)
                {
                    mb.AddStructureInfo(MetaName.CScenarioPoint);
                    mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
                    scp.MyPoints = mb.AddItemArrayPtr(MetaName.CScenarioPoint, myPoints);
                }

                _Data.Points = scp;
            }
            else
            {
                _Data.Points = new CScenarioPointContainer();
            }

            if ((EntityOverrides != null) && (EntityOverrides.Length > 0))
            {

                mb.AddStructureInfo(MetaName.CScenarioEntityOverride);
                var cents = new CScenarioEntityOverride[EntityOverrides.Length];
                for (int i = 0; i < EntityOverrides.Length; i++)
                {
                    var mcent = EntityOverrides[i];
                    var cent = mcent.Data;
                    var scps = mcent.GetCScenarioPoints();
                    if (scps != null)
                    {
                        mb.AddStructureInfo(MetaName.CExtensionDefSpawnPoint);
                        mb.AddEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp);
                        mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
                        cent.ScenarioPoints = mb.AddItemArrayPtr(MetaName.CExtensionDefSpawnPoint, scps);
                    }
                    cents[i] = cent;
                }
                _Data.EntityOverrides = mb.AddItemArrayPtr(MetaName.CScenarioEntityOverride, cents);

            }
            else
            {
                _Data.EntityOverrides = new Array_Structure();
            }

            if (Paths != null)
            {
                var pd = new CScenarioChainingGraph();

                var nodes = Paths.GetCNodes();
                if (nodes != null)
                {
                    mb.AddStructureInfo(MetaName.CScenarioChainingNode);
                    pd.Nodes = mb.AddItemArrayPtr(MetaName.CScenarioChainingNode, nodes);
                }
                var edges = Paths.GetCEdges();
                if (edges != null)
                {
                    mb.AddStructureInfo(MetaName.CScenarioChainingEdge);
                    mb.AddEnumInfo(MetaName.CScenarioChainingEdge__eAction);
                    mb.AddEnumInfo(MetaName.CScenarioChainingEdge__eNavMode);
                    mb.AddEnumInfo(MetaName.CScenarioChainingEdge__eNavSpeed);
                    pd.Edges = mb.AddItemArrayPtr(MetaName.CScenarioChainingEdge, edges);
                }
                if (Paths.Chains != null)
                {
                    foreach (var chain in Paths.Chains)
                    {
                        if (chain.EdgeIds != null)
                        {
                            chain._Data.EdgeIds = mb.AddUshortArrayPtr(chain.EdgeIds);
                        }
                        else
                        {
                            chain._Data.EdgeIds = new Array_ushort();
                        }
                    }
                }
                var chains = Paths.GetCChains();
                if (chains != null)
                {
                    mb.AddStructureInfo(MetaName.CScenarioChain);
                    pd.Chains = mb.AddItemArrayPtr(MetaName.CScenarioChain, chains);
                }

                _Data.ChainingGraph = pd;
            }
            else
            {
                _Data.ChainingGraph = new CScenarioChainingGraph();
            }

            if ((Clusters != null) && (Clusters.Length > 0))
            {
                _Data.Clusters = mb.AddWrapperArray(Clusters);
            }
            else
            {
                _Data.Clusters = new Array_Structure();
            }

            if ((Unk_3844724227 != null) && (Unk_3844724227.Length > 0))
            {
                _Data.Unk_3844724227 = mb.AddUshortArrayPtr(Unk_3844724227);
            }
            else
            {
                _Data.Unk_3844724227 = new Array_ushort();
            }

            if (LookUps != null)
            {
                var spl = new CScenarioPointLookUps();
                if ((LookUps.TypeNames != null) && (LookUps.TypeNames.Length > 0))
                {
                    spl.TypeNames = mb.AddHashArrayPtr(LookUps.TypeNames);
                }
                if ((LookUps.PedModelSetNames != null) && (LookUps.PedModelSetNames.Length > 0))
                {
                    spl.PedModelSetNames = mb.AddHashArrayPtr(LookUps.PedModelSetNames);
                }
                if ((LookUps.VehicleModelSetNames != null) && (LookUps.VehicleModelSetNames.Length > 0))
                {
                    spl.VehicleModelSetNames = mb.AddHashArrayPtr(LookUps.VehicleModelSetNames);
                }
                if ((LookUps.GroupNames != null) && (LookUps.GroupNames.Length > 0))
                {
                    spl.GroupNames = mb.AddHashArrayPtr(LookUps.GroupNames);
                }
                if ((LookUps.InteriorNames != null) && (LookUps.InteriorNames.Length > 0))
                {
                    spl.InteriorNames = mb.AddHashArrayPtr(LookUps.InteriorNames);
                }
                if ((LookUps.RequiredIMapNames != null) && (LookUps.RequiredIMapNames.Length > 0))
                {
                    spl.RequiredIMapNames = mb.AddHashArrayPtr(LookUps.RequiredIMapNames);
                }
                _Data.LookUps = spl;
            }
            else
            {
                _Data.LookUps = new CScenarioPointLookUps();
            }

            mb.AddString("Made with CodeWalker by dexyfex. " + DateTime.Now.ToString());

            return mb.AddItemPtr(MetaName.CScenarioPointRegion, _Data);
        }

        public void AddCluster(MCScenarioPointCluster cluster)
        {
            List<MCScenarioPointCluster> newclusters = new List<MCScenarioPointCluster>();
            if (Clusters != null)
            {
                newclusters.AddRange(Clusters);
            }
            cluster.Region = this;
            newclusters.Add(cluster);
            Clusters = newclusters.ToArray();
        }
        public void AddEntity(MCScenarioEntityOverride ent)
        {
            List<MCScenarioEntityOverride> newents = new List<MCScenarioEntityOverride>();
            if (EntityOverrides != null)
            {
                newents.AddRange(EntityOverrides);
            }
            ent.Region = this;
            newents.Add(ent);
            EntityOverrides = newents.ToArray();
        }

        public bool RemoveCluster(MCScenarioPointCluster cluster)
        {
            bool r = false;
            if (Clusters != null)
            {
                List<MCScenarioPointCluster> newclusters = new List<MCScenarioPointCluster>();
                foreach (var nc in Clusters)
                {
                    if (nc == cluster)
                    {
                        r = true;
                    }
                    else
                    {
                        newclusters.Add(nc);
                    }
                }
                if (r)
                {
                    Clusters = newclusters.ToArray();
                }
            }
            return r;
        }
        public bool RemoveEntity(MCScenarioEntityOverride ent)
        {
            bool r = false;
            if (EntityOverrides != null)
            {
                List<MCScenarioEntityOverride> newents = new List<MCScenarioEntityOverride>();
                foreach (var nc in EntityOverrides)
                {
                    if (nc == ent)
                    {
                        r = true;
                    }
                    else
                    {
                        newents.Add(nc);
                    }
                }
                if (r)
                {
                    EntityOverrides = newents.ToArray();
                }
            }
            return r;
        }

        public override string Name
        {
            get
            {
                return Ymt?.ToString() ?? "CScenarioPointRegion";
            }
        }

        public override string ToString()
        {
            return Name;
        }

    }

    [TC(typeof(EXP))] public struct CScenarioPointContainer
    {
        public Array_Structure LoadSavePoints { get; set; }
        public Array_Structure MyPoints { get; set; }
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }

        public override string ToString()
        {
            return LoadSavePoints.Count1.ToString() + " LoadSavePoints, " + MyPoints.Count1.ToString() + " MyPoints";
        }
    }
    [TC(typeof(EXP))] public class MCScenarioPointContainer : MetaWrapper
    {
        [TC(typeof(EXP))] public object Parent { get; set; }
        public MCScenarioPointRegion Region { get; private set; }

        public CScenarioPointContainer _Data;
        public CScenarioPointContainer Data { get { return _Data; } set { _Data = value; } }

        public MCExtensionDefSpawnPoint[] LoadSavePoints { get; set; }
        public MCScenarioPoint[] MyPoints { get; set; }

        public MCScenarioPointContainer() { }
        public MCScenarioPointContainer(MCScenarioPointRegion region)
        {
            Region = region;
        }
        public MCScenarioPointContainer(MCScenarioPointRegion region, Meta meta, CScenarioPointContainer d)
        {
            Region = region;
            _Data = d;
            Init(meta);
        }

        public void Init(Meta meta)
        {
            var vLoadSavePoints = MetaTypes.ConvertDataArray<CExtensionDefSpawnPoint>(meta, MetaName.CExtensionDefSpawnPoint, _Data.LoadSavePoints);
            if (vLoadSavePoints != null)
            {
                LoadSavePoints = new MCExtensionDefSpawnPoint[vLoadSavePoints.Length];
                for (int i = 0; i < vLoadSavePoints.Length; i++)
                {
                    LoadSavePoints[i] = new MCExtensionDefSpawnPoint(Region, meta, vLoadSavePoints[i], this);
                }
            }

            var vMyPoints = MetaTypes.ConvertDataArray<CScenarioPoint>(meta, MetaName.CScenarioPoint, _Data.MyPoints);
            if (vMyPoints != null)
            {
                MyPoints = new MCScenarioPoint[vMyPoints.Length];
                for (int i = 0; i < vMyPoints.Length; i++)
                {
                    MyPoints[i] = new MCScenarioPoint(Region, meta, vMyPoints[i], this);
                    MyPoints[i].PointIndex = i;
                }
            }
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioPointContainer>(meta, ptr);
            Init(meta);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioPointContainer);
            return mb.AddItemPtr(MetaName.CScenarioPointContainer, _Data);
        }

        public CExtensionDefSpawnPoint[] GetCLoadSavePoints()
        {
            if ((LoadSavePoints == null) || (LoadSavePoints.Length == 0)) return null;
            CExtensionDefSpawnPoint[] r = new CExtensionDefSpawnPoint[LoadSavePoints.Length];
            for (int i = 0; i < LoadSavePoints.Length; i++)
            {
                r[i] = LoadSavePoints[i].Data;
            }
            return r;
        }
        public CScenarioPoint[] GetCMyPoints()
        {
            if ((MyPoints == null) || (MyPoints.Length == 0)) return null;
            CScenarioPoint[] r = new CScenarioPoint[MyPoints.Length];
            for (int i = 0; i < MyPoints.Length; i++)
            {
                r[i] = MyPoints[i].Data;
            }
            return r;
        }

        public void AddMyPoint(MCScenarioPoint p)
        {
            List<MCScenarioPoint> newpoints = new List<MCScenarioPoint>();
            if (MyPoints != null)
            {
                newpoints.AddRange(MyPoints);
            }
            p.PointIndex = newpoints.Count;
            newpoints.Add(p);
            p.Container = this;
            MyPoints = newpoints.ToArray();
        }
        public void AddLoadSavePoint(MCExtensionDefSpawnPoint p)
        {
            List<MCExtensionDefSpawnPoint> newpoints = new List<MCExtensionDefSpawnPoint>();
            if (LoadSavePoints != null)
            {
                newpoints.AddRange(LoadSavePoints);
            }
            newpoints.Add(p);
            p.Parent = this;
            LoadSavePoints = newpoints.ToArray();
        }

        public bool RemoveMyPoint(MCScenarioPoint p)
        {
            bool r = false;
            if (MyPoints != null)
            {
                List<MCScenarioPoint> newpoints = new List<MCScenarioPoint>();
                foreach (var mp in MyPoints)
                {
                    if (mp == p)
                    {
                        r = true;
                    }
                    else
                    {
                        newpoints.Add(mp);
                    }
                }
                if (r)
                {
                    MyPoints = newpoints.ToArray();

                    for (int i = 0; i < MyPoints.Length; i++)
                    {
                        MyPoints[i].PointIndex = i;
                    }
                }
            }
            return r;
        }
        public bool RemoveLoadSavePoint(MCExtensionDefSpawnPoint p)
        {
            bool r = false;
            if (LoadSavePoints != null)
            {
                List<MCExtensionDefSpawnPoint> newpoints = new List<MCExtensionDefSpawnPoint>();
                foreach (var mp in LoadSavePoints)
                {
                    if (mp == p)
                    {
                        r = true;
                    }
                    else
                    {
                        newpoints.Add(mp);
                    }
                }
                if (r)
                {
                    LoadSavePoints = newpoints.ToArray();
                }
            }
            return r;
        }

        public override string Name
        {
            get
            {
                return "CScenarioPointContainer";
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }

    }

    [TC(typeof(EXP))] public struct CScenarioPoint
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public byte Unused5 { get; set; }
        public byte iType { get; set; }
        public byte ModelSetId { get; set; }
        public byte iInterior { get; set; }
        public byte iRequiredIMapId { get; set; }
        public byte iProbability { get; set; }
        public byte uAvailableInMpSp { get; set; }
        public byte iTimeStartOverride { get; set; }
        public byte iTimeEndOverride { get; set; }
        public byte iRadius { get; set; }
        public byte iTimeTillPedLeaves { get; set; }
        public byte Unused6 { get; set; }
        public ushort iScenarioGroup { get; set; }
        public ushort Unused7 { get; set; }
        public CScenarioPointFlags__Flags Flags { get; set; }
        public uint Unused8 { get; set; }
        public uint Unused9 { get; set; }
        public Vector4 vPositionAndDirection { get; set; }

        public override string ToString()
        {
            return FloatUtil.GetVector4String(vPositionAndDirection);
        }
    }
    [TC(typeof(EXP))] public class MCScenarioPoint : MetaWrapper
    {
        [TC(typeof(EXP))] public MCScenarioPointContainer Container { get; set; }
        public MCScenarioPointRegion Region { get; set; }

        public CScenarioPoint _Data;
        public CScenarioPoint Data { get { return _Data; } set { _Data = value; } }

        public Vector3 Position { get { return _Data.vPositionAndDirection.XYZ(); } set { _Data.vPositionAndDirection = new Vector4(value, Direction); } }
        public float Direction { get { return _Data.vPositionAndDirection.W; } set { _Data.vPositionAndDirection = new Vector4(Position, value); } }
        public Quaternion Orientation
        {
            get { return Quaternion.RotationAxis(Vector3.UnitZ, Direction); }
            set
            {
                Vector3 dir = value.Multiply(Vector3.UnitX);
                float dira = (float)Math.Atan2(dir.Y, dir.X);
                Direction = dira;
            }
        }

        public byte TypeId { get { return _Data.iType; } set { _Data.iType = value; } }
        public ScenarioTypeRef Type { get; set; }

        public byte ModelSetId { get { return _Data.ModelSetId; } set { _Data.ModelSetId = value; } }
        public AmbientModelSet ModelSet { get; set; }

        public byte InteriorId {  get { return _Data.iInterior; } set { _Data.iInterior = value; } }
        public MetaHash InteriorName { get; set; }

        public ushort GroupId { get { return _Data.iScenarioGroup; } set { _Data.iScenarioGroup = value; } }
        public MetaHash GroupName { get; set; }

        public byte IMapId { get { return _Data.iRequiredIMapId; } set { _Data.iRequiredIMapId = value; } }
        public MetaHash IMapName { get; set; }

        public string TimeRange { get { return _Data.iTimeStartOverride.ToString().PadLeft(2, '0') + ":00 - " + _Data.iTimeEndOverride.ToString().PadLeft(2, '0') + ":00"; } }
        public byte TimeStart { get { return _Data.iTimeStartOverride; } set { _Data.iTimeStartOverride = value; } }
        public byte TimeEnd { get { return _Data.iTimeEndOverride; } set { _Data.iTimeEndOverride = value; } }
        public byte Probability { get { return _Data.iProbability; } set { _Data.iProbability = value; } }
        public byte AvailableMpSp { get { return _Data.uAvailableInMpSp; } set { _Data.uAvailableInMpSp = value; } }
        public byte Radius { get { return _Data.iRadius; } set { _Data.iRadius = value; } }
        public byte WaitTime { get { return _Data.iTimeTillPedLeaves; } set { _Data.iTimeTillPedLeaves = value; } }
        public CScenarioPointFlags__Flags Flags { get { return _Data.Flags; } set { _Data.Flags = value; } }

        public int PointIndex { get; set; }

        public MCScenarioPoint(MCScenarioPointRegion region) { Region = region; }
        public MCScenarioPoint(MCScenarioPointRegion region, Meta meta, CScenarioPoint d, MCScenarioPointContainer container)
        {
            Region = region;
            Container = container;
            _Data = d;
        }
        public MCScenarioPoint(MCScenarioPointRegion region, MCScenarioPoint copy)
        {
            Region = region;
            if (copy != null)
            {
                _Data = copy.Data;
                Type = copy.Type;
                ModelSet = copy.ModelSet;
                InteriorName = copy.InteriorName;
                GroupName = copy.GroupName;
                IMapName = copy.IMapName;
            }
        }

        public void CopyFrom(MCScenarioPoint copy)
        {
            _Data = copy.Data;
            Type = copy.Type;
            ModelSet = copy.ModelSet;
            InteriorName = copy.InteriorName;
            GroupName = copy.GroupName;
            IMapName = copy.IMapName;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioPoint>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioPoint);
            return mb.AddItemPtr(MetaName.CScenarioPoint, _Data);
        }

        public override string Name
        {
            get
            {
                return (Type?.ToString() ?? "") + ": " + (ModelSet?.ToString() ?? "");
            }
        }

        public override string ToString()
        {
            return Name + ": " + TimeRange;
        }

    }

    [TC(typeof(EXP))] public struct CScenarioEntityOverride
    {
        public Vector3 EntityPosition { get; set; }
        public float Unused00 { get; set; }
        public MetaHash EntityType { get; set; }
        public uint Unused01 { get; set; }
        public Array_Structure ScenarioPoints { get; set; }
        public uint Unused02 { get; set; }
        public uint Unused03 { get; set; }
        public uint Unused04 { get; set; }
        public uint Unused05 { get; set; }
        public uint Unused06 { get; set; }
        public uint Unused07 { get; set; }
        public byte EntityMayNotAlwaysExist { get; set; }
        public byte SpecificallyPreventArtPoints { get; set; }
        public ushort Unused08 { get; set; }
        public uint Unused09 { get; set; }
        public uint Unused10 { get; set; }
        public uint Unused11 { get; set; }

        public override string ToString()
        {
            return EntityType.ToString() + ", " + ScenarioPoints.Count1.ToString() + " ScenarioPoints";
        }
    }
    [TC(typeof(EXP))] public class MCScenarioEntityOverride : MetaWrapper
    {
        [TC(typeof(EXP))] public object Parent { get; set; }
        public MCScenarioPointRegion Region { get; set; }

        public CScenarioEntityOverride _Data;
        public CScenarioEntityOverride Data { get { return _Data; } set { _Data = value; } }

        public Vector3 Position { get { return _Data.EntityPosition; } set { _Data.EntityPosition = value; } }

        public MetaHash TypeName {  get { return _Data.EntityType; } set { _Data.EntityType = value; } }
        public bool EntityMayNotAlwaysExist { get { return _Data.EntityMayNotAlwaysExist == 1; } set { _Data.EntityMayNotAlwaysExist = (byte)(value ? 1 : 0); } }
        public bool SpecificallyPreventArtPoints { get { return _Data.SpecificallyPreventArtPoints == 1; } set { _Data.SpecificallyPreventArtPoints = (byte)(value ? 1 : 0); } }

        public MCExtensionDefSpawnPoint[] ScenarioPoints { get; set; }

        public MCScenarioEntityOverride() { }
        public MCScenarioEntityOverride(MCScenarioPointRegion region, MCScenarioEntityOverride copy)
        {
            Region = region;
            if (copy != null)
            {
                _Data = copy.Data;
            }
        }
        public MCScenarioEntityOverride(MCScenarioPointRegion region, Meta meta, CScenarioEntityOverride d)
        {
            Region = region;
            _Data = d;
            Init(meta);
        }

        public void Init(Meta meta)
        {

            var scenarioPoints = MetaTypes.ConvertDataArray<CExtensionDefSpawnPoint>(meta, MetaName.CExtensionDefSpawnPoint, _Data.ScenarioPoints);
            if (scenarioPoints != null)
            {
                ScenarioPoints = new MCExtensionDefSpawnPoint[scenarioPoints.Length];
                for (int i = 0; i < scenarioPoints.Length; i++)
                {
                    ScenarioPoints[i] = new MCExtensionDefSpawnPoint(Region, meta, scenarioPoints[i], this);
                    ScenarioPoints[i].ParentPosition = Position;
                }
            }

        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioEntityOverride>(meta, ptr);
            Init(meta);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioEntityOverride);

            if (ScenarioPoints != null)
            {
                mb.AddStructureInfo(MetaName.CExtensionDefSpawnPoint);
                mb.AddEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp);
                mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
                _Data.ScenarioPoints = mb.AddWrapperArray(ScenarioPoints);
            }

            return mb.AddItemPtr(MetaName.CScenarioEntityOverride, _Data);
        }

        public void AddScenarioPoint(MCExtensionDefSpawnPoint p)
        {
            List<MCExtensionDefSpawnPoint> newpoints = new List<MCExtensionDefSpawnPoint>();
            if (ScenarioPoints != null)
            {
                newpoints.AddRange(ScenarioPoints);
            }
            newpoints.Add(p);
            p.Parent = this;
            ScenarioPoints = newpoints.ToArray();
        }
        public bool RemoveScenarioPoint(MCExtensionDefSpawnPoint p)
        {
            bool r = false;
            if (ScenarioPoints != null)
            {
                List<MCExtensionDefSpawnPoint> newpoints = new List<MCExtensionDefSpawnPoint>();
                foreach (var mp in ScenarioPoints)
                {
                    if (mp == p)
                    {
                        r = true;
                    }
                    else
                    {
                        newpoints.Add(mp);
                    }
                }
                if (r)
                {
                    ScenarioPoints = newpoints.ToArray();
                }
            }
            return r;
        }

        public CExtensionDefSpawnPoint[] GetCScenarioPoints()
        {
            if ((ScenarioPoints == null) || (ScenarioPoints.Length == 0)) return null;
            CExtensionDefSpawnPoint[] r = new CExtensionDefSpawnPoint[ScenarioPoints.Length];
            for (int i = 0; i < ScenarioPoints.Length; i++)
            {
                r[i] = ScenarioPoints[i].Data;
            }
            return r;
        }

        public override string Name
        {
            get
            {
                return "CScenarioEntityOverride " + _Data.EntityType.ToString();
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CScenarioChainingGraph
    {
        public Array_Structure Nodes { get; set; }
        public Array_Structure Edges { get; set; }
        public Array_Structure Chains { get; set; }
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }
        public uint Unused7 { get; set; }
        public uint Unused8 { get; set; }
        public uint Unused9 { get; set; }

        public override string ToString()
        {
            return Nodes.Count1.ToString() + " Nodes, " + Edges.Count1.ToString() + " Edges, " + Chains.Count1.ToString() + " Chains";
        }
    }
    [TC(typeof(EXP))] public class MCScenarioChainingGraph : MetaWrapper
    {
        public MCScenarioPointRegion Region { get; private set; }

        public CScenarioChainingGraph _Data;
        public CScenarioChainingGraph Data { get { return _Data; } set { _Data = value; } }

        public MCScenarioChainingNode[] Nodes { get; set; }
        public MCScenarioChainingEdge[] Edges { get; set; }
        public MCScenarioChain[] Chains { get; set; }

        public MCScenarioChainingGraph() { }
        public MCScenarioChainingGraph(MCScenarioPointRegion region) { Region = region; }
        public MCScenarioChainingGraph(MCScenarioPointRegion region, Meta meta, CScenarioChainingGraph d)
        {
            Region = region;
            _Data = d;
            Init(meta);
        }

        public void Init(Meta meta)
        {
            var pathnodes = MetaTypes.ConvertDataArray<CScenarioChainingNode>(meta, MetaName.CScenarioChainingNode, _Data.Nodes);
            if (pathnodes != null)
            {
                Nodes = new MCScenarioChainingNode[pathnodes.Length];
                for (int i = 0; i < pathnodes.Length; i++)
                {
                    Nodes[i] = new MCScenarioChainingNode(Region, meta, pathnodes[i], this, i);
                }
            }

            var pathedges = MetaTypes.ConvertDataArray<CScenarioChainingEdge>(meta, MetaName.CScenarioChainingEdge, _Data.Edges);
            if (pathedges != null)
            {
                Edges = new MCScenarioChainingEdge[pathedges.Length];
                for (int i = 0; i < pathedges.Length; i++)
                {
                    Edges[i] = new MCScenarioChainingEdge(Region, meta, pathedges[i], i);
                }
            }

            var pathchains = MetaTypes.ConvertDataArray<CScenarioChain>(meta, MetaName.CScenarioChain, _Data.Chains);
            if (pathchains != null)
            {
                Chains = new MCScenarioChain[pathchains.Length];
                for (int i = 0; i < pathchains.Length; i++)
                {
                    Chains[i] = new MCScenarioChain(Region, meta, pathchains[i]);
                }
            }
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioChainingGraph>(meta, ptr);
            Init(meta);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioChainingGraph);
            return mb.AddItemPtr(MetaName.CScenarioChainingGraph, _Data);
        }

        public CScenarioChainingNode[] GetCNodes()
        {
            if ((Nodes == null) || (Nodes.Length == 0)) return null;
            CScenarioChainingNode[] r = new CScenarioChainingNode[Nodes.Length];
            for (int i = 0; i < Nodes.Length; i++)
            {
                r[i] = Nodes[i].Data;
            }
            return r;
        }
        public CScenarioChainingEdge[] GetCEdges()
        {
            if ((Edges == null) || (Edges.Length == 0)) return null;
            CScenarioChainingEdge[] r = new CScenarioChainingEdge[Edges.Length];
            for (int i = 0; i < Edges.Length; i++)
            {
                r[i] = Edges[i].Data;
            }
            return r;
        }
        public CScenarioChain[] GetCChains()
        {
            if ((Chains == null) || (Chains.Length == 0)) return null;
            CScenarioChain[] r = new CScenarioChain[Chains.Length];
            for (int i = 0; i < Chains.Length; i++)
            {
                r[i] = Chains[i].Data;
            }
            return r;
        }

        public void AddNode(MCScenarioChainingNode node)
        {
            List<MCScenarioChainingNode> newnodes = new List<MCScenarioChainingNode>();
            if (Nodes != null)
            {
                newnodes.AddRange(Nodes);
            }
            node.Parent = this;
            node.Region = Region;
            node.NodeIndex = newnodes.Count;
            newnodes.Add(node);
            Nodes = newnodes.ToArray();
        }
        public void AddEdge(MCScenarioChainingEdge edge)
        {
            List<MCScenarioChainingEdge> newedges = new List<MCScenarioChainingEdge>();
            if (Edges != null)
            {
                newedges.AddRange(Edges);
            }
            edge.Region = Region;
            edge.EdgeIndex = newedges.Count;
            newedges.Add(edge);
            Edges = newedges.ToArray();
        }
        public void AddChain(MCScenarioChain chain)
        {
            List<MCScenarioChain> newchains = new List<MCScenarioChain>();
            if (Chains != null)
            {
                newchains.AddRange(Chains);
            }
            chain.Region = Region;
            chain.ChainIndex = newchains.Count;
            newchains.Add(chain);
            Chains = newchains.ToArray();
        }

        public bool RemoveNode(MCScenarioChainingNode n)
        {
            bool r = false;
            if (Edges != null)
            {
                List<MCScenarioChainingEdge> remedges = new List<MCScenarioChainingEdge>();
                foreach (var edge in Edges)
                {
                    if ((edge.NodeFrom == n) || (edge.NodeTo == n))
                    {
                        remedges.Add(edge);
                    }
                }
                foreach (var edge in remedges)
                {
                    RemoveEdge(edge);
                }
            }

            if (Nodes != null)
            {
                List<MCScenarioChainingNode> newnodes = new List<MCScenarioChainingNode>();
                foreach (var nn in Nodes)
                {
                    if (nn == n)
                    {
                        r = true;
                    }
                    else
                    {
                        nn.NodeIndex = newnodes.Count;
                        newnodes.Add(nn);
                    }
                }
                if (r)
                {
                    Nodes = newnodes.ToArray();

                    foreach (var e in Edges)
                    {
                        e.NodeIndexFrom = (ushort)e.NodeFrom.NodeIndex;
                        e.NodeIndexTo = (ushort)e.NodeTo.NodeIndex;
                    }

                }
            }
            return r;
        }
        public bool RemoveEdge(MCScenarioChainingEdge e)
        {
            bool r = false;
            if (Edges != null)
            {
                List<MCScenarioChainingEdge> newedges = new List<MCScenarioChainingEdge>();
                foreach (var ne in Edges)
                {
                    if (ne == e)
                    {
                        r = true;
                    }
                    else
                    {
                        ne.EdgeIndex = newedges.Count;
                        newedges.Add(ne);
                    }
                }
                if (r)
                {
                    Edges = newedges.ToArray();

                    var remchains = new List<MCScenarioChain>();
                    foreach (var c in Chains)
                    {
                        if (c == null) continue;
                        if (c.RemoveEdge(e))
                        {
                            if ((c.Edges?.Length ?? 0) == 0)
                            {
                                remchains.Add(c);
                            }
                        }
                    }
                    foreach (var c in remchains)
                    {
                        RemoveChain(c);
                    }
                }
            }
            return r;
        }
        public bool RemoveChain(MCScenarioChain c)
        {
            bool r = false;
            if (Chains != null)
            {
                List<MCScenarioChain> newchains = new List<MCScenarioChain>();
                foreach (var nc in Chains)
                {
                    if (nc == c)
                    {
                        r = true;
                    }
                    else
                    {
                        nc.ChainIndex = newchains.Count;
                        newchains.Add(nc);
                    }
                }
                if (r)
                {
                    Chains = newchains.ToArray();
                }
            }
            return r;
        }

        public override string Name
        {
            get
            {
                return "CScenarioChainingGraph " + _Data.ToString();
            }
        }
        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CScenarioChainingNode
    {
        public Vector3 Position { get; set; }
        public float Unused0 { get; set; }
        public MetaHash Unk_2602393771 { get; set; }
        public MetaHash ScenarioType { get; set; }
        public byte HasIncomingEdges { get; set; }
        public byte HasOutgoingEdges { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }

        public override string ToString()
        {
            return
                ScenarioType.ToString() + ", " + Unk_2602393771.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCScenarioChainingNode : MetaWrapper
    {
        [TC(typeof(EXP))] public MCScenarioChainingGraph Parent { get; set; }
        public MCScenarioPointRegion Region { get; set; }
        public ScenarioNode ScenarioNode { get; set; }

        public CScenarioChainingNode _Data;
        public CScenarioChainingNode Data { get { return _Data; } set { _Data = value; } }

        public Vector3 Position { get { return _Data.Position; } set { _Data.Position = value; } }
        public MetaHash PropHash { get { return _Data.Unk_2602393771; } set { _Data.Unk_2602393771 = value; } }
        public MetaHash TypeHash { get { return _Data.ScenarioType; } set { _Data.ScenarioType = value; } }
        public ScenarioTypeRef Type { get; set; }
        public bool HasIncomingEdges { get { return _Data.HasIncomingEdges == 1; } set { _Data.HasIncomingEdges = (byte)(value ? 1 : 0); } }
        public bool HasOutgoingEdges { get { return _Data.HasOutgoingEdges == 1; } set { _Data.HasOutgoingEdges = (byte)(value ? 1 : 0); } }

        public int NodeIndex { get; set; }
        public MCScenarioChain Chain { get; set; }

        public MCScenarioChainingNode() { }
        public MCScenarioChainingNode(MCScenarioPointRegion region, Meta meta, CScenarioChainingNode d, MCScenarioChainingGraph parent, int index)
        {
            Region = region;
            Parent = parent;
            _Data = d;
            NodeIndex = index;
        }
        public MCScenarioChainingNode(MCScenarioPointRegion region, MCScenarioChainingNode copy)
        {
            Region = region;
            _Data = copy._Data;
            Type = copy.Type;
        }

        public void CopyFrom(MCScenarioChainingNode copy)
        {
            _Data = copy._Data;
            Type = copy.Type;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioChainingNode>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioChainingNode);
            return mb.AddItemPtr(MetaName.CScenarioChainingNode, _Data);
        }

        public override string Name
        {
            get
            {
                return "CScenarioChainingNode";
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }

    }

    [TC(typeof(EXP))] public struct CScenarioChainingEdge
    {
        public ushort NodeIndexFrom { get; set; }
        public ushort NodeIndexTo { get; set; }
        public CScenarioChainingEdge__eAction Action { get; set; }
        public CScenarioChainingEdge__eNavMode NavMode { get; set; }
        public CScenarioChainingEdge__eNavSpeed NavSpeed { get; set; }
        public byte Unused0 { get; set; }

        public override string ToString()
        {
            return NodeIndexFrom.ToString() + ", " + NodeIndexTo.ToString() + ", " + Action.ToString() + ", " + NavMode.ToString() + ", " + NavSpeed.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCScenarioChainingEdge : MetaWrapper
    {
        public MCScenarioPointRegion Region { get; set; }

        public CScenarioChainingEdge _Data;
        public CScenarioChainingEdge Data { get { return _Data; } set { _Data = value; } }

        public MCScenarioChainingNode NodeFrom { get; set; }
        public MCScenarioChainingNode NodeTo { get; set; }
        public ushort NodeIndexFrom { get { return _Data.NodeIndexFrom; } set { _Data.NodeIndexFrom = value; } }
        public ushort NodeIndexTo { get { return _Data.NodeIndexTo; } set { _Data.NodeIndexTo = value; } }
        public CScenarioChainingEdge__eAction Action { get { return _Data.Action; } set { _Data.Action = value; } }
        public CScenarioChainingEdge__eNavMode NavMode { get { return _Data.NavMode; } set { _Data.NavMode = value; } }
        public CScenarioChainingEdge__eNavSpeed NavSpeed { get { return _Data.NavSpeed; } set { _Data.NavSpeed = value; } }

        public int EdgeIndex { get; set; }

        public MCScenarioChainingEdge() { }
        public MCScenarioChainingEdge(MCScenarioPointRegion region, Meta meta, CScenarioChainingEdge d, int index)
        {
            Region = region;
            _Data = d;
            EdgeIndex = index;
        }
        public MCScenarioChainingEdge(MCScenarioPointRegion region, MCScenarioChainingEdge copy)
        {
            Region = region;
            _Data = copy._Data;
            NodeFrom = copy.NodeFrom;
            NodeTo = copy.NodeTo;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioChainingEdge>(meta, ptr);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioChainingEdge);
            return mb.AddItemPtr(MetaName.CScenarioChainingEdge, _Data);
        }

        public override string Name
        {
            get
            {
                return "CScenarioChainingEdge";
            }
        }

        public override string ToString()
        {
            return Action.ToString() + ", " + NavMode.ToString() + ", " + NavSpeed.ToString();
        }

    }

    [TC(typeof(EXP))] public struct CScenarioChain
    {
        public byte Unk_1156691834 { get; set; }
        public byte Unused0 { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public Array_ushort EdgeIds { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }

        public override string ToString()
        {
            return Unk_1156691834.ToString() + ": " + EdgeIds.Count1.ToString() + " EdgeIds";
        }
    }
    [TC(typeof(EXP))] public class MCScenarioChain : MetaWrapper
    {
        public MCScenarioPointRegion Region { get; set; }

        public CScenarioChain _Data;
        public CScenarioChain Data { get { return _Data; } set { _Data = value; } }

        public byte Unk1 { get { return _Data.Unk_1156691834; } set { _Data.Unk_1156691834 = value; } }

        public ushort[] EdgeIds { get; set; }
        public MCScenarioChainingEdge[] Edges { get; set; }

        public int ChainIndex { get; set; }

        public MCScenarioChain() { }
        public MCScenarioChain(MCScenarioPointRegion region, Meta meta, CScenarioChain d)
        {
            Region = region;
            _Data = d;
            EdgeIds = MetaTypes.GetUshortArray(meta, _Data.EdgeIds);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioChain>(meta, ptr);
            EdgeIds = MetaTypes.GetUshortArray(meta, _Data.EdgeIds);
        }

        public void AddEdge(MCScenarioChainingEdge edge)
        {
            List<MCScenarioChainingEdge> newedges = new List<MCScenarioChainingEdge>();
            List<ushort> newedgeids = new List<ushort>();
            if (Edges != null)
            {
                newedges.AddRange(Edges);
            }
            if (EdgeIds != null)
            {
                newedgeids.AddRange(EdgeIds);
            }
            edge.Region = Region;
            newedges.Add(edge);
            newedgeids.Add((ushort)edge.EdgeIndex);
            Edges = newedges.ToArray();
            EdgeIds = newedgeids.ToArray();
        }
        public bool RemoveEdge(MCScenarioChainingEdge e)
        {
            bool r = false;
            if (Edges != null)
            {
                List<MCScenarioChainingEdge> newedges = new List<MCScenarioChainingEdge>();
                List<ushort> newedgeids = new List<ushort>();
                foreach (var ne in Edges)
                {
                    if (ne == e)
                    {
                        r = true;
                    }
                    else
                    {
                        newedges.Add(ne);
                        newedgeids.Add((ushort)ne.EdgeIndex);
                    }
                }
                if (r)
                {
                    Edges = newedges.ToArray();
                    EdgeIds = newedgeids.ToArray();
                }
            }
            return r;
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {

            mb.AddStructureInfo(MetaName.CScenarioChain);
            return mb.AddItemPtr(MetaName.CScenarioChain, _Data);
        }

        public override string Name
        {
            get
            {
                return "CScenarioChain";
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }

    }

    [TC(typeof(EXP))] public struct CScenarioPointCluster
    {
        public CScenarioPointContainer Points { get; set; }
        public rage__spdSphere ClusterSphere { get; set; }
        public float NextSpawnAttemptDelay { get; set; }
        public byte AllPointsRequiredForSpawn { get; set; }
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }

        public override string ToString()
        {
            return Points.ToString();
        }
    }
    [TC(typeof(EXP))] public class MCScenarioPointCluster : MetaWrapper
    {
        public MCScenarioPointRegion Region { get; set; }

        public CScenarioPointCluster _Data;
        public CScenarioPointCluster Data { get { return _Data; } set { _Data = value; } }

        public MCScenarioPointContainer Points { get; set; }

        public Vector3 Position
        {
            get { return _Data.ClusterSphere.centerAndRadius.XYZ(); }
            set
            {
                var v4 = new Vector4(value, _Data.ClusterSphere.centerAndRadius.W);
                _Data.ClusterSphere = new rage__spdSphere() { centerAndRadius = v4 };
            }
        }
        public float Radius
        {
            get { return _Data.ClusterSphere.centerAndRadius.W; }
            set
            {
                var v4 = new Vector4(_Data.ClusterSphere.centerAndRadius.XYZ(), value);
                _Data.ClusterSphere = new rage__spdSphere() { centerAndRadius = v4 };
            }
        }
        public float NextSpawnAttemptDelay { get { return _Data.NextSpawnAttemptDelay; } set { _Data.NextSpawnAttemptDelay = value; } }
        public bool AllPointsRequiredForSpawn { get { return _Data.AllPointsRequiredForSpawn==1; } set { _Data.AllPointsRequiredForSpawn = (byte)(value?1:0); } }

        public MCScenarioPointCluster() { }
        public MCScenarioPointCluster(MCScenarioPointRegion region) { Region = region; }
        public MCScenarioPointCluster(MCScenarioPointRegion region, MCScenarioPointCluster copy)
        {
            Region = region;
            if (copy != null)
            {
                _Data = copy.Data;
            }
            Points = new MCScenarioPointContainer(region);
            Points.Parent = this;
        }
        public MCScenarioPointCluster(MCScenarioPointRegion region, Meta meta, CScenarioPointCluster d)
        {
            Region = region;
            _Data = d;
            Points = new MCScenarioPointContainer(region, meta, d.Points);
            Points.Parent = this;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioPointCluster>(meta, ptr);
            Points = new MCScenarioPointContainer(Region, meta, _Data.Points);
            Points.Parent = this;
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.rage__spdSphere);
            mb.AddStructureInfo(MetaName.CScenarioPointCluster);

            if (Points != null)
            {
                var scp = new CScenarioPointContainer();

                var loadSavePoints = Points.GetCLoadSavePoints();
                if (loadSavePoints != null)
                {
                    mb.AddStructureInfo(MetaName.CExtensionDefSpawnPoint);
                    mb.AddEnumInfo(MetaName.CSpawnPoint__AvailabilityMpSp);
                    mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
                    scp.LoadSavePoints = mb.AddItemArrayPtr(MetaName.CExtensionDefSpawnPoint, loadSavePoints);
                }
                var myPoints = Points.GetCMyPoints();
                if (myPoints != null)
                {
                    mb.AddStructureInfo(MetaName.CScenarioPoint);
                    mb.AddEnumInfo(MetaName.CScenarioPointFlags__Flags);
                    scp.MyPoints = mb.AddItemArrayPtr(MetaName.CScenarioPoint, myPoints);
                }

                _Data.Points = scp;
            }
            else
            {
                _Data.Points = new CScenarioPointContainer();
            }

            return mb.AddItemPtr(MetaName.CScenarioPointCluster, _Data);
        }

        public override string Name
        {
            get { return "CScenarioPointCluster"; }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CScenarioPointLookUps
    {
        public Array_uint TypeNames { get; set; }
        public Array_uint PedModelSetNames { get; set; }
        public Array_uint VehicleModelSetNames { get; set; }
        public Array_uint GroupNames { get; set; }
        public Array_uint InteriorNames { get; set; }
        public Array_uint RequiredIMapNames { get; set; }

        public override string ToString()
        {
            return "CScenarioPointLookUps";
        }
    }
    [TC(typeof(EXP))] public class MCScenarioPointLookUps : MetaWrapper
    {
        public MCScenarioPointRegion Region { get; set; }

        public CScenarioPointLookUps _Data;
        public CScenarioPointLookUps Data { get { return _Data; } set { _Data = value; } }

        public MetaHash[] TypeNames { get; set; }
        public MetaHash[] PedModelSetNames { get; set; }
        public MetaHash[] VehicleModelSetNames { get; set; }
        public MetaHash[] GroupNames { get; set; }
        public MetaHash[] InteriorNames { get; set; }
        public MetaHash[] RequiredIMapNames { get; set; }

        public MCScenarioPointLookUps() { }
        public MCScenarioPointLookUps(MCScenarioPointRegion region)
        {
            Region = region;
        }
        public MCScenarioPointLookUps(MCScenarioPointRegion region, Meta meta, CScenarioPointLookUps d)
        {
            Region = region;
            _Data = d;
            Init(meta);
        }

        public void Init(Meta meta)
        {
            TypeNames = MetaTypes.GetHashArray(meta, _Data.TypeNames);
            PedModelSetNames = MetaTypes.GetHashArray(meta, _Data.PedModelSetNames);
            VehicleModelSetNames = MetaTypes.GetHashArray(meta, _Data.VehicleModelSetNames);
            GroupNames = MetaTypes.GetHashArray(meta, _Data.GroupNames);
            InteriorNames = MetaTypes.GetHashArray(meta, _Data.InteriorNames);
            RequiredIMapNames = MetaTypes.GetHashArray(meta, _Data.RequiredIMapNames);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CScenarioPointLookUps>(meta, ptr);
            Init(meta);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            mb.AddStructureInfo(MetaName.CScenarioPointLookUps);
            return mb.AddItemPtr(MetaName.CScenarioPointLookUps, _Data);
        }

        public override string Name
        {
            get
            {
                return "CScenarioPointLookUps";
            }
        }

        public override string ToString()
        {
            return _Data.ToString();
        }

    }

    [TC(typeof(EXP))] public struct CCompositeEntityType
    {
        public ArrayOfChars64 Name { get; set; }
        public float lodDist { get; set; }
        public uint flags { get; set; }
        public uint specialAttribute { get; set; }
        public uint Unused0 { get; set; }
        public Vector3 bbMin { get; set; }
        public float Unused1 { get; set; }
        public Vector3 bbMax { get; set; }
        public float Unused2 { get; set; }
        public Vector3 bsCentre { get; set; }
        public float Unused3 { get; set; }
        public float bsRadius { get; set; }
        public uint Unused4 { get; set; }
        public ArrayOfChars64 StartModel { get; set; }
        public ArrayOfChars64 EndModel { get; set; }
        public MetaHash StartImapFile { get; set; }
        public MetaHash EndImapFile { get; set; }
        public MetaHash PtFxAssetName { get; set; }
        public uint Unused5 { get; set; }
        public Array_Structure Animations { get; set; }
        public uint Unused6 { get; set; }
        public uint Unused7 { get; set; }

        public override string ToString()
        {
            return Name.ToString() + ", " + StartModel.ToString() + ", " + EndModel.ToString() + ", " +
                    StartImapFile.ToString() + ", " + EndImapFile.ToString() + ", " + PtFxAssetName.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CCompEntityAnims
    {
        public ArrayOfChars64 AnimDict { get; set; }
        public ArrayOfChars64 AnimName { get; set; }
        public ArrayOfChars64 AnimatedModel { get; set; }
        public float punchInPhase { get; set; }
        public float punchOutPhase { get; set; }
        public Array_Structure effectsData { get; set; }
    }

    [TC(typeof(EXP))] public struct CCompEntityEffectsData
    {
        public uint fxType { get; set; }
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public Vector3 fxOffsetPos { get; set; }
        public float Unused3 { get; set; }
        public Vector4 fxOffsetRot { get; set; }
        public uint boneTag { get; set; }
        public float startPhase { get; set; }
        public float endPhase { get; set; }
        public byte ptFxIsTriggered { get; set; }
        public ArrayOfChars64 ptFxTag { get; set; }
        public byte Unused4 { get; set; }
        public ushort Unused5 { get; set; }
        public float ptFxScale { get; set; }
        public float ptFxProbability { get; set; }
        public byte ptFxHasTint { get; set; }
        public byte ptFxTintR { get; set; }
        public byte ptFxTintG { get; set; }
        public byte ptFxTintB { get; set; }
        public uint Unused6 { get; set; }
        public Vector3 ptFxSize { get; set; }
        public uint Unused7 { get; set; }
    }

    [TC(typeof(EXP))] public struct CStreamingRequestRecord
    {
        public Array_Structure Frames { get; set; }
        public Array_Structure CommonSets { get; set; }
        public byte NewStyle { get; set; }
        public byte Unused0 { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }
    }

    [TC(typeof(EXP))] public struct CStreamingRequestFrame
    {
        public Array_uint AddList { get; set; }
        public Array_uint RemoveList { get; set; }
        public Array_uint PromoteToHDList { get; set; }
        public Vector3 CamPos { get; set; }
        public float Unused0 { get; set; }
        public Vector3 CamDir { get; set; }
        public float Unused1 { get; set; }
        public Array_byte CommonAddSets { get; set; }
        public uint Flags { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
    }

    [TC(typeof(EXP))] public struct CStreamingRequestFrame_v2
    {
        public Array_uint AddList { get; set; }
        public Array_uint RemoveList { get; set; }
        public Vector3 CamPos { get; set; }
        public float Unused0 { get; set; }
        public Vector3 CamDir { get; set; }
        public float Unused1 { get; set; }
        public Array_byte Unk_1762439591 { get; set; }
        public uint Flags { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
    }

    [TC(typeof(EXP))] public struct CStreamingRequestCommonSet
    {
        public Array_uint Requests { get; set; }
    }

    [TC(typeof(EXP))] public struct CCreatureMetaData
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public Array_Structure shaderVariableComponents { get; set; }
        public Array_Structure pedPropExpressions { get; set; }
        public Array_Structure pedCompExpressions { get; set; }
    }

    [TC(typeof(EXP))] public struct CShaderVariableComponent
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint pedcompID { get; set; }
        public uint maskID { get; set; }
        public uint shaderVariableHashString { get; set; }
        public uint Unused2 { get; set; }
        public Array_byte tracks { get; set; }
        public Array_ushort ids { get; set; }
        public Array_byte components { get; set; }
    }

    [TC(typeof(EXP))] public struct CPedPropExpressionData
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint pedPropID { get; set; }
        public int pedPropVarIndex { get; set; }
        public uint pedPropExpressionIndex { get; set; }
        public uint Unused2 { get; set; }
        public Array_byte tracks { get; set; }
        public Array_ushort ids { get; set; }
        public Array_byte types { get; set; }
        public Array_byte components { get; set; }
    }

    [TC(typeof(EXP))] public struct CPedCompExpressionData
    {
        public uint Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public uint pedCompID { get; set; }
        public int pedCompVarIndex { get; set; }
        public uint pedCompExpressionIndex { get; set; }
        public uint Unused2 { get; set; }
        public Array_byte tracks { get; set; }
        public Array_ushort ids { get; set; }
        public Array_byte types { get; set; }
        public Array_byte components { get; set; }
    }

    [TC(typeof(EXP))] public struct CPedVariationInfo : IPsoSwapEnd
    {
        public byte bHasTexVariations { get; set; }
        public byte bHasDrawblVariations { get; set; }
        public byte bHasLowLODs { get; set; }
        public byte bIsSuperLOD { get; set; }
        public ArrayOfBytes12 availComp { get; set; }
        public Array_Structure aComponentData3 { get; set; }
        public Array_Structure aSelectionSets { get; set; }
        public Array_Structure compInfos { get; set; }
        public CPedPropInfo propInfo { get; set; }
        public MetaHash dlcName { get; set; }
        public uint Unused0 { get; set; }

        public void SwapEnd()
        {
            aComponentData3 = aComponentData3.SwapEnd();
            aSelectionSets = aSelectionSets.SwapEnd();
            compInfos = compInfos.SwapEnd();
            propInfo = propInfo.SwapEnd();
            dlcName = MetaTypes.SwapBytes(dlcName);
        }
    }
    [TC(typeof(EXP))] public class MCPedVariationInfo : MetaWrapper
    {
        public CPedVariationInfo _Data;
        public CPedVariationInfo Data { get { return _Data; } }

        public byte[] ComponentIndices { get; set; }
        public MCPVComponentData[] ComponentData3 { get; set; }
        public MCPedSelectionSet[] SelectionSets { get; set; }
        public MCComponentInfo[] CompInfos { get; set; }
        public MCPedPropInfo PropInfo { get; set; }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            var data = MetaTypes.GetData<CPedVariationInfo>(meta, ptr);
            Load(meta, data);
        }
        public void Load(Meta meta, CPedVariationInfo data)
        {

            _Data = data;

            ComponentIndices = data.availComp.GetArray();

            var aComponentData3 = MetaTypes.ConvertDataArray<CPVComponentData>(meta, MetaName.CPVComponentData, _Data.aComponentData3);
            if (aComponentData3 != null)
            {
                ComponentData3 = new MCPVComponentData[aComponentData3.Length];
                for (int i = 0; i < aComponentData3.Length; i++)
                {
                    ComponentData3[i] = new MCPVComponentData(meta, aComponentData3[i], this);
                }
            }

            var vSelectionSets = MetaTypes.ConvertDataArray<CPedSelectionSet>(meta, MetaName.CPedSelectionSet, _Data.aSelectionSets);
            if (vSelectionSets != null)
            {
                SelectionSets = new MCPedSelectionSet[vSelectionSets.Length];
                for (int i = 0; i < vSelectionSets.Length; i++)
                {
                    SelectionSets[i] = new MCPedSelectionSet(meta, vSelectionSets[i], this);
                }
            }

            var vCompInfos = MetaTypes.ConvertDataArray<CComponentInfo>(meta, MetaName.CComponentInfo, _Data.compInfos);
            if (vCompInfos != null)
            {
                CompInfos = new MCComponentInfo[vCompInfos.Length];
                for (int i = 0; i < vCompInfos.Length; i++)
                {
                    CompInfos[i] = new MCComponentInfo(meta, vCompInfos[i], this);
                }
            }

            PropInfo = new MCPedPropInfo(meta, data.propInfo, this);

            for (int i = 0; i < 12; i++)
            {
                var compInd = ComponentIndices[i];
                if ((compInd > 0) && (compInd < ComponentData3?.Length))
                {
                    var compvar = ComponentData3[compInd];
                    compvar.ComponentType = i;

                    if (compvar.DrawblData3 != null)
                    {
                        foreach (var cvp in compvar.DrawblData3)
                        {
                            cvp.ComponentType = i;
                        }
                    }
                }
            }

        }
        public void Load(PsoFile pso, CPedVariationInfo data)
        {
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }

        public MCPVComponentData GetComponentData(int componentType)
        {
            if ((componentType < 0) || (componentType > 11)) return null;
            if (ComponentIndices == null) return null;
            var index = ComponentIndices[componentType];
            if (index > ComponentData3?.Length) return null;
            return ComponentData3[index];
        }

    }

    [TC(typeof(EXP))] public struct CPVComponentData
    {
        public byte numAvailTex { get; set; }
        public byte Unused0 { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public Array_Structure aDrawblData3 { get; set; }
    }
    [TC(typeof(EXP))] public class MCPVComponentData : MetaWrapper
    {
        public MCPedVariationInfo Owner { get; set; }

        public CPVComponentData _Data;
        public CPVComponentData Data { get { return _Data; } }

        public byte numAvailTex { get { return _Data.numAvailTex; } set { _Data.numAvailTex = value; } }

        public MCPVDrawblData[] DrawblData3 { get; set; }

        public int ComponentType { get; set; } = 0;
        public static string[] ComponentTypeNames { get; } =
        {
            "head",
            "berd",
            "hair",
            "uppr",
            "lowr",
            "hand",
            "feet",
            "teef",
            "accs",
            "task",
            "decl",
            "jbib",
        };

        public MCPVComponentData() { }
        public MCPVComponentData(Meta meta, CPVComponentData data, MCPedVariationInfo owner)
        {
            _Data = data;
            Owner = owner;
            Init(meta);
        }

        private void Init(Meta meta)
        {
            var aDrawblData3 = MetaTypes.ConvertDataArray<CPVDrawblData>(meta, MetaName.CPVDrawblData, _Data.aDrawblData3);
            if (aDrawblData3 != null)
            {
                DrawblData3 = new MCPVDrawblData[aDrawblData3.Length];
                for (int i = 0; i < aDrawblData3.Length; i++)
                {
                    DrawblData3[i] = new MCPVDrawblData(meta, aDrawblData3[i], this, i);
                }
            }
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            _Data = MetaTypes.GetData<CPVComponentData>(meta, ptr);
            Init(meta);
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            string r = (ComponentType < 12) ? ComponentTypeNames[ComponentType] : "error";
            return r + " : " + DrawblData3?.Length.ToString() ?? base.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CPVDrawblData
    {
        public byte propMask { get; set; }
        public byte numAlternatives { get; set; }
        public ushort Unused0 { get; set; }
        public uint Unused1 { get; set; }
        public Array_Structure aTexData { get; set; }
        public CPVDrawblData__CPVClothComponentData clothData { get; set; }
    }
    [TC(typeof(EXP))] public class MCPVDrawblData : MetaWrapper
    {
        public MCPVComponentData Owner { get; set; }

        public CPVDrawblData _Data;
        public CPVDrawblData Data { get { return _Data; } }

        public CPVTextureData[] TexData { get; set; }

        public int ComponentType { get; set; } = 0;
        public int DrawableIndex { get; set; } = 0;
        public int PropMask { get { return _Data.propMask; } }
        public int NumAlternatives { get { return _Data.numAlternatives; } }

        public int PropType { get { return (PropMask >> 4) & 3; } }

        public string GetDrawableName(int altnum = 0)
        {
            string r = (ComponentType < 12) ? MCPVComponentData.ComponentTypeNames[ComponentType] : "error";
            r += "_";
            r += DrawableIndex.ToString("000");
            r += "_";
            switch (PropType)
            {
                case 0: r += "u"; break;
                case 1: r += "r"; break;
                case 2: r += "m"; break;
                case 3: r += "m"; break;
                default:
                    break;
            }
            if (altnum > 0)
            {
                r += "_";
                r += altnum.ToString();
            }
            return r;
        }

        public string GetTextureName(int texnum = 0)
        {
            return GetTexturePrefix() + GetTextureSuffix(texnum);
        }
        public string GetTexturePrefix()
        {
            string r = (ComponentType < 12) ? MCPVComponentData.ComponentTypeNames[ComponentType] : "error";
            r += "_diff_";
            r += DrawableIndex.ToString("000");
            r += "_";
            return r;
        }
        public string GetTextureSuffix(int texnum)
        {
            if (texnum < 0) texnum = 0;
            const string alphas = "abcdefghijklmnopqrstuvwxyz";
            var tex = TexData[texnum];
            var texid = tex.texId;
            var distr = tex.distribution;
            var r = string.Empty;
            r += alphas[texnum % 26];
            r += "_";
            switch (texid)
            {
                case 0:
                    r += "uni";
                    break;
                case 1:
                    r += "whi";
                    break;
                case 2:
                    r += "bla";
                    break;
                case 3:
                    r += "chi";
                    break;
                case 4:
                    r += "lat";
                    break;
                case 5:
                    r += "ara";
                    break;
                case 8:
                    r += "kor";
                    break;
                case 10:
                    r += "pak";
                    break;
                default:
                    r += "whi";
                    break;
            }
            return r;
        }

        public MCPVDrawblData() { }
        public MCPVDrawblData(Meta meta, CPVDrawblData data, MCPVComponentData owner, int index)
        {
            _Data = data;
            Owner = owner;
            DrawableIndex = index;

            TexData = MetaTypes.ConvertDataArray<CPVTextureData>(meta, MetaName.CPVTextureData, _Data.aTexData);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            throw new NotImplementedException();
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return GetDrawableName();
        }
    }

    [TC(typeof(EXP))] public struct CPVTextureData
    {
        public byte texId { get; set; }
        public byte distribution { get; set; }
        public byte Unused0 { get; set; }
    }

    [TC(typeof(EXP))] public struct CPVDrawblData__CPVClothComponentData
    {
        public byte ownsCloth { get; set; }
        public byte Unused0 { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public uint Unused5 { get; set; }
        public uint Unused6 { get; set; }
    }

    [TC(typeof(EXP))] public struct CPedSelectionSet
    {
        public MetaHash name { get; set; }
        public ArrayOfBytes12 compDrawableId { get; set; }
        public ArrayOfBytes12 compTexId { get; set; }
        public ArrayOfBytes6 propAnchorId { get; set; }
        public ArrayOfBytes6 propDrawableId { get; set; }
        public ArrayOfBytes6 propTexId { get; set; }
        public ushort Unused0 { get; set; }
    }
    [TC(typeof(EXP))] public class MCPedSelectionSet : MetaWrapper
    {
        public MCPedVariationInfo Owner { get; set; }

        public CPedSelectionSet _Data;
        public CPedSelectionSet Data { get { return _Data; } }

        public MCPedSelectionSet() { }
        public MCPedSelectionSet(Meta meta, CPedSelectionSet data, MCPedVariationInfo owner)
        {
            _Data = data;
            Owner = owner;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            throw new NotImplementedException();
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }
    }

    [TC(typeof(EXP))] public struct CComponentInfo
    {
        public MetaHash pedXml_audioID { get; set; }
        public MetaHash pedXml_audioID2 { get; set; }
        public ArrayOfFloats5 pedXml_expressionMods { get; set; }
        public uint flags { get; set; }
        public int inclusions { get; set; }
        public int exclusions { get; set; }
        public ePedVarComp pedXml_vfxComps { get; set; }
        public ushort pedXml_flags { get; set; }
        public byte pedXml_compIdx { get; set; }
        public byte pedXml_drawblIdx { get; set; }
        public ushort Unused5 { get; set; }
    }
    [TC(typeof(EXP))] public class MCComponentInfo : MetaWrapper
    {
        public MCPedVariationInfo Owner { get; set; }

        public CComponentInfo _Data;
        public CComponentInfo Data { get { return _Data; } }

        public int ComponentType { get { return _Data.pedXml_compIdx; } }
        public int ComponentIndex { get { return _Data.pedXml_drawblIdx; } }

        public MCComponentInfo() { }
        public MCComponentInfo(Meta meta, CComponentInfo data, MCPedVariationInfo owner)
        {
            _Data = data;
            Owner = owner;
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            throw new NotImplementedException();
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }

        public override string ToString()
        {
            return (ComponentType < 12) ? MCPVComponentData.ComponentTypeNames[ComponentType] + "_" + ComponentIndex.ToString("000") : base.ToString();
        }
    }

    [TC(typeof(EXP))] public struct CPedPropInfo
    {
        public byte numAvailProps { get; set; }
        public byte Unused0 { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public Array_Structure aPropMetaData { get; set; }
        public Array_Structure aAnchors { get; set; }
        public CPedPropInfo SwapEnd()
        {
            aPropMetaData = aPropMetaData.SwapEnd();
            aAnchors = aAnchors.SwapEnd();
            return this;
        }
    }
    [TC(typeof(EXP))] public class MCPedPropInfo : MetaWrapper
    {
        public MCPedVariationInfo Owner { get; set; }

        public CPedPropInfo _Data;
        public CPedPropInfo Data { get { return _Data; } }

        public MCPedPropMetaData[] PropMetaData { get; set; }
        public MCAnchorProps[] Anchors { get; set; }

        public MCPedPropInfo() { }
        public MCPedPropInfo(Meta meta, CPedPropInfo data, MCPedVariationInfo owner)
        {
            _Data = data;
            Owner = owner;

            var vPropMetaData = MetaTypes.ConvertDataArray<CPedPropMetaData>(meta, MetaName.CPedPropMetaData, _Data.aPropMetaData);
            if (vPropMetaData != null)
            {
                PropMetaData = new MCPedPropMetaData[vPropMetaData.Length];
                for (int i = 0; i < vPropMetaData.Length; i++)
                {
                    PropMetaData[i] = new MCPedPropMetaData(meta, vPropMetaData[i], this);
                }
            }

            var vAnchors = MetaTypes.ConvertDataArray<CAnchorProps>(meta, MetaName.CAnchorProps, _Data.aAnchors);
            if (vAnchors != null)
            {
                Anchors = new MCAnchorProps[vAnchors.Length];
                for (int i = 0; i < vAnchors.Length; i++)
                {
                    Anchors[i] = new MCAnchorProps(meta, vAnchors[i], this);
                }
            }

        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            throw new NotImplementedException();
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }
    }

    [TC(typeof(EXP))] public struct CPedPropMetaData
    {
        public MetaHash audioId { get; set; }
        public ArrayOfBytes5 expressionMods { get; set; }
        public byte Unused0 { get; set; }
        public ushort Unused1 { get; set; }
        public uint Unused2 { get; set; }
        public uint Unused3 { get; set; }
        public uint Unused4 { get; set; }
        public Array_Structure texData { get; set; }
        public ePropRenderFlags renderFlags { get; set; }
        public uint propFlags { get; set; }
        public ushort flags { get; set; }
        public byte anchorId { get; set; }
        public byte propId { get; set; }
        public byte stickyness { get; set; }
        public byte Unused5 { get; set; }
        public ushort Unused6 { get; set; }
    }
    [TC(typeof(EXP))] public class MCPedPropMetaData : MetaWrapper
    {
        public MCPedPropInfo Owner { get; set; }

        public CPedPropMetaData _Data;
        public CPedPropMetaData Data { get { return _Data; } }

        public CPedPropTexData[] TexData { get; set; }

        public MCPedPropMetaData(Meta meta, CPedPropMetaData data, MCPedPropInfo owner)
        {
            _Data = data;
            Owner = owner;

            TexData = MetaTypes.ConvertDataArray<CPedPropTexData>(meta, MetaName.CPedPropTexData, _Data.texData);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            throw new NotImplementedException();
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }
    }

    [TC(typeof(EXP))] public struct CPedPropTexData
    {
        public int inclusions { get; set; }
        public int exclusions { get; set; }
        public byte texId { get; set; }
        public byte inclusionId { get; set; }
        public byte exclusionId { get; set; }
        public byte distribution { get; set; }
    }

    [TC(typeof(EXP))] public struct CAnchorProps
    {
        public Array_byte props { get; set; }
        public eAnchorPoints anchor { get; set; }
        public uint Unused0 { get; set; }
    }
    [TC(typeof(EXP))] public class MCAnchorProps : MetaWrapper
    {
        public MCPedPropInfo Owner { get; set; }

        public CAnchorProps _Data;
        public CAnchorProps Data { get { return _Data; } }

        public byte[] Props { get; set; }

        public MCAnchorProps(Meta meta, CAnchorProps data, MCPedPropInfo owner)
        {
            _Data = data;
            Owner = owner;

            Props = MetaTypes.GetByteArray(meta, _Data.props);
        }

        public override void Load(Meta meta, MetaPOINTER ptr)
        {
            throw new NotImplementedException();
        }

        public override MetaPOINTER Save(MetaBuilder mb)
        {
            throw new NotImplementedException();
        }
    }

}
