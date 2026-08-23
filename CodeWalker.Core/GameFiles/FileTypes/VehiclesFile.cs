using SharpDX;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace CodeWalker.GameFiles
{
    public class VehiclesFile : GameFile, PackedFile
    {

        public string ResidentTxd { get; set; }
        public List<VehicleInitData> InitDatas { get; set; }
        public Dictionary<string, string> TxdRelationships { get; set; }

        public VehiclesFile() : base(null, GameFileType.Vehicles)
        {
        }
        public VehiclesFile(RpfFileEntry entry) : base(entry, GameFileType.Vehicles)
        {
        }

        public void Load(byte[] data, RpfFileEntry entry)
        {
            RpfFileEntry = entry;
            Name = entry.Name;
            FilePath = Name;

            if (entry.NameLower.EndsWith(".meta"))
            {
                string xml = TextUtil.GetUTF8Text(data);

                XmlDocument xmldoc = new XmlDocument();
                xmldoc.LoadXml(xml);

                ResidentTxd = Xml.GetChildInnerText(xmldoc.SelectSingleNode("CVehicleModelInfo__InitDataList"), "residentTxd");

                LoadInitDatas(xmldoc);

                LoadTxdRelationships(xmldoc);

                Loaded = true;
            }
        }

        private void LoadInitDatas(XmlDocument xmldoc)
        {
            XmlNodeList items = xmldoc.SelectNodes("CVehicleModelInfo__InitDataList/InitDatas/Item | CVehicleModelInfo__InitDataList/InitDatas/item");

            InitDatas = new List<VehicleInitData>();
            for (int i = 0; i < items.Count; i++)
            {
                var node = items[i];
                VehicleInitData d = new VehicleInitData();
                d.Load(node);
                InitDatas.Add(d);
            }
        }

        private void LoadTxdRelationships(XmlDocument xmldoc)
        {
            XmlNodeList items = xmldoc.SelectNodes("CVehicleModelInfo__InitDataList/txdRelationships/Item | CVehicleModelInfo__InitDataList/txdRelationships/item");

            TxdRelationships = new Dictionary<string, string>();
            for (int i = 0; i < items.Count; i++)
            {
                string parentstr = Xml.GetChildInnerText(items[i], "parent");
                string childstr = Xml.GetChildInnerText(items[i], "child");

                if ((!string.IsNullOrEmpty(parentstr)) && (!string.IsNullOrEmpty(childstr)))
                {
                    if (!TxdRelationships.ContainsKey(childstr))
                    {
                        TxdRelationships.Add(childstr, parentstr);
                    }
                    else
                    { }
                }
            }
        }

    }

    public class VehicleInitData
    {

        public string modelName { get; set; }
        public string txdName { get; set; }
        public string handlingId { get; set; }
        public string gameName { get; set; }
        public string vehicleMakeName { get; set; }
        public string expressionDictName { get; set; }
        public string expressionName { get; set; }
        public string animConvRoofDictName { get; set; }
        public string animConvRoofName { get; set; }
        public string animConvRoofWindowsAffected { get; set; }
        public string ptfxAssetName { get; set; }
        public string audioNameHash { get; set; }
        public string layout { get; set; }
        public string coverBoundOffsets { get; set; }
        public string explosionInfo { get; set; }
        public string scenarioLayout { get; set; }
        public string cameraName { get; set; }
        public string aimCameraName { get; set; }
        public string bonnetCameraName { get; set; }
        public string povCameraName { get; set; }
        public Vector3 FirstPersonDriveByIKOffset { get; set; }
        public Vector3 FirstPersonDriveByUnarmedIKOffset { get; set; }
        public Vector3 FirstPersonProjectileDriveByIKOffset { get; set; }
        public Vector3 FirstPersonProjectileDriveByPassengerIKOffset { get; set; }
        public Vector3 FirstPersonDriveByRightPassengerIKOffset { get; set; }
        public Vector3 FirstPersonDriveByRightPassengerUnarmedIKOffset { get; set; }
        public Vector3 FirstPersonMobilePhoneOffset { get; set; }
        public Vector3 FirstPersonPassengerMobilePhoneOffset { get; set; }
        public Vector3 PovCameraOffset { get; set; }
        public Vector3 PovCameraVerticalAdjustmentForRollCage { get; set; }
        public Vector3 PovPassengerCameraOffset { get; set; }
        public Vector3 PovRearPassengerCameraOffset { get; set; }
        public string vfxInfoName { get; set; }
        public bool shouldUseCinematicViewMode { get; set; }
        public bool shouldCameraTransitionOnClimbUpDown { get; set; }
        public bool shouldCameraIgnoreExiting { get; set; }
        public bool AllowPretendOccupants { get; set; }
        public bool AllowJoyriding { get; set; }
        public bool AllowSundayDriving { get; set; }
        public bool AllowBodyColorMapping { get; set; }
        public float wheelScale { get; set; }
        public float wheelScaleRear { get; set; }
        public float dirtLevelMin { get; set; }
        public float dirtLevelMax { get; set; }
        public float envEffScaleMin { get; set; }
        public float envEffScaleMax { get; set; }
        public float envEffScaleMin2 { get; set; }
        public float envEffScaleMax2 { get; set; }
        public float damageMapScale { get; set; }
        public float damageOffsetScale { get; set; }
        public Color4 diffuseTint { get; set; }
        public float steerWheelMult { get; set; }
        public float HDTextureDist { get; set; }
        public float[] lodDistances { get; set; }
        public float minSeatHeight { get; set; }
        public float identicalModelSpawnDistance { get; set; }
        public int maxNumOfSameColor { get; set; }
        public float defaultBodyHealth { get; set; }
        public float pretendOccupantsScale { get; set; }
        public float visibleSpawnDistScale { get; set; }
        public float trackerPathWidth { get; set; }
        public float weaponForceMult { get; set; }
        public float frequency { get; set; }
        public string swankness { get; set; }
        public int maxNum { get; set; }
        public string[] flags { get; set; }
        public string type { get; set; }
        public string plateType { get; set; }
        public string dashboardType { get; set; }
        public string vehicleClass { get; set; }
        public string wheelType { get; set; }
        public string[] trailers { get; set; }
        public string[] additionalTrailers { get; set; }
        public VehicleDriver[] drivers { get; set; }
        public string[] extraIncludes { get; set; }
        public string[] doorsWithCollisionWhenClosed { get; set; }
        public string[] driveableDoors { get; set; }
        public bool bumpersNeedToCollideWithMap { get; set; }
        public bool needsRopeTexture { get; set; }
        public string[] requiredExtras { get; set; }
        public string[] rewards { get; set; }
        public string[] cinematicPartCamera { get; set; }
        public string NmBraceOverrideSet { get; set; }
        public Vector3 buoyancySphereOffset { get; set; }
        public float buoyancySphereSizeScale { get; set; }
        public VehicleOverrideRagdollThreshold pOverrideRagdollThreshold { get; set; }
        public string[] firstPersonDrivebyData { get; set; }

        public void Load(XmlNode node)
        {
            modelName = Xml.GetChildInnerText(node, "modelName");
            txdName = Xml.GetChildInnerText(node, "txdName");
            handlingId = Xml.GetChildInnerText(node, "handlingId");
            gameName = Xml.GetChildInnerText(node, "gameName");
            vehicleMakeName = Xml.GetChildInnerText(node, "vehicleMakeName");
            expressionDictName = Xml.GetChildInnerText(node, "expressionDictName");
            expressionName = Xml.GetChildInnerText(node, "expressionName");
            animConvRoofDictName = Xml.GetChildInnerText(node, "animConvRoofDictName");
            animConvRoofName = Xml.GetChildInnerText(node, "animConvRoofName");
            animConvRoofWindowsAffected = Xml.GetChildInnerText(node, "animConvRoofWindowsAffected");
            ptfxAssetName = Xml.GetChildInnerText(node, "ptfxAssetName");
            audioNameHash = Xml.GetChildInnerText(node, "audioNameHash");
            layout = Xml.GetChildInnerText(node, "layout");
            coverBoundOffsets = Xml.GetChildInnerText(node, "coverBoundOffsets");
            explosionInfo = Xml.GetChildInnerText(node, "explosionInfo");
            scenarioLayout = Xml.GetChildInnerText(node, "scenarioLayout");
            cameraName = Xml.GetChildInnerText(node, "cameraName");
            aimCameraName = Xml.GetChildInnerText(node, "aimCameraName");
            bonnetCameraName = Xml.GetChildInnerText(node, "bonnetCameraName");
            povCameraName = Xml.GetChildInnerText(node, "povCameraName");
            FirstPersonDriveByIKOffset = Xml.GetChildVector3Attributes(node, "FirstPersonDriveByIKOffset");
            FirstPersonDriveByUnarmedIKOffset = Xml.GetChildVector3Attributes(node, "FirstPersonDriveByUnarmedIKOffset");
            FirstPersonProjectileDriveByIKOffset = Xml.GetChildVector3Attributes(node, "FirstPersonProjectileDriveByIKOffset");
            FirstPersonProjectileDriveByPassengerIKOffset = Xml.GetChildVector3Attributes(node, "FirstPersonProjectileDriveByPassengerIKOffset");
            FirstPersonDriveByRightPassengerIKOffset = Xml.GetChildVector3Attributes(node, "FirstPersonDriveByRightPassengerIKOffset");
            FirstPersonDriveByRightPassengerUnarmedIKOffset = Xml.GetChildVector3Attributes(node, "FirstPersonDriveByRightPassengerUnarmedIKOffset");
            FirstPersonMobilePhoneOffset = Xml.GetChildVector3Attributes(node, "FirstPersonMobilePhoneOffset");
            FirstPersonPassengerMobilePhoneOffset = Xml.GetChildVector3Attributes(node, "FirstPersonPassengerMobilePhoneOffset");
            PovCameraOffset = Xml.GetChildVector3Attributes(node, "PovCameraOffset");
            PovCameraVerticalAdjustmentForRollCage = Xml.GetChildVector3Attributes(node, "PovCameraVerticalAdjustmentForRollCage");
            PovPassengerCameraOffset = Xml.GetChildVector3Attributes(node, "PovPassengerCameraOffset");
            PovRearPassengerCameraOffset = Xml.GetChildVector3Attributes(node, "PovRearPassengerCameraOffset");
            vfxInfoName = Xml.GetChildInnerText(node, "vfxInfoName");
            shouldUseCinematicViewMode = Xml.GetChildBoolAttribute(node, "shouldUseCinematicViewMode", "value");
            shouldCameraTransitionOnClimbUpDown = Xml.GetChildBoolAttribute(node, "shouldCameraTransitionOnClimbUpDown", "value");
            shouldCameraIgnoreExiting = Xml.GetChildBoolAttribute(node, "shouldCameraIgnoreExiting", "value");
            AllowPretendOccupants = Xml.GetChildBoolAttribute(node, "AllowPretendOccupants", "value");
            AllowJoyriding = Xml.GetChildBoolAttribute(node, "AllowJoyriding", "value");
            AllowSundayDriving = Xml.GetChildBoolAttribute(node, "AllowSundayDriving", "value");
            AllowBodyColorMapping = Xml.GetChildBoolAttribute(node, "AllowBodyColorMapping", "value");
            wheelScale = Xml.GetChildFloatAttribute(node, "wheelScale", "value");
            wheelScaleRear = Xml.GetChildFloatAttribute(node, "wheelScaleRear", "value");
            dirtLevelMin = Xml.GetChildFloatAttribute(node, "dirtLevelMin", "value");
            dirtLevelMax = Xml.GetChildFloatAttribute(node, "dirtLevelMax", "value");
            envEffScaleMin = Xml.GetChildFloatAttribute(node, "envEffScaleMin", "value");
            envEffScaleMax = Xml.GetChildFloatAttribute(node, "envEffScaleMax", "value");
            envEffScaleMin2 = Xml.GetChildFloatAttribute(node, "envEffScaleMin2", "value");
            envEffScaleMax2 = Xml.GetChildFloatAttribute(node, "envEffScaleMax2", "value");
            damageMapScale = Xml.GetChildFloatAttribute(node, "damageMapScale", "value");
            damageOffsetScale = Xml.GetChildFloatAttribute(node, "damageOffsetScale", "value");
            diffuseTint = new Color4(Convert.ToUInt32(Xml.GetChildStringAttribute(node, "diffuseTint", "value").Replace("0x", ""), 16));
            steerWheelMult = Xml.GetChildFloatAttribute(node, "steerWheelMult", "value");
            HDTextureDist = Xml.GetChildFloatAttribute(node, "HDTextureDist", "value");
            lodDistances = GetFloatArray(node, "lodDistances", '\n');
            minSeatHeight = Xml.GetChildFloatAttribute(node, "minSeatHeight", "value");
            identicalModelSpawnDistance = Xml.GetChildFloatAttribute(node, "identicalModelSpawnDistance", "value");
            maxNumOfSameColor = Xml.GetChildIntAttribute(node, "maxNumOfSameColor", "value");
            defaultBodyHealth = Xml.GetChildFloatAttribute(node, "defaultBodyHealth", "value");
            pretendOccupantsScale = Xml.GetChildFloatAttribute(node, "pretendOccupantsScale", "value");
            visibleSpawnDistScale = Xml.GetChildFloatAttribute(node, "visibleSpawnDistScale", "value");
            trackerPathWidth = Xml.GetChildFloatAttribute(node, "trackerPathWidth", "value");
            weaponForceMult = Xml.GetChildFloatAttribute(node, "weaponForceMult", "value");
            frequency = Xml.GetChildFloatAttribute(node, "frequency", "value");
            swankness = Xml.GetChildInnerText(node, "swankness");
            maxNum = Xml.GetChildIntAttribute(node, "maxNum", "value");
            flags = GetStringArray(node, "flags", ' ');
            type = Xml.GetChildInnerText(node, "type");
            plateType = Xml.GetChildInnerText(node, "plateType");
            dashboardType = Xml.GetChildInnerText(node, "dashboardType");
            vehicleClass = Xml.GetChildInnerText(node, "vehicleClass");
            wheelType = Xml.GetChildInnerText(node, "wheelType");
            trailers = GetStringItemArray(node, "trailers");
            additionalTrailers = GetStringItemArray(node, "additionalTrailers");
            var dnode = node.SelectSingleNode("drivers");
            if (dnode != null)
            {
                var items = dnode.SelectNodes("Item");
                if (items.Count > 0)
                {
                    drivers = new VehicleDriver[items.Count];
                    for (int i = 0; i < items.Count; i++)
                    {
                        var item = items[i];
                        var driver = new VehicleDriver();
                        driver.driverName = Xml.GetChildInnerText(item, "driverName");
                        driver.npcName = Xml.GetChildInnerText(item, "npcName");
                        drivers[i] = driver;
                    }
                }
            }
            extraIncludes = GetStringItemArray(node, "extraIncludes");
            doorsWithCollisionWhenClosed = GetStringItemArray(node, "doorsWithCollisionWhenClosed");
            driveableDoors = GetStringItemArray(node, "driveableDoors");
            bumpersNeedToCollideWithMap = Xml.GetChildBoolAttribute(node, "bumpersNeedToCollideWithMap", "value");
            needsRopeTexture = Xml.GetChildBoolAttribute(node, "needsRopeTexture", "value");
            requiredExtras = GetStringArray(node, "requiredExtras", ' ');
            rewards = GetStringItemArray(node, "rewards");
            cinematicPartCamera = GetStringItemArray(node, "cinematicPartCamera");
            NmBraceOverrideSet = Xml.GetChildInnerText(node, "NmBraceOverrideSet");
            buoyancySphereOffset = Xml.GetChildVector3Attributes(node, "buoyancySphereOffset");
            buoyancySphereSizeScale = Xml.GetChildFloatAttribute(node, "buoyancySphereSizeScale", "value");
            var tnode = node.SelectSingleNode("pOverrideRagdollThreshold");
            if (tnode != null)
            {
                var ttype = tnode.Attributes["type"]?.Value;
                switch (ttype)
                {
                    case "NULL": break;
                    case "CVehicleModelInfo__CVehicleOverrideRagdollThreshold":
                        pOverrideRagdollThreshold = new VehicleOverrideRagdollThreshold();
                        pOverrideRagdollThreshold.MinComponent = Xml.GetChildIntAttribute(tnode, "MinComponent", "value");
                        pOverrideRagdollThreshold.MaxComponent = Xml.GetChildIntAttribute(tnode, "MaxComponent", "value");
                        pOverrideRagdollThreshold.ThresholdMult = Xml.GetChildFloatAttribute(tnode, "ThresholdMult", "value");
                        break;
                    default:
                        break;
                }
            }
            firstPersonDrivebyData = GetStringItemArray(node, "firstPersonDrivebyData");
        }

        private string[] GetStringItemArray(XmlNode node, string childName)
        {
            var cnode = node.SelectSingleNode(childName);
            if (cnode == null) return null;
            var items = cnode.SelectNodes("Item");
            if (items == null) return null;
            getStringArrayList.Clear();
            foreach (XmlNode inode in items)
            {
                var istr = inode.InnerText;
                if (!string.IsNullOrEmpty(istr))
                {
                    getStringArrayList.Add(istr);
                }
            }
            if (getStringArrayList.Count == 0) return null;
            return getStringArrayList.ToArray();
        }
        private string[] GetStringArray(XmlNode node, string childName, char delimiter)
        {
            var ldastr = Xml.GetChildInnerText(node, childName);
            var ldarr = ldastr?.Split(delimiter);
            if (ldarr == null) return null;
            getStringArrayList.Clear();
            foreach (var ldstr in ldarr)
            {
                var ldt = ldstr?.Trim();
                if (!string.IsNullOrEmpty(ldt))
                {
                    getStringArrayList.Add(ldt);
                }
            }
            if (getStringArrayList.Count == 0) return null;
            return getStringArrayList.ToArray();
        }
        private float[] GetFloatArray(XmlNode node, string childName, char delimiter)
        {
            var ldastr = Xml.GetChildInnerText(node, childName);
            var ldarr = ldastr?.Split(delimiter);
            if (ldarr == null) return null;
            getFloatArrayList.Clear();
            foreach (var ldstr in ldarr)
            {
                var ldt = ldstr?.Trim();
                if (!string.IsNullOrEmpty(ldt))
                {
                    float f;
                    if (FloatUtil.TryParse(ldt, out f))
                    {
                        getFloatArrayList.Add(f);
                    }
                }
            }
            if (getFloatArrayList.Count == 0) return null;
            return getFloatArrayList.ToArray();
        }

        private static List<string> getStringArrayList = new List<string>();
        private static List<float> getFloatArrayList = new List<float>();

        public override string ToString()
        {
            return modelName;
        }
    }

    public class VehicleOverrideRagdollThreshold
    {
        public int MinComponent { get; set; }
        public int MaxComponent { get; set; }
        public float ThresholdMult { get; set; }

        public override string ToString()
        {
            return MinComponent.ToString() + ", " + MaxComponent.ToString() + ", " + ThresholdMult.ToString();
        }
    }
    public class VehicleDriver
    {
        public string driverName { get; set; }
        public string npcName { get; set; }

        public override string ToString()
        {
            return driverName + ", " + npcName;
        }
    }

}
