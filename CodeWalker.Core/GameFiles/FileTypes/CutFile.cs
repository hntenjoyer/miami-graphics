using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using System.Xml;

using TC = System.ComponentModel.TypeConverterAttribute;
using EXP = System.ComponentModel.ExpandableObjectConverter;

namespace CodeWalker.GameFiles
{
    [TC(typeof(EXP))] public class CutFile : PackedFile
    {
        public RpfFileEntry FileEntry { get; set; }
        public PsoFile Pso { get; set; }

        public CutsceneFile2 CutsceneFile2 { get; set; }

        public CutFile()
        { }
        public CutFile(RpfFileEntry entry)
        {
            FileEntry = entry;
        }

        public void Load(byte[] data, RpfFileEntry entry)
        {
            FileEntry = entry;

            MemoryStream ms = new MemoryStream(data);

            if (PsoFile.IsPSO(ms))
            {
                Pso = new PsoFile();
                Pso.Load(ms);

                var xml = PsoXml.GetXml(Pso);
                XmlDocument doc = new XmlDocument();
                doc.LoadXml(xml);
                var node = doc.DocumentElement;

                CutsceneFile2 = new CutsceneFile2();
                CutsceneFile2.ReadXml(node);

            }
            else
            {

            }
        }

    }

    [TC(typeof(EXP))] public abstract class CutBase : IMetaXmlItem
    {
        public virtual void ReadXml(XmlNode node)
        { }
        public virtual void WriteXml(StringBuilder sb, int indent)
        { }
    }

    [TC(typeof(EXP))] public class CutsceneFile2 : CutBase
    {
        public float fTotalDuration { get; set; }
        public string cFaceDir { get; set; }
        public uint[] iCutsceneFlags { get; set; }
        public Vector3 vOffset { get; set; }
        public float fRotation { get; set; }
        public Vector3 vTriggerOffset { get; set; }
        public object[] pCutsceneObjects { get; set; }
        public object[] pCutsceneLoadEventList { get; set; }
        public object[] pCutsceneEventList { get; set; }
        public object[] pCutsceneEventArgsList { get; set; }
        public CutParAttributeList attributes { get; set; }
        public CutFAttributeList cutfAttributes { get; set; }
        public int iRangeStart { get; set; }
        public int iRangeEnd { get; set; }
        public int iAltRangeEnd { get; set; }
        public float fSectionByTimeSliceDuration { get; set; }
        public float fFadeOutCutsceneDuration { get; set; }
        public float fFadeInGameDuration { get; set; }
        public uint fadeInColor { get; set; }
        public int iBlendOutCutsceneDuration { get; set; }
        public int iBlendOutCutsceneOffset { get; set; }
        public float fFadeOutGameDuration { get; set; }
        public float fFadeInCutsceneDuration { get; set; }
        public uint fadeOutColor { get; set; }
        public uint DayCoCHours { get; set; }
        public float[] cameraCutList { get; set; }
        public float[] sectionSplitList { get; set; }
        public CutConcatData[] concatDataList { get; set; }
        public CutHaltFrequency[] discardFrameList { get; set; }

        public Dictionary<int, CutObject> ObjectsDict { get; set; } = new Dictionary<int, CutObject>();

        public override void ReadXml(XmlNode node)
        {
            fTotalDuration = Xml.GetChildFloatAttribute(node, "fTotalDuration", "value");
            cFaceDir = Xml.GetChildInnerText(node, "cFaceDir");
            iCutsceneFlags = Xml.GetChildRawUintArray(node, "iCutsceneFlags");
            vOffset = Xml.GetChildVector3Attributes(node, "vOffset");
            fRotation = Xml.GetChildFloatAttribute(node, "fRotation", "value");
            vTriggerOffset = Xml.GetChildVector3Attributes(node, "vTriggerOffset");
            pCutsceneObjects = ReadObjectArray(node, "pCutsceneObjects");
            pCutsceneLoadEventList = ReadObjectArray(node, "pCutsceneLoadEventList");
            pCutsceneEventList = ReadObjectArray(node, "pCutsceneEventList");
            pCutsceneEventArgsList = ReadObjectArray(node, "pCutsceneEventArgsList");
            attributes = ReadObject<CutParAttributeList>(node, "attributes");
            cutfAttributes = ReadObject<CutFAttributeList>(node, "cutfAttributes");
            iRangeStart = Xml.GetChildIntAttribute(node, "iRangeStart", "value");
            iRangeEnd = Xml.GetChildIntAttribute(node, "iRangeEnd", "value");
            iAltRangeEnd = Xml.GetChildIntAttribute(node, "iAltRangeEnd", "value");
            fSectionByTimeSliceDuration = Xml.GetChildFloatAttribute(node, "fSectionByTimeSliceDuration", "value");
            fFadeOutCutsceneDuration = Xml.GetChildFloatAttribute(node, "fFadeOutCutsceneDuration", "value");
            fFadeInGameDuration = Xml.GetChildFloatAttribute(node, "fFadeInGameDuration", "value");
            fadeInColor = Xml.GetChildUIntAttribute(node, "fadeInColor", "value");
            iBlendOutCutsceneDuration = Xml.GetChildIntAttribute(node, "iBlendOutCutsceneDuration", "value");
            iBlendOutCutsceneOffset = Xml.GetChildIntAttribute(node, "iBlendOutCutsceneOffset", "value");
            fFadeOutGameDuration = Xml.GetChildFloatAttribute(node, "fFadeOutGameDuration", "value");
            fFadeInCutsceneDuration = Xml.GetChildFloatAttribute(node, "fFadeInCutsceneDuration", "value");
            fadeOutColor = Xml.GetChildUIntAttribute(node, "fadeOutColor", "value");
            DayCoCHours = Xml.GetChildUIntAttribute(node, "DayCoCHours", "value");
            cameraCutList = Xml.GetChildRawFloatArray(node, "cameraCutList");
            sectionSplitList = Xml.GetChildRawFloatArray(node, "sectionSplitList");
            concatDataList = XmlMeta.ReadItemArrayNullable<CutConcatData>(node, "concatDataList");
            discardFrameList = XmlMeta.ReadItemArrayNullable<CutHaltFrequency>(node, "discardFrameList");

            AssociateObjects();
        }

        public void AssociateObjects()
        {
            ObjectsDict.Clear();
            if (pCutsceneObjects != null)
            {
                foreach (var obj in pCutsceneObjects)
                {
                    if (obj is CutObject cobj)
                    {
                        ObjectsDict[cobj.iObjectId] = cobj;
                    }
                }
            }

            CutEventArgs getEventArgs(int i)
            {
                if (i < 0) return null;
                if (i >= pCutsceneEventArgsList?.Length) return null;
                var args = pCutsceneEventArgsList[i];
                if (!(args is CutEventArgs))
                { }
                return args as CutEventArgs;
            }
            CutObject getObject(int i)
            {
                CutObject o = null;
                ObjectsDict.TryGetValue(i, out o);
                return o;
            }

            if (pCutsceneEventArgsList != null)
            {
                foreach (var arg in pCutsceneEventArgsList)
                {
                    if (arg is CutObjectIdEventArgs oarg)
                    {
                        oarg.Object = getObject(oarg.iObjectId);
                    }
                    if (arg is CutObjectIdListEventArgs larg)
                    {
                        var objs = new CutObject[larg.iObjectIdList?.Length ?? 0];
                        for (int i = 0; i < objs.Length; i++)
                        {
                            objs[i] = getObject(larg.iObjectIdList[i]);
                        }
                        larg.ObjectList = objs;
                    }
                }
            }
            if (pCutsceneEventList != null)
            {
                foreach (var evt in pCutsceneEventList)
                {
                    if (evt is CutObjectIdEvent oevt)
                    {
                        oevt.Object = getObject(oevt.iObjectId);
                    }
                    if (evt is CutEvent cevt)
                    {
                        cevt.EventArgs = getEventArgs(cevt.iEventArgsIndex);
                    }
                    else
                    { }
                }
            }
            if (pCutsceneLoadEventList != null)
            {
                foreach (var evt in pCutsceneLoadEventList)
                {
                    if (evt is CutObjectIdEvent oevt)
                    {
                        oevt.Object = getObject(oevt.iObjectId);
                    }
                    if (evt is CutEvent cevt)
                    {
                        cevt.EventArgs = getEventArgs(cevt.iEventArgsIndex);
                    }
                    else
                    { }
                }
            }

        }

        public static CutBase ConstructObject(string type)
        {
            switch (type)
            {
                case "rage__cutfAssetManagerObject": return new CutAssetManagerObject();
                case "rage__cutfAnimationManagerObject": return new CutAnimationManagerObject();
                case "rage__cutfCameraObject": return new CutCameraObject();
                case "rage__cutfPedModelObject": return new CutPedModelObject();
                case "rage__cutfPropModelObject": return new CutPropModelObject();
                case "rage__cutfBlockingBoundsObject": return new CutBlockingBoundsObject();
                case "rage__cutfAudioObject": return new CutAudioObject();
                case "rage__cutfHiddenModelObject": return new CutHiddenModelObject();
                case "rage__cutfOverlayObject": return new CutOverlayObject();
                case "rage__cutfSubtitleObject": return new CutSubtitleObject();
                case "rage__cutfLightObject": return new CutLightObject();
                case "rage__cutfAnimatedLightObject": return new CutAnimatedLightObject();
                case "rage__cutfObjectIdEvent": return new CutObjectIdEvent();
                case "rage__cutfObjectVariationEventArgs": return new CutObjectVariationEventArgs();
                case "rage__cutfEventArgs": return new CutEventArgs();
                case "rage__cutfLoadSceneEventArgs": return new CutLoadSceneEventArgs();
                case "rage__cutfObjectIdEventArgs": return new CutObjectIdEventArgs();
                case "rage__cutfObjectIdListEventArgs": return new CutObjectIdListEventArgs();
                case "rage__cutfNameEventArgs": return new CutNameEventArgs();
                case "rage__cutfCameraCutEventArgs": return new CutCameraCutEventArgs();
                case "rage__cutfSubtitleEventArgs": return new CutSubtitleEventArgs();
                case "rage__cutfFinalNameEventArgs": return new CutFinalNameEventArgs();
                case "rage__cutfObjectIdNameEventArgs": return new CutObjectIdNameEventArgs();
                case "rage__cutfVehicleModelObject": return new CutVehicleModelObject();
                case "rage__cutfEvent": return new CutEvent();
                case "rage__cutfCascadeShadowEventArgs": return new CutCascadeShadowEventArgs();
                case "rage__cutfFloatValueEventArgs": return new CutFloatValueEventArgs();
                case "rage__cutfAnimatedParticleEffectObject": return new CutAnimatedParticleEffectObject();
                case "rage__cutfWeaponModelObject": return new CutWeaponModelObject();
                case "rage__cutfPlayParticleEffectEventArgs": return new CutPlayParticleEffectEventArgs();
                case "rage__cutfBoolValueEventArgs": return new CutBoolValueEventArgs();
                case "rage__cutfRayfireObject": return new CutRayfireObject();
                case "rage__cutfParticleEffectObject": return new CutParticleEffectObject();
                case "rage__cutfDecalObject": return new CutDecalObject();
                case "rage__cutfDecalEventArgs": return new CutDecalEventArgs();
                case "rage__cutfScreenFadeObject": return new CutScreenFadeObject();
                case "rage__cutfVehicleVariationEventArgs": return new CutVehicleVariationEventArgs();
                case "rage__cutfScreenFadeEventArgs": return new CutScreenFadeEventArgs();
                case "rage__cutfTriggerLightEffectEventArgs": return new CutTriggerLightEffectEventArgs();
                case "rage__cutfVehicleExtraEventArgs": return new CutVehicleExtraEventArgs();
                case "rage__cutfFixupModelObject": return new CutFixupModelObject();
                case "cutf_float": return new CutFloat();
                case "cutf_int": return new CutInt();
                case "cutf_string": return new CutString();
                default: return null;
            }
        }
        public static T ReadObject<T>(XmlNode node, string name) where T : IMetaXmlItem, new()
        {
            var onode = node.SelectSingleNode(name);
            if (onode != null)
            {
                var o = new T();
                o.ReadXml(onode);
                return o;
            }
            return default(T);
        }
        public static object[] ReadObjectArray(XmlNode node, string name)
        {
            var aNode = node.SelectSingleNode(name);
            if (aNode != null)
            {
                var inodes = aNode.SelectNodes("Item");
                if (inodes?.Count > 0)
                {
                    var oList = new List<object>();
                    foreach (XmlNode inode in inodes)
                    {
                        var type = Xml.GetStringAttribute(inode, "type");
                        var o = ConstructObject(type);
                        o.ReadXml(inode);
                        oList.Add(o);
                    }
                    return oList.ToArray();
                }
            }
            return null;
        }

    }

    [TC(typeof(EXP))] public class CutParAttributeList : CutBase
    {
        public byte UserData1 { get; set; }
        public byte UserData2 { get; set; }

        public override void ReadXml(XmlNode node)
        {
            UserData1 = (byte)Xml.GetChildUIntAttribute(node, "UserData1", "value");
            UserData2 = (byte)Xml.GetChildUIntAttribute(node, "UserData2", "value");

            if ((UserData1 != 0) || (UserData2 != 0))
            { }
        }

        public override string ToString()
        {
            return UserData1.ToString() + ", " + UserData2.ToString();
        }
    }

    [TC(typeof(EXP))] public class CutFAttributeList : CutBase
    {
        public object[] Items { get; set; }

        public override void ReadXml(XmlNode node)
        {
            Items = CutsceneFile2.ReadObjectArray(node, "Items");

            if (Items?.Length > 0)
            { }
        }

        public override string ToString()
        {
            return (Items?.Length ?? 0).ToString() + " items";
        }
    }
    [TC(typeof(EXP))] public class CutInt : CutBase
    {
        public MetaHash Name { get; set; }
        public int Value { get; set; }

        public override void ReadXml(XmlNode node)
        {
            Name = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Name"));
            Value = Xml.GetChildIntAttribute(node, "Value", "value");
        }

        public override string ToString()
        {
            return Name.ToString() + ": " + Value.ToString();
        }
    }
    [TC(typeof(EXP))] public class CutFloat : CutBase
    {
        public MetaHash Name { get; set; }
        public float Value { get; set; }

        public override void ReadXml(XmlNode node)
        {
            Name = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Name"));
            Value = Xml.GetChildFloatAttribute(node, "Value", "value");
        }

        public override string ToString()
        {
            return Name.ToString() + ": " + Value.ToString();
        }
    }
    [TC(typeof(EXP))] public class CutString : CutBase
    {
        public MetaHash Name { get; set; }
        public string Value { get; set; }

        public override void ReadXml(XmlNode node)
        {
            Name = XmlMeta.GetHash(Xml.GetChildInnerText(node, "Name"));
            Value = Xml.GetChildInnerText(node, "Value");
        }

        public override string ToString()
        {
            return Name.ToString() + ": " + Value.ToString();
        }
    }

    [TC(typeof(EXP))] public class CutConcatData : CutBase
    {
        public MetaHash cSceneName { get; set; }
        public Vector3 vOffset { get; set; }
        public float fStartTime { get; set; }
        public float fRotation { get; set; }
        public float fPitch { get; set; }
        public float fRoll { get; set; }
        public int iRangeStart { get; set; }
        public int iRangeEnd { get; set; }
        public bool bValidForPlayBack { get; set; }

        public override void ReadXml(XmlNode node)
        {
            cSceneName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cSceneName"));
            vOffset = Xml.GetChildVector3Attributes(node, "vOffset");
            fStartTime = Xml.GetChildFloatAttribute(node, "fStartTime", "value");
            fRotation = Xml.GetChildFloatAttribute(node, "fRotation", "value");
            fPitch = Xml.GetChildFloatAttribute(node, "fPitch", "value");
            fRoll = Xml.GetChildFloatAttribute(node, "fRoll", "value");
            iRangeStart = Xml.GetChildIntAttribute(node, "iRangeStart", "value");
            iRangeEnd = Xml.GetChildIntAttribute(node, "iRangeEnd", "value");
            bValidForPlayBack = Xml.GetChildBoolAttribute(node, "bValidForPlayBack", "value");
        }
    }

    [TC(typeof(EXP))] public class CutHaltFrequency : CutBase
    {
        public MetaHash cSceneName { get; set; }
        public int[] frames { get; set; }

        public override void ReadXml(XmlNode node)
        {
            cSceneName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cSceneName"));
            frames = Xml.GetChildRawIntArray(node, "frames");
        }
    }

    [TC(typeof(EXP))] public abstract class CutObject : CutBase
    {
        public int iObjectId { get; set; }
        public CutParAttributeList attributeList { get; set; }
        public CutFAttributeList cutfAttributes { get; set; }

        public override void ReadXml(XmlNode node)
        {
            iObjectId = Xml.GetChildIntAttribute(node, "iObjectId", "value");
            attributeList = CutsceneFile2.ReadObject<CutParAttributeList>(node, "attributeList");
            cutfAttributes = CutsceneFile2.ReadObject<CutFAttributeList>(node, "cutfAttributes");
        }

        public override string ToString()
        {
            return iObjectId.ToString() + ": " + base.ToString().Replace("CodeWalker.GameFiles.Cut", "");
        }
    }
    [TC(typeof(EXP))] public class CutAssetManagerObject : CutObject
    {
    }
    [TC(typeof(EXP))] public class CutAnimationManagerObject : CutObject
    {
    }
    [TC(typeof(EXP))] public abstract class CutNamedObject : CutObject
    {
        public MetaHash cName { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            cName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cName"));
        }

        public override string ToString()
        {
            return base.ToString() + ": " + cName.ToString();
        }
    }
    [TC(typeof(EXP))] public class CutCameraObject : CutNamedObject
    {
        public uint AnimStreamingBase { get; set; }
        public float fNearDrawDistance { get; set; }
        public float fFarDrawDistance { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
            fNearDrawDistance = Xml.GetChildFloatAttribute(node, "fNearDrawDistance", "value");
            fFarDrawDistance = Xml.GetChildFloatAttribute(node, "fFarDrawDistance", "value");
        }
    }
    [TC(typeof(EXP))] public class CutPedModelObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public uint AnimStreamingBase { get; set; }
        public MetaHash cAnimExportCtrlSpecFile { get; set; }
        public MetaHash cFaceExportCtrlSpecFile { get; set; }
        public MetaHash cAnimCompressionFile { get; set; }
        public MetaHash cHandle { get; set; }
        public uint Unk_673165049 { get; set; }
        public MetaHash typeFile { get; set; }
        public MetaHash overrideFaceAnimationFilename { get; set; }
        public bool bFoundFaceAnimation { get; set; }
        public bool bFaceAndBodyAreMerged { get; set; }
        public bool bOverrideFaceAnimation { get; set; }
        public MetaHash faceAnimationNodeName { get; set; }
        public MetaHash faceAttributesFilename { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
            cAnimExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimExportCtrlSpecFile"));
            cFaceExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cFaceExportCtrlSpecFile"));
            cAnimCompressionFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimCompressionFile"));
            cHandle = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cHandle"));
            Unk_673165049 = Xml.GetChildUIntAttribute(node, "hash_281FAEF9", "value");
            typeFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "typeFile"));
            overrideFaceAnimationFilename = XmlMeta.GetHash(Xml.GetChildInnerText(node, "overrideFaceAnimationFilename"));
            bFoundFaceAnimation = Xml.GetChildBoolAttribute(node, "bFoundFaceAnimation", "value");
            bFaceAndBodyAreMerged = Xml.GetChildBoolAttribute(node, "bFaceAndBodyAreMerged", "value");
            bOverrideFaceAnimation = Xml.GetChildBoolAttribute(node, "bOverrideFaceAnimation", "value");
            faceAnimationNodeName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "faceAnimationNodeName"));
            faceAttributesFilename = XmlMeta.GetHash(Xml.GetChildInnerText(node, "faceAttributesFilename"));
        }
    }
    [TC(typeof(EXP))] public class CutPropModelObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public uint AnimStreamingBase { get; set; }
        public MetaHash cAnimExportCtrlSpecFile { get; set; }
        public MetaHash cFaceExportCtrlSpecFile { get; set; }
        public MetaHash cAnimCompressionFile { get; set; }
        public MetaHash cHandle { get; set; }
        public MetaHash typeFile { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
            cAnimExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimExportCtrlSpecFile"));
            cFaceExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cFaceExportCtrlSpecFile"));
            cAnimCompressionFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimCompressionFile"));
            cHandle = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cHandle"));
            typeFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "typeFile"));
        }
    }
    [TC(typeof(EXP))] public class CutBlockingBoundsObject : CutNamedObject
    {
        public Vector3[] vCorners { get; set; }
        public float fHeight { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vCorners = Xml.GetChildRawVector3Array(node, "vCorners");
            fHeight = Xml.GetChildFloatAttribute(node, "fHeight", "value");
        }
    }
    [TC(typeof(EXP))] public class CutAudioObject : CutNamedObject
    {
        public float fOffset { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            fOffset = Xml.GetChildFloatAttribute(node, "fOffset", "value");
        }
    }
    [TC(typeof(EXP))] public class CutHiddenModelObject : CutNamedObject
    {
        public Vector3 vPosition { get; set; }
        public float fRadius { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vPosition = Xml.GetChildVector3Attributes(node, "vPosition");
            fRadius = Xml.GetChildFloatAttribute(node, "fRadius", "value");
        }
    }
    [TC(typeof(EXP))] public class CutOverlayObject : CutNamedObject
    {
        public string cRenderTargetName { get; set; }
        public uint iOverlayType { get; set; }
        public MetaHash modelHashName { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            cRenderTargetName = Xml.GetChildInnerText(node, "cRenderTargetName");
            iOverlayType = Xml.GetChildUIntAttribute(node, "iOverlayType", "value");
            modelHashName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "modelHashName"));
        }
    }
    [TC(typeof(EXP))] public class CutSubtitleObject : CutNamedObject
    {
    }
    [TC(typeof(EXP))] public class CutLightObject : CutNamedObject
    {
        public Vector3 vDirection { get; set; }
        public Vector3 vColour { get; set; }
        public Vector3 vPosition { get; set; }
        public float fIntensity { get; set; }
        public float fFallOff { get; set; }
        public float fConeAngle { get; set; }
        public float fVolumeIntensity { get; set; }
        public float fVolumeSizeScale { get; set; }
        public float fCoronaSize { get; set; }
        public float fCoronaIntensity { get; set; }
        public float fCoronaZBias { get; set; }
        public float fInnerConeAngle { get; set; }
        public float fExponentialFallOff { get; set; }
        public float fShadowBlur { get; set; }
        public int iLightType { get; set; }
        public int iLightProperty { get; set; }
        public int TextureDictID { get; set; }
        public int TextureKey { get; set; }
        public uint uLightFlags { get; set; }
        public uint uHourFlags { get; set; }
        public bool bStatic { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vDirection = Xml.GetChildVector3Attributes(node, "vDirection");
            vColour = Xml.GetChildVector3Attributes(node, "vColour");
            vPosition = Xml.GetChildVector3Attributes(node, "vPosition");
            fIntensity = Xml.GetChildFloatAttribute(node, "fIntensity", "value");
            fFallOff = Xml.GetChildFloatAttribute(node, "fFallOff", "value");
            fConeAngle = Xml.GetChildFloatAttribute(node, "fConeAngle", "value");
            fVolumeIntensity = Xml.GetChildFloatAttribute(node, "fVolumeIntensity", "value");
            fVolumeSizeScale = Xml.GetChildFloatAttribute(node, "fVolumeSizeScale", "value");
            fCoronaSize = Xml.GetChildFloatAttribute(node, "fCoronaSize", "value");
            fCoronaIntensity = Xml.GetChildFloatAttribute(node, "fCoronaIntensity", "value");
            fCoronaZBias = Xml.GetChildFloatAttribute(node, "fCoronaZBias", "value");
            fInnerConeAngle = Xml.GetChildFloatAttribute(node, "fInnerConeAngle", "value");
            fExponentialFallOff = Xml.GetChildFloatAttribute(node, "fExponentialFallOff", "value");
            fShadowBlur = Xml.GetChildFloatAttribute(node, "fShadowBlur", "value");
            iLightType = Xml.GetChildIntAttribute(node, "iLightType", "value");
            iLightProperty = Xml.GetChildIntAttribute(node, "iLightProperty", "value");
            TextureDictID = Xml.GetChildIntAttribute(node, "TextureDictID", "value");
            TextureKey = Xml.GetChildIntAttribute(node, "TextureKey", "value");
            uLightFlags = Xml.GetChildUIntAttribute(node, "uLightFlags", "value");
            uHourFlags = Xml.GetChildUIntAttribute(node, "uHourFlags", "value");
            bStatic = Xml.GetChildBoolAttribute(node, "bStatic", "value");
        }
    }
    [TC(typeof(EXP))] public class CutAnimatedLightObject : CutNamedObject
    {
        public Vector3 vDirection { get; set; }
        public Vector3 vColour { get; set; }
        public Vector3 vPosition { get; set; }
        public float fIntensity { get; set; }
        public float fFallOff { get; set; }
        public float fConeAngle { get; set; }
        public float fVolumeIntensity { get; set; }
        public float fVolumeSizeScale { get; set; }
        public float fCoronaSize { get; set; }
        public float fCoronaIntensity { get; set; }
        public float fCoronaZBias { get; set; }
        public float fInnerConeAngle { get; set; }
        public float fExponentialFallOff { get; set; }
        public float fShadowBlur { get; set; }
        public int iLightType { get; set; }
        public int iLightProperty { get; set; }
        public int TextureDictID { get; set; }
        public int TextureKey { get; set; }
        public uint uLightFlags { get; set; }
        public uint uHourFlags { get; set; }
        public bool bStatic { get; set; }
        public uint AnimStreamingBase { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vDirection = Xml.GetChildVector3Attributes(node, "vDirection");
            vColour = Xml.GetChildVector3Attributes(node, "vColour");
            vPosition = Xml.GetChildVector3Attributes(node, "vPosition");
            fIntensity = Xml.GetChildFloatAttribute(node, "fIntensity", "value");
            fFallOff = Xml.GetChildFloatAttribute(node, "fFallOff", "value");
            fConeAngle = Xml.GetChildFloatAttribute(node, "fConeAngle", "value");
            fVolumeIntensity = Xml.GetChildFloatAttribute(node, "fVolumeIntensity", "value");
            fVolumeSizeScale = Xml.GetChildFloatAttribute(node, "fVolumeSizeScale", "value");
            fCoronaSize = Xml.GetChildFloatAttribute(node, "fCoronaSize", "value");
            fCoronaIntensity = Xml.GetChildFloatAttribute(node, "fCoronaIntensity", "value");
            fCoronaZBias = Xml.GetChildFloatAttribute(node, "fCoronaZBias", "value");
            fInnerConeAngle = Xml.GetChildFloatAttribute(node, "fInnerConeAngle", "value");
            fExponentialFallOff = Xml.GetChildFloatAttribute(node, "fExponentialFallOff", "value");
            fShadowBlur = Xml.GetChildFloatAttribute(node, "fShadowBlur", "value");
            iLightType = Xml.GetChildIntAttribute(node, "iLightType", "value");
            iLightProperty = Xml.GetChildIntAttribute(node, "iLightProperty", "value");
            TextureDictID = Xml.GetChildIntAttribute(node, "TextureDictID", "value");
            TextureKey = Xml.GetChildIntAttribute(node, "TextureKey", "value");
            uLightFlags = Xml.GetChildUIntAttribute(node, "uLightFlags", "value");
            uHourFlags = Xml.GetChildUIntAttribute(node, "uHourFlags", "value");
            bStatic = Xml.GetChildBoolAttribute(node, "bStatic", "value");
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
        }
    }
    [TC(typeof(EXP))] public class CutVehicleModelObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public uint AnimStreamingBase { get; set; }
        public MetaHash cAnimExportCtrlSpecFile { get; set; }
        public MetaHash cFaceExportCtrlSpecFile { get; set; }
        public MetaHash cAnimCompressionFile { get; set; }
        public MetaHash cHandle { get; set; }
        public MetaHash typeFile { get; set; }
        public string[] cRemoveBoneNameList { get; set; }
        public bool bCanApplyRealDamage { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
            cAnimExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimExportCtrlSpecFile"));
            cFaceExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cFaceExportCtrlSpecFile"));
            cAnimCompressionFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimCompressionFile"));
            cHandle = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cHandle"));
            typeFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "typeFile"));
            cRemoveBoneNameList = XmlMeta.ReadStringItemArray(node, "cRemoveBoneNameList");
            bCanApplyRealDamage = Xml.GetChildBoolAttribute(node, "bCanApplyRealDamage", "value");
        }
    }
    [TC(typeof(EXP))] public class CutWeaponModelObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public uint AnimStreamingBase { get; set; }
        public MetaHash cAnimExportCtrlSpecFile { get; set; }
        public MetaHash cFaceExportCtrlSpecFile { get; set; }
        public MetaHash cAnimCompressionFile { get; set; }
        public MetaHash cHandle { get; set; }
        public MetaHash typeFile { get; set; }
        public uint GenericWeaponType { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
            cAnimExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimExportCtrlSpecFile"));
            cFaceExportCtrlSpecFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cFaceExportCtrlSpecFile"));
            cAnimCompressionFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cAnimCompressionFile"));
            cHandle = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cHandle"));
            typeFile = XmlMeta.GetHash(Xml.GetChildInnerText(node, "typeFile"));
            GenericWeaponType = Xml.GetChildUIntAttribute(node, "GenericWeaponType", "value");
        }
    }
    [TC(typeof(EXP))] public class CutRayfireObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public Vector3 vStartPosition { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            vStartPosition = Xml.GetChildVector3Attributes(node, "vStartPosition");
        }
    }
    [TC(typeof(EXP))] public class CutParticleEffectObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public MetaHash athFxListHash { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            athFxListHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "athFxListHash"));
        }
    }
    [TC(typeof(EXP))] public class CutAnimatedParticleEffectObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public uint AnimStreamingBase { get; set; }
        public MetaHash athFxListHash { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            AnimStreamingBase = Xml.GetChildUIntAttribute(node, "AnimStreamingBase", "value");
            athFxListHash = XmlMeta.GetHash(Xml.GetChildInnerText(node, "athFxListHash"));
        }
    }
    [TC(typeof(EXP))] public class CutDecalObject : CutNamedObject
    {
        public MetaHash StreamingName { get; set; }
        public uint RenderId { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            StreamingName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "StreamingName"));
            RenderId = Xml.GetChildUIntAttribute(node, "RenderId", "value");
        }
    }
    [TC(typeof(EXP))] public class CutScreenFadeObject : CutNamedObject
    {
    }
    [TC(typeof(EXP))] public class CutFixupModelObject : CutNamedObject
    {
        public Vector3 vPosition { get; set; }
        public float fRadius { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vPosition = Xml.GetChildVector3Attributes(node, "vPosition");
            fRadius = Xml.GetChildFloatAttribute(node, "fRadius", "value");
        }
    }

    public enum CutEventType : int
    {
        LoadScene = 0,
        LoadAnimation = 2,
        LoadAudio = 4,
        LoadModels = 6,
        UnloadModels = 7,
        LoadParticles = 8,
        LoadOverlays = 10,
        LoadGxt2 = 12,
        EnableHideObject = 14,
        DisableHideObject = 15,
        EnableFixupModel = 16,
        EnableBlockBounds = 18,
        DisableBlockBounds = 19,
        EnableScreenFade = 20,
        DisableScreenFade = 21,
        EnableAnimation = 22,
        DisableAnimation = 23,
        EnableParticleEffect = 24,
        DisableParticleEffect = 25,
        EnableOverlay = 26,
        DisableOverlay = 27,
        EnableAudio = 28,
        DisableAudio = 29,
        Subtitle = 30,
        PedVariation = 34,
        CameraCut = 43,
        LoadRayfireDes = 46,
        UnloadRayfireDes = 47,
        EnableCamera = 48,
        CameraUnk1 = 49,
        CameraUnk2 = 50,
        DisableCamera = 51,
        DecalUnk1 = 52,
        DecalUnk2 = 53,
        CameraShadowCascade = 54,
        CameraUnk3 = 55,
        CameraUnk4 = 59,
        CameraUnk5 = 63,
        CameraUnk6 = 64,
        PropUnk1 = 73,
        EnableLight = 74,
        DisableLight = 75,
        CameraUnk7 = 76,
        Unk1 = 77,
        Unk2 = 78,
        CameraUnk8 = 79,
        VehicleUnk1 = 258,
        PedUnk1 = 262,
    }
    [TC(typeof(EXP))] public class CutEvent : CutBase
    {
        public float fTime { get; set; }
        public CutEventType iEventId { get; set; }
        public int iEventArgsIndex { get; set; }
        public uint StickyId { get; set; }
        public bool IsChild { get; set; }

        public CutEventArgs EventArgs { get; set; }

        public override void ReadXml(XmlNode node)
        {
            fTime = Xml.GetChildFloatAttribute(node, "fTime", "value");
            iEventId = (CutEventType)Xml.GetChildIntAttribute(node, "iEventId", "value");
            iEventArgsIndex = Xml.GetChildIntAttribute(node, "iEventArgsIndex", "value");
            StickyId = Xml.GetChildUIntAttribute(node, "StickyId", "value");
            IsChild = Xml.GetChildBoolAttribute(node, "IsChild", "value");

            var cNode = node.SelectSingleNode("pChildEvents");
            if ((cNode?.ChildNodes?.Count > 0) || (cNode?.Attributes?.Count > 0))
            { }
        }

        public override string ToString()
        {
            return fTime.ToString("0.00") + " : " + iEventId.ToString();
        }
    }
    [TC(typeof(EXP))] public class CutObjectIdEvent : CutEvent
    {
        public int iObjectId { get; set; }

        public CutObject Object { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iObjectId = Xml.GetChildIntAttribute(node, "iObjectId", "value");
        }
    }

    [TC(typeof(EXP))] public class CutEventArgs : CutBase
    {
        public CutParAttributeList attributeList { get; set; }
        public CutFAttributeList cutfAttributes { get; set; }

        public override void ReadXml(XmlNode node)
        {
            attributeList = CutsceneFile2.ReadObject<CutParAttributeList>(node, "attributeList");
            cutfAttributes = CutsceneFile2.ReadObject<CutFAttributeList>(node, "cutfAttributes");
        }
    }
    [TC(typeof(EXP))] public class CutNameEventArgs : CutEventArgs
    {
        public MetaHash cName { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            cName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cName"));
        }
    }
    [TC(typeof(EXP))] public class CutFinalNameEventArgs : CutEventArgs
    {
        public string cName { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            cName = Xml.GetChildInnerText(node, "cName");
        }
    }
    [TC(typeof(EXP))] public class CutObjectIdEventArgs : CutEventArgs
    {
        public int iObjectId { get; set; }

        public CutObject Object { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iObjectId = Xml.GetChildIntAttribute(node, "iObjectId", "value");
        }
    }
    [TC(typeof(EXP))] public class CutObjectIdListEventArgs : CutEventArgs
    {
        public int[] iObjectIdList { get; set; }

        public CutObject[] ObjectList { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iObjectIdList = Xml.GetChildRawIntArray(node, "iObjectIdList");
        }
    }
    [TC(typeof(EXP))] public class CutFloatValueEventArgs : CutEventArgs
    {
        public float fValue { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            fValue = Xml.GetChildFloatAttribute(node, "fValue", "value");
        }
    }
    [TC(typeof(EXP))] public class CutBoolValueEventArgs : CutEventArgs
    {
        public bool bValue { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            bValue = Xml.GetChildBoolAttribute(node, "bValue", "value");
        }
    }

    [TC(typeof(EXP))] public class CutLoadSceneEventArgs : CutNameEventArgs
    {
        public Vector3 vOffset { get; set; }
        public float fRotation { get; set; }
        public float fPitch { get; set; }
        public float fRoll { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vOffset = Xml.GetChildVector3Attributes(node, "vOffset");
            fRotation = Xml.GetChildFloatAttribute(node, "fRotation", "value");
            fPitch = Xml.GetChildFloatAttribute(node, "fPitch", "value");
            fRoll = Xml.GetChildFloatAttribute(node, "fRoll", "value");
        }
    }
    [TC(typeof(EXP))] public class CutSubtitleEventArgs : CutNameEventArgs
    {
        public int iLanguageID { get; set; }
        public int iTransitionIn { get; set; }
        public float fTransitionInDuration { get; set; }
        public int iTransitionOut { get; set; }
        public float fTransitionOutDuration { get; set; }
        public float fSubtitleDuration { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iLanguageID = Xml.GetChildIntAttribute(node, "iLanguageID", "value");
            iTransitionIn = Xml.GetChildIntAttribute(node, "iTransitionIn", "value");
            fTransitionInDuration = Xml.GetChildFloatAttribute(node, "fTransitionInDuration", "value");
            iTransitionOut = Xml.GetChildIntAttribute(node, "iTransitionOut", "value");
            fTransitionOutDuration = Xml.GetChildFloatAttribute(node, "fTransitionOutDuration", "value");
            fSubtitleDuration = Xml.GetChildFloatAttribute(node, "fSubtitleDuration", "value");
        }
    }
    [TC(typeof(EXP))] public class CutCameraCutEventArgs : CutNameEventArgs
    {
        public Vector3 vPosition { get; set; }
        public Quaternion vRotationQuaternion { get; set; }
        public float fNearDrawDistance { get; set; }
        public float fFarDrawDistance { get; set; }
        public float fMapLodScale { get; set; }
        public float ReflectionLodRangeStart { get; set; }
        public float ReflectionLodRangeEnd { get; set; }
        public float ReflectionSLodRangeStart { get; set; }
        public float ReflectionSLodRangeEnd { get; set; }
        public float LodMultHD { get; set; }
        public float LodMultOrphanedHD { get; set; }
        public float LodMultLod { get; set; }
        public float LodMultSLod1 { get; set; }
        public float LodMultSLod2 { get; set; }
        public float LodMultSLod3 { get; set; }
        public float LodMultSLod4 { get; set; }
        public float WaterReflectionFarClip { get; set; }
        public float SSAOLightInten { get; set; }
        public float ExposurePush { get; set; }
        public float LightFadeDistanceMult { get; set; }
        public float LightShadowFadeDistanceMult { get; set; }
        public float LightSpecularFadeDistMult { get; set; }
        public float LightVolumetricFadeDistanceMult { get; set; }
        public float DirectionalLightMultiplier { get; set; }
        public float LensArtefactMultiplier { get; set; }
        public float BloomMax { get; set; }
        public bool DisableHighQualityDof { get; set; }
        public bool FreezeReflectionMap { get; set; }
        public bool DisableDirectionalLighting { get; set; }
        public bool AbsoluteIntensityEnabled { get; set; }
        public CutCameraCutCharacterLightParams CharacterLight { get; set; }
        public CutCameraCutTimeOfDayDofModifier[] TimeOfDayDofModifers { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vPosition = Xml.GetChildVector3Attributes(node, "vPosition");
            vRotationQuaternion = Xml.GetChildVector4Attributes(node, "vRotationQuaternion").ToQuaternion();
            fNearDrawDistance = Xml.GetChildFloatAttribute(node, "fNearDrawDistance", "value");
            fFarDrawDistance = Xml.GetChildFloatAttribute(node, "fFarDrawDistance", "value");
            fMapLodScale = Xml.GetChildFloatAttribute(node, "fMapLodScale", "value");
            ReflectionLodRangeStart = Xml.GetChildFloatAttribute(node, "ReflectionLodRangeStart", "value");
            ReflectionLodRangeEnd = Xml.GetChildFloatAttribute(node, "ReflectionLodRangeEnd", "value");
            ReflectionSLodRangeStart = Xml.GetChildFloatAttribute(node, "ReflectionSLodRangeStart", "value");
            ReflectionSLodRangeEnd = Xml.GetChildFloatAttribute(node, "ReflectionSLodRangeEnd", "value");
            LodMultHD = Xml.GetChildFloatAttribute(node, "LodMultHD", "value");
            LodMultOrphanedHD = Xml.GetChildFloatAttribute(node, "LodMultOrphanedHD", "value");
            LodMultLod = Xml.GetChildFloatAttribute(node, "LodMultLod", "value");
            LodMultSLod1 = Xml.GetChildFloatAttribute(node, "LodMultSLod1", "value");
            LodMultSLod2 = Xml.GetChildFloatAttribute(node, "LodMultSLod2", "value");
            LodMultSLod3 = Xml.GetChildFloatAttribute(node, "LodMultSLod3", "value");
            LodMultSLod4 = Xml.GetChildFloatAttribute(node, "LodMultSLod4", "value");
            WaterReflectionFarClip = Xml.GetChildFloatAttribute(node, "WaterReflectionFarClip", "value");
            SSAOLightInten = Xml.GetChildFloatAttribute(node, "SSAOLightInten", "value");
            ExposurePush = Xml.GetChildFloatAttribute(node, "ExposurePush", "value");
            LightFadeDistanceMult = Xml.GetChildFloatAttribute(node, "LightFadeDistanceMult", "value");
            LightShadowFadeDistanceMult = Xml.GetChildFloatAttribute(node, "LightShadowFadeDistanceMult", "value");
            LightSpecularFadeDistMult = Xml.GetChildFloatAttribute(node, "LightSpecularFadeDistMult", "value");
            LightVolumetricFadeDistanceMult = Xml.GetChildFloatAttribute(node, "LightVolumetricFadeDistanceMult", "value");
            DirectionalLightMultiplier = Xml.GetChildFloatAttribute(node, "DirectionalLightMultiplier", "value");
            LensArtefactMultiplier = Xml.GetChildFloatAttribute(node, "LensArtefactMultiplier", "value");
            BloomMax = Xml.GetChildFloatAttribute(node, "BloomMax", "value");
            DisableHighQualityDof = Xml.GetChildBoolAttribute(node, "DisableHighQualityDof", "value");
            FreezeReflectionMap = Xml.GetChildBoolAttribute(node, "FreezeReflectionMap", "value");
            DisableDirectionalLighting = Xml.GetChildBoolAttribute(node, "DisableDirectionalLighting", "value");
            AbsoluteIntensityEnabled = Xml.GetChildBoolAttribute(node, "AbsoluteIntensityEnabled", "value");
            CharacterLight = CutsceneFile2.ReadObject<CutCameraCutCharacterLightParams>(node, "CharacterLight");
            TimeOfDayDofModifers = XmlMeta.ReadItemArrayNullable<CutCameraCutTimeOfDayDofModifier>(node, "TimeOfDayDofModifers");
        }
    }
    [TC(typeof(EXP))] public class CutCameraCutCharacterLightParams : CutBase
    {
        public bool bUseTimeCycleValues { get; set; }
        public Vector3 vDirection { get; set; }
        public Vector3 vColour { get; set; }
        public float fIntensity { get; set; }

        public override void ReadXml(XmlNode node)
        {
            bUseTimeCycleValues = Xml.GetChildBoolAttribute(node, "bUseTimeCycleValues", "value");
            vDirection = Xml.GetChildVector3Attributes(node, "vDirection");
            vColour = Xml.GetChildVector3Attributes(node, "vColour");
            fIntensity = Xml.GetChildFloatAttribute(node, "fIntensity", "value");
        }
    }
    [TC(typeof(EXP))] public class CutCameraCutTimeOfDayDofModifier : CutBase
    {
    }

    [TC(typeof(EXP))] public class CutObjectIdNameEventArgs : CutObjectIdEventArgs
    {
        public MetaHash cName { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            cName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cName"));
        }
    }
    [TC(typeof(EXP))] public class CutObjectVariationEventArgs : CutObjectIdEventArgs
    {
        public int iComponent { get; set; }
        public int iDrawable { get; set; }
        public int iTexture { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iComponent = Xml.GetChildIntAttribute(node, "iComponent", "value");
            iDrawable = Xml.GetChildIntAttribute(node, "iDrawable", "value");
            iTexture = Xml.GetChildIntAttribute(node, "iTexture", "value");
        }
    }
    [TC(typeof(EXP))] public class CutVehicleVariationEventArgs : CutObjectIdEventArgs
    {
        public int iMainBodyColour { get; set; }
        public int iSecondBodyColour { get; set; }
        public int iSpecularColour { get; set; }
        public int iWheelTrimColour { get; set; }
        public int Unk_2747538743 { get; set; }
        public int iLivery { get; set; }
        public int iLivery2 { get; set; }
        public float fDirtLevel { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iMainBodyColour = Xml.GetChildIntAttribute(node, "iMainBodyColour", "value");
            iSecondBodyColour = Xml.GetChildIntAttribute(node, "iSecondBodyColour", "value");
            iSpecularColour = Xml.GetChildIntAttribute(node, "iSpecularColour", "value");
            iWheelTrimColour = Xml.GetChildIntAttribute(node, "iWheelTrimColour", "value");
            Unk_2747538743 = Xml.GetChildIntAttribute(node, "hash_A3C41D37", "value");
            iLivery = Xml.GetChildIntAttribute(node, "iLivery", "value");
            iLivery2 = Xml.GetChildIntAttribute(node, "iLivery2", "value");
            fDirtLevel = Xml.GetChildFloatAttribute(node, "fDirtLevel", "value");
        }
    }
    [TC(typeof(EXP))] public class CutVehicleExtraEventArgs : CutObjectIdEventArgs
    {
        public int[] pExtraBoneIds { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            pExtraBoneIds = Xml.GetChildRawIntArray(node, "pExtraBoneIds");
        }
    }

    [TC(typeof(EXP))] public class CutDecalEventArgs : CutEventArgs
    {
        public Vector3 vPosition { get; set; }
        public Quaternion vRotation { get; set; }
        public float fWidth { get; set; }
        public float fHeight { get; set; }
        public uint Colour { get; set; }
        public float fLifeTime { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vPosition = Xml.GetChildVector3Attributes(node, "vPosition");
            vRotation = Xml.GetChildVector4Attributes(node, "vRotation").ToQuaternion();
            fWidth = Xml.GetChildFloatAttribute(node, "fWidth", "value");
            fHeight = Xml.GetChildFloatAttribute(node, "fHeight", "value");
            Colour = Xml.GetChildUIntAttribute(node, "Colour", "value");
            fLifeTime = Xml.GetChildFloatAttribute(node, "fLifeTime", "value");
        }
    }
    [TC(typeof(EXP))] public class CutScreenFadeEventArgs : CutEventArgs
    {
        public float fValue { get; set; }
        public uint color { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            fValue = Xml.GetChildFloatAttribute(node, "fValue", "value");
            color = Xml.GetChildUIntAttribute(node, "color", "value");
        }
    }
    [TC(typeof(EXP))] public class CutCascadeShadowEventArgs : CutEventArgs
    {
        public MetaHash cameraCutHashName { get; set; }
        public Vector3 position { get; set; }
        public float radius { get; set; }
        public float interpTime { get; set; }
        public int cascadeIndex { get; set; }
        public bool enabled { get; set; }
        public bool interpolateToDisabled { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            cameraCutHashName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "cameraCutHashName"));
            position = Xml.GetChildVector3Attributes(node, "position");
            radius = Xml.GetChildFloatAttribute(node, "radius", "value");
            interpTime = Xml.GetChildFloatAttribute(node, "interpTime", "value");
            cascadeIndex = Xml.GetChildIntAttribute(node, "cascadeIndex", "value");
            enabled = Xml.GetChildBoolAttribute(node, "enabled", "value");
            interpolateToDisabled = Xml.GetChildBoolAttribute(node, "interpolateToDisabled", "value");
        }
    }
    [TC(typeof(EXP))] public class CutTriggerLightEffectEventArgs : CutEventArgs
    {
        public int iAttachParentId { get; set; }
        public ushort iAttachBoneHash { get; set; }
        public MetaHash AttachedParentName { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            iAttachParentId = Xml.GetChildIntAttribute(node, "iAttachParentId", "value");
            iAttachBoneHash = (ushort)Xml.GetChildUIntAttribute(node, "iAttachBoneHash", "value");
            AttachedParentName = XmlMeta.GetHash(Xml.GetChildInnerText(node, "AttachedParentName"));
        }
    }
    [TC(typeof(EXP))] public class CutPlayParticleEffectEventArgs : CutEventArgs
    {
        public Quaternion vInitialBoneRotation { get; set; }
        public Vector3 vInitialBoneOffset { get; set; }
        public int iAttachParentId { get; set; }
        public ushort iAttachBoneHash { get; set; }

        public override void ReadXml(XmlNode node)
        {
            base.ReadXml(node);
            vInitialBoneRotation = Xml.GetChildVector4Attributes(node, "vInitialBoneRotation").ToQuaternion();
            vInitialBoneOffset = Xml.GetChildVector3Attributes(node, "vInitialBoneOffset");
            iAttachParentId = Xml.GetChildIntAttribute(node, "iAttachParentId", "value");
            iAttachBoneHash = (ushort)Xml.GetChildUIntAttribute(node, "iAttachBoneHash", "value");
        }
    }

}
