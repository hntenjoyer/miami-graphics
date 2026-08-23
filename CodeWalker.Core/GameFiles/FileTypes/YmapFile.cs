using SharpDX;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CodeWalker.Core.Utils;
using CodeWalker.World;

namespace CodeWalker.GameFiles
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapFile : GameFile, PackedFile
    {

        public Meta Meta { get; set; }
        public PsoFile Pso { get; set; }
        public RbfFile Rbf { get; set; }

        public CMapData _CMapData;

        public CMapData CMapData { get { return _CMapData; } set { _CMapData = value; } }
        public CEntityDef[] CEntityDefs { get; set; }
        public CMloInstanceDef[] CMloInstanceDefs { get; set; }
        public CCarGen[] CCarGens { get; set; }
        public CTimeCycleModifier[] CTimeCycleModifiers { get; set; }
        public MetaHash[] physicsDictionaries { get; set; }

        public BoxOccluder[] CBoxOccluders { get; set; }
        public OccludeModel[] COccludeModels { get; set; }

        public string[] Strings { get; set; }
        public YmapEntityDef[] AllEntities;
        public YmapEntityDef[] RootEntities;
        public YmapEntityDef[] MloEntities;

        public YmapFile Parent { get; set; }
        public YmapFile[] ChildYmaps = null;
        public bool MergedWithParent = false;

        public bool IsScripted { get { return (_CMapData.flags & 1) > 0; } }

        public YmapGrassInstanceBatch[] GrassInstanceBatches { get; set; }
        public YmapPropInstanceBatch[] PropInstanceBatches { get; set; }

        public YmapDistantLODLights DistantLODLights { get; set; }

        public YmapLODLights LODLights { get; set; }

        public YmapTimeCycleModifier[] TimeCycleModifiers { get; set; }

        public YmapCarGen[] CarGenerators { get; set; }

        public YmapBoxOccluder[] BoxOccluders { get; set; }
        public YmapOccludeModel[] OccludeModels { get; set; }

        public bool HasChanged { get; set; } = false;
        public List<string> SaveWarnings = null;
        public bool LodManagerUpdate = false;
        public YmapEntityDef[] LodManagerOldEntities = null;

        public YmapFile() : base(null, GameFileType.Ymap)
        {
        }
        public YmapFile(RpfFileEntry entry) : base(entry, GameFileType.Ymap)
        {
            RpfFileEntry = entry;
        }

        public void Load(byte[] data)
        {

            RpfFile.LoadResourceFile(this, data, 2);

            Loaded = true;
        }

        public void Load(byte[] data, RpfFileEntry entry)
        {
            Name = entry.Name;
            RpfFileEntry = entry;

            RpfResourceFileEntry resentry = entry as RpfResourceFileEntry;
            if (resentry == null)
            {
                NonMetaLoad(data);
                Loaded = true;
                return;
            }

            ResourceDataReader rd = new ResourceDataReader(resentry, data);

            Meta = rd.ReadBlock<Meta>();

            CMapData = MetaTypes.GetTypedData<CMapData>(Meta, MetaName.CMapData);

            Strings = MetaTypes.GetStrings(Meta);
            if (Strings != null)
            {
                foreach (string str in Strings)
                {
                    JenkIndex.Ensure(str);
                }
            }

            physicsDictionaries = MetaTypes.GetHashArray(Meta, _CMapData.physicsDictionaries);

            EnsureEntities(Meta);

            EnsureInstances(Meta);

            EnsureLODLights(Meta);

            EnsureDistantLODLights(Meta);

            EnsureTimeCycleModifiers(Meta);

            EnsureCarGens(Meta);

            EnsureBoxOccluders(Meta);

            EnsureOccludeModels(Meta);

            EnsureContainerLods(Meta);

            #region data block test and old code

            #endregion

        }

        private void NonMetaLoad(byte[] data)
        {
            MemoryStream ms = new MemoryStream(data);
            if (RbfFile.IsRBF(ms))
            {
                Rbf = new RbfFile();
                Rbf.Load(ms);
            }
            else if (PsoFile.IsPSO(ms))
            {
                Pso = new PsoFile();
                Pso.Load(ms);
            }
            else
            {
            }

        }

        private void EnsureEntities(Meta Meta)
        {
            var eptrs = MetaTypes.GetPointerArray(Meta, _CMapData.entities);

            CMloInstanceDefs = MetaTypes.GetTypedPointerArray<CMloInstanceDef>(Meta, MetaName.CMloInstanceDef, eptrs);
            if (CMloInstanceDefs != null)
            { }

            CEntityDefs = MetaTypes.GetTypedPointerArray<CEntityDef>(Meta, MetaName.CEntityDef, eptrs);
            if (CEntityDefs != null)
            { }

            int instcount = 0;
            if (CEntityDefs != null) instcount += CEntityDefs.Length;
            if (CMloInstanceDefs != null) instcount += CMloInstanceDefs.Length;

            if (instcount > 0)
            {

                List<YmapEntityDef> roots = new List<YmapEntityDef>(instcount);
                List<YmapEntityDef> alldefs = new List<YmapEntityDef>(instcount);
                List<YmapEntityDef> mlodefs = null;

                if (CEntityDefs != null)
                {
                    for (int i = 0; i < CEntityDefs.Length; i++)
                    {
                        YmapEntityDef d = new YmapEntityDef(this, i, ref CEntityDefs[i]);
                        alldefs.Add(d);
                    }
                }
                if (CMloInstanceDefs != null)
                {
                    mlodefs = new List<YmapEntityDef>();
                    for (int i = 0; i < CMloInstanceDefs.Length; i++)
                    {
                        YmapEntityDef d = new YmapEntityDef(this, i, ref CMloInstanceDefs[i]);
                        MetaHash[] defentsets = MetaTypes.GetHashArray(Meta, CMloInstanceDefs[i].defaultEntitySets);
                        if (d.MloInstance != null)
                        {
                            d.MloInstance.defaultEntitySets = defentsets;
                        }
                        alldefs.Add(d);
                        mlodefs.Add(d);
                    }
                }

                for (int i = 0; i < alldefs.Count; i++)
                {
                    YmapEntityDef d = alldefs[i];
                    int pind = d._CEntityDef.parentIndex;
                    bool isroot = false;
                    if ((pind < 0) || (pind >= alldefs.Count) || d.LodInParentYmap)
                    {
                        isroot = true;
                    }
                    else
                    {
                        YmapEntityDef p = alldefs[pind];
                        if ((p._CEntityDef.lodLevel <= d._CEntityDef.lodLevel) ||
                            ((p._CEntityDef.lodLevel == rage__eLodType.LODTYPES_DEPTH_ORPHANHD) &&
                             (d._CEntityDef.lodLevel != rage__eLodType.LODTYPES_DEPTH_ORPHANHD)))
                        {
                            isroot = true;
                            p = null;
                        }
                    }

                    if (isroot)
                    {
                        roots.Add(d);
                    }
                    else
                    {
                        YmapEntityDef p = alldefs[pind];
                        p.AddChild(d);
                    }
                }
                for (int i = 0; i < alldefs.Count; i++)
                {
                    alldefs[i].ChildListToArray();
                }

                AllEntities = alldefs.ToArray();
                RootEntities = roots.ToArray();
                if (mlodefs != null)
                {
                    MloEntities = mlodefs.ToArray();
                }

                foreach (var ent in AllEntities)
                {
                    ent.Extensions = MetaTypes.GetExtensions(Meta, ent._CEntityDef.extensions);
                }
            }

        }

        private void EnsureInstances(Meta Meta)
        {
            if (_CMapData.instancedData.GrassInstanceList.Count1 != 0)
            {
                rage__fwGrassInstanceListDef[] batches = MetaTypes.ConvertDataArray<rage__fwGrassInstanceListDef>(Meta, MetaName.rage__fwGrassInstanceListDef, _CMapData.instancedData.GrassInstanceList);
                YmapGrassInstanceBatch[] gbatches = new YmapGrassInstanceBatch[batches.Length];
                for (int i = 0; i < batches.Length; i++)
                {
                    var batch = batches[i];
                    rage__fwGrassInstanceListDef__InstanceData[] instdatas = MetaTypes.ConvertDataArray<rage__fwGrassInstanceListDef__InstanceData>(Meta, MetaName.rage__fwGrassInstanceListDef__InstanceData, batch.InstanceList);
                    YmapGrassInstanceBatch gbatch = new YmapGrassInstanceBatch();
                    gbatch.Ymap = this;
                    gbatch.Batch = batch;
                    gbatch.Instances = instdatas;
                    gbatch.Position = (batch.BatchAABB.min.XYZ() + batch.BatchAABB.max.XYZ()) * 0.5f;
                    gbatch.Radius = (batch.BatchAABB.max.XYZ() - gbatch.Position).Length();
                    gbatch.AABBMin = (batch.BatchAABB.min.XYZ());
                    gbatch.AABBMax = (batch.BatchAABB.max.XYZ());
                    gbatches[i] = gbatch;
                }
                GrassInstanceBatches = gbatches;
            }
            if (_CMapData.instancedData.PropInstanceList.Count1 != 0)
            {
            }
        }

        private void EnsureLODLights(Meta Meta)
        {
            if (_CMapData.LODLightsSOA.direction.Count1 != 0)
            {
                var soa = _CMapData.LODLightsSOA;
                LODLights = new YmapLODLights();
                LODLights.Ymap = this;
                LODLights.CLODLight = soa;
                LODLights.direction = MetaTypes.ConvertDataArray<MetaVECTOR3>(Meta, MetaName.FloatXYZ, soa.direction);
                LODLights.falloff = MetaTypes.GetFloatArray(Meta, soa.falloff);
                LODLights.falloffExponent = MetaTypes.GetFloatArray(Meta, soa.falloffExponent);
                LODLights.timeAndStateFlags = MetaTypes.GetUintArray(Meta, soa.timeAndStateFlags);
                LODLights.hash = MetaTypes.GetUintArray(Meta, soa.hash);
                LODLights.coneInnerAngle = MetaTypes.GetByteArray(Meta, soa.coneInnerAngle);
                LODLights.coneOuterAngleOrCapExt = MetaTypes.GetByteArray(Meta, soa.coneOuterAngleOrCapExt);
                LODLights.coronaIntensity = MetaTypes.GetByteArray(Meta, soa.coronaIntensity);
                LODLights.CalcBB();
            }
        }

        private void EnsureDistantLODLights(Meta Meta)
        {
            if (_CMapData.DistantLODLightsSOA.position.Count1 != 0)
            {
                var soa = _CMapData.DistantLODLightsSOA;
                DistantLODLights = new YmapDistantLODLights();
                DistantLODLights.Ymap = this;
                DistantLODLights.CDistantLODLight = soa;
                DistantLODLights.colours = MetaTypes.GetUintArray(Meta, soa.RGBI);
                DistantLODLights.positions = MetaTypes.ConvertDataArray<MetaVECTOR3>(Meta, MetaName.FloatXYZ, soa.position);
                DistantLODLights.CalcBB();
            }
        }

        private void EnsureTimeCycleModifiers(Meta Meta)
        {
            CTimeCycleModifiers = MetaTypes.ConvertDataArray<CTimeCycleModifier>(Meta, MetaName.CTimeCycleModifier, _CMapData.timeCycleModifiers);
            if (CTimeCycleModifiers != null)
            {
                TimeCycleModifiers = new YmapTimeCycleModifier[CTimeCycleModifiers.Length];
                for (int i = 0; i < CTimeCycleModifiers.Length; i++)
                {
                    YmapTimeCycleModifier tcm = new YmapTimeCycleModifier();
                    tcm.Ymap = this;
                    tcm.CTimeCycleModifier = CTimeCycleModifiers[i];
                    tcm.BBMin = tcm.CTimeCycleModifier.minExtents;
                    tcm.BBMax = tcm.CTimeCycleModifier.maxExtents;
                    TimeCycleModifiers[i] = tcm;
                }
            }
        }

        private void EnsureCarGens(Meta Meta)
        {

            CCarGens = MetaTypes.ConvertDataArray<CCarGen>(Meta, MetaName.CCarGen, _CMapData.carGenerators);
            if (CCarGens != null)
            {
                CarGenerators = new YmapCarGen[CCarGens.Length];
                for (int i = 0; i < CCarGens.Length; i++)
                {
                    CarGenerators[i] = new YmapCarGen(this, CCarGens[i]);
                }
            }
        }

        private void EnsureBoxOccluders(Meta meta)
        {
            CBoxOccluders = MetaTypes.ConvertDataArray<BoxOccluder>(Meta, MetaName.BoxOccluder, _CMapData.boxOccluders);
            if (CBoxOccluders != null)
            {
                BoxOccluders = new YmapBoxOccluder[CBoxOccluders.Length];
                for (int i = 0; i < CBoxOccluders.Length; i++)
                {
                    BoxOccluders[i] = new YmapBoxOccluder(this, CBoxOccluders[i]);
                    BoxOccluders[i].Index = i;
                }
            }
        }

        private void EnsureOccludeModels(Meta meta)
        {
            COccludeModels = MetaTypes.ConvertDataArray<OccludeModel>(Meta, MetaName.OccludeModel, _CMapData.occludeModels);
            if (COccludeModels != null)
            {
                OccludeModels = new YmapOccludeModel[COccludeModels.Length];
                for (int i = 0; i < COccludeModels.Length; i++)
                {
                    OccludeModels[i] = new YmapOccludeModel(this, COccludeModels[i]);
                    OccludeModels[i].Index = i;
                    OccludeModels[i].Load(Meta);

                }
            }
        }

        private void EnsureContainerLods(Meta meta)
        {

            if (_CMapData.containerLods.Count1 > 0)
            {

            }

        }

        public void BuildCEntityDefs()
        {

            CEntityDefs = null;
            CMloInstanceDefs = null;
            if (AllEntities == null)
            {
                return;
            }

            List<CEntityDef> centdefs = new List<CEntityDef>();
            List<CMloInstanceDef> cmlodefs = new List<CMloInstanceDef>();

            for (int i = 0; i < AllEntities.Length; i++)
            {
                var ent = AllEntities[i];
                if (ent.MloInstance != null)
                {
                    cmlodefs.Add(ent.MloInstance.Instance);
                }
                else
                {
                    centdefs.Add(ent._CEntityDef);
                }
            }

            if (centdefs.Count > 0)
            {
                CEntityDefs = centdefs.ToArray();
            }
            if (cmlodefs.Count > 0)
            {
                CMloInstanceDefs = cmlodefs.ToArray();
            }
        }
        public void BuildCCarGens()
        {
            if (CarGenerators == null)
            {
                CCarGens = null;
                return;
            }

            int count = CarGenerators.Length;
            CCarGens = new CCarGen[count];
            for (int i = 0; i < count; i++)
            {
                CCarGens[i] = CarGenerators[i].CCarGen;
            }
        }
        public void BuildInstances()
        {
            if (GrassInstanceBatches == null)
            {
                return;
            }

            if (PropInstanceBatches == null)
            { }

            int count = GrassInstanceBatches.Length;
            for (int i = 0; i < count; i++)
            {
                var g = GrassInstanceBatches[i];
                var b = g.Batch;

                var aabb = new rage__spdAABB();
                aabb.min = new Vector4(g.AABBMin, 0);
                aabb.max = new Vector4(g.AABBMax, 0);

                b.BatchAABB = aabb;

                GrassInstanceBatches[i].Batch = b;
            }
        }
        public void BuildLodLights()
        {
            if (LODLights == null) return;
            LODLights.RebuildFromLodLights();
        }
        public void BuildDistantLodLights()
        {
        }
        public void BuildBoxOccluders()
        {
            if (BoxOccluders == null) return;
            if (BoxOccluders.Length == 0) return;

            var boxes = new BoxOccluder[BoxOccluders.Length];
            for (int i = 0; i < BoxOccluders.Length; i++)
            {
                var box = BoxOccluders[i];
                box.UpdateBoxStruct();
                boxes[i] = box._Box;
            }

            CBoxOccluders = boxes;

        }
        public void BuildOccludeModels()
        {
            if (OccludeModels == null) return;
            if (OccludeModels.Length == 0) return;
        }

        public byte[] Save()
        {

            BuildCEntityDefs();
            BuildCCarGens();
            BuildInstances();
            BuildLodLights();
            BuildDistantLodLights();
            BuildBoxOccluders();
            BuildOccludeModels();

            MetaBuilder mb = new MetaBuilder();

            var mdb = mb.EnsureBlock(MetaName.CMapData);

            CMapData mapdata = CMapData;

            if ((AllEntities != null) && (AllEntities.Length > 0))
            {
                for (int i = 0; i < AllEntities.Length; i++)
                {
                    var ent = AllEntities[i];
                    ent._CEntityDef.extensions = mb.AddWrapperArrayPtr(ent.Extensions);
                }

                MetaPOINTER[] ptrs = new MetaPOINTER[AllEntities.Length];
                for (int i = 0; i < AllEntities.Length; i++)
                {
                    var ent = AllEntities[i];
                    if (ent.MloInstance != null)
                    {
                        ent.MloInstance.UpdateDefaultEntitySets();

                        ent.MloInstance._Instance.CEntityDef = ent.CEntityDef;
                        ent.MloInstance._Instance.defaultEntitySets = mb.AddHashArrayPtr(ent.MloInstance.defaultEntitySets);

                        ptrs[i] = mb.AddItemPtr(MetaName.CMloInstanceDef, ent.MloInstance.Instance);
                    }
                    else
                    {
                        ptrs[i] = mb.AddItemPtr(MetaName.CEntityDef, ent.CEntityDef);
                    }
                }
                mapdata.entities = mb.AddPointerArray(ptrs);
            }
            else
            {
                mapdata.entities = new Array_StructurePointer();
            }

            mapdata.timeCycleModifiers = mb.AddItemArrayPtr(MetaName.CTimeCycleModifier, CTimeCycleModifiers);

            mapdata.physicsDictionaries = mb.AddHashArrayPtr(physicsDictionaries);

            mapdata.carGenerators = mb.AddItemArrayPtr(MetaName.CCarGen, CCarGens);

            if (mapdata.containerLods.Count1 != 0) LogSaveWarning("containerLods were not saved. (TODO!)");
            if (mapdata.instancedData.PropInstanceList.Count1 != 0) LogSaveWarning("instancedData.PropInstanceList was not saved. (TODO!)");
            mapdata.containerLods = new Array_Structure();

            if ((GrassInstanceBatches != null) && (GrassInstanceBatches.Length > 0))
            {
                var instancedData = new rage__fwInstancedMapData();
                rage__fwGrassInstanceListDef[] batches = new rage__fwGrassInstanceListDef[GrassInstanceBatches.Length];
                for (int i = 0; i < GrassInstanceBatches.Length; i++)
                {
                    var batch = GrassInstanceBatches[i];

                    if (batch != null)
                    {
                        var b = batch.Batch;
                        b.InstanceList = mb.AddItemArrayPtr(MetaName.rage__fwGrassInstanceListDef__InstanceData, batch.Instances);
                        batches[i] = b;
                    }
                }

                instancedData.GrassInstanceList = mb.AddItemArrayPtr(MetaName.rage__fwGrassInstanceListDef, batches);
                mapdata.instancedData = instancedData;
            }
            else
            {
                mapdata.instancedData = new rage__fwInstancedMapData();
            }

            if ((LODLights != null) && (LODLights.direction != null))
            {
                var soa = new CLODLight();
                soa.direction = mb.AddItemArrayPtr(MetaName.FloatXYZ, LODLights.direction);
                soa.falloff = mb.AddFloatArrayPtr(LODLights.falloff);
                soa.falloffExponent = mb.AddFloatArrayPtr(LODLights.falloffExponent);
                soa.timeAndStateFlags = mb.AddUintArrayPtr(LODLights.timeAndStateFlags);
                soa.hash = mb.AddUintArrayPtr(LODLights.hash);
                soa.coneInnerAngle = mb.AddByteArrayPtr(LODLights.coneInnerAngle);
                soa.coneOuterAngleOrCapExt = mb.AddByteArrayPtr(LODLights.coneOuterAngleOrCapExt);
                soa.coronaIntensity = mb.AddByteArrayPtr(LODLights.coronaIntensity);
                mapdata.LODLightsSOA = soa;
            }
            else
            {
                mapdata.LODLightsSOA = new CLODLight();
            }
            if ((DistantLODLights != null) && (DistantLODLights.positions != null))
            {
                var soa = DistantLODLights.CDistantLODLight;
                soa.position = mb.AddItemArrayPtr(MetaName.FloatXYZ, DistantLODLights.positions);
                soa.RGBI = mb.AddUintArrayPtr(DistantLODLights.colours);
                mapdata.DistantLODLightsSOA = soa;
            }
            else
            {
                mapdata.DistantLODLightsSOA = new CDistantLODLight();
            }

            if ((CBoxOccluders != null) && (CBoxOccluders.Length > 0))
            {
                mapdata.boxOccluders = mb.AddItemArrayPtr(MetaName.BoxOccluder, CBoxOccluders);
            }
            else
            {
                mapdata.boxOccluders = new Array_Structure();
            }
            if ((OccludeModels != null) && (OccludeModels.Length > 0))
            {
                COccludeModels = new OccludeModel[OccludeModels.Length];
                for (int i = 0; i < OccludeModels.Length; i++)
                {
                    var model = OccludeModels[i];
                    model.BuildVertices();
                    model.BuildData();
                    var cocc = model._OccludeModel;
                    cocc.verts = mb.AddDataBlockPtr(model.Data, (MetaName)MetaStructureEntryDataType.UnsignedByte);
                    COccludeModels[i] = cocc;
                }
                mapdata.occludeModels = mb.AddItemArrayPtr(MetaName.OccludeModel, COccludeModels);
            }
            else
            {
                mapdata.occludeModels = new Array_Structure();
            }

            var block = new CBlockDesc();
            block.name = mb.AddStringPtr(Path.GetFileNameWithoutExtension(Name));
            block.exportedBy = mb.AddStringPtr("CodeWalker");
            block.time = mb.AddStringPtr(DateTime.UtcNow.ToString("dd MMMM yyyy HH:mm"));

            mapdata.block = block;

            string name = Path.GetFileNameWithoutExtension(Name);
            uint nameHash = JenkHash.GenHash(name);
            mapdata.name = new MetaHash(nameHash);

            mb.AddItem(MetaName.CMapData, mapdata);

            if ((GrassInstanceBatches != null) && (GrassInstanceBatches.Length > 0))
            {
                mb.AddStructureInfo(MetaName.rage__spdAABB);
                mb.AddStructureInfo(MetaName.rage__fwGrassInstanceListDef__InstanceData);
                mb.AddStructureInfo(MetaName.rage__fwGrassInstanceListDef);
            }
            mb.AddStructureInfo(MetaName.rage__fwInstancedMapData);
            mb.AddStructureInfo(MetaName.CLODLight);
            mb.AddStructureInfo(MetaName.CDistantLODLight);
            mb.AddStructureInfo(MetaName.CBlockDesc);
            mb.AddStructureInfo(MetaName.CMapData);
            if ((AllEntities != null) && (AllEntities.Length > 0))
            {
                mb.AddStructureInfo(MetaName.CEntityDef);
                mb.AddStructureInfo(MetaName.CMloInstanceDef);
                mb.AddEnumInfo(MetaName.rage__eLodType);
                mb.AddEnumInfo(MetaName.rage__ePriorityLevel);
            }
            if ((CTimeCycleModifiers != null) && (CTimeCycleModifiers.Length > 0))
            {
                mb.AddStructureInfo(MetaName.CTimeCycleModifier);
            }
            if ((CCarGens != null) && (CCarGens.Length > 0))
            {
                mb.AddStructureInfo(MetaName.CCarGen);
            }
            if ((LODLights != null) && (LODLights.direction != null))
            {
                mb.AddStructureInfo(MetaName.FloatXYZ);
            }
            if ((DistantLODLights != null) && (DistantLODLights.positions != null))
            {
                mb.AddStructureInfo(MetaName.FloatXYZ);
            }
            if ((CBoxOccluders != null) && (CBoxOccluders.Length > 0))
            {
                mb.AddStructureInfo(MetaName.BoxOccluder);
            }
            if ((COccludeModels != null) && (COccludeModels.Length > 0))
            {
                mb.AddStructureInfo(MetaName.OccludeModel);
            }

            Meta meta = mb.GetMeta();

            byte[] data = ResourceBuilder.Build(meta, 2);

            return data;
        }

        private void LogSaveWarning(string w)
        {
            if (SaveWarnings == null) SaveWarnings = new List<string>();
            SaveWarnings.Add(w);
        }

        public void EnsureChildYmaps(GameFileCache gfc)
        {
            if (ChildYmaps == null)
            {
                var node = gfc.GetMapNode(RpfFileEntry.ShortNameHash);
                if ((node != null) && (node.Children != null) && (node.Children.Length > 0))
                {
                    ChildYmaps = new YmapFile[node.Children.Length];
                    for (int i = 0; i < ChildYmaps.Length; i++)
                    {
                        var chash = node.Children[i].Name;
                        ChildYmaps[i] = gfc.GetYmap(chash);
                        if (ChildYmaps[i] == null)
                        {
                        }
                    }
                }
            }

            bool needupd = false;
            if (ChildYmaps != null)
            {
                for (int i = 0; i < ChildYmaps.Length; i++)
                {
                    var cmap = ChildYmaps[i];
                    if (cmap == null) continue;
                    if (!cmap.Loaded)
                    {
                        cmap = gfc.GetYmap(cmap.Key.Hash);
                        ChildYmaps[i] = cmap;
                    }
                    if ((cmap.Loaded) && (!cmap.MergedWithParent))
                    {
                        needupd = true;
                    }
                }
            }

            if ((ChildYmaps != null) && needupd)
            {
                List<YmapEntityDef> newroots = new List<YmapEntityDef>(RootEntities);
                for (int i = 0; i < ChildYmaps.Length; i++)
                {
                    var cmap = ChildYmaps[i];
                    if (cmap == null) continue;
                    if ((cmap.Loaded) && (!cmap.MergedWithParent))
                    {
                        cmap.MergedWithParent = true;
                        if (cmap.RootEntities != null)
                        {
                            foreach (var rcent in cmap.RootEntities)
                            {
                                int pind = rcent._CEntityDef.parentIndex;
                                if (pind < 0)
                                {
                                    if (rcent._CEntityDef.lodLevel != rage__eLodType.LODTYPES_DEPTH_ORPHANHD)
                                    {
                                    }
                                }
                                if ((pind >= 0) && (pind < AllEntities.Length) && !rcent.LodInParentYmap)
                                {
                                    var pentity = AllEntities[pind];
                                    pentity.AddChild(rcent);
                                }
                                else
                                {
                                }
                            }
                        }
                    }
                }
                if (AllEntities != null)
                {
                    for (int i = 0; i < AllEntities.Length; i++)
                    {
                        AllEntities[i].ChildListToMergedArray();
                    }
                }

                RootEntities = newroots.ToArray();
            }

        }

        public void ConnectToParent(YmapFile pymap)
        {
            Parent = pymap;
            if (RootEntities != null)
            {
                for (int i = 0; i < RootEntities.Length; i++)
                {
                    var ent = RootEntities[i];
                    int pind = ent._CEntityDef.parentIndex;
                    if (pind >= 0)
                    {
                        YmapEntityDef p = null;
                        if ((pymap != null) && (pymap.AllEntities != null))
                        {
                            if ((pind < pymap.AllEntities.Length))
                            {
                                p = pymap.AllEntities[pind];
                                ent.Parent = p;
                                ent.ParentName = p._CEntityDef.archetypeName;
                            }
                        }
                        else
                        { }
                    }
                }
            }
            if (LODLights != null)
            {
                if (Parent?.DistantLODLights != null)
                {
                    LODLights.Init(Parent.DistantLODLights);
                }
                else
                { }
            }
        }

        public void AddEntity(YmapEntityDef ent)
        {

            List<YmapEntityDef> allents = new List<YmapEntityDef>();
            if (AllEntities != null) allents.AddRange(AllEntities);
            ent.Index = allents.Count;
            ent.Ymap = this;
            allents.Add(ent);
            AllEntities = allents.ToArray();

            if ((ent.Parent == null) || (ent.Parent.Ymap != this))
            {

                List<YmapEntityDef> rootents = new List<YmapEntityDef>();
                if (RootEntities != null) rootents.AddRange(RootEntities);
                rootents.Add(ent);
                RootEntities = rootents.ToArray();
            }

            HasChanged = true;
            LodManagerUpdate = true;
        }

        public bool RemoveEntity(YmapEntityDef ent)
        {
            if (ent == null) return false;

            var res = true;

            int idx = ent.Index;
            List<YmapEntityDef> newAllEntities = new List<YmapEntityDef>();
            List<YmapEntityDef> newRootEntities = new List<YmapEntityDef>();

            for (int i = 0; i < AllEntities.Length; i++)
            {
                var oent = AllEntities[i];
                oent.Index = newAllEntities.Count;
                if (oent != ent) newAllEntities.Add(oent);
                else if (i != idx)
                {
                    res = false;
                }
            }
            for (int i = 0; i < RootEntities.Length; i++)
            {
                var oent = RootEntities[i];
                if (oent != ent) newRootEntities.Add(oent);
            }

            if ((AllEntities.Length == newAllEntities.Count) || (RootEntities.Length == newRootEntities.Count))
            {
                res = false;
            }

            LodManagerOldEntities = AllEntities;
            AllEntities = newAllEntities.ToArray();
            RootEntities = newRootEntities.ToArray();

            HasChanged = true;
            LodManagerUpdate = true;

            return res;
        }

        public void AddCarGen(YmapCarGen cargen)
        {
            List<YmapCarGen> cargens = new List<YmapCarGen>();
            if (CarGenerators != null) cargens.AddRange(CarGenerators);
            cargen.Ymap = this;
            cargens.Add(cargen);
            CarGenerators = cargens.ToArray();

            HasChanged = true;
        }

        public bool RemoveCarGen(YmapCarGen cargen)
        {
            if (cargen == null) return false;

            List<YmapCarGen> newcargens = new List<YmapCarGen>();

            if (CarGenerators != null)
            {
                for (int i = 0; i < CarGenerators.Length; i++)
                {
                    var cg = CarGenerators[i];
                    if (cg != cargen)
                    {
                        newcargens.Add(cg);
                    }
                }
                if (newcargens.Count == CarGenerators.Length)
                {
                    return false;
                }
            }

            CarGenerators = newcargens.ToArray();

            HasChanged = true;

            return true;
        }

        public void AddLodLight(YmapLODLight lodlight)
        {
            if (LODLights == null)
            {
                LODLights = new YmapLODLights();
                LODLights.Ymap = this;
            }
            List<YmapLODLight> lodlights = new List<YmapLODLight>();
            if (LODLights?.LodLights != null) lodlights.AddRange(LODLights.LodLights);
            lodlight.LodLights = this.LODLights;
            lodlight.Index = lodlights.Count;
            lodlights.Add(lodlight);
            LODLights.LodLights = lodlights.ToArray();

            HasChanged = true;

            if (Parent?.DistantLODLights != null)
            {
                Parent.DistantLODLights.RebuildFromLodLights(LODLights.LodLights);
                Parent.HasChanged = true;
            }
        }

        public bool RemoveLodLight(YmapLODLight lodlight)
        {
            if (lodlight == null) return false;

            List<YmapLODLight> newlodlights = new List<YmapLODLight>();

            var lodlights = LODLights?.LodLights;
            if (lodlights != null)
            {
                for (int i = 0; i < lodlights.Length; i++)
                {
                    var ll = lodlights[i];
                    if (ll != lodlight)
                    {
                        newlodlights.Add(ll);
                    }
                }
                if (newlodlights.Count == lodlights.Length)
                {
                    return false;
                }
            }

            for (int i = 0; i < newlodlights.Count; i++)
            {
                newlodlights[i].Index = i;
            }

            LODLights.LodLights = newlodlights.ToArray();

            HasChanged = true;

            if (Parent?.DistantLODLights != null)
            {
                Parent.DistantLODLights.RebuildFromLodLights(LODLights.LodLights);
                Parent.HasChanged = true;
            }

            return true;
        }

        public void AddBoxOccluder(YmapBoxOccluder box)
        {
            if (box == null) return;
            var boxes = new List<YmapBoxOccluder>();
            if (BoxOccluders != null) boxes.AddRange(BoxOccluders);
            box.Ymap = this;
            box.Index = boxes.Count;
            boxes.Add(box);
            BoxOccluders = boxes.ToArray();

            HasChanged = true;
        }

        public bool RemoveBoxOccluder(YmapBoxOccluder box)
        {
            if (box == null) return false;
            var newboxes = new List<YmapBoxOccluder>();
            if (BoxOccluders != null)
            {
                foreach (var oldbox in BoxOccluders)
                {
                    if (oldbox != box)
                    {
                        oldbox.Index = newboxes.Count;
                        newboxes.Add(oldbox);
                    }
                }
                if (newboxes.Count == BoxOccluders.Length)
                {
                    return false;
                }
            }

            BoxOccluders = newboxes.ToArray();

            HasChanged = true;

            return true;
        }

        public void AddOccludeModel(YmapOccludeModel model)
        {
            if (model == null) return;
            var models = new List<YmapOccludeModel>();
            if (OccludeModels != null) models.AddRange(OccludeModels);
            model.Ymap = this;
            models.Add(model);
            OccludeModels = models.ToArray();

            HasChanged = true;
        }

        public bool RemoveOccludeModel(YmapOccludeModel model)
        {
            if (model == null) return false;
            var newmodels = new List<YmapOccludeModel>();
            if (OccludeModels != null)
            {
                foreach (var oldmodel in OccludeModels)
                {
                    if (oldmodel != model)
                    {
                        oldmodel.Index = newmodels.Count;
                        newmodels.Add(oldmodel);
                    }
                }
                if (newmodels.Count == OccludeModels.Length)
                {
                    return false;
                }
            }

            OccludeModels = newmodels.ToArray();

            HasChanged = true;

            return true;
        }

        public void AddOccludeModelTriangle(YmapOccludeModelTriangle tri)
        {
            if (tri == null) return;
            if (tri.Model == null) return;

            var tris = tri.Model.Triangles.ToList();
            tri.Index = tris.Count;
            tris.Add(tri);
            tri.Model.Triangles = tris.ToArray();

            HasChanged = true;
        }

        public bool RemoveOccludeModelTriangle(YmapOccludeModelTriangle tri)
        {
            if (tri == null) return false;
            if (tri.Model == null) return false;

            var newtris = new List<YmapOccludeModelTriangle>();
            if (tri.Model.Triangles != null)
            {
                foreach (var oldtri in tri.Model.Triangles)
                {
                    if (oldtri != tri)
                    {
                        oldtri.Index = newtris.Count;
                        newtris.Add(oldtri);
                    }
                }
            }
            tri.Model.Triangles = newtris.ToArray();

            HasChanged = true;

            return true;
        }

        public void AddGrassBatch(YmapGrassInstanceBatch newbatch)
        {
            List<YmapGrassInstanceBatch> batches = new List<YmapGrassInstanceBatch>();
            if (GrassInstanceBatches != null) batches.AddRange(GrassInstanceBatches);
            newbatch.Ymap = this;
            batches.Add(newbatch);
            GrassInstanceBatches = batches.ToArray();

            HasChanged = true;
            UpdateGrassPhysDict(true);
        }

        public bool RemoveGrassBatch(YmapGrassInstanceBatch batch)
        {
            if (batch == null) return false;

            List<YmapGrassInstanceBatch> batches = new List<YmapGrassInstanceBatch>();

            if (GrassInstanceBatches != null)
            {
                for (int i = 0; i < GrassInstanceBatches.Length; i++)
                {
                    var gb = GrassInstanceBatches[i];
                    if (gb != batch)
                    {
                        batches.Add(gb);
                    }
                }
                if (batches.Count == GrassInstanceBatches.Length)
                {
                    return false;
                }
            }

            if (batches.Count <= 0)
            {
                UpdateGrassPhysDict(false);
            }

            GrassInstanceBatches = batches.ToArray();

            HasChanged = true;

            return true;
        }

        public void SetName(string newname)
        {
            var newnamel = newname.ToLowerInvariant();
            var newnamex = newname + ".ymap";
            var newnamexl = newname.ToLowerInvariant();
            var newhash = JenkHash.GenHash(newnamel);
            JenkIndex.Ensure(newnamel);
            if (RpfFileEntry != null)
            {
                RpfFileEntry.Name = newnamex;
                RpfFileEntry.NameLower = newnamexl;
                RpfFileEntry.NameHash = JenkHash.GenHash(newnamexl);
                RpfFileEntry.ShortNameHash = newhash;
            }
            Name = newnamex;
            _CMapData.name = newhash;
        }
        public void SetFilePath(string filepath)
        {
            FilePath = filepath.ToLowerInvariant();
            var newname = Path.GetFileNameWithoutExtension(filepath);
            SetName(newname);
        }

        public bool CalcFlags()
        {
            uint flags = 0;
            uint contentFlags = 0;

            if (AllEntities != null)
            {
                foreach (var yent in AllEntities)
                {
                    switch (yent._CEntityDef.lodLevel)
                    {
                        case rage__eLodType.LODTYPES_DEPTH_ORPHANHD:
                        case rage__eLodType.LODTYPES_DEPTH_HD:
                            contentFlags = SetBit(contentFlags, 0);
                            break;
                        case rage__eLodType.LODTYPES_DEPTH_LOD:
                            contentFlags = SetBit(contentFlags, 1);
                            flags = SetBit(flags, 1);
                            break;
                        case rage__eLodType.LODTYPES_DEPTH_SLOD1:
                            contentFlags = SetBit(contentFlags, 4);
                            flags = SetBit(flags, 1);
                            break;
                        case rage__eLodType.LODTYPES_DEPTH_SLOD2:
                        case rage__eLodType.LODTYPES_DEPTH_SLOD3:
                        case rage__eLodType.LODTYPES_DEPTH_SLOD4:
                            contentFlags = SetBit(contentFlags, 2);
                            contentFlags = SetBit(contentFlags, 4);
                            flags = SetBit(flags, 1);
                            break;
                    }
                    if (yent.MloInstance != null)
                    {
                        contentFlags = SetBit(contentFlags, 3);
                    }
                }
            }

            if ((CMloInstanceDefs != null) && (CMloInstanceDefs.Length > 0))
            {
                contentFlags = SetBit(contentFlags, 3);
            }
            if ((physicsDictionaries != null) && (physicsDictionaries.Length > 0))
            {
                contentFlags = SetBit(contentFlags, 6);
            }
            if ((GrassInstanceBatches != null) && (GrassInstanceBatches.Length > 0))
            {
                contentFlags = SetBit(contentFlags, 10);
            }
            if ((LODLights != null) && ((LODLights.direction?.Length ?? 0) > 0))
            {
                contentFlags = SetBit(contentFlags, 7);
            }
            if ((DistantLODLights != null) && ((DistantLODLights.positions?.Length ?? 0) > 0))
            {
                flags = SetBit(flags, 1);
                contentFlags = SetBit(contentFlags, 8);
            }
            if ((BoxOccluders != null) || (OccludeModels != null))
            {
                contentFlags = SetBit(contentFlags, 5);
            }

            bool change = false;
            if (_CMapData.flags != flags)
            {
                _CMapData.flags = flags;
                change = true;
            }
            if (_CMapData.contentFlags != contentFlags)
            {
                _CMapData.contentFlags = contentFlags;
                change = true;
            }
            return change;
        }

        public bool CalcExtents()
        {
            Vector3 emin = new Vector3(float.MaxValue);
            Vector3 emax = new Vector3(float.MinValue);
            Vector3 smin = new Vector3(float.MaxValue);
            Vector3 smax = new Vector3(float.MinValue);
            Vector3[] c = new Vector3[8];
            Vector3[] s = new Vector3[8];

            if (AllEntities != null)
            {
                for (int i = 0; i < AllEntities.Length; i++)
                {
                    var ent = AllEntities[i];
                    var arch = ent.Archetype;
                    var ori = ent.Orientation;
                    float loddist = ent._CEntityDef.lodDist;

                    Vector3 bbmin = ent.Position - ent.BSRadius;
                    Vector3 bbmax = ent.Position + ent.BSRadius;
                    Vector3 sbmin = bbmin - loddist;
                    Vector3 sbmax = bbmax + loddist;
                    if (arch != null)
                    {
                        if (loddist <= 0.0f)
                        {
                            loddist = arch.LodDist;
                        }

                        Vector3 abmin = arch.BBMin * ent.Scale;
                        Vector3 abmax = arch.BBMax * ent.Scale;
                        c[0] = abmin;
                        c[1] = new Vector3(abmin.X, abmin.Y, abmax.Z);
                        c[2] = new Vector3(abmin.X, abmax.Y, abmin.Z);
                        c[3] = new Vector3(abmin.X, abmax.Y, abmax.Z);
                        c[4] = new Vector3(abmax.X, abmin.Y, abmin.Z);
                        c[5] = new Vector3(abmax.X, abmin.Y, abmax.Z);
                        c[6] = new Vector3(abmax.X, abmax.Y, abmin.Z);
                        c[7] = abmax;

                        abmin = arch.BBMin * ent.Scale - loddist;
                        abmax = arch.BBMax * ent.Scale + loddist;
                        s[0] = abmin;
                        s[1] = new Vector3(abmin.X, abmin.Y, abmax.Z);
                        s[2] = new Vector3(abmin.X, abmax.Y, abmin.Z);
                        s[3] = new Vector3(abmin.X, abmax.Y, abmax.Z);
                        s[4] = new Vector3(abmax.X, abmin.Y, abmin.Z);
                        s[5] = new Vector3(abmax.X, abmin.Y, abmax.Z);
                        s[6] = new Vector3(abmax.X, abmax.Y, abmin.Z);
                        s[7] = abmax;

                        bbmin = new Vector3(float.MaxValue);
                        bbmax = new Vector3(float.MinValue);
                        sbmin = new Vector3(float.MaxValue);
                        sbmax = new Vector3(float.MinValue);
                        for (int j = 0; j < 8; j++)
                        {
                            Vector3 corn = ori.Multiply(c[j]) + ent.Position;
                            bbmin = Vector3.Min(bbmin, corn);
                            bbmax = Vector3.Max(bbmax, corn);

                            corn = ori.Multiply(s[j]) + ent.Position;
                            sbmin = Vector3.Min(sbmin, corn);
                            sbmax = Vector3.Max(sbmax, corn);
                        }
                    }

                    emin = Vector3.Min(emin, bbmin);
                    emax = Vector3.Max(emax, bbmax);
                    smin = Vector3.Min(smin, sbmin);
                    smax = Vector3.Max(smax, sbmax);
                }
            }

            if (GrassInstanceBatches != null)
            {
                foreach (var batch in GrassInstanceBatches)
                {
                    emin = Vector3.Min(emin, batch.AABBMin);
                    emax = Vector3.Max(emax, batch.AABBMax);
                    smin = Vector3.Min(smin, (batch.AABBMin - batch.Batch.lodDist));
                    smax = Vector3.Max(smax, (batch.AABBMax + batch.Batch.lodDist));
                }
            }

            if (CarGenerators != null)
            {
                foreach (var cargen in CarGenerators)
                {
                    var len = cargen._CCarGen.perpendicularLength;
                    emin = Vector3.Min(emin, cargen.Position - len);
                    emax = Vector3.Max(emax, cargen.Position + len);
                    smin = Vector3.Min(smin, cargen.Position - len*2.0f);
                    smax = Vector3.Max(smax, cargen.Position + len*2.0f);
                }
            }

            if (LODLights != null)
            {
                LODLights.CalcBB();
                emin = Vector3.Min(emin, LODLights.BBMin - 20.0f);
                emax = Vector3.Max(emax, LODLights.BBMax + 20.0f);
                smin = Vector3.Min(smin, (LODLights.BBMin - 950.0f));
                smax = Vector3.Max(smax, (LODLights.BBMax + 950.0f));
            }

            if (DistantLODLights != null)
            {
                DistantLODLights.CalcBB();
                emin = Vector3.Min(emin, DistantLODLights.BBMin - 20.0f);
                emax = Vector3.Max(emax, DistantLODLights.BBMax + 20.0f);
                smin = Vector3.Min(smin, (DistantLODLights.BBMin - 3000.0f));
                smax = Vector3.Max(smax, (DistantLODLights.BBMax + 3000.0f));
            }

            if (BoxOccluders != null)
            {
                foreach (var box in BoxOccluders)
                {
                    var siz = box.Size.Length() * 0.5f;
                    emin = Vector3.Min(emin, box.Position - siz);
                    emax = Vector3.Max(emax, box.Position + siz);
                    smin = Vector3.Min(smin, box.Position - siz);
                    smax = Vector3.Max(smax, box.Position + siz);
                }
            }

            if (OccludeModels != null)
            {
                foreach (var model in OccludeModels)
                {
                    emin = Vector3.Min(emin, (model.BVH?.Box.Minimum ?? model._OccludeModel.bmin));
                    emax = Vector3.Max(emax, (model.BVH?.Box.Maximum ?? model._OccludeModel.bmax));
                    smin = Vector3.Min(smin, (model.BVH?.Box.Minimum ?? model._OccludeModel.bmin));
                    smax = Vector3.Max(smax, (model.BVH?.Box.Maximum ?? model._OccludeModel.bmax));
                }
            }

            bool change = false;
            if (_CMapData.entitiesExtentsMin != emin)
            {
                _CMapData.entitiesExtentsMin = emin;
                change = true;
            }
            if (_CMapData.entitiesExtentsMax != emax)
            {
                _CMapData.entitiesExtentsMax = emax;
                change = true;
            }
            if (_CMapData.streamingExtentsMin != smin)
            {
                _CMapData.streamingExtentsMin = smin;
                change = true;
            }
            if (_CMapData.streamingExtentsMax != smax)
            {
                _CMapData.streamingExtentsMax = smax;
                change = true;
            }
            return change;
        }

        private void UpdateGrassPhysDict(bool add)
        {
            var physDict = physicsDictionaries?.ToList() ?? new List<MetaHash>();
            var vproc1 = JenkHash.GenHash("v_proc1");
            var vproc2 = JenkHash.GenHash("v_proc2");
            var change = false;
            if (!physDict.Contains(vproc1))
            {
                change = true;
                if (add) physDict.Add(vproc1);
                else physDict.Remove(vproc1);
            }
            if (!physDict.Contains(vproc2))
            {
                change = true;
                if (add) physDict.Add(vproc2);
                else physDict.Remove(vproc2);
            }
            if (change) physicsDictionaries = physDict.ToArray();
        }

        public void InitYmapEntityArchetypes(GameFileCache gfc)
        {
            if (AllEntities != null)
            {
                for (int i = 0; i < AllEntities.Length; i++)
                {
                    var ent = AllEntities[i];
                    var arch = gfc.GetArchetype(ent._CEntityDef.archetypeName);
                    ent.SetArchetype(arch);
                    if (ent.IsMlo) ent.MloInstance.InitYmapEntityArchetypes(gfc);
                }
            }
            if (GrassInstanceBatches != null)
            {
                for (int i = 0; i < GrassInstanceBatches.Length; i++)
                {
                    var batch = GrassInstanceBatches[i];
                    batch.Archetype = gfc.GetArchetype(batch.Batch.archetypeName);
                }
            }

            if (TimeCycleModifiers != null)
            {
                for (int i = 0; i < TimeCycleModifiers.Length; i++)
                {
                    var tcm = TimeCycleModifiers[i];
                    World.TimecycleMod wtcm;
                    if (gfc.TimeCycleModsDict.TryGetValue(tcm.CTimeCycleModifier.name.Hash, out wtcm))
                    {
                        tcm.TimeCycleModData = wtcm;
                    }
                }
            }

        }

        private static uint SetBit(uint value, int bit)
        {
            return (value | (1u << bit));
        }

    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapEntityDef
    {
        public Archetype Archetype { get; set; }
        public Vector3 BBMin;
        public Vector3 BBMax;
        public Vector3 BBCenter;
        public Vector3 BBExtent;
        public Vector3 BSCenter;
        public float BSRadius;
        public float LodDist;
        public float ChildLodDist;

        public CEntityDef _CEntityDef;
        public CEntityDef CEntityDef { get { return _CEntityDef; } set { _CEntityDef = value; } }
        private List<YmapEntityDef> ChildList { get; set; }
        public YmapEntityDef[] Children { get; set; }
        public YmapEntityDef[] ChildrenMerged;
        public Vector3 Position { get; set; }
        public Quaternion Orientation { get; set; }
        public Vector3 Scale { get; set; }
        public bool IsMlo { get; set; }
        public MloInstanceData MloInstance { get; set; }
        public YmapEntityDef MloParent { get; set; }
        public MloInstanceEntitySet MloEntitySet { get; set; }
        public Vector3 MloRefPosition { get; set; }
        public Quaternion MloRefOrientation { get; set; }
        public MetaWrapper[] Extensions { get; set; }

        public int Index { get; set; }
        public float Distance { get; set; }
        public bool IsVisible;
        public bool ChildrenVisible;
        public bool ChildrenRendered;
        public YmapEntityDef Parent { get; set; }
        public MetaHash ParentName { get; set; }

        public YmapFile Ymap { get; set; }

        public Vector3 PivotPosition = Vector3.Zero;
        public Quaternion PivotOrientation = Quaternion.Identity;
        public Vector3 WidgetPosition = Vector3.Zero;
        public Quaternion WidgetOrientation = Quaternion.Identity;

        public uint EntityHash { get; set; } = 0;

        public LinkedList<YmapEntityDef> LodManagerChildren = null;
        public object LodManagerRenderable = null;

        public LightInstance[] Lights { get; set; }

        public bool LodInParentYmap { get { return ((_CEntityDef.flags >> 3) & 1) > 0; } }

        public string Name
        {
            get
            {
                return _CEntityDef.archetypeName.ToString();
            }
        }

        public YmapEntityDef()
        {
            Scale = Vector3.One;
            Position = Vector3.One;
            Orientation = Quaternion.Identity;
        }
        public YmapEntityDef(YmapFile ymap, int index, ref CEntityDef def)
        {
            Ymap = ymap;
            Index = index;
            CEntityDef = def;
            Scale = new Vector3(new Vector2(_CEntityDef.scaleXY), _CEntityDef.scaleZ);
            Position = _CEntityDef.position;
            Orientation = new Quaternion(_CEntityDef.rotation);
            if (Orientation != Quaternion.Identity)
            {
                Orientation = Quaternion.Invert(Orientation);
            }
            IsMlo = false;

            UpdateWidgetPosition();
            UpdateWidgetOrientation();
            UpdateEntityHash();
        }

        public YmapEntityDef(YmapFile ymap, int index, ref CMloInstanceDef mlo)
        {
            Ymap = ymap;
            Index = index;
            CEntityDef = mlo.CEntityDef;
            Scale = new Vector3(new Vector2(_CEntityDef.scaleXY), _CEntityDef.scaleZ);
            Position = _CEntityDef.position;
            Orientation = new Quaternion(_CEntityDef.rotation);
            IsMlo = true;

            MloInstance = new MloInstanceData(this, null);
            MloInstance.Instance = mlo;

            UpdateWidgetPosition();
            UpdateWidgetOrientation();
            UpdateEntityHash();
        }

        public YmapEntityDef(YmapEntityDef mloParent, MCEntityDef ent, int index)
        {
            Ymap = null;
            Index = index;
            CEntityDef = ent._Data;
            Scale = new Vector3(new Vector2(_CEntityDef.scaleXY), _CEntityDef.scaleZ);
            MloRefPosition = _CEntityDef.position;
            MloRefOrientation = new Quaternion(_CEntityDef.rotation);
            if (MloRefOrientation != Quaternion.Identity)
            {
                MloRefOrientation = Quaternion.Invert(MloRefOrientation);
            }
            IsMlo = false;

            Extensions = ent.Extensions;
            MloParent = mloParent;
            Position = mloParent.Position + mloParent.Orientation.Multiply(MloRefPosition);
            Orientation = Quaternion.Multiply(mloParent.Orientation, MloRefOrientation);

            UpdateWidgetPosition();
            UpdateWidgetOrientation();
            UpdateEntityHash();
        }

        public void SetArchetype(Archetype arch)
        {
            Archetype = arch;
            if (Archetype != null)
            {
                UpdateBB();

                if (Archetype.Type == MetaName.CMloArchetypeDef)
                {
                    var mloa = Archetype as MloArchetype;
                    var mloi = MloInstance;
                    MloInstance = new MloInstanceData(this, mloa);
                    if (mloi != null)
                    {
                        MloInstance.Instance = mloi.Instance;
                        MloInstance.defaultEntitySets = mloi.defaultEntitySets;
                    }
                    if (mloa != null)
                    {
                        if (!IsMlo)
                        {
                            IsMlo = true;
                            List<YmapEntityDef> mloEntities = Ymap.MloEntities?.ToList() ?? new List<YmapEntityDef>();
                            mloEntities.Add(this);
                            Ymap.MloEntities = mloEntities.ToArray();
                        }

                        MloInstance.CreateYmapEntities();
                    }

                    if (BSRadius == 0.0f)
                    {
                        BSRadius = LodDist;
                    }
                    if (BBMin == BBMax)
                    {
                        BBMin = Position - BSRadius;
                        BBMax = Position + BSRadius;
                        BBCenter = (BBMax + BBMin) * 0.5f;
                        BBExtent = (BBMax - BBMin) * 0.5f;
                    }
                }
                else if (IsMlo)
                {
                    IsMlo = false;
                    MloInstance = null;

                    if (Ymap.MloEntities != null)
                    {
                        List<YmapEntityDef> mloEntities = Ymap.MloEntities.ToList();
                        if (mloEntities.Remove(this))
                        {
                            Ymap.MloEntities = mloEntities.ToArray();
                        }
                    }
                }
            }
        }

        public void SetPosition(Vector3 pos)
        {
            Position = pos;
            if (MloParent != null)
            {
                _CEntityDef.position = Quaternion.Normalize(Quaternion.Invert(MloParent.Orientation)).Multiply(pos - MloParent.Position);
                MloRefPosition = _CEntityDef.position;
                UpdateBB();
                UpdateMloArchetype();
            }
            else
            {
                _CEntityDef.position = pos;
                UpdateBB();
            }

            if (MloInstance != null)
            {
                MloInstance.SetPosition(Position);
                MloInstance.UpdateEntities();
            }

            UpdateEntityHash();
            UpdateWidgetPosition();
        }

        public void SetPositionRaw(Vector3 pos)
        {

            _CEntityDef.position = pos;

            if (MloParent != null)
            {
                MloRefPosition = pos;
                Position = MloParent.Position + MloParent.Orientation.Multiply(MloRefPosition);
                UpdateBB();
                UpdateMloArchetype();
            }
            else
            {
                Position = pos;
                UpdateBB();
            }

            if (MloInstance != null)
            {
                MloInstance.SetPosition(Position);
                MloInstance.UpdateEntities();
            }

            UpdateEntityHash();
            UpdateWidgetPosition();
        }

        private void UpdateBB()
        {
            if (Archetype != null)
            {
                BSCenter = Orientation.Multiply(Archetype.BSCenter) * Scale;
                BSRadius = Archetype.BSRadius * Math.Max(Scale.X, Scale.Z);
                if (Orientation == Quaternion.Identity)
                {
                    BBMin = (Vector3.Min(Archetype.BBMin, Archetype.BBMax) * Scale) + Position;
                    BBMax = (Vector3.Max(Archetype.BBMin, Archetype.BBMax) * Scale) + Position;
                    BBCenter = (BBMax + BBMin) * 0.5f;
                    BBExtent = (BBMax - BBMin) * 0.5f;
                }
                else
                {
                    var mat = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, Scale, Vector3.Zero, Orientation, Position);
                    var matabs = mat;
                    matabs.Column1 = mat.Column1.Abs();
                    matabs.Column2 = mat.Column2.Abs();
                    matabs.Column3 = mat.Column3.Abs();
                    matabs.Column4 = mat.Column4.Abs();
                    var bbcenter = (Archetype.BBMax + Archetype.BBMin) * 0.5f;
                    var bbextent = (Archetype.BBMax - Archetype.BBMin) * 0.5f;
                    var ncenter = Vector3.TransformCoordinate(bbcenter, mat);
                    var nextent = Vector3.TransformNormal(bbextent, matabs).Abs();
                    BBCenter = ncenter;
                    BBExtent = nextent;
                    BBMin = ncenter - nextent;
                    BBMax = ncenter + nextent;
                }
                LodDist = _CEntityDef.lodDist;
                if (LodDist <= 0)
                {
                    LodDist = Archetype.LodDist;
                }
                ChildLodDist = _CEntityDef.childLodDist;
                if (ChildLodDist < 0)
                {
                    ChildLodDist = LodDist * 0.5f;
                }
            }
        }

        public void UpdateEntityHash()
        {
            uint xhash = (uint)(Position.X * 100);
            uint yhash = (uint)(Position.Y * 100);
            uint zhash = (uint)(Position.Z * 100);
            EntityHash = _CEntityDef.archetypeName.Hash ^ xhash ^ yhash ^ zhash & 0xffffffff;
        }

        public void SetOrientation(Quaternion ori, bool inverse = false)
        {
            if (MloParent != null)
            {
                var mloInv = Quaternion.Normalize(Quaternion.Invert(MloParent.Orientation));
                Quaternion rel = Quaternion.Normalize(Quaternion.Multiply(mloInv, ori));
                Quaternion inv = Quaternion.Normalize(Quaternion.Invert(rel));
                Orientation = ori;
                _CEntityDef.rotation = inv.ToVector4();
            }
            else
            {
                Orientation = inverse ? Quaternion.Normalize(Quaternion.Invert(ori)) : ori;
                if (MloInstance != null)
                {
                    _CEntityDef.rotation = Orientation.ToVector4();
                }
                else
                {
                    Quaternion inv = inverse ? ori : Quaternion.Normalize(Quaternion.Invert(ori));
                    _CEntityDef.rotation = inv.ToVector4();
                }
            }

            if (MloInstance != null)
            {
                MloInstance.SetOrientation(ori);
                MloInstance.UpdateEntities();
            }

            UpdateBB();
            UpdateWidgetPosition();
            UpdateWidgetOrientation();
        }

        public void SetOrientationRaw(Quaternion ori)
        {

            _CEntityDef.rotation = ori.ToVector4();

            if (MloParent != null)
            {
                MloRefOrientation = ori;
                if (MloRefOrientation != Quaternion.Identity)
                {
                    MloRefOrientation = Quaternion.Invert(MloRefOrientation);
                }
                Orientation = Quaternion.Multiply(MloParent.Orientation, MloRefOrientation);
            }
            else
            {
                Orientation = ori;
                if (MloInstance == null)
                {
                    if (Orientation != Quaternion.Identity)
                    {
                        Orientation = Quaternion.Invert(Orientation);
                    }
                }
            }

            if (MloInstance != null)
            {
                MloInstance.SetOrientation(Orientation);
                MloInstance.UpdateEntities();
            }

            UpdateBB();
            UpdateWidgetPosition();
            UpdateWidgetOrientation();
        }

        public void SetScale(Vector3 s)
        {
            Scale = new Vector3(s.X, s.X, s.Z);
            _CEntityDef.scaleXY = s.X;
            _CEntityDef.scaleZ = s.Z;

            MloInstanceData mloInstance = MloParent?.MloInstance;
            if (mloInstance != null)
            {
                var mcEntity = mloInstance.TryGetArchetypeEntity(this);
                if (mcEntity != null)
                {
                    mcEntity._Data.scaleXY = s.X;
                    mcEntity._Data.scaleZ = s.Z;
                }
            }
            if (Archetype != null)
            {
                float smax = Math.Max(Scale.X, Scale.Z);
                BSRadius = Archetype.BSRadius * smax;
            }

            SetPosition(Position);
        }

        private void UpdateMloArchetype()
        {
            if (!(MloParent.Archetype is MloArchetype mloArchetype)) return;

            MCEntityDef entity = null;
            if ((MloEntitySet?.Entities != null) && (MloEntitySet?.EntitySet?.Entities != null))
            {
                var idx = MloEntitySet.Entities.IndexOf(this);
                if ((idx < 0) || (idx >= MloEntitySet.EntitySet.Entities.Length)) return;
                entity = MloEntitySet.EntitySet.Entities[idx];
            }
            else
            {
                if (Index >= mloArchetype.entities.Length) return;
                entity = mloArchetype.entities[Index];
            }

            entity._Data.position = _CEntityDef.position;
            entity._Data.rotation = _CEntityDef.rotation;
        }

        public void SetPivotPosition(Vector3 pos)
        {
            PivotPosition = pos;

            UpdateWidgetPosition();
        }

        public void SetPivotOrientation(Quaternion ori)
        {
            PivotOrientation = ori;

            UpdateWidgetOrientation();
        }

        public void SetPositionFromWidget(Vector3 pos)
        {
            SetPosition(pos - Orientation.Multiply(PivotPosition));
        }
        public void SetOrientationFromWidget(Quaternion ori)
        {
            var newori = Quaternion.Normalize(Quaternion.Multiply(ori, Quaternion.Invert(PivotOrientation)));
            var newpos = WidgetPosition - newori.Multiply(PivotPosition);
            SetOrientation(newori);
            SetPosition(newpos);
        }
        public void SetPivotPositionFromWidget(Vector3 pos)
        {
            var orinv = Quaternion.Invert(Orientation);
            SetPivotPosition(orinv.Multiply(pos - Position));
        }
        public void SetPivotOrientationFromWidget(Quaternion ori)
        {
            var orinv = Quaternion.Invert(Orientation);
            SetPivotOrientation(Quaternion.Multiply(orinv, ori));
        }

        public void UpdateWidgetPosition()
        {
            WidgetPosition = Position + Orientation.Multiply(PivotPosition);
        }
        public void UpdateWidgetOrientation()
        {
            WidgetOrientation = Quaternion.Multiply(Orientation, PivotOrientation);
        }

        public void AddChild(YmapEntityDef c)
        {
            if (ChildList == null)
            {
                ChildList = new List<YmapEntityDef>();
            }
            c.Parent = this;
            c.ParentName = _CEntityDef.archetypeName;

            ChildList.Add(c);
        }

        public void ChildListToArray()
        {
            if (ChildList == null) return;
            Children = ChildList.ToArray();
            ChildrenMerged = Children;
            ChildList.Clear();
            ChildList = null;
        }
        public void ChildListToMergedArray()
        {
            if (ChildList == null) return;
            if (ChildrenMerged == null)
            {
                ChildrenMerged = ChildList.ToArray();
            }
            else
            {
                List<YmapEntityDef> newc = new List<YmapEntityDef>(ChildrenMerged.Length + ChildList.Count);
                newc.AddRange(ChildrenMerged);
                newc.AddRange(ChildList);
                ChildrenMerged = newc.ToArray();
            }
            ChildList.Clear();
            ChildList = null;
        }

        public void LodManagerAddChild(YmapEntityDef child)
        {
            if (LodManagerChildren == null)
            {
                LodManagerChildren = new LinkedList<YmapEntityDef>();
            }
            LodManagerChildren.AddLast(child);
        }
        public void LodManagerRemoveChild(YmapEntityDef child)
        {
            LodManagerChildren?.Remove(child);
        }

        public override string ToString()
        {
            return _CEntityDef.ToString() + ((ChildList != null) ? (" (" + ChildList.Count.ToString() + " children) ") : " ") + _CEntityDef.lodLevel.ToString();
        }

        public void EnsureLights(DrawableBase db)
        {
            if (Lights != null) return;
            if (Archetype == null) return;
            if (db == null) return;

            var dd = db as Drawable;
            var fd = db as FragDrawable;
            var skel = db.Skeleton;
            LightAttributes[] lightAttrs = null;
            Bounds b = null;
            if (dd != null)
            {
                lightAttrs = dd.LightAttributes?.data_items;
                b = dd.Bound;
            }
            else if (fd != null)
            {
                var frag = fd?.OwnerFragment;
                skel = skel ?? frag?.Drawable?.Skeleton;
                lightAttrs = frag?.LightAttributes?.data_items;
                b = frag?.PhysicsLODGroup?.PhysicsLOD1?.Bound;
            }
            if (lightAttrs == null) return;

            var abmin = Vector3.Min(Archetype.BBMin, db.BoundingBoxMin);
            var abmax = Vector3.Max(Archetype.BBMax, db.BoundingBoxMax);
            if (b != null)
            {
                abmin = Vector3.Min(abmin, b.BoxMin);
                abmax = Vector3.Max(abmax, b.BoxMax);
            }
            var bb = new BoundingBox(abmin, abmax).Transform(Position, Orientation, Scale);
            var ints = new uint[7];
            ints[0] = (uint)(bb.Minimum.X * 10.0f);
            ints[1] = (uint)(bb.Minimum.Y * 10.0f);
            ints[2] = (uint)(bb.Minimum.Z * 10.0f);
            ints[3] = (uint)(bb.Maximum.X * 10.0f);
            ints[4] = (uint)(bb.Maximum.Y * 10.0f);
            ints[5] = (uint)(bb.Maximum.Z * 10.0f);

            var bones = skel?.BonesMap;
            var exts = (Archetype.Extensions?.Length ?? 0);

            var lightInsts = new LightInstance[lightAttrs.Length];
            for (int i = 0; i < lightAttrs.Length; i++)
            {
                ints[6] = (uint)(exts + i);
                var la = lightAttrs[i];

                var xform = Matrix.Identity;
                if ((bones != null) && (bones.TryGetValue(la.BoneId, out Bone bone)))
                {
                    xform = bone.AbsTransform;
                }

                var li = new LightInstance();
                li.Attributes = la;
                li.Hash = ComputeLightHash(ints);
                li.Position = Orientation.Multiply(xform.Multiply(la.Position)) + Position;
                li.Direction = Orientation.Multiply(xform.MultiplyRot(la.Direction));
                lightInsts[i] = li;
            }
            Lights = lightInsts;

        }

        public static uint ComputeLightHash(uint[] ints, uint seed = 0)
        {
            var a2 = ints.Length;
            var v3 = a2;
            var v5 = (uint)(seed + 0xDEADBEEF + 4 * ints.Length);
            var v6 = v5;
            var v7 = v5;

            var c = 0;
            for (var i = 0; i < (ints.Length - 4) / 3 + 1; i++, v3 -= 3, c += 3)
            {
                var v9 = ints[c + 2] + v5;
                var v10 = ints[c + 1] + v6;
                var v11 = ints[c] - v9;
                var v13 = v10 + v9;
                var v14 = (v7 + v11) ^ BitUtil.RotateLeft(v9, 4);
                var v15 = v10 - v14;
                var v17 = v13 + v14;
                var v18 = v15 ^ BitUtil.RotateLeft(v14, 6);
                var v19 = v13 - v18;
                var v21 = v17 + v18;
                var v22 = v19 ^ BitUtil.RotateLeft(v18, 8);
                var v23 = v17 - v22;
                var v25 = v21 + v22;
                var v26 = v23 ^ BitUtil.RotateLeft(v22, 16);
                var v27 = v21 - v26;
                var v29 = v27 ^ BitUtil.RotateRight(v26, 13);
                var v30 = v25 - v29;
                v7 = v25 + v26;
                v6 = v7 + v29;
                v5 = v30 ^ BitUtil.RotateLeft(v29, 4);
            }

            if (v3 == 3)
            {
                v5 += ints[c + 2];
            }

            if (v3 >= 2)
            {
                v6 += ints[c + 1];
            }

            if (v3 >= 1)
            {
                var v34 = (v6 ^ v5) - BitUtil.RotateLeft(v6, 14);
                var v35 = (v34 ^ (v7 + ints[c])) - BitUtil.RotateLeft(v34, 11);
                var v36 = (v35 ^ v6) - BitUtil.RotateRight(v35, 7);
                var v37 = (v36 ^ v34) - BitUtil.RotateLeft(v36, 16);
                var v38 = BitUtil.RotateLeft(v37, 4);
                var v39 = (((v35 ^ v37) - v38) ^ v36) - BitUtil.RotateLeft((v35 ^ v37) - v38, 14);
                return (v39 ^ v37) - BitUtil.RotateRight(v39, 8);
            }

            return v5;
        }

        [TypeConverter(typeof(ExpandableObjectConverter))]
        public class LightInstance
        {
            public LightAttributes Attributes { get; set; }
            public uint Hash { get; set; }
            public Vector3 Position { get; set; }
            public Vector3 Direction { get; set; }

            public override string ToString()
            {
                return Hash.ToString() + ": " + Attributes.Type.ToString();
            }
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapGrassInstanceBatch
    {
        private const float BatchVertMultiplier = 0.00001525878f;

        public Archetype Archetype { get; set; }
        public rage__fwGrassInstanceListDef Batch { get; set; }
        public rage__fwGrassInstanceListDef__InstanceData[] Instances { get; set; }
        public Vector3 Position { get; set; }
        public float Radius { get; set; }
        public Vector3 AABBMin { get; set; }
        public Vector3 AABBMax { get; set; }
        public Vector3 CamRel;
        public float Distance;
        public YmapFile Ymap { get; set; }

        private List<BoundingBox> grassBounds;
        public bool BrushEnabled;
        public float BrushRadius = 5f;
        public bool HasChanged;

        const float BoundingSize = 0.3F;
        static readonly Vector3 GrassMinMax = Vector3.One * BoundingSize;

        public override string ToString()
        {
            return Batch.ToString();
        }

        public void UpdateInstanceCount()
        {
            var b = Batch;
            var ins = b.InstanceList;
            ins.Count1 = (ushort)Instances.Length;
            b.InstanceList = ins;
            Batch = b;
        }

        public bool IsPointBlockedByInstance(Vector3 point)
        {
            return grassBounds.Any(bb => bb.Contains(point) == ContainmentType.Contains);
        }

        private void ReInitializeBoundingCache()
        {
            if (grassBounds != null && (grassBounds.Count == Instances.Length))
                return;

            if (grassBounds == null)
                grassBounds = new List<BoundingBox>();
            else grassBounds?.Clear();

            foreach (var inst in Instances)
            {
                var worldPos = GetGrassWorldPos(inst.Position, new BoundingBox(AABBMin, AABBMax));
                var bb = new BoundingBox(worldPos - GrassMinMax, worldPos + GrassMinMax);
                grassBounds.Add(bb);
            }
        }

        public bool EraseInstancesAtMouse(
            YmapGrassInstanceBatch batch,
            SpaceRayIntersectResult mouseRay,
            float radius)
        {
            rage__spdAABB batchAABB = batch.Batch.BatchAABB;
            var oldInstanceBounds = new BoundingBox
            (
                batchAABB.min.XYZ(),
                batchAABB.max.XYZ()
            );
            var deleteSphere = new BoundingSphere(mouseRay.Position, radius);

            var insList = new List<rage__fwGrassInstanceListDef__InstanceData>();
            foreach (var instance in batch.Instances)
            {
                var worldPos = GetGrassWorldPos(instance.Position, oldInstanceBounds);

                var instanceBounds = new BoundingBox(worldPos - GrassMinMax, worldPos + GrassMinMax);

                var bb = new BoundingBox(instanceBounds.Minimum, instanceBounds.Maximum);
                var ct = deleteSphere.Contains(ref bb);
                if (ct == ContainmentType.Contains || ct == ContainmentType.Intersects)
                {
                    continue;
                }
                insList.Add(instance);
            }
            if (insList.Count == Instances.Length)
                return false;

            var newBounds = GetNewGrassBounds(insList, oldInstanceBounds);
            var b = RecalcBatch(newBounds, batch);
            batch.Batch = b;
            insList = RecalculateInstances(insList, oldInstanceBounds, newBounds);
            batch.Instances = insList.ToArray();
            return true;
        }

        public void CreateInstancesAtMouse(
            YmapGrassInstanceBatch batch,
            SpaceRayIntersectResult mouseRay,
            float radius,
            int amount,
            Func<Vector3, SpaceRayIntersectResult> spawnRayFunc,
            Color color,
            int ao,
            int scale,
            Vector3 pad,
            bool randomScale)
        {

            ReInitializeBoundingCache();
            var spawnPosition = mouseRay.Position;
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();

            GetSpawns(spawnPosition, spawnRayFunc, positions, normals, radius, amount);
            if (positions.Count <= 0) return;

            var instances =
                batch.Instances?.ToList() ?? new List<rage__fwGrassInstanceListDef__InstanceData>();
            var batchAABB = batch.Batch.BatchAABB;

            var oldInstanceBounds = new BoundingBox(batchAABB.min.XYZ(), batchAABB.max.XYZ());

            if (positions.Count <= 0)
                return;

            var grassBound = new BoundingBox(positions[0] - GrassMinMax, positions[0] + GrassMinMax);
            grassBound = EncapsulatePositions(positions, grassBound);

            var newInstanceBounds = new BoundingBox(oldInstanceBounds.Minimum, oldInstanceBounds.Maximum);
            newInstanceBounds = instances.Count > 0
                ? newInstanceBounds.Encapsulate(grassBound)
                : new BoundingBox(grassBound.Minimum, grassBound.Maximum);

            instances = RecalculateInstances(instances, oldInstanceBounds, newInstanceBounds);

            SpawnInstances(positions, normals, instances, newInstanceBounds, color, ao, scale, pad, randomScale);

            var b = RecalcBatch(newInstanceBounds, batch);

            batch.Batch = b;

            batch.Instances = instances.ToArray();
            grassBounds.Clear();
        }

        public YmapGrassInstanceBatch[] OptimizeInstances(YmapGrassInstanceBatch batch, float minRadius)
        {
            var oldInstanceBounds = new BoundingBox(batch.Batch.BatchAABB.min.XYZ(), batch.Batch.BatchAABB.max.XYZ());

            if (oldInstanceBounds.Radius() < minRadius)
            {
                return new [] { batch };
            }

            var split = SplitGrassRecursive(batch.Instances.ToList(), oldInstanceBounds, minRadius);

            var newBatches = new List<YmapGrassInstanceBatch>();

            foreach (var grassList in split)
            {
                var newBatch = new YmapGrassInstanceBatch
                {
                    Archetype = batch.Archetype,
                    Ymap = batch.Ymap
                };

                var newInstanceBounds = GetNewGrassBounds(grassList, oldInstanceBounds);

                var b = RecalcBatch(newInstanceBounds, newBatch);
                newBatch.Batch = b;

                var ins = RecalculateInstances(grassList, oldInstanceBounds, newInstanceBounds);
                newBatch.Instances = ins.ToArray();
                newBatches.Add(newBatch);
            }

            return newBatches.ToArray();
        }

        private List<List<rage__fwGrassInstanceListDef__InstanceData>> SplitGrassRecursive(
            IReadOnlyList<rage__fwGrassInstanceListDef__InstanceData> grassInstances,
            BoundingBox batchAABB,
            float minRadius = 15F
        )
        {
            var ret = new List<List<rage__fwGrassInstanceListDef__InstanceData>>();
            var oldPoints = SplitGrass(grassInstances, batchAABB);
            while (true)
            {
                var stop = true;
                var newPoints = new List<List<rage__fwGrassInstanceListDef__InstanceData>>();
                foreach (var mb in oldPoints)
                {
                    if (mb == null)
                        continue;

                    var radius = GetNewGrassBounds(mb, batchAABB).Radius();

                    if (radius <= minRadius)
                    {
                        ret.Add(mb);
                        continue;
                    }

                    stop = false;

                    var s = SplitGrass(mb, batchAABB);

                    newPoints.AddRange(s);
                }

                oldPoints = newPoints.ToArray();

                if (stop) break;
            }
            return ret;
        }

        private List<rage__fwGrassInstanceListDef__InstanceData>[] SplitGrass(
            IReadOnlyList<rage__fwGrassInstanceListDef__InstanceData> points,
            BoundingBox batchAABB)
        {
            var pointGroup = new List<rage__fwGrassInstanceListDef__InstanceData>[2];

            var m = GetNewGrassBounds(points, batchAABB);

            var mm = new Vector3
            {
                X = Math.Abs(m.Minimum.X - m.Maximum.X),
                Y = Math.Abs(m.Minimum.Y - m.Maximum.Y),
                Z = Math.Abs(m.Minimum.Z - m.Maximum.Z)
            };

            if (mm.X > mm.Y && mm.X > mm.Z)
            {
                var lhs = new BoundingBox(m.Minimum, m.Maximum - new Vector3(mm.X * 0.5F, 0, 0));
                var rhs = new BoundingBox(m.Minimum + new Vector3(mm.X * 0.5F, 0, 0), m.Maximum);

                pointGroup[0] = points
                    .Where(p => lhs.Contains(GetGrassWorldPos(p.Position, batchAABB)) == ContainmentType.Contains).ToList();
                pointGroup[1] = points
                    .Where(p => rhs.Contains(GetGrassWorldPos(p.Position, batchAABB)) == ContainmentType.Contains).ToList();
            }
            else if (mm.Y > mm.X && mm.Y > mm.Z)
            {
                var lhs = new BoundingBox(m.Minimum, m.Maximum - new Vector3(0, mm.Y * 0.5F, 0));
                var rhs = new BoundingBox(m.Minimum + new Vector3(0, mm.Y * 0.5F, 0), m.Maximum);

                pointGroup[0] = points
                    .Where(p => lhs.Contains(GetGrassWorldPos(p.Position, batchAABB)) == ContainmentType.Contains).ToList();
                pointGroup[1] = points
                    .Where(p => rhs.Contains(GetGrassWorldPos(p.Position, batchAABB)) == ContainmentType.Contains).ToList();
            }
            else if (mm.Z > mm.X && mm.Z > mm.Y)
            {
                var lhs = new BoundingBox(m.Minimum, m.Maximum - new Vector3(0, 0, mm.Z * 0.5F));
                var rhs = new BoundingBox(m.Minimum + new Vector3(0, 0, mm.Z * 0.5F), m.Maximum);

                pointGroup[0] = points
                    .Where(p => lhs.Contains(GetGrassWorldPos(p.Position, batchAABB)) == ContainmentType.Contains).ToList();
                pointGroup[1] = points
                    .Where(p => rhs.Contains(GetGrassWorldPos(p.Position, batchAABB)) == ContainmentType.Contains).ToList();
            }
            return pointGroup;
        }

        private static BoundingBox GetNewGrassBounds(IReadOnlyList<rage__fwGrassInstanceListDef__InstanceData> newGrass, BoundingBox oldAABB)
        {
            var grassPositions = newGrass.Select(x => GetGrassWorldPos(x.Position, oldAABB)).ToArray();
            return BoundingBox.FromPoints(grassPositions).Expand(1f);
        }

        private void SpawnInstances(
            IReadOnlyList<Vector3> positions,
            IReadOnlyList<Vector3> normals,
            ICollection<rage__fwGrassInstanceListDef__InstanceData> instanceList,
            BoundingBox instanceBounds,
            Color color,
            int ao,
            int scale,
            Vector3 pad,
            bool randomScale)
        {
            for (var i = 0; i < positions.Count; i++)
            {
                var pos = positions[i];
                var newInstance = CreateNewInstance(normals[i], color, ao, scale, pad, randomScale);

                var grassPosition = GetGrassPos(pos, instanceBounds);
                newInstance.Position = grassPosition;
                instanceList.Add(newInstance);
            }
        }

        private rage__fwGrassInstanceListDef__InstanceData CreateNewInstance(Vector3 normal, Color color, int ao, int scale, Vector3 pad,
            bool randomScale = false)
        {
            var rand = new Random();
            if (randomScale)
                scale = rand.Next(scale / 2, scale);
            var newInstance = new rage__fwGrassInstanceListDef__InstanceData
            {
                Ao = (byte)ao,
                Scale = (byte)scale,
                Color = new ArrayOfBytes3 { b0 = color.R, b1 = color.G, b2 = color.B },
                Pad = new ArrayOfBytes3 { b0 = (byte)pad.X, b1 = (byte)pad.Y, b2 = (byte)pad.Z },
                NormalX = (byte)((normal.X + 1) * 0.5F * 255F),
                NormalY = (byte)((normal.Y + 1) * 0.5F * 255F)
            };
            return newInstance;
        }

        private rage__fwGrassInstanceListDef RecalcBatch(BoundingBox newInstanceBounds, YmapGrassInstanceBatch batch)
        {
            batch.AABBMax = newInstanceBounds.Maximum;
            batch.AABBMin = newInstanceBounds.Minimum;
            batch.Position = newInstanceBounds.Center();
            batch.Radius = newInstanceBounds.Radius();
            var b = batch.Batch;
            b.BatchAABB = new rage__spdAABB
            {
                min =
                    new Vector4(newInstanceBounds.Minimum,
                        0),
                max = new Vector4(newInstanceBounds.Maximum, 0)
            };
            return b;
        }

        private void GetSpawns(
            Vector3 origin, Func<Vector3,
                SpaceRayIntersectResult> spawnRayFunc,
            ICollection<Vector3> positions,
            ICollection<Vector3> normals,
            float radius,
            int resolution = 28)
        {
            var rand = new Random();
            for (var i = 0; i < resolution; i++)
            {
                var randX = (float)rand.NextDouble(-radius, radius);
                var randY = (float)rand.NextDouble(-radius, radius);
                if (Math.Abs(randX) > 0 && Math.Abs(randY) > 0)
                {
                    randX *= .7071f;
                    randY *= .7071f;
                }
                var posOffset = origin + new Vector3(randX, randY, 2f);
                var spaceRay = spawnRayFunc.Invoke(posOffset);
                if (!spaceRay.Hit) continue;
                if (IsPointBlockedByInstance(spaceRay.Position)) continue;
                normals.Add(spaceRay.Normal);
                positions.Add(spaceRay.Position);
            }
        }

        private static List<rage__fwGrassInstanceListDef__InstanceData> RecalculateInstances(
            List<rage__fwGrassInstanceListDef__InstanceData> instances,
            BoundingBox oldInstanceBounds,
            BoundingBox newInstanceBounds)
        {
            var refreshList = new List<rage__fwGrassInstanceListDef__InstanceData>();
            foreach (var inst in instances)
            {
                var copy =
                    new rage__fwGrassInstanceListDef__InstanceData
                    {
                        Position = inst.Position,
                        Ao = inst.Ao,
                        Color = inst.Color,
                        NormalX = inst.NormalX,
                        NormalY = inst.NormalY,
                        Pad = inst.Pad,
                        Scale = inst.Scale
                    };
                var oldPos = GetGrassWorldPos(copy.Position, oldInstanceBounds);
                copy.Position = GetGrassPos(oldPos, newInstanceBounds);
                refreshList.Add(copy);
            }
            instances = refreshList.ToList();
            return instances;
        }

        private static BoundingBox EncapsulatePositions(IEnumerable<Vector3> positions, BoundingBox bounds)
        {
            foreach (var pos in positions)
            {
                var posBounds = new BoundingBox(pos - (GrassMinMax + 0.1f), pos + (GrassMinMax + 0.1f));
                bounds = bounds.Encapsulate(posBounds);
            }
            return bounds;
        }

        private static ArrayOfUshorts3 GetGrassPos(Vector3 worldPos, BoundingBox batchAABB)
        {
            var offset = worldPos - batchAABB.Minimum;
            var size = batchAABB.Size();
            var percentage =
                new Vector3(
                    offset.X / size.X,
                    offset.Y / size.Y,
                    offset.Z / size.Z
                );
            var instancePos = percentage / BatchVertMultiplier;
            return new ArrayOfUshorts3
            {
                u0 = (ushort)instancePos.X,
                u1 = (ushort)instancePos.Y,
                u2 = (ushort)instancePos.Z
            };
        }

        private static Vector3 GetGrassWorldPos(ArrayOfUshorts3 grassPos, BoundingBox batchAABB)
        {
            return batchAABB.Minimum + batchAABB.Size() * (grassPos.XYZ() * BatchVertMultiplier);
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapPropInstanceBatch
    {
        public YmapFile Ymap { get; set; }

    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapDistantLODLights
    {
        public CDistantLODLight CDistantLODLight { get; set; }
        public uint[] colours { get; set; }
        public MetaVECTOR3[] positions { get; set; }

        public Vector3 BBMin { get; set; }
        public Vector3 BBMax { get; set; }
        public YmapFile Ymap { get; set; }

        public void CalcBB()
        {
            if (positions != null)
            {
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    Vector3 pv = p.ToVector3();
                    min = Vector3.Min(min, pv);
                    max = Vector3.Max(max, pv);
                }
                BBMin = min;
                BBMax = max;
            }

        }

        public void RebuildFromLodLights(YmapLODLight[] lodlights)
        {
            var n = lodlights?.Length ?? 0;
            if (n == 0) return;

            colours = new uint[n];
            positions = new MetaVECTOR3[n];
            var nstreetlights = 0;
            for (int i = 0; i < n; i++)
            {
                var ll = lodlights[i];
                colours[i] = (uint)(ll.Colour.ToBgra());
                positions[i] = new MetaVECTOR3(ll.Position);
                if ((ll.StateFlags1 & 1) > 0)
                {
                    nstreetlights++;
                }
            }

            var cdll = CDistantLODLight;
            cdll.numStreetLights = (ushort)nstreetlights;

            CalcBB();

        }

        public override string ToString()
        {
            if (Ymap != null)
            {
                return Ymap.ToString();
            }
            return base.ToString();
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapLODLights
    {
        public CLODLight CLODLight { get; set; }
        public MetaVECTOR3[] direction { get; set; }
        public float[] falloff { get; set; }
        public float[] falloffExponent { get; set; }
        public uint[] timeAndStateFlags { get; set; }
        public uint[] hash { get; set; }
        public byte[] coneInnerAngle { get; set; }
        public byte[] coneOuterAngleOrCapExt { get; set; }
        public byte[] coronaIntensity { get; set; }

        public Vector3 BBMin { get; set; }
        public Vector3 BBMax { get; set; }
        public YmapFile Ymap { get; set; }

        public YmapLODLight[] LodLights { get; set; }

        public PathBVH BVH { get; set; }

        public void Init(YmapDistantLODLights parent)
        {
            if (parent == null) return;

            BuildLodLights(parent);
            CalcBB();
            BuildBVH();
        }

        public void BuildLodLights(YmapDistantLODLights parent)
        {
            var n = direction?.Length ?? 0;
            n = Math.Min(n, parent.positions?.Length ?? 0);
            n = Math.Min(n, parent.colours?.Length ?? 0);
            n = Math.Min(n, falloff?.Length ?? 0);
            n = Math.Min(n, falloffExponent?.Length ?? 0);
            n = Math.Min(n, timeAndStateFlags?.Length ?? 0);
            n = Math.Min(n, hash?.Length ?? 0);
            n = Math.Min(n, coneInnerAngle?.Length ?? 0);
            n = Math.Min(n, coneOuterAngleOrCapExt?.Length ?? 0);
            n = Math.Min(n, coronaIntensity?.Length ?? 0);
            if (n == 0) return;

            LodLights = new YmapLODLight[n];
            for (int i = 0; i < n; i++)
            {
                var l = new YmapLODLight();
                l.Init(this, parent, i);
                LodLights[i] = l;
            }
        }

        public void BuildBVH()
        {
            BVH = new PathBVH(LodLights, 10, 10);
        }

        public void CalcBB()
        {
            var positions = Ymap?.Parent?.DistantLODLights?.positions;
            if (positions != null)
            {
                Vector3 min = new Vector3(float.MaxValue);
                Vector3 max = new Vector3(float.MinValue);
                for (int i = 0; i < positions.Length; i++)
                {
                    var p = positions[i];
                    Vector3 pv = p.ToVector3();
                    min = Vector3.Min(min, pv);
                    max = Vector3.Max(max, pv);
                }
                BBMin = min;
                BBMax = max;
            }
            else if (Ymap != null)
            {
                BBMin = Ymap._CMapData.entitiesExtentsMin;
                BBMax = Ymap._CMapData.entitiesExtentsMax;
            }
            else
            { }
        }

        public void RebuildFromLodLights()
        {
            var n = LodLights?.Length ?? 0;
            if (n <= 0)
            {
                direction = null;
                falloff = null;
                falloffExponent = null;
                timeAndStateFlags = null;
                hash = null;
                coneInnerAngle = null;
                coneOuterAngleOrCapExt = null;
                coronaIntensity = null;
            }
            else
            {
                direction = new MetaVECTOR3[n];
                falloff = new float[n];
                falloffExponent = new float[n];
                timeAndStateFlags = new uint[n];
                hash = new uint[n];
                coneInnerAngle = new byte[n];
                coneOuterAngleOrCapExt = new byte[n];
                coronaIntensity = new byte[n];

                for (int i = 0; i < n; i++)
                {
                    var ll = LodLights[i];
                    direction[i] = new MetaVECTOR3(ll.Direction);
                    falloff[i] = ll.Falloff;
                    falloffExponent[i] = ll.FalloffExponent;
                    timeAndStateFlags[i] = ll.TimeAndStateFlags;
                    hash[i] = ll.Hash;
                    coneInnerAngle[i] = ll.ConeInnerAngle;
                    coneOuterAngleOrCapExt[i] = ll.ConeOuterAngleOrCapExt;
                    coronaIntensity[i] = ll.CoronaIntensity;
                }
            }

        }

        public override string ToString()
        {
            if (Ymap != null)
            {
                return Ymap.ToString();
            }
            return base.ToString();
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapLODLight : BasePathNode
    {
        public YmapFile Ymap { get { return LodLights?.Ymap ?? DistLodLights?.Ymap; } }
        public YmapLODLights LodLights { get; set; }
        public YmapDistantLODLights DistLodLights { get; set; }
        public int Index { get; set; }
        public Color Colour { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Direction { get; set; }
        public float Falloff { get; set; }
        public float FalloffExponent { get; set; }
        public uint TimeAndStateFlags { get; set; }
        public uint Hash { get; set; }
        public byte ConeInnerAngle { get; set; }
        public byte ConeOuterAngleOrCapExt { get; set; }
        public byte CoronaIntensity { get; set; }

        public Quaternion Orientation { get; set; }
        public Vector3 Scale { get; set; }

        public Vector3 TangentX { get; set; }
        public Vector3 TangentY { get; set; }

        public LightType Type
        {
            get
            {
                return (LightType)((TimeAndStateFlags >> 26) & 7);
            }
            set
            {
                TimeAndStateFlags = (TimeAndStateFlags & 0xE3FFFFFF) + (((uint)value & 7) << 26);
            }
        }
        public FlagsUint TimeFlags
        {
            get
            {
                return (TimeAndStateFlags & 0xFFFFFF);
            }
            set
            {
                TimeAndStateFlags = (TimeAndStateFlags & 0xFF000000) + (value & 0xFFFFFF);
            }
        }
        public uint StateFlags1
        {
            get
            {
                return (TimeAndStateFlags >> 24) & 3;
            }
            set
            {
                TimeAndStateFlags = (TimeAndStateFlags & 0xFCFFFFFF) + ((value & 3) << 24);
            }
        }
        public uint StateFlags2
        {
            get
            {
                return (TimeAndStateFlags >> 29) & 7;
            }
            set
            {
                TimeAndStateFlags = (TimeAndStateFlags & 0x1FFFFFFF) + ((value & 7) << 29);
            }
        }

        public bool Enabled { get; set; } = true;

        public void Init(YmapLODLights l, YmapDistantLODLights p, int i)
        {
            LodLights = l;
            DistLodLights = p;
            Index = i;

            if (p.colours == null) return;
            if ((i < 0) || (i >= p.colours.Length)) return;

            Colour = Color.FromBgra(p.colours[i]);
            Position = p.positions[i].ToVector3();
            Direction = l.direction[i].ToVector3();
            Falloff = l.falloff[i];
            FalloffExponent = l.falloffExponent[i];
            TimeAndStateFlags = l.timeAndStateFlags[i];
            Hash = l.hash[i];
            ConeInnerAngle = l.coneInnerAngle[i];
            ConeOuterAngleOrCapExt = l.coneOuterAngleOrCapExt[i];
            CoronaIntensity = l.coronaIntensity[i];

            UpdateTangentsAndOrientation();

            Scale = new Vector3(Falloff);
        }

        public void UpdateTangentsAndOrientation()
        {
            switch (Type)
            {
                default:
                case LightType.Point:
                    TangentX = Vector3.UnitX;
                    TangentY = Vector3.UnitY;
                    Orientation = Quaternion.Identity;
                    break;
                case LightType.Spot:
                    TangentX = Vector3.Normalize(Direction.GetPerpVec());
                    TangentY = Vector3.Normalize(Vector3.Cross(Direction, TangentX));
                    break;
                case LightType.Capsule:
                    TangentX = -Vector3.Normalize(Direction.GetPerpVec());
                    TangentY = Vector3.Normalize(Vector3.Cross(Direction, TangentX));
                    break;
            }

            if (Type == LightType.Point)
            {
                Orientation = Quaternion.Identity;
            }
            else
            {
                var m = new Matrix();
                m.Row1 = new Vector4(TangentX, 0);
                m.Row2 = new Vector4(TangentY, 0);
                m.Row3 = new Vector4(Direction, 0);
                Orientation = Quaternion.RotationMatrix(m);
            }
        }

        public void SetColour(Color c)
        {
            Colour = c;

            if ((DistLodLights?.colours != null) && (DistLodLights.colours.Length >= Index))
            {
                DistLodLights.colours[Index] = (uint)(c.ToBgra());
            }

        }
        public void SetPosition(Vector3 pos)
        {
            Position = pos;

            if ((DistLodLights?.positions != null) && (DistLodLights.positions.Length >= Index))
            {
                DistLodLights.positions[Index] = new MetaVECTOR3(pos);
            }

        }
        public void SetOrientation(Quaternion ori)
        {
            var inv = Quaternion.Invert(Orientation);
            var delta = ori * inv;
            TangentX = Vector3.Normalize(delta.Multiply(TangentX));
            TangentY = Vector3.Normalize(delta.Multiply(TangentY));
            Direction = Vector3.Normalize(delta.Multiply(Direction));
            Orientation = ori;
        }
        public void SetScale(Vector3 scale)
        {
            switch (Type)
            {
                case LightType.Point:
                case LightType.Spot:
                    Falloff = scale.Z;
                    break;
                case LightType.Capsule:
                    Falloff = scale.X;
                    ConeOuterAngleOrCapExt = (byte)Math.Min(255, Math.Max(0, Math.Round(Math.Max((uint)ConeOuterAngleOrCapExt, 2) * Math.Abs(scale.Z / Math.Max(Scale.Z, 0.01f)))));
                    break;
            }
            Scale = scale;
        }

        public void CopyFrom(YmapLODLight l)
        {
            Colour = l.Colour;
            Position = l.Position;
            Direction = l.Direction;
            Falloff = l.Falloff;
            FalloffExponent = l.FalloffExponent;
            TimeAndStateFlags = l.TimeAndStateFlags;
            Hash = l.Hash;
            ConeInnerAngle = l.ConeInnerAngle;
            ConeOuterAngleOrCapExt = l.ConeOuterAngleOrCapExt;
            CoronaIntensity = l.CoronaIntensity;

            Orientation = l.Orientation;
            Scale = l.Scale;
            TangentX = l.TangentX;
            TangentY = l.TangentY;
        }

        public override string ToString()
        {
            return Index.ToString() + ": " + Position.ToString();
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapTimeCycleModifier
    {
        public CTimeCycleModifier CTimeCycleModifier { get; set; }
        public World.TimecycleMod TimeCycleModData { get; set; }

        public Vector3 BBMin { get; set; }
        public Vector3 BBMax { get; set; }

        public YmapFile Ymap { get; set; }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapCarGen
    {
        public CCarGen _CCarGen;
        public CCarGen CCarGen { get { return _CCarGen; } set { _CCarGen = value; } }

        public Vector3 Position { get; set; }
        public Quaternion Orientation { get; set; }
        public Vector3 BBMin { get; set; }
        public Vector3 BBMax { get; set; }

        public YmapFile Ymap { get; set; }

        public YmapCarGen(YmapFile ymap, CCarGen cargen)
        {
            float hlen = cargen.perpendicularLength * 0.5f;
            Ymap = ymap;
            CCarGen = cargen;
            Position = cargen.position;
            CalcOrientation();
            BBMin = new Vector3(-hlen);
            BBMax = new Vector3(hlen);
        }

        public void CalcOrientation()
        {
            float angl = (float)Math.Atan2(_CCarGen.orientY, _CCarGen.orientX);
            Orientation = Quaternion.RotationYawPitchRoll(0.0f, 0.0f, angl);
        }

        public void SetPosition(Vector3 pos)
        {
            Position = pos;
            _CCarGen.position = pos;
        }
        public void SetOrientation(Quaternion ori)
        {
            Orientation = ori;

            float len = Math.Max(_CCarGen.perpendicularLength * 1.5f, 5.0f);
            Vector3 v = new Vector3(len, 0, 0);
            Vector3 t = ori.Multiply(v);

            _CCarGen.orientX = t.X;
            _CCarGen.orientY = t.Y;
        }
        public void SetScale(Vector3 scale)
        {
            float s = scale.X;
            float hlen = s * 0.5f;
            _CCarGen.perpendicularLength = s;

            BBMin = new Vector3(-hlen);
            BBMax = new Vector3(hlen);
        }

        public void SetLength(float length)
        {
            _CCarGen.perpendicularLength = length;
            float hlen = length * 0.5f;

            BBMin = new Vector3(-hlen);
            BBMax = new Vector3(hlen);
        }

        public string NameString()
        {
            MetaHash mh = _CCarGen.carModel;
            if ((mh == 0) && (_CCarGen.popGroup != 0))
            {
                mh = _CCarGen.popGroup;
            }
            return mh.ToString();
        }

        public override string ToString()
        {
            return _CCarGen.carModel.ToString() + ", " + Position.ToString() + ", " + _CCarGen.popGroup.ToString() + ", " + _CCarGen.livery.ToString();
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapOccludeModel : BasePathData
    {
        public OccludeModel _OccludeModel;
        public OccludeModel OccludeModel { get { return _OccludeModel; } set { _OccludeModel = value; } }

        public YmapFile Ymap { get; set; }

        public byte[] Data { get; set; }
        public Vector3[] Vertices { get; set; }
        public byte[] Indices { get; set; }
        public int Index { get; set; }

        public YmapOccludeModelTriangle[] Triangles { get; set; }
        public TriangleBVH BVH { get; set; }

        public FlagsUint Flags
        {
            get
            {
                return _OccludeModel.flags;
            }
            set
            {
                _OccludeModel.flags = value;
            }
        }

        public YmapOccludeModel(YmapFile ymap, OccludeModel model)
        {
            Ymap = ymap;
            _OccludeModel = model;
        }

        public void Load(Meta meta)
        {
            var vptr = _OccludeModel.verts;
            var dataSize = _OccludeModel.dataSize;
            var indicesOffset = _OccludeModel.numVertsInBytes;
            var vertexCount = indicesOffset / 12;
            var indexCount = (int)(dataSize - indicesOffset);
            Data = MetaTypes.GetByteArray(meta, vptr, dataSize);
            Vertices = MetaTypes.ConvertDataArray<Vector3>(Data, 0, vertexCount);
            Indices = new byte[indexCount];
            Buffer.BlockCopy(Data, indicesOffset, Indices, 0, indexCount);
            BuildTriangles();
        }

        public void BuildTriangles()
        {
            if ((Vertices == null) || (Indices == null))
            {
                Triangles = null;
                return;
            }
            var tris = new List<YmapOccludeModelTriangle>();
            for (int i = 0; i < Indices.Length; i += 3)
            {
                var tri = new YmapOccludeModelTriangle(this, Vertices[Indices[i]], Vertices[Indices[i+1]], Vertices[Indices[i+2]], tris.Count);
                tris.Add(tri);
            }
            Triangles = tris.ToArray();
        }
        public void BuildBVH()
        {
            if (Triangles == null)
            {
                BVH = null;
                return;
            }

            BVH = new TriangleBVH(Triangles);

        }
        public void BuildVertices()
        {
            if (Triangles == null)
            {
                Vertices = null;
                Indices = null;
                return;
            }
            var vdict = new Dictionary<Vector3, byte>();
            var verts = new List<Vector3>();
            var inds = new List<byte>();
            byte ensureVert(Vector3 v)
            {
                if (vdict.TryGetValue(v, out byte b))
                {
                    return b;
                }
                if (verts.Count > 255)
                {
                    return 0;
                }
                var i = (byte)verts.Count;
                vdict[v] = i;
                verts.Add(v);
                return i;
            }
            for (int i = 0; i < Triangles.Length; i++)
            {
                var tri = Triangles[i];
                inds.Add(ensureVert(tri.Corner1));
                inds.Add(ensureVert(tri.Corner2));
                inds.Add(ensureVert(tri.Corner3));
            }
            Vertices = verts.ToArray();
            Indices = inds.ToArray();
        }
        public void BuildData()
        {
            if (Vertices == null) return;
            if (Indices == null) return;
            var dlen = (Vertices.Length * 12) + (Indices.Length * 1);
            var d = new byte[dlen];
            var vbytes = MetaTypes.ConvertArrayToBytes(Vertices);
            var ibytes = Indices;
            Buffer.BlockCopy(vbytes, 0, d, 0, vbytes.Length);
            Buffer.BlockCopy(ibytes, 0, d, vbytes.Length, ibytes.Length);
            Data = d;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            for (int i = 0; i < Vertices.Length; i++)
            {
                min = Vector3.Min(min, Vertices[i]);
                max = Vector3.Max(max, Vertices[i]);
            }
            _OccludeModel.bmin = min;
            _OccludeModel.bmax = max;
            _OccludeModel.Unused0 = min.X;
            _OccludeModel.Unused1 = max.X;
            _OccludeModel.dataSize = (uint)dlen;
            _OccludeModel.numVertsInBytes = (ushort)vbytes.Length;
            _OccludeModel.numTris = (ushort)((ibytes.Length / 3) + 32768);
        }

        public YmapOccludeModelTriangle RayIntersect(ref Ray ray, ref float hitdist)
        {
            if (Triangles == null)
            {
                BuildTriangles();
                BuildBVH();
            }
            else if (BVH == null)
            {
                BuildBVH();
            }
            if (BVH == null) return null;
            return BVH.RayIntersect(ref ray, ref hitdist) as YmapOccludeModelTriangle;
        }

        public EditorVertex[] GetTriangleVertices()
        {
            if ((Vertices == null) || (Indices == null)) return null;
            EditorVertex[] res = new EditorVertex[Indices.Length];
            var colour = new Color4(1.0f, 0.0f, 0.0f, 0.8f);
            var colourval = (uint)colour.ToRgba();
            for (int i = 0; i < Indices.Length; i++)
            {
                res[i].Position = Vertices[Indices[i]];
                res[i].Colour = colourval;
            }
            return res;
        }
        public EditorVertex[] GetPathVertices()
        {
            return null;
        }
        public Vector4[] GetNodePositions()
        {
            return null;
        }

        public override string ToString()
        {
            return Index.ToString() + ": " + (Vertices?.Length ?? 0).ToString() + " vertices, " + (Triangles?.Length ?? 0).ToString() + " triangles";
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapOccludeModelTriangle : TriangleBVHItem
    {
        public YmapOccludeModel Model { get; set; }
        public YmapFile Ymap { get { return Model?.Ymap; } }
        public int Index { get; set; }

        public YmapOccludeModelTriangle(YmapOccludeModel model, Vector3 v1, Vector3 v2, Vector3 v3, int i)
        {
            Model = model;
            Corner1 = v1;
            Corner2 = v2;
            Corner3 = v3;
            Index = i;
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class YmapBoxOccluder : BasePathData
    {
        public BoxOccluder _Box;
        public BoxOccluder Box { get { return _Box; } set { _Box = value; } }

        public YmapFile Ymap { get; set; }

        public Vector3 Position { get; set; }
        public Vector3 Size { get; set; }
        public Vector3 BBMin { get; set; }
        public Vector3 BBMax { get; set; }
        public Quaternion Orientation { get; set; }
        public int Index { get; set; }

        public YmapBoxOccluder(YmapFile ymap, BoxOccluder box)
        {
            Ymap = ymap;
            _Box = box;

            Position = new Vector3(box.iCenterX, box.iCenterY, box.iCenterZ) / 4.0f;
            Size = new Vector3(box.iLength, box.iWidth, box.iHeight) / 4.0f;
            BBMin = Size * -0.5f;
            BBMax = Size * 0.5f;

            float cosz = box.iCosZ / 32767.0f;
            float sinz = box.iSinZ / 32767.0f;

            float angl = (float)Math.Atan2(cosz, sinz);
            Orientation = Quaternion.RotationYawPitchRoll(0.0f, 0.0f, angl);

        }

        public void UpdateBoxStruct()
        {
            _Box.iCenterX = (short)Math.Round(Position.X * 4.0f);
            _Box.iCenterY = (short)Math.Round(Position.Y * 4.0f);
            _Box.iCenterZ = (short)Math.Round(Position.Z * 4.0f);
            _Box.iLength = (short)Math.Round(Size.X * 4.0f);
            _Box.iWidth = (short)Math.Round(Size.Y * 4.0f);
            _Box.iHeight = (short)Math.Round(Size.Z * 4.0f);

            var dir = Orientation.Multiply(Vector3.UnitX) * 0.5f;
            _Box.iSinZ = (short)Math.Round(dir.X * 32767.0f);
            _Box.iCosZ = (short)Math.Round(dir.Y * 32767.0f);
        }

        public void SetSize(Vector3 s)
        {
            Size = s;
            BBMin = Size * -0.5f;
            BBMax = Size * 0.5f;
        }

        public EditorVertex[] GetTriangleVertices()
        {
            Vector3 xform(float x, float y, float z)
            {
                return Orientation.Multiply(new Vector3(x, y, z)) + Position;
            }
            EditorVertex[] res = new EditorVertex[36];
            var colour = new Color4(0.0f, 0.0f, 1.0f, 0.8f);
            var c = (uint)colour.ToRgba();
            var s = Size * 0.5f;
            var v0 = new EditorVertex() { Position = xform(-s.X, -s.Y, -s.Z), Colour = c };
            var v1 = new EditorVertex() { Position = xform(-s.X, -s.Y, +s.Z), Colour = c };
            var v2 = new EditorVertex() { Position = xform(-s.X, +s.Y, -s.Z), Colour = c };
            var v3 = new EditorVertex() { Position = xform(-s.X, +s.Y, +s.Z), Colour = c };
            var v4 = new EditorVertex() { Position = xform(+s.X, -s.Y, -s.Z), Colour = c };
            var v5 = new EditorVertex() { Position = xform(+s.X, -s.Y, +s.Z), Colour = c };
            var v6 = new EditorVertex() { Position = xform(+s.X, +s.Y, -s.Z), Colour = c };
            var v7 = new EditorVertex() { Position = xform(+s.X, +s.Y, +s.Z), Colour = c };
            res[00] = v0; res[01] = v1; res[02] = v2; res[03] = v2; res[04] = v1; res[05] = v3;
            res[06] = v2; res[07] = v3; res[08] = v6; res[09] = v6; res[10] = v3; res[11] = v7;
            res[12] = v1; res[13] = v5; res[14] = v3; res[15] = v3; res[16] = v5; res[17] = v7;
            res[18] = v6; res[19] = v7; res[20] = v4; res[21] = v4; res[22] = v7; res[23] = v5;
            res[24] = v4; res[25] = v5; res[26] = v0; res[27] = v0; res[28] = v5; res[29] = v1;
            res[30] = v2; res[31] = v6; res[32] = v0; res[33] = v0; res[34] = v6; res[35] = v4;
            return res;
        }
        public EditorVertex[] GetPathVertices()
        {
            return null;
        }
        public Vector4[] GetNodePositions()
        {
            return null;
        }

        public override string ToString()
        {
            return Index.ToString() + ": " + FloatUtil.GetVector3String(Position);
        }
    }

}
