using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CodeWalker.GameFiles
{

    public class RpfFile
    {
        public string Name { get; set; }
        public string NameLower { get; set; }
        public string Path { get; set; }
        public string FilePath { get; set; }
        public long FileSize { get; set; }
        public string LastError { get; set; }
        public Exception LastException { get; set; }

        public RpfDirectoryEntry Root { get; set; }

        public bool IsAESEncrypted { get; set; }
        public bool IsNGEncrypted { get; set; }

        public long StartPos { get; set; }

        public uint Version { get; set; }
        public uint EntryCount { get; set; }
        public uint NamesLength { get; set; }
        public RpfEncryption Encryption { get; set; }

        public List<RpfEntry> AllEntries { get; set; }
        public List<RpfFile> Children { get; set; }
        public RpfFile Parent { get; set; }
        public RpfBinaryFileEntry ParentFileEntry { get; set; }

        public BinaryReader CurrentFileReader { get; set; }

        public uint TotalFileCount { get; set; }
        public uint TotalFolderCount { get; set; }
        public uint TotalResourceCount { get; set; }
        public uint TotalBinaryFileCount { get; set; }
        public uint GrandTotalRpfCount { get; set; }
        public uint GrandTotalFileCount { get; set; }
        public uint GrandTotalFolderCount { get; set; }
        public uint GrandTotalResourceCount { get; set; }
        public uint GrandTotalBinaryFileCount { get; set; }
        public long ExtractedByteCount { get; set; }

        public RpfFile(string fpath, string relpath)
        {
            FileInfo fi = new FileInfo(fpath);
            Name = fi.Name;
            NameLower = Name.ToLowerInvariant();
            Path = relpath.ToLowerInvariant();
            FilePath = fpath;
            FileSize = fi.Length;
        }
        public RpfFile(string name, string path, long filesize)
        {
            Name = name;
            NameLower = Name.ToLowerInvariant();
            Path = path.ToLowerInvariant();
            FilePath = path;
            FileSize = filesize;
        }

        public string CopyToModsFolder(out string status)
        {
            RpfFile parentFile = GetTopParent();
            string rel_parent_path = parentFile.Path;
            string full_parent_path = parentFile.FilePath;

            if(rel_parent_path.StartsWith(@"mods\"))
            {
                status = "already in mods folder";
                return null;
            }

            if(!full_parent_path.EndsWith(rel_parent_path))
            {
                throw new DirectoryNotFoundException("Expected full parent path to end with relative path");
            }

            string mods_base_path = full_parent_path.Replace(rel_parent_path, @"mods\");
            string dest_path = mods_base_path + rel_parent_path;

            try
            {
                File.Copy(full_parent_path, dest_path);
                status = $"copied \"{parentFile.Name}\" from \"{full_parent_path}\" to \"{dest_path}\"";
                return dest_path;
            } catch (IOException e)
            {
                status = $"unable to copy \"{parentFile.Name}\" from \"{full_parent_path}\" to \"{dest_path}\": {e.Message}";
                return null;
            }
        }

        public bool IsInModsFolder()
        {
            return GetTopParent().Path.StartsWith(@"mods\");
        }

        public RpfFile GetTopParent()
        {
            RpfFile pfile = this;
            while (pfile.Parent != null)
            {
                pfile = pfile.Parent;
            }
            return pfile;
        }

        public string GetPhysicalFilePath()
        {
            return GetTopParent().FilePath;
        }

        private void ReadHeader(BinaryReader br)
        {
            CurrentFileReader = br;

            StartPos = br.BaseStream.Position;

            Version = br.ReadUInt32();
            EntryCount = br.ReadUInt32();
            NamesLength = br.ReadUInt32();
            Encryption = (RpfEncryption)br.ReadUInt32();

            if (Version != 0x52504637)
            {
                throw new Exception("Invalid Resource - not GTAV!");
            }

            byte[] entriesdata = br.ReadBytes((int)EntryCount * 16);
            byte[] namesdata = br.ReadBytes((int)NamesLength);

            switch (Encryption)
            {
                case RpfEncryption.NONE:
                case RpfEncryption.OPEN:
                    break;
                case RpfEncryption.AES:
                    entriesdata = GTACrypto.DecryptAES(entriesdata);
                    namesdata = GTACrypto.DecryptAES(namesdata);
                    IsAESEncrypted = true;
                    break;
                case RpfEncryption.NG:
                    entriesdata = GTACrypto.DecryptNG(entriesdata, Name, (uint)FileSize);
                    namesdata = GTACrypto.DecryptNG(namesdata, Name, (uint)FileSize);
                    IsNGEncrypted = true;
                    break;
                default:
                    entriesdata = GTACrypto.DecryptNG(entriesdata, Name, (uint)FileSize);
                    namesdata = GTACrypto.DecryptNG(namesdata, Name, (uint)FileSize);
                    break;
            }

            var entriesrdr = new DataReader(new MemoryStream(entriesdata));
            var namesrdr = new DataReader(new MemoryStream(namesdata));
            AllEntries = new List<RpfEntry>();
            TotalFileCount = 0;
            TotalFolderCount = 0;
            TotalResourceCount = 0;
            TotalBinaryFileCount = 0;

            for (uint i = 0; i < EntryCount; i++)
            {
                uint y = entriesrdr.ReadUInt32();
                uint x = entriesrdr.ReadUInt32();
                entriesrdr.Position -= 8;

                RpfEntry e;

                if (x == 0x7fffff00)
                {
                    e = new RpfDirectoryEntry();
                    TotalFolderCount++;
                }
                else if ((x & 0x80000000) == 0)
                {
                    e = new RpfBinaryFileEntry();
                    TotalBinaryFileCount++;
                    TotalFileCount++;
                }
                else
                {
                    e = new RpfResourceFileEntry();
                    TotalResourceCount++;
                    TotalFileCount++;
                }

                e.File = this;
                e.H1 = y;
                e.H2 = x;

                e.Read(entriesrdr);

                namesrdr.Position = e.NameOffset;
                e.Name = namesrdr.ReadString();
                if (e.Name.Length > 256)
                {
                    e.Name = e.Name.Substring(0, 256);
                }
                e.NameLower = e.Name.ToLowerInvariant();

                if ((e is RpfFileEntry) && string.IsNullOrEmpty(e.Name))
                {
                }
                if ((e is RpfResourceFileEntry))
                {
                    var rfe = e as RpfResourceFileEntry;
                    rfe.IsEncrypted = rfe.NameLower.EndsWith(".ysc");
                }

                AllEntries.Add(e);
            }

            Root = (RpfDirectoryEntry)AllEntries[0];
            Root.Path = Path.ToLowerInvariant();
            var stack = new Stack<RpfDirectoryEntry>();
            stack.Push(Root);
            while (stack.Count > 0)
            {
                var item = stack.Pop();

                int starti = (int)item.EntriesIndex;
                int endi = (int)(item.EntriesIndex + item.EntriesCount);

                for (int i = starti; i < endi; i++)
                {
                    RpfEntry e = AllEntries[i];
                    e.Parent = item;
                    if (e is RpfDirectoryEntry)
                    {
                        RpfDirectoryEntry rde = e as RpfDirectoryEntry;
                        rde.Path = item.Path + "\\" + rde.NameLower;
                        item.Directories.Add(rde);
                        stack.Push(rde);
                    }
                    else if (e is RpfFileEntry)
                    {
                        RpfFileEntry rfe = e as RpfFileEntry;
                        rfe.Path = item.Path + "\\" + rfe.NameLower;
                        item.Files.Add(rfe);
                    }
                }
            }

            br.BaseStream.Position = StartPos;

            CurrentFileReader = null;

        }

        public void ScanStructure(Action<string> updateStatus, Action<string> errorLog)
        {
            using (BinaryReader br = new BinaryReader(File.OpenRead(FilePath)))
            {
                try
                {
                    ScanStructure(br, updateStatus, errorLog);
                }
                catch (Exception ex)
                {
                    LastError = ex.ToString();
                    LastException = ex;
                    errorLog(FilePath + ": " + LastError);
                }
            }
        }
        private void ScanStructure(BinaryReader br, Action<string> updateStatus, Action<string> errorLog)
        {
            ReadHeader(br);

            GrandTotalRpfCount = 1;
            GrandTotalFileCount = 1;
            GrandTotalFolderCount = 0;
            GrandTotalResourceCount = 0;
            GrandTotalBinaryFileCount = 0;

            Children = new List<RpfFile>();

            updateStatus?.Invoke("Scanning " + Path + "...");

            foreach (RpfEntry entry in AllEntries)
            {
                try
                {
                    if (entry is RpfBinaryFileEntry)
                    {
                        RpfBinaryFileEntry binentry = entry as RpfBinaryFileEntry;

                        var lname = binentry.NameLower;
                        if (lname.EndsWith(".rpf") && IsValidPath(binentry.Path))
                        {
                            br.BaseStream.Position = StartPos + ((long)binentry.FileOffset * 512);

                            long l = binentry.GetFileSize();

                            RpfFile subfile = new RpfFile(binentry.Name, binentry.Path, l);
                            subfile.Parent = this;
                            subfile.ParentFileEntry = binentry;

                            subfile.ScanStructure(br, updateStatus, errorLog);

                            GrandTotalRpfCount += subfile.GrandTotalRpfCount;
                            GrandTotalFileCount += subfile.GrandTotalFileCount;
                            GrandTotalFolderCount += subfile.GrandTotalFolderCount;
                            GrandTotalResourceCount += subfile.GrandTotalResourceCount;
                            GrandTotalBinaryFileCount += subfile.GrandTotalBinaryFileCount;

                            Children.Add(subfile);
                        }
                        else
                        {
                            GrandTotalBinaryFileCount++;
                            GrandTotalFileCount++;
                        }
                    }
                    else if (entry is RpfResourceFileEntry)
                    {
                        GrandTotalResourceCount++;
                        GrandTotalFileCount++;
                    }
                    else if (entry is RpfDirectoryEntry)
                    {
                        GrandTotalFolderCount++;
                    }
                }
                catch (Exception ex)
                {
                    errorLog?.Invoke(entry.Path + ": " + ex.ToString());
                }
            }

        }

        public void ExtractScripts(string outputfolder, Action<string> updateStatus)
        {
            FileStream fs = File.OpenRead(FilePath);
            BinaryReader br = new BinaryReader(fs);

            try
            {
                ExtractScripts(br, outputfolder, updateStatus);
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                LastException = ex;
            }

            br.Close();
            br.Dispose();
            fs.Dispose();
        }
        private void ExtractScripts(BinaryReader br, string outputfolder, Action<string> updateStatus)
        {
            updateStatus?.Invoke("Searching " + Name + "...");

            ReadHeader(br);

            foreach (RpfEntry entry in AllEntries)
            {
                if (entry is RpfBinaryFileEntry)
                {
                    RpfBinaryFileEntry binentry = entry as RpfBinaryFileEntry;
                    long l = binentry.GetFileSize();

                    string lname = binentry.NameLower;
                    if (lname.EndsWith(".rpf"))
                    {
                        br.BaseStream.Position = StartPos + ((long)binentry.FileOffset * 512);

                        RpfFile subfile = new RpfFile(binentry.Name, binentry.Path, l);
                        subfile.Parent = this;
                        subfile.ParentFileEntry = binentry;

                        subfile.ExtractScripts(br, outputfolder, updateStatus);
                    }

                }
                else if (entry is RpfResourceFileEntry)
                {

                    RpfResourceFileEntry resentry = entry as RpfResourceFileEntry;

                    string lname = resentry.NameLower;

                    if (lname.EndsWith(".ysc"))
                    {
                        updateStatus?.Invoke("Extracting " + resentry.Name + "...");

                        string ofpath = outputfolder + "\\" + resentry.Name;

                        br.BaseStream.Position = StartPos + ((long)resentry.FileOffset * 512);

                        if (resentry.FileSize > 0)
                        {
                            uint offset = 0x10;
                            uint totlen = resentry.FileSize - offset;

                            byte[] tbytes = new byte[totlen];

                            br.BaseStream.Position += offset;

                            br.Read(tbytes, 0, (int)totlen);

                            byte[] decr;
                            if (IsAESEncrypted)
                            {
                                decr = GTACrypto.DecryptAES(tbytes);

                                ofpath = outputfolder + "\\" + Name + "___" + resentry.Name;
                            }
                            else
                            {
                                decr = GTACrypto.DecryptNG(tbytes, resentry.Name, resentry.FileSize);
                            }

                            try
                            {
                                MemoryStream ms = new MemoryStream(decr);
                                DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress);

                                MemoryStream outstr = new MemoryStream();
                                ds.CopyTo(outstr);
                                byte[] deflated = outstr.GetBuffer();
                                byte[] outbuf = new byte[outstr.Length];
                                Array.Copy(deflated, outbuf, outbuf.Length);

                                bool pathok = true;
                                if (File.Exists(ofpath))
                                {
                                    ofpath = outputfolder + "\\" + Name + "_" + resentry.Name;
                                    if (File.Exists(ofpath))
                                    {
                                        LastError = "Output file " + ofpath + " already exists!";
                                        pathok = false;
                                    }
                                }
                                if (pathok)
                                {
                                    File.WriteAllBytes(ofpath, outbuf);
                                }
                            }
                            catch (Exception ex)
                            {
                                LastError = ex.ToString();
                                LastException = ex;
                            }

                        }
                    }

                }
            }

        }

        public byte[] ExtractFile(RpfFileEntry entry)
        {
            try
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead(GetPhysicalFilePath())))
                {
                    if (entry is RpfBinaryFileEntry)
                    {
                        return ExtractFileBinary(entry as RpfBinaryFileEntry, br);
                    }
                    else if (entry is RpfResourceFileEntry)
                    {
                        return ExtractFileResource(entry as RpfResourceFileEntry, br);
                    }
                    else
                    {
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                LastException = ex;
                return null;
            }
        }
        public byte[] ExtractFileBinary(RpfBinaryFileEntry entry, BinaryReader br)
        {
            br.BaseStream.Position = StartPos + ((long)entry.FileOffset * 512);

            long l = entry.GetFileSize();

            if (l > 0)
            {
                uint offset = 0;
                uint totlen = (uint)l - offset;

                byte[] tbytes = new byte[totlen];

                br.BaseStream.Position += offset;
                br.Read(tbytes, 0, (int)totlen);

                byte[] decr = tbytes;

                if (entry.IsEncrypted)
                {
                    if (IsAESEncrypted)
                    {
                        decr = GTACrypto.DecryptAES(tbytes);
                    }
                    else
                    {
                        decr = GTACrypto.DecryptNG(tbytes, entry.Name, entry.FileUncompressedSize);
                    }
                }

                byte[] defl = decr;

                if (entry.FileSize > 0)
                {
                    defl = DecompressBytes(decr);
                }
                else
                {
                }

                return defl;
            }

            return null;
        }
        public byte[] ExtractFileResource(RpfResourceFileEntry entry, BinaryReader br)
        {
            br.BaseStream.Position = StartPos + ((long)entry.FileOffset * 512);

            if (entry.FileSize > 0)
            {
                uint offset = 0x10;
                uint totlen = entry.FileSize - offset;

                byte[] tbytes = new byte[totlen];

                br.BaseStream.Position += offset;

                br.Read(tbytes, 0, (int)totlen);

                byte[] decr = tbytes;
                if (entry.IsEncrypted)
                {
                    if (IsAESEncrypted)
                    {
                        decr = GTACrypto.DecryptAES(tbytes);
                    }
                    else
                    {
                        decr = GTACrypto.DecryptNG(tbytes, entry.Name, entry.FileSize);
                    }
                }

                byte[] deflated = DecompressBytes(decr);

                byte[] data = null;

                if (deflated != null)
                {
                    data = deflated;
                }
                else
                {
                    entry.FileSize -= offset;
                    data = decr;
                }

                return data;
            }

            return null;
        }

        public static T GetFile<T>(RpfEntry e) where T : class, PackedFile, new()
        {
            T file = null;
            byte[] data = null;
            RpfFileEntry entry = e as RpfFileEntry;
            if (entry != null)
            {
                data = entry.File.ExtractFile(entry);
            }
            if (data != null)
            {
                file = new T();
                file.Load(data, entry);
            }
            return file;
        }
        public static T GetFile<T>(RpfEntry e, byte[] data) where T : class, PackedFile, new()
        {
            T file = null;
            RpfFileEntry entry = e as RpfFileEntry;
            if ((data != null))
            {
                if (entry == null)
                {
                    entry = CreateResourceFileEntry(ref data, 0);
                }
                file = new T();
                file.Load(data, entry);
            }
            return file;
        }

        public static T GetResourceFile<T>(byte[] data) where T : class, PackedFile, new()
        {
            T file = null;
            RpfFileEntry entry = CreateResourceFileEntry(ref data, 0);
            if ((data != null) && (entry != null))
            {
                data = ResourceBuilder.Decompress(data);
                file = new T();
                file.Load(data, entry);
            }
            return file;
        }
        public static void LoadResourceFile<T>(T file, byte[] data, uint ver) where T : class, PackedFile
        {

            RpfResourceFileEntry resentry = CreateResourceFileEntry(ref data, ver);

            if (file is GameFile)
            {
                GameFile gfile = file as GameFile;

                var oldresentry = gfile.RpfFileEntry as RpfResourceFileEntry;
                if (oldresentry != null)
                {
                    oldresentry.SystemFlags = resentry.SystemFlags;
                    oldresentry.GraphicsFlags = resentry.GraphicsFlags;
                    resentry.Name = oldresentry.Name;
                    resentry.NameHash = oldresentry.NameHash;
                    resentry.NameLower = oldresentry.NameLower;
                    resentry.ShortNameHash = oldresentry.ShortNameHash;
                }
                else
                {
                    gfile.RpfFileEntry = resentry;
                }
            }

            data = ResourceBuilder.Decompress(data);

            file.Load(data, resentry);

        }
        public static RpfResourceFileEntry CreateResourceFileEntry(ref byte[] data, uint ver)
        {
            var resentry = new RpfResourceFileEntry();

            uint rsc7 = BitConverter.ToUInt32(data, 0);
            if (rsc7 == 0x37435352)
            {
                int version = BitConverter.ToInt32(data, 4);
                resentry.SystemFlags = BitConverter.ToUInt32(data, 8);
                resentry.GraphicsFlags = BitConverter.ToUInt32(data, 12);
                if (data.Length > 16)
                {
                    int newlen = data.Length - 16;
                    byte[] newdata = new byte[newlen];
                    Buffer.BlockCopy(data, 16, newdata, 0, newlen);
                    data = newdata;
                }
            }
            else
            {
                resentry.SystemFlags = RpfResourceFileEntry.GetFlagsFromSize(data.Length, 0);
                resentry.GraphicsFlags = RpfResourceFileEntry.GetFlagsFromSize(0, ver);
            }

            resentry.Name = "";
            resentry.NameLower = "";

            return resentry;
        }

        public string TestExtractAllFiles()
        {
            StringBuilder sb = new StringBuilder();
            ExtractedByteCount = 0;
            try
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead(GetPhysicalFilePath())))
                {
                    foreach (RpfEntry entry in AllEntries)
                    {
                        try
                        {
                            LastError = string.Empty;
                            LastException = null;
                            if (!entry.NameLower.EndsWith(".rpf"))
                            {
                                if (entry is RpfBinaryFileEntry)
                                {
                                    RpfBinaryFileEntry binentry = entry as RpfBinaryFileEntry;
                                    byte[] data = ExtractFileBinary(binentry, br);
                                    if (data == null)
                                    {
                                        if (binentry.FileSize == 0)
                                        {
                                            sb.AppendFormat("{0} : Binary FileSize is 0.", entry.Path);
                                            sb.AppendLine();
                                        }
                                        else
                                        {
                                            sb.AppendFormat("{0} : {1}", entry.Path, LastError);
                                            sb.AppendLine();
                                        }
                                    }
                                    else if (data.Length == 0)
                                    {
                                        sb.AppendFormat("{0} : Decompressed output was empty.", entry.Path);
                                        sb.AppendLine();
                                    }
                                    else
                                    {
                                        ExtractedByteCount += data.Length;
                                    }
                                }
                                else if (entry is RpfResourceFileEntry)
                                {
                                    RpfResourceFileEntry resentry = entry as RpfResourceFileEntry;
                                    byte[] data = ExtractFileResource(resentry, br);
                                    if (data == null)
                                    {
                                        if (resentry.FileSize == 0)
                                        {
                                            sb.AppendFormat("{0} : Resource FileSize is 0.", entry.Path);
                                            sb.AppendLine();
                                        }
                                        else
                                        {
                                            sb.AppendFormat("{0} : {1}", entry.Path, LastError);
                                            sb.AppendLine();
                                        }
                                    }
                                    else if (data.Length == 0)
                                    {
                                        sb.AppendFormat("{0} : Decompressed output was empty.", entry.Path);
                                        sb.AppendLine();
                                    }
                                    else
                                    {
                                        ExtractedByteCount += data.Length;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LastError = ex.ToString();
                            LastException = ex;
                            sb.AppendFormat("{0} : {1}", entry.Path, ex.Message);
                            sb.AppendLine();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = ex.ToString();
                LastException = ex;
                sb.AppendFormat("{0} : {1}", Path, ex.Message);
                sb.AppendLine();
                return null;
            }
            return sb.ToString();
        }

        public List<RpfFileEntry> GetFiles(string folder, bool recurse)
        {
            List<RpfFileEntry> result = new List<RpfFileEntry>();
            string[] parts = folder.ToLowerInvariant().Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            RpfDirectoryEntry dir = Root;
            for (int i = 0; i < parts.Length; i++)
            {
                if (dir == null) break;
                dir = FindSubDirectory(dir, parts[i]);
            }
            if (dir != null)
            {
                GetFiles(dir, result, recurse);
            }
            return result;
        }
        public void GetFiles(RpfDirectoryEntry dir, List<RpfFileEntry> result, bool recurse)
        {
            if (dir.Files != null)
            {
                result.AddRange(dir.Files);
            }
            if (recurse)
            {
                if (dir.Directories != null)
                {
                    for (int i = 0; i < dir.Directories.Count; i++)
                    {
                        GetFiles(dir.Directories[i], result, recurse);
                    }
                }
            }
        }

        private RpfDirectoryEntry FindSubDirectory(RpfDirectoryEntry dir, string name)
        {
            if (dir == null) return null;
            if (dir.Directories == null) return null;
            for (int i = 0; i < dir.Directories.Count; i++)
            {
                var cdir = dir.Directories[i];
                if (cdir.Name.ToLowerInvariant() == name)
                {
                    return cdir;
                }
            }
            return null;
        }

        public byte[] DecompressBytes(byte[] bytes)
        {
            try
            {
                using (DeflateStream ds = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress))
                {
                    using (var outstr = new MemoryStream())
                    {
                        ds.CopyTo(outstr);
                        byte[] deflated = outstr.GetBuffer();
                        byte[] outbuf = new byte[outstr.Length];
                        Buffer.BlockCopy(deflated, 0, outbuf, 0, outbuf.Length);

                        if (outbuf.Length <= bytes.Length)
                        {
                            LastError = "Warning: Decompressed data was smaller than compressed data...";
                        }

                        return outbuf;
                    }
                }
            }
            catch (Exception ex)
            {
                LastError = "Could not decompress.";
                LastException = ex;
                return null;
            }
        }
        public static byte[] CompressBytes(byte[] data)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                using (var ds = new DeflateStream(ms, CompressionMode.Compress, true))
                {
                    ds.Write(data, 0, data.Length);
                    ds.Close();
                    byte[] deflated = ms.GetBuffer();
                    byte[] outbuf = new byte[ms.Length];
                    Buffer.BlockCopy(deflated, 0, outbuf, 0, outbuf.Length);
                    return outbuf;
                }
            }
        }

        private void WriteHeader(BinaryWriter bw)
        {
            var namesdata = GetHeaderNamesData();
            NamesLength = (uint)namesdata.Length;

            var headersize = GetHeaderBlockCount() * 512;
            EnsureSpace(bw, null, headersize);

            var entriesdata = GetHeaderEntriesData();

            switch (Encryption)
            {
                case RpfEncryption.NONE:
                case RpfEncryption.OPEN:
                    break;
                case RpfEncryption.AES:
                    entriesdata = GTACrypto.EncryptAES(entriesdata);
                    namesdata = GTACrypto.EncryptAES(namesdata);
                    IsAESEncrypted = true;
                    break;
                case RpfEncryption.NG:
                    entriesdata = GTACrypto.EncryptNG(entriesdata, Name, (uint)FileSize);
                    namesdata = GTACrypto.EncryptNG(namesdata, Name, (uint)FileSize);
                    IsNGEncrypted = true;
                    break;
                default:
                    entriesdata = GTACrypto.EncryptNG(entriesdata, Name, (uint)FileSize);
                    namesdata = GTACrypto.EncryptNG(namesdata, Name, (uint)FileSize);
                    break;
            }

            bw.BaseStream.Position = StartPos;

            bw.Write(Version);
            bw.Write(EntryCount);
            bw.Write(NamesLength);
            bw.Write((uint)Encryption);
            bw.Write(entriesdata);
            bw.Write(namesdata);

            WritePadding(bw.BaseStream, StartPos + headersize);
        }

        private static void WritePadding(Stream s, long upto)
        {
            int diff = (int)(upto - s.Position);
            if (diff > 0)
            {
                s.Write(new byte[diff], 0, diff);
            }
        }

        private void EnsureAllEntries()
        {
            if (AllEntries == null)
            {
                AllEntries = new List<RpfEntry>();
                Root = new RpfDirectoryEntry();
                Root.File = this;
                Root.Name = string.Empty;
                Root.NameLower = string.Empty;
                Root.Path = Path.ToLowerInvariant();
            }
            if (Children == null)
            {
                Children = new List<RpfFile>();
            }

            List<RpfEntry> temp = new List<RpfEntry>();
            AllEntries.Clear();
            AllEntries.Add(Root);
            Stack<RpfDirectoryEntry> stack = new Stack<RpfDirectoryEntry>();
            stack.Push(Root);
            while (stack.Count > 0)
            {
                var item = stack.Pop();

                item.EntriesCount = (uint)(item.Directories.Count + item.Files.Count);
                item.EntriesIndex = (uint)AllEntries.Count;

                temp.Clear();
                temp.AddRange(item.Directories);
                temp.AddRange(item.Files);
                temp.Sort((a, b) => String.CompareOrdinal(a.Name, b.Name));

                foreach (var entry in temp)
                {
                    AllEntries.Add(entry);
                    RpfDirectoryEntry dir = entry as RpfDirectoryEntry;
                    if (dir != null)
                    {
                        stack.Push(dir);
                    }
                }
            }

            EntryCount = (uint)AllEntries.Count;

        }
        private byte[] GetHeaderNamesData()
        {
            MemoryStream namesstream = new MemoryStream();
            DataWriter nameswriter = new DataWriter(namesstream);
            var namedict = new Dictionary<string, uint>();
            foreach (var entry in AllEntries)
            {
                uint nameoffset;
                string name = entry.Name ?? "";
                if (namedict.TryGetValue(name, out nameoffset))
                {
                    entry.NameOffset = nameoffset;
                }
                else
                {
                    entry.NameOffset = (uint)namesstream.Length;
                    namedict.Add(name, entry.NameOffset);
                    nameswriter.Write(name);
                }
            }
            var buf = new byte[namesstream.Length];
            namesstream.Position = 0;
            namesstream.Read(buf, 0, buf.Length);
            return PadBuffer(buf, 16);
        }
        private byte[] GetHeaderEntriesData()
        {
            MemoryStream entriesstream = new MemoryStream();
            DataWriter entrieswriter = new DataWriter(entriesstream);
            foreach (var entry in AllEntries)
            {
                entry.Write(entrieswriter);
            }
            var buf = new byte[entriesstream.Length];
            entriesstream.Position = 0;
            entriesstream.Read(buf, 0, buf.Length);
            return buf;
        }
        private uint GetHeaderBlockCount()
        {
            uint headerusedbytes = 16 + (EntryCount * 16) + NamesLength;
            uint headerblockcount = GetBlockCount(headerusedbytes);
            return headerblockcount;
        }
        private static byte[] PadBuffer(byte[] buf, uint n)
        {
            uint buflen = (uint)buf.Length;
            uint newlen = PadLength(buflen, n);
            if (newlen != buflen)
            {
                byte[] buf2 = new byte[newlen];
                Buffer.BlockCopy(buf, 0, buf2, 0, buf.Length);
                return buf2;
            }
            return buf;
        }
        private static uint PadLength(uint l, uint n)
        {
            uint rem = l % n;
            return l + ((rem > 0) ? (n - rem) : 0);
        }
        private static uint GetBlockCount(long bytecount)
        {
            uint b0 = (uint)(bytecount & 0x1FF);
            uint b1 = (uint)(bytecount >> 9);
            if (b0 == 0) return b1;
            return b1 + 1;
        }
        private RpfFileEntry FindFirstFileAfter(uint block)
        {
            RpfFileEntry nextentry = null;
            foreach (var entry in AllEntries)
            {
                RpfFileEntry fe = entry as RpfFileEntry;
                if ((fe != null) && (fe.FileOffset > block))
                {
                    if ((nextentry == null) || (fe.FileOffset < nextentry.FileOffset))
                    {
                        nextentry = fe;
                    }
                }
            }
            return nextentry;
        }
        private uint FindHole(uint reqblocks, uint ignorestart, uint ignoreend)
        {

            List<RpfFileEntry> allfiles = new List<RpfFileEntry>();
            foreach (var entry in AllEntries)
            {
                RpfFileEntry rfe = entry as RpfFileEntry;
                if (rfe != null)
                {
                    allfiles.Add(rfe);
                }
            }
            allfiles.Sort((e1, e2) => e1.FileOffset.CompareTo(e2.FileOffset));

            uint found = 0;
            uint foundsize = 0xFFFFFFFF;

            uint e1end = GetHeaderBlockCount();
            uint e1next = e1end;

            for (int i = 0; i < allfiles.Count(); i++)
            {
                RpfFileEntry e2 = allfiles[i];
                uint e2cnt = GetBlockCount(e2.GetFileSize());
                uint e2beg = e2.FileOffset;
                e1end = e1next;
                e1next = e2.FileOffset + e2cnt;
                if ((e2beg > ignorestart) && (e1end < ignoreend))
                {
                    continue;
                }
                if (e1end < e2beg)
                {
                    uint space = e2beg - e1end;
                    if ((space >= reqblocks) && (space < foundsize))
                    {
                        found = e1end;
                        foundsize = space;
                    }
                }
            }

            return found;
        }
        private uint FindEndBlock()
        {
            uint endblock = 0;
            foreach (var entry in AllEntries)
            {
                RpfFileEntry e = entry as RpfFileEntry;
                if (e != null)
                {
                    uint ecnt = GetBlockCount(e.GetFileSize());
                    uint eend = e.FileOffset + ecnt;
                    if (eend > endblock)
                    {
                        endblock = eend;
                    }
                }
            }

            if (endblock == 0)
            {
                endblock = GetHeaderBlockCount();
            }

            return endblock;
        }
        private void GrowArchive(BinaryWriter bw, uint newblockcount)
        {
            uint newsize = newblockcount * 512;
            if (newsize < FileSize)
            {
                return;
            }
            if (FileSize == newsize)
            {
                return;
            }

            FileSize = newsize;

            if (Parent != null)
            {
                if (ParentFileEntry == null)
                {
                    throw new Exception("Can't grow archive " + Path + ": ParentFileEntry was null!");
                }

                ParentFileEntry.FileUncompressedSize = newsize;
                ParentFileEntry.FileSize = 0;

                Parent.EnsureSpace(bw, ParentFileEntry, newsize);
            }
        }
        private void RelocateFile(BinaryWriter bw, RpfFileEntry f, uint newblock)
        {

            uint flen = GetBlockCount(f.GetFileSize());
            uint fbeg = f.FileOffset;
            uint fend = fbeg + flen;
            uint nend = newblock + flen;
            if ((nend > fbeg) && (newblock < fend))
            {
                throw new Exception("Unable to relocate file " + f.Path + ": new position was inside the original!");
            }

            var stream = bw.BaseStream;
            long origpos = stream.Position;
            long source = StartPos + ((long)fbeg * 512);
            long dest = StartPos + ((long)newblock * 512);
            long newstart = dest;
            long length = (long)flen * 512;
            long destend = dest + length;
            const int BUFFER_SIZE = 16384;
            var buffer = new byte[BUFFER_SIZE];
            while (length > 0)
            {
                stream.Position = source;
                int i = stream.Read(buffer, 0, (int)Math.Min(length, BUFFER_SIZE));
                stream.Position = dest;
                stream.Write(buffer, 0, i);
                source += i;
                dest += i;
                length -= i;
            }

            WritePadding(stream, destend);

            stream.Position = origpos;

            f.FileOffset = newblock;

            var child = FindChildArchive(f);
            if (child != null)
            {
                child.UpdateStartPos(newstart);
            }

        }
        private void EnsureSpace(BinaryWriter bw, RpfFileEntry e, long bytecount)
        {

            uint blockcount = GetBlockCount(bytecount);
            uint startblock = e?.FileOffset ?? 0;
            uint endblock = startblock + blockcount;

            RpfFileEntry nextentry = FindFirstFileAfter(startblock);

            while (nextentry != null)
            {

                if (nextentry.FileOffset >= endblock)
                {
                    break;
                }

                uint entryblocks = GetBlockCount(nextentry.GetFileSize());
                uint newblock = FindHole(entryblocks, startblock, endblock);
                if (newblock == 0)
                {
                    newblock = FindEndBlock();
                    GrowArchive(bw, newblock + entryblocks);
                }

                RelocateFile(bw, nextentry, newblock);

                nextentry = FindFirstFileAfter(startblock);
            }

            if (nextentry == null)
            {
                uint newblock = FindEndBlock();
                GrowArchive(bw, newblock + ((e != null) ? blockcount : 0));
            }

            if (e != null)
            {
                WriteHeader(bw);
            }

        }
        private void InsertFileSpace(BinaryWriter bw, RpfFileEntry entry)
        {

            uint blockcount = GetBlockCount(entry.GetFileSize());
            entry.FileOffset = FindHole(blockcount, 0, 0);
            if (entry.FileOffset == 0)
            {
                entry.FileOffset = FindEndBlock();
                GrowArchive(bw, entry.FileOffset + blockcount);
            }
            EnsureAllEntries();
            WriteHeader(bw);
        }

        private void WriteNewArchive(BinaryWriter bw, RpfEncryption encryption)
        {
            var stream = bw.BaseStream;
            Encryption = encryption;
            Version = 0x52504637;
            IsAESEncrypted = (encryption == RpfEncryption.AES);
            IsNGEncrypted = (encryption == RpfEncryption.NG);
            StartPos = stream.Position;
            EnsureAllEntries();
            WriteHeader(bw);
            FileSize = stream.Position - StartPos;
        }

        private void UpdatePaths(RpfDirectoryEntry dir = null)
        {
            if (dir == null)
            {
                Root.Path = Path.ToLowerInvariant();
                dir = Root;
            }
            foreach (var file in dir.Files)
            {
                file.Path = dir.Path + "\\" + file.NameLower;

                RpfBinaryFileEntry binf = file as RpfBinaryFileEntry;
                if ((binf != null) && file.NameLower.EndsWith(".rpf"))
                {
                    RpfFile childrpf = FindChildArchive(binf);
                    if (childrpf != null)
                    {
                        childrpf.Path = binf.Path;
                        childrpf.FilePath = binf.Path;
                        childrpf.UpdatePaths();
                    }
                    else
                    { }
                }

            }
            foreach (var subdir in dir.Directories)
            {
                subdir.Path = dir.Path + "\\" + subdir.NameLower;
                UpdatePaths(subdir);
            }
        }

        public RpfFile FindChildArchive(RpfFileEntry f)
        {
            RpfFile c = null;
            if (Children != null)
            {
                foreach (var child in Children)
                {
                    if (child.ParentFileEntry == f)
                    {
                        c = child;
                        break;
                    }
                }
            }
            return c;
        }

        public long GetDefragmentedFileSize(bool recursive = true)
        {

            if (!recursive)
            {
                uint blockcount = GetHeaderBlockCount();

                foreach (var entry in AllEntries)
                {
                    var fentry = entry as RpfFileEntry;
                    if (fentry != null)
                    {
                        blockcount += GetBlockCount(fentry.GetFileSize());
                    }
                }

                return (long)blockcount * 512;
            }
            else
            {
                uint blockcount = GetHeaderBlockCount();
                long childRpfsSize = 0;

                foreach (var entry in AllEntries)
                {
                    var fentry = entry as RpfFileEntry;
                    if (fentry != null)
                    {
                        var childRpf = this.FindChildArchive(fentry);
                        if (childRpf == null)
                        {
                            blockcount += GetBlockCount(fentry.GetFileSize());
                        }
                        else
                        {
                            childRpfsSize += childRpf.GetDefragmentedFileSize(true);
                        }
                    }
                }

                return (long)blockcount * 512 + childRpfsSize;
            }
        }

        private void UpdateStartPos(long newpos)
        {
            StartPos = newpos;

            if (Children != null)
            {
                foreach (var child in Children)
                {
                    if (child.ParentFileEntry == null) continue;
                    var cpos = StartPos + (long)child.ParentFileEntry.FileOffset * 512;
                    child.UpdateStartPos(cpos);
                }
            }
        }

        public static RpfFile CreateNew(string gtafolder, string relpath, RpfEncryption encryption = RpfEncryption.OPEN)
        {

            string fpath = gtafolder;
            fpath = fpath.EndsWith("\\") ? fpath : fpath + "\\";
            fpath = relpath.Contains(":") ? relpath : fpath + relpath;

            if (File.Exists(fpath))
            {
                throw new Exception("File " + fpath + " already exists!");
            }

            File.Create(fpath).Dispose();

            RpfFile file = new RpfFile(fpath, relpath);

            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    file.WriteNewArchive(bw, encryption);
                }
            }

            return file;
        }

        public static RpfFile CreateNew(RpfDirectoryEntry dir, string name, RpfEncryption encryption = RpfEncryption.OPEN)
        {

            string namel = name.ToLowerInvariant();
            RpfFile parent = dir.File;
            string fpath = parent.GetPhysicalFilePath();
            string rpath = dir.Path + "\\" + namel;

            if (!File.Exists(fpath))
            {
                throw new Exception("Root RPF file " + fpath + " does not exist!");
            }

            RpfFile file = new RpfFile(name, rpath, 512);
            file.Parent = parent;
            file.ParentFileEntry = new RpfBinaryFileEntry();

            RpfBinaryFileEntry entry = file.ParentFileEntry;
            entry.Parent = dir;
            entry.FileOffset = 0;
            entry.FileSize = 0;
            entry.FileUncompressedSize = (uint)file.FileSize;
            entry.EncryptionType = 0;
            entry.IsEncrypted = false;
            entry.File = parent;
            entry.Path = rpath;
            entry.Name = name;
            entry.NameLower = namel;
            entry.NameHash = JenkHash.GenHash(name);
            entry.ShortNameHash = JenkHash.GenHash(entry.GetShortNameLower());

            dir.Files.Add(entry);

            parent.Children.Add(file);

            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    parent.InsertFileSpace(bw, entry);

                    fstream.Position = parent.StartPos + ((long)entry.FileOffset * 512);

                    file.WriteNewArchive(bw, encryption);
                }
            }

            return file;
        }

        public static RpfDirectoryEntry CreateDirectory(RpfDirectoryEntry dir, string name)
        {

            RpfFile parent = dir.File;
            string namel = name.ToLowerInvariant();
            string fpath = parent.GetPhysicalFilePath();
            string rpath = dir.Path + "\\" + namel;

            if (!File.Exists(fpath))
            {
                throw new Exception("Root RPF file " + fpath + " does not exist!");
            }

            RpfDirectoryEntry entry = new RpfDirectoryEntry();
            entry.Parent = dir;
            entry.File = parent;
            entry.Path = rpath;
            entry.Name = name;
            entry.NameLower = namel;
            entry.NameHash = JenkHash.GenHash(name);
            entry.ShortNameHash = JenkHash.GenHash(entry.GetShortNameLower());

            foreach (var exdir in dir.Directories)
            {
                if (exdir.NameLower == entry.NameLower)
                {
                    throw new Exception("RPF Directory \"" + entry.Name + "\" already exists!");
                }
            }

            dir.Directories.Add(entry);

            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    parent.EnsureAllEntries();
                    parent.WriteHeader(bw);
                }
            }

            return entry;
        }

        public static RpfFileEntry CreateFile(RpfDirectoryEntry dir, string name, byte[] data, bool overwrite = true)
        {
            string namel = name.ToLowerInvariant();
            if (overwrite)
            {
                foreach (var exfile in dir.Files)
                {
                    if (exfile.NameLower == namel)
                    {
                        DeleteEntry(exfile);
                        break;
                    }
                }
            }

            RpfFile parent = dir.File;
            string fpath = parent.GetPhysicalFilePath();
            string rpath = dir.Path + "\\" + namel;
            if (!File.Exists(fpath))
            {
                throw new Exception("Root RPF file " + fpath + " does not exist!");
            }

            RpfFileEntry entry = null;
            uint len = (uint)data.Length;

            bool isrpf = false;
            bool isawc = false;
            uint hdr = 0;
            if (len >= 16)
            {
                hdr = BitConverter.ToUInt32(data, 0);
            }

            if (hdr == 0x37435352)
            {
                var rentry = new RpfResourceFileEntry();
                var version = BitConverter.ToUInt32(data, 4);
                rentry.SystemFlags = BitConverter.ToUInt32(data, 8);
                rentry.GraphicsFlags = BitConverter.ToUInt32(data, 12);
                rentry.FileSize = len;
                if (len >= 0xFFFFFF)
                {
                    data[7] = (byte)((len >> 0) & 0xFF);
                    data[14] = (byte)((len >> 8) & 0xFF);
                    data[5] = (byte)((len >> 16) & 0xFF);
                    data[2] = (byte)((len >> 24) & 0xFF);
                }

                entry = rentry;
            }

            if (namel.EndsWith(".rpf") && (hdr == 0x52504637))
            {
                isrpf = true;
            }
            if (namel.EndsWith(".awc"))
            {
                isawc = true;
            }

            if (entry == null)
            {
                var compressed = (isrpf||isawc) ? data : CompressBytes(data);
                var bentry = new RpfBinaryFileEntry();
                bentry.EncryptionType = 0;
                bentry.IsEncrypted = false;
                bentry.FileUncompressedSize = (uint)data.Length;
                bentry.FileSize = (isrpf||isawc) ? 0 : (uint)compressed.Length;
                if (bentry.FileSize > 0xFFFFFF)
                {
                    bentry.FileSize = 0;
                    compressed = data;
                }
                data = compressed;
                entry = bentry;
            }

            entry.Parent = dir;
            entry.File = parent;
            entry.Path = rpath;
            entry.Name = name;
            entry.NameLower = name.ToLowerInvariant();
            entry.NameHash = JenkHash.GenHash(name);
            entry.ShortNameHash = JenkHash.GenHash(entry.GetShortNameLower());

            foreach (var exfile in dir.Files)
            {
                if (exfile.NameLower == entry.NameLower)
                {
                    throw new Exception("File \"" + entry.Name + "\" already exists!");
                }
            }

            dir.Files.Add(entry);

            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    parent.InsertFileSpace(bw, entry);
                    long bbeg = parent.StartPos + ((long)entry.FileOffset * 512);
                    long bend = bbeg + ((long)GetBlockCount(entry.GetFileSize()) * 512);
                    fstream.Position = bbeg;
                    fstream.Write(data, 0, data.Length);
                    WritePadding(fstream, bend);
                }
            }

            if (isrpf)
            {
                RpfFile file = new RpfFile(name, rpath, data.LongLength);
                file.Parent = parent;
                file.ParentFileEntry = entry as RpfBinaryFileEntry;
                file.StartPos = parent.StartPos + ((long)entry.FileOffset * 512);
                parent.Children.Add(file);

                using (var fstream = File.OpenRead(fpath))
                {
                    using (var br = new BinaryReader(fstream))
                    {
                        fstream.Position = file.StartPos;
                        file.ScanStructure(br, null, null);
                    }
                }
            }

            return entry;
        }

        public static void RenameArchive(RpfFile file, string newname)
        {

            file.Name = newname;
            file.NameLower = newname.ToLowerInvariant();
            file.Path = GetParentPath(file.Path) + newname;
            file.FilePath = GetParentPath(file.FilePath) + newname;

            file.UpdatePaths();

        }

        public static void RenameEntry(RpfEntry entry, string newname)
        {

            string dirpath = GetParentPath(entry.Path);

            entry.Name = newname;
            entry.NameLower = newname.ToLowerInvariant();
            entry.Path = dirpath + newname;

            string sname = entry.GetShortNameLower();
            JenkIndex.Ensure(sname);
            entry.NameHash = JenkHash.GenHash(newname);
            entry.ShortNameHash = JenkHash.GenHash(sname);

            RpfFile parent = entry.File;
            string fpath = parent.GetPhysicalFilePath();

            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    parent.EnsureAllEntries();
                    parent.WriteHeader(bw);
                }
            }

            if (entry is RpfDirectoryEntry)
            {
                parent.UpdatePaths(entry as RpfDirectoryEntry);
            }

        }

        public static void DeleteEntry(RpfEntry entry)
        {

            RpfFile parent = entry.File;
            string fpath = parent.GetPhysicalFilePath();
            if (!File.Exists(fpath))
            {
                throw new Exception("Root RPF file " + fpath + " does not exist!");
            }

            RpfDirectoryEntry entryasdir = entry as RpfDirectoryEntry;
            RpfFileEntry entryasfile = entry as RpfFileEntry;

            if (entryasdir != null)
            {
                var deldirs = entryasdir.Directories.ToArray();
                var delfiles = entryasdir.Files.ToArray();
                foreach(var deldir in deldirs)
                {
                    DeleteEntry(deldir);
                }
                foreach (var delfile in delfiles)
                {
                    DeleteEntry(delfile);
                }
            }

            if (entry.Parent == null)
            {
                throw new Exception("Parent directory is null! This shouldn't happen - please refresh the folder!");
            }

            if (entryasdir != null)
            {
                entry.Parent.Directories.Remove(entryasdir);
            }
            if (entryasfile != null)
            {
                entry.Parent.Files.Remove(entryasfile);

                var child = parent.FindChildArchive(entryasfile);
                if (child != null)
                {
                    parent.Children.Remove(child);
                }
            }

            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    parent.EnsureAllEntries();
                    parent.WriteHeader(bw);
                }
            }

        }

        public static bool IsValidEncryption(RpfFile file, bool recursive = false)
        {
            if (file == null) return false;

            if (file.Encryption != RpfEncryption.OPEN) return false;

            var parent = file.Parent;
            while (parent != null)
            {
                if (parent.Encryption != RpfEncryption.OPEN) return false;
                parent = parent.Parent;
            }

            if (recursive && (file.Children != null))
            {
                var stack = new Stack<RpfFile>(file.Children);
                while (stack.Count > 0)
                {
                    var child = stack.Pop();
                    if (child == null) continue;
                    if (child.Encryption != RpfEncryption.OPEN)
                    {
                        return false;
                    }
                    if (child.Children != null)
                    {
                        foreach (var cchild in child.Children)
                        {
                            stack.Push(cchild);
                        }
                    }
                }
            }

            return true;
        }

        public static bool EnsureValidEncryption(RpfFile file, Func<RpfFile, bool> confirm, bool recursive = false)
        {
            if (file == null) return false;

            var files = new List<RpfFile>();
            if (recursive && (file.Children != null))
            {
                var stack = new Stack<RpfFile>(file.Children);
                while (stack.Count > 0)
                {
                    var child = stack.Pop();
                    if (child == null) continue;
                    if (child.Encryption != RpfEncryption.OPEN)
                    {
                        files.Add(child);
                    }
                    if (child.Children != null)
                    {
                        foreach (var cchild in child.Children)
                        {
                            stack.Push(cchild);
                        }
                    }
                }
                files.Reverse();
            }
            var needsupd = (files.Count > 0);
            var f = file;
            while (f != null)
            {
                if (f.Encryption != RpfEncryption.OPEN)
                {
                    if ((confirm != null) && !confirm(f))
                    {
                        return false;
                    }
                    needsupd = true;
                }
                if (needsupd)
                {
                    files.Add(f);
                }
                f = f.Parent;
            }

            files.Reverse();
            foreach (var cfile in files)
            {
                SetEncryptionType(cfile, RpfEncryption.OPEN);
            }

            return true;
        }

        public static void SetEncryptionType(RpfFile file, RpfEncryption encryption)
        {
            file.Encryption = encryption;
            string fpath = file.GetPhysicalFilePath();
            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    file.WriteHeader(bw);
                }
            }
        }

        public static void Defragment(RpfFile file, Action<string, float> progress = null, bool recursive = true)
        {
            if (file?.AllEntries == null) return;

            if (recursive)
            {
                foreach (var entry in file?.AllEntries)
                {
                    if (entry is RpfFileEntry)
                    {
                        var childRpf = file.FindChildArchive(entry as RpfFileEntry);
                        if (childRpf != null)
                        {
                            Defragment(childRpf, null, true);
                        }
                    }
                }
            }

            string fpath = file.GetPhysicalFilePath();
            using (var fstream = File.Open(fpath, FileMode.Open, FileAccess.ReadWrite))
            {
                using (var bw = new BinaryWriter(fstream))
                {
                    uint destblock = file.GetHeaderBlockCount();

                    const int BUFFER_SIZE = 16384;
                    var buffer = new byte[BUFFER_SIZE];

                    var allfiles = new List<RpfFileEntry>();
                    for (int i = 0; i < file.AllEntries.Count; i++)
                    {
                        var entry = file.AllEntries[i] as RpfFileEntry;
                        if (entry != null) allfiles.Add(entry);
                    }
                    allfiles.Sort((a, b) => { return a.FileOffset.CompareTo(b.FileOffset); });

                    for (int i = 0; i < allfiles.Count; i++)
                    {
                        var entry = allfiles[i];
                        float prog = (float)i / allfiles.Count;
                        string txt = "Relocating " + entry.Name + "...";
                        progress?.Invoke(txt, prog);

                        var sourceblock = entry.FileOffset;
                        var blockcount = GetBlockCount(entry.GetFileSize());

                        if (sourceblock > destblock)
                        {
                            var source = file.StartPos + (long)sourceblock * 512;
                            var dest = file.StartPos + (long)destblock * 512;
                            var remlength = (long)blockcount * 512;
                            while (remlength > 0)
                            {
                                fstream.Position = source;
                                int n = fstream.Read(buffer, 0, (int)Math.Min(remlength, BUFFER_SIZE));
                                fstream.Position = dest;
                                fstream.Write(buffer, 0, n);
                                source += n;
                                dest += n;
                                remlength -= n;
                            }
                            entry.FileOffset = destblock;

                            var entryrpf = file.FindChildArchive(entry);
                            if (entryrpf != null)
                            {
                                entryrpf.UpdateStartPos(file.StartPos + (long)entry.FileOffset * 512);
                            }
                        }
                        else if (sourceblock != destblock)
                        { }

                        destblock += blockcount;
                    }

                    file.FileSize = (long)destblock * 512;

                    file.WriteHeader(bw);

                    if (file.ParentFileEntry != null)
                    {
                        file.ParentFileEntry.FileUncompressedSize = (uint)file.FileSize;
                        file.ParentFileEntry.FileSize = 0;
                        if (file.Parent != null)
                        {
                            file.Parent.WriteHeader(bw);
                        }
                    }
                    if (file.Parent == null)
                    {
                        fstream.SetLength(file.FileSize);
                    }
                }
            }
        }

        private static string GetParentPath(string path)
        {
            string dirpath = path.Replace('/', '\\');
            int lidx = dirpath.LastIndexOf('\\');
            if (lidx > 0)
            {
                dirpath = dirpath.Substring(0, lidx + 1);
            }
            if (!dirpath.EndsWith("\\"))
            {
                dirpath = dirpath + "\\";
            }
            return dirpath;
        }

        private static bool IsValidPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (path.Length > 500) return false;
            var dirc = 0;
            for (int i = 0; i < path.Length; i++)
            {
                var c = path[i];
                if (c == ':') return false;
                if (c == ';') return false;
                if (c == '/') dirc++;
                if (c == '\\') dirc++;
            }
            if (dirc > 20) return false;
            return true;
        }

        public override string ToString()
        {
            return Path;
        }
    }

    public enum RpfEncryption : uint
    {
        NONE = 0,
        OPEN = 0x4E45504F,
        AES =  0x0FFFFFF9,
        NG =   0x0FEFFFFF,
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public abstract class RpfEntry
    {
        public RpfFile File { get; set; }
        public RpfDirectoryEntry Parent { get; set; }

        public uint NameHash { get; set; }
        public uint ShortNameHash { get; set; }

        public uint NameOffset { get; set; }
        public string Name { get; set; }
        public string NameLower { get; set; }
        public string Path { get; set; }

        public uint H1;
        public uint H2;

        public abstract void Read(DataReader reader);
        public abstract void Write(DataWriter writer);

        public override string ToString()
        {
            return Path;
        }

        public string GetShortName()
        {
            int ind = Name.LastIndexOf('.');
            if (ind > 0)
            {
                return Name.Substring(0, ind);
            }
            return Name;
        }
        public string GetShortNameLower()
        {
            if (NameLower == null)
            {
                NameLower = Name.ToLowerInvariant();
            }
            int ind = NameLower.LastIndexOf('.');
            if (ind > 0)
            {
                return NameLower.Substring(0, ind);
            }
            return NameLower;
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class RpfDirectoryEntry : RpfEntry
    {
        public uint EntriesIndex { get; set; }
        public uint EntriesCount { get; set; }

        public List<RpfDirectoryEntry> Directories = new List<RpfDirectoryEntry>();
        public List<RpfFileEntry> Files = new List<RpfFileEntry>();

        public override void Read(DataReader reader)
        {
            NameOffset = reader.ReadUInt32();
            uint ident = reader.ReadUInt32();
            if (ident != 0x7FFFFF00u)
            {
                throw new Exception("Error in RPF7 directory entry.");
            }
            EntriesIndex = reader.ReadUInt32();
            EntriesCount = reader.ReadUInt32();
        }
        public override void Write(DataWriter writer)
        {
            writer.Write(NameOffset);
            writer.Write(0x7FFFFF00u);
            writer.Write(EntriesIndex);
            writer.Write(EntriesCount);
        }
        public override string ToString()
        {
            return "Directory: " + Path;
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public abstract class RpfFileEntry : RpfEntry
    {
        public uint FileOffset { get; set; }
        public uint FileSize { get; set; }
        public bool IsEncrypted { get; set; }

        public abstract long GetFileSize();
        public abstract void SetFileSize(uint s);
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class RpfBinaryFileEntry : RpfFileEntry
    {
        public uint FileUncompressedSize { get; set; }
        public uint EncryptionType { get; set; }

        public override void Read(DataReader reader)
        {
            ulong buf = reader.ReadUInt64();
            NameOffset = (uint)buf & 0xFFFF;
            FileSize = (uint)(buf >> 16) & 0xFFFFFF;
            FileOffset = (uint)(buf >> 40) & 0xFFFFFF;

            FileUncompressedSize = reader.ReadUInt32();

            EncryptionType = reader.ReadUInt32();

            switch (EncryptionType)
            {
                case 0: IsEncrypted = false; break;
                case 1: IsEncrypted = true; break;
                default:
                    throw new Exception("Error in RPF7 file entry.");
            }

        }
        public override void Write(DataWriter writer)
        {
            writer.Write((ushort)NameOffset);

            var buf1 = new byte[] {
                (byte)((FileSize >> 0) & 0xFF),
                (byte)((FileSize >> 8) & 0xFF),
                (byte)((FileSize >> 16) & 0xFF)
            };
            writer.Write(buf1);

            var buf2 = new byte[] {
                (byte)((FileOffset >> 0) & 0xFF),
                (byte)((FileOffset >> 8) & 0xFF),
                (byte)((FileOffset >> 16) & 0xFF)
            };
            writer.Write(buf2);

            writer.Write(FileUncompressedSize);

            if (IsEncrypted)
                writer.Write((uint)1);
            else
                writer.Write((uint)0);
        }
        public override string ToString()
        {
            return "Binary file: " + Path;
        }

        public override long GetFileSize()
        {
            return (FileSize == 0) ? FileUncompressedSize : FileSize;
        }
        public override void SetFileSize(uint s)
        {
            FileSize = s;
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public class RpfResourceFileEntry : RpfFileEntry
    {
        public RpfResourcePageFlags SystemFlags { get; set; }
        public RpfResourcePageFlags GraphicsFlags { get; set; }

        public static int GetSizeFromFlags(uint flags)
        {
            var s0 = ((flags >> 27) & 0x1)  << 0;
            var s1 = ((flags >> 26) & 0x1)  << 1;
            var s2 = ((flags >> 25) & 0x1)  << 2;
            var s3 = ((flags >> 24) & 0x1)  << 3;
            var s4 = ((flags >> 17) & 0x7F) << 4;
            var s5 = ((flags >> 11) & 0x3F) << 5;
            var s6 = ((flags >> 7)  & 0xF)  << 6;
            var s7 = ((flags >> 5)  & 0x3)  << 7;
            var s8 = ((flags >> 4)  & 0x1)  << 8;
            var ss = ((flags >> 0)  & 0xF);
            var baseSize = 0x200 << (int)ss;
            var size = baseSize * (s0 + s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8);
            return (int)size;

            #region dexyfex testing

            #endregion

            #region  original neo version (system)
            #endregion

            #region  original neo version (graphics)
            #endregion

        }
        public static uint GetFlagsFromSize(int size, uint version)
        {

            int origsize = size;
            int remainder = size & 0x1FF;
            int blocksize = 0x200;
            if (remainder != 0)
            {
                size = (size - remainder) + blocksize;
            }

            uint blockcount = (uint)size >> 9;
            uint ss = 0;
            while (blockcount > 1024)
            {
                ss++;
                blockcount = blockcount >> 1;
            }
            if (ss > 0)
            {
                size = origsize;
                blocksize = blocksize << (int)ss;
                remainder = size & blocksize;
                if(remainder!=0)
                {
                    size = (size - remainder) + blocksize;
                }
            }

            var s0 = (blockcount >> 0) & 0x1;
            var s1 = (blockcount >> 1) & 0x1;
            var s2 = (blockcount >> 2) & 0x1;
            var s3 = (blockcount >> 3) & 0x1;
            var s4 = (blockcount >> 4) & 0x7F;
            var s5 = (blockcount >> 5) & 0x3F;
            var s6 = (blockcount >> 6) & 0xF;
            var s7 = (blockcount >> 7) & 0x3;
            var s8 = (blockcount >> 8) & 0x1;

            if (ss > 4)
            { }
            if (s4 > 0x7F)
            { }

            uint f = 0;
            f |= (version & 0xF) << 28;
            f |= (s0 & 0x1) << 27;
            f |= (s1 & 0x1) << 26;
            f |= (s2 & 0x1) << 25;
            f |= (s3 & 0x1) << 24;
            f |= (s4 & 0x7F) << 17;
            f |= (ss & 0xF);

            return f;

        }
        public static uint GetFlagsFromBlocks(uint blockCount, uint blockSize, uint version)
        {

            uint s0 = 0;
            uint s1 = 0;
            uint s2 = 0;
            uint s3 = 0;
            uint s4 = 0;
            uint s5 = 0;
            uint s6 = 0;
            uint s7 = 0;
            uint s8 = 0;
            uint ss = 0;

            uint bst = blockSize;
            if (blockCount > 0)
            {
                while (bst > 0x200)
                {
                    ss++;
                    bst = bst >> 1;
                }
            }
            s0 = (blockCount >> 0) & 0x1;
            s1 = (blockCount >> 1) & 0x1;
            s2 = (blockCount >> 2) & 0x1;
            s3 = (blockCount >> 3) & 0x1;
            s4 = (blockCount >> 4) & 0x7F;

            if (ss > 0xF)
            { }
            if (s4 > 0x7F)
            { }

            uint f = 0;
            f |= (version & 0xF) << 28;
            f |= (s0 & 0x1) << 27;
            f |= (s1 & 0x1) << 26;
            f |= (s2 & 0x1) << 25;
            f |= (s3 & 0x1) << 24;
            f |= (s4 & 0x7F) << 17;
            f |= (s5 & 0x3F) << 11;
            f |= (s6 & 0xF) << 7;
            f |= (s7 & 0x3) << 5;
            f |= (s8 & 0x1) << 4;
            f |= (ss & 0xF);

            return f;
        }
        public static int GetVersionFromFlags(uint sysFlags, uint gfxFlags)
        {
            var sv = (sysFlags >> 28) & 0xF;
            var gv = (gfxFlags >> 28) & 0xF;
            return (int)((sv << 4) + gv);
        }

        public int Version
        {
            get
            {
                return GetVersionFromFlags(SystemFlags, GraphicsFlags);
            }
        }

        public int SystemSize
        {
            get
            {
                return (int)SystemFlags.Size;
            }
        }
        public int GraphicsSize
        {
            get
            {
                return (int)GraphicsFlags.Size;
            }
        }

        public override void Read(DataReader reader)
        {
            NameOffset = reader.ReadUInt16();

            var buf1 = reader.ReadBytes(3);
            FileSize = (uint)buf1[0] + (uint)(buf1[1] << 8) + (uint)(buf1[2] << 16);

            var buf2 = reader.ReadBytes(3);
            FileOffset = ((uint)buf2[0] + (uint)(buf2[1] << 8) + (uint)(buf2[2] << 16)) & 0x7FFFFF;

            SystemFlags = reader.ReadUInt32();
            GraphicsFlags = reader.ReadUInt32();

            if (FileSize == 0xFFFFFF)
            {
                BinaryReader cfr = File.CurrentFileReader;
                long opos = cfr.BaseStream.Position;
                cfr.BaseStream.Position = File.StartPos + ((long)FileOffset * 512);
                var buf = cfr.ReadBytes(16);
                FileSize = ((uint)buf[7] << 0) | ((uint)buf[14] << 8) | ((uint)buf[5] << 16) | ((uint)buf[2] << 24);
                cfr.BaseStream.Position = opos;
            }

        }
        public override void Write(DataWriter writer)
        {
            writer.Write((ushort)NameOffset);

            var fs = FileSize;
            if (fs > 0xFFFFFF) fs = 0xFFFFFF;

            var buf1 = new byte[] {
                (byte)((fs >> 0) & 0xFF),
                (byte)((fs >> 8) & 0xFF),
                (byte)((fs >> 16) & 0xFF)
            };
            writer.Write(buf1);

            var buf2 = new byte[] {
                (byte)((FileOffset >> 0) & 0xFF),
                (byte)((FileOffset >> 8) & 0xFF),
                (byte)(((FileOffset >> 16) & 0xFF) | 0x80)
            };
            writer.Write(buf2);

            writer.Write(SystemFlags);
            writer.Write(GraphicsFlags);
        }
        public override string ToString()
        {
            return "Resource file: " + Path;
        }

        public override long GetFileSize()
        {
            return (FileSize == 0) ? (long)(SystemSize + GraphicsSize) : FileSize;
        }
        public override void SetFileSize(uint s)
        {
            FileSize = s;
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public struct RpfResourcePageFlags
    {
        public uint Value { get; set; }

        public RpfResourcePage[] Pages
        {
            get
            {
                var count = Count;
                if (count == 0) return null;
                var pages = new RpfResourcePage[count];
                var counts = PageCounts;
                var sizes = BaseSizes;
                int n = 0;
                uint o = 0;
                for (int i = 0; i < counts.Length; i++)
                {
                    var c = counts[i];
                    var s = sizes[i];
                    for (int p = 0; p < c; p++)
                    {
                        pages[n] = new RpfResourcePage() { Size = s, Offset = o };
                        o += s;
                        n++;
                    }
                }
                return pages;
            }
        }

        public uint TypeVal { get { return (Value >> 28) & 0xF; } }
        public uint BaseShift { get { return (Value & 0xF); } }
        public uint BaseSize { get { return (0x200u << (int)BaseShift); } }
        public uint[] BaseSizes
        {
            get
            {
                var baseSize = BaseSize;
                return new uint[]
                {
                    baseSize << 8,
                    baseSize << 7,
                    baseSize << 6,
                    baseSize << 5,
                    baseSize << 4,
                    baseSize << 3,
                    baseSize << 2,
                    baseSize << 1,
                    baseSize << 0,
                };
            }
        }
        public uint[] PageCounts
        {
            get
            {
                return new uint[]
                {
                    ((Value >> 4)  & 0x1),
                    ((Value >> 5)  & 0x3),
                    ((Value >> 7)  & 0xF),
                    ((Value >> 11) & 0x3F),
                    ((Value >> 17) & 0x7F),
                    ((Value >> 24) & 0x1),
                    ((Value >> 25) & 0x1),
                    ((Value >> 26) & 0x1),
                    ((Value >> 27) & 0x1),
                };
            }
        }
        public uint[] PageSizes
        {
            get
            {
                var counts = PageCounts;
                var baseSizes = BaseSizes;
                return new uint[]
                {
                    baseSizes[0] * counts[0],
                    baseSizes[1] * counts[1],
                    baseSizes[2] * counts[2],
                    baseSizes[3] * counts[3],
                    baseSizes[4] * counts[4],
                    baseSizes[5] * counts[5],
                    baseSizes[6] * counts[6],
                    baseSizes[7] * counts[7],
                    baseSizes[8] * counts[8],
                };
            }
        }
        public uint Count
        {
            get
            {
                var c = PageCounts;
                return c[0] + c[1] + c[2] + c[3] + c[4] + c[5] + c[6] + c[7] + c[8];
            }
        }
        public uint Size
        {
            get
            {
                var flags = Value;
                var s0 = ((flags >> 27) & 0x1)  << 0;
                var s1 = ((flags >> 26) & 0x1)  << 1;
                var s2 = ((flags >> 25) & 0x1)  << 2;
                var s3 = ((flags >> 24) & 0x1)  << 3;
                var s4 = ((flags >> 17) & 0x7F) << 4;
                var s5 = ((flags >> 11) & 0x3F) << 5;
                var s6 = ((flags >> 7)  & 0xF)  << 6;
                var s7 = ((flags >> 5)  & 0x3)  << 7;
                var s8 = ((flags >> 4)  & 0x1)  << 8;
                var ss = ((flags >> 0)  & 0xF);
                var baseSize = 0x200u << (int)ss;
                return baseSize * (s0 + s1 + s2 + s3 + s4 + s5 + s6 + s7 + s8);
            }
        }

        public RpfResourcePageFlags(uint v)
        {
            Value = v;
        }

        public RpfResourcePageFlags(uint[] pageCounts, uint baseShift)
        {
            var v = baseShift & 0xF;
            v += (pageCounts[0] & 0x1)  << 4;
            v += (pageCounts[1] & 0x3)  << 5;
            v += (pageCounts[2] & 0xF)  << 7;
            v += (pageCounts[3] & 0x3F) << 11;
            v += (pageCounts[4] & 0x7F) << 17;
            v += (pageCounts[5] & 0x1)  << 24;
            v += (pageCounts[6] & 0x1)  << 25;
            v += (pageCounts[7] & 0x1)  << 26;
            v += (pageCounts[8] & 0x1)  << 27;
            Value = v;
        }

        public static implicit operator uint(RpfResourcePageFlags f)
        {
            return f.Value;
        }
        public static implicit operator RpfResourcePageFlags(uint v)
        {
            return new RpfResourcePageFlags(v);
        }

        public override string ToString()
        {
            return "Size: " + Size.ToString() + ", Pages: " + Count.ToString();
        }
    }

    [TypeConverter(typeof(ExpandableObjectConverter))] public struct RpfResourcePage
    {
        public uint Size { get; set; }
        public uint Offset { get; set; }

        public override string ToString()
        {
            return Size.ToString() + ": " + Offset.ToString();
        }
    }

    public interface PackedFile
    {
        void Load(byte[] data, RpfFileEntry entry);
    }

}
