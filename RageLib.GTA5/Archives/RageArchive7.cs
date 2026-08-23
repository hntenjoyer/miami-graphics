/*
    Copyright(c) 2015 Neodymium

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

using RageLib.Cryptography;
using RageLib.Data;
using RageLib.GTA5.Cryptography;
using System;
using System.Collections.Generic;
using System.IO;

namespace RageLib.GTA5.Archives
{
    public interface IRageArchiveEntry7
    {
        uint NameOffset { get; set; }
        string Name { get; set; }

        void Read(DataReader reader);
        void Write(DataWriter writer);
    }

    public interface IRageArchiveFileEntry7 : IRageArchiveEntry7
    {
        uint FileOffset { get; set; }
        uint FileSize { get; set; }
    }

    public enum RageArchiveEncryption7
    {
        None,
        AES,
        NG
    }

    public class RageArchive7 : IDisposable
    {
        private const uint IDENT = 0x52504637;

        public RageArchiveEncryption7 Encryption { get; set; }

        private bool LeaveOpen;
        public Stream BaseStream { get; private set; }

        public RageArchiveDirectory7 Root { get; set; }

        public RageArchive7(Stream fileStream, bool leaveOpen = false)
        {
            BaseStream = fileStream;
            LeaveOpen = leaveOpen;
        }

        public void ReadHeader(byte[] aesKey = null, byte[] ngKey = null)
        {
            var reader = new DataReader(BaseStream);
            var posbak = reader.Position;
            reader.Position = 0;

            uint header_identifier = reader.ReadUInt32();
            if (header_identifier != IDENT)
                throw new Exception("The identifier " + header_identifier.ToString("X8") + " did not match the expected value of 0x52504637");

            uint header_entriesCount = reader.ReadUInt32();
            uint header_namesLength = reader.ReadUInt32();
            uint header_encryption = reader.ReadUInt32();

            byte[] entries_data_dec = null;
            byte[] names_data_dec = null;

            var headerPayloadStart = reader.Position;
            var entriesRaw = reader.ReadBytes(16 * (int)header_entriesCount);
            var namesRaw   = reader.ReadBytes((int)header_namesLength);

            RageArchiveEncryption7 decideMode(uint marker)
            {
                if (marker == 0x04E45504F) return RageArchiveEncryption7.None;
                if (marker == 0x0ffffff9)  return RageArchiveEncryption7.AES;
                return RageArchiveEncryption7.NG;
            }
            (byte[] e, byte[] n) decryptAs(RageArchiveEncryption7 mode)
            {
                switch (mode)
                {
                    case RageArchiveEncryption7.None:
                        return ((byte[])entriesRaw.Clone(), (byte[])namesRaw.Clone());
                    case RageArchiveEncryption7.AES:
                        if (aesKey is null) return (null, null);
                        return (AesEncryption.DecryptData(entriesRaw, aesKey),
                                AesEncryption.DecryptData(namesRaw,   aesKey));
                    default:
                        if (ngKey is null) return (null, null);
                        return (GTA5Crypto.Decrypt(entriesRaw, ngKey),
                                GTA5Crypto.Decrypt(namesRaw,   ngKey));
                }
            }
            bool hasDirectory(byte[] entriesDec)
            {
                if (entriesDec is null) return false;
                int n = entriesDec.Length / 16;
                for (int i = 0; i < n; i++)
                {
                    int off = i * 16 + 4;
                    if (off + 4 > entriesDec.Length) break;
                    int x = (entriesDec[off]) | (entriesDec[off+1] << 8)
                          | (entriesDec[off+2] << 16) | (entriesDec[off+3] << 24);
                    if (x == 0x7FFFFF00) return true;
                }
                return false;
            }

            var primaryMode = decideMode(header_encryption);
            var (eDec, nDec) = decryptAs(primaryMode);
            if (hasDirectory(eDec))
            {
                Encryption       = primaryMode;
                entries_data_dec = eDec;
                names_data_dec   = nDec;
            }
            else
            {
                var fallback = new[]
                {
                    RageArchiveEncryption7.None,
                    RageArchiveEncryption7.AES,
                    RageArchiveEncryption7.NG,
                };
                bool recovered = false;
                foreach (var m in fallback)
                {
                    if (m == primaryMode) continue;
                    var pair = decryptAs(m);
                    if (hasDirectory(pair.e))
                    {
                        Encryption       = m;
                        entries_data_dec = pair.e;
                        names_data_dec   = pair.n;
                        System.Diagnostics.Debug.WriteLine(
                            $"[ragearchive7] non-standard encryption marker 0x{header_encryption:X8} — " +
                            $"recovered using {m} after retry");
                        recovered = true;
                        break;
                    }
                }
                if (!recovered && GTA5Constants.PC_NG_KEYS != null)
                {
                    foreach (var k in GTA5Constants.PC_NG_KEYS)
                    {
                        if (k is null || k.Length == 0) continue;
                        byte[] eTry;
                        try { eTry = GTA5Crypto.Decrypt(entriesRaw, k); }
                        catch { continue; }
                        if (!hasDirectory(eTry)) continue;
                        byte[] nTry;
                        try { nTry = GTA5Crypto.Decrypt(namesRaw, k); }
                        catch { continue; }
                        Encryption       = RageArchiveEncryption7.NG;
                        entries_data_dec = eTry;
                        names_data_dec   = nTry;
                        System.Diagnostics.Debug.WriteLine(
                            "[ragearchive7] NG key index mismatch (файл переименован или изменился размер) — " +
                            "recovered by scanning the key set");
                        recovered = true;
                        break;
                    }
                }

                if (!recovered)
                {
                    Encryption       = primaryMode;
                    entries_data_dec = eDec ?? (byte[])entriesRaw.Clone();
                    names_data_dec   = nDec ?? (byte[])namesRaw.Clone();
                }
            }
            _ = headerPayloadStart;

            var entries_reader = new DataReader(new MemoryStream(entries_data_dec));
            var names_reader = new DataReader(new MemoryStream(names_data_dec));

            var entries = new List<IRageArchiveEntry7>();
            for (var index = 0; index < header_entriesCount; index++)
            {
                entries_reader.Position += 4;
                int x = entries_reader.ReadInt32();
                entries_reader.Position -= 8;

                if (x == 0x7fffff00)
                {
                    var e = new RageArchiveDirectory7();
                    e.Read(entries_reader);

                    names_reader.Position = e.NameOffset;
                    e.Name = names_reader.ReadString();

                    entries.Add(e);
                }
                else
                {
                    if ((x & 0x80000000) == 0)
                    {
                        var e = new RageArchiveBinaryFile7();
                        e.Read(entries_reader);

                        names_reader.Position = e.NameOffset;
                        e.Name = names_reader.ReadString();

                        entries.Add(e);
                    }
                    else
                    {
                        var e = new RageArchiveResourceFile7();
                        e.Read(entries_reader);

                        if (e.FileSize == 0xFFFFFF)
                        {
                            reader.Position = 512 * e.FileOffset;
                            var buf = reader.ReadBytes(16);
                            e.FileSize = ((uint)buf[7] << 0) | ((uint)buf[14] << 8) | ((uint)buf[5] << 16) | ((uint)buf[2] << 24);
                        }

                        names_reader.Position = e.NameOffset;
                        e.Name = names_reader.ReadString();

                        entries.Add(e);
                    }
                }
            }

            int rootIdx = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] is RageArchiveDirectory7) { rootIdx = i; break; }
            }
            if (rootIdx < 0)
                throw new Exception("RPF7 has no directory entries — file is malformed or uses an unsupported format.");

            var stack = new Stack<RageArchiveDirectory7>();
            var rootDir = (RageArchiveDirectory7)entries[rootIdx];
            stack.Push(rootDir);
            Root = rootDir;
            while (stack.Count > 0)
            {
                var item = stack.Pop();

                for (int index = (int)item.EntriesIndex; index < (item.EntriesIndex + item.EntriesCount); index++)
                {
                    if (entries[index] is RageArchiveDirectory7)
                    {
                        item.Directories.Add(entries[index] as RageArchiveDirectory7);
                        stack.Push(entries[index] as RageArchiveDirectory7);
                    }
                    else
                    {
                        item.Files.Add(entries[index]);
                    }
                }
            }

            reader.Position = posbak;
        }

        public void WriteHeader(byte[] aesKey = null, byte[] ngKey = null)
        {
            var positionBackup = BaseStream.Position;

            var writer = new DataWriter(BaseStream);

            var entries = new List<IRageArchiveEntry7>();
            var stack = new Stack<RageArchiveDirectory7>();
            var nameOffset = 1;

            entries.Add(Root);
            stack.Push(Root);

            var nameDict = new Dictionary<string, uint>();
            nameDict.Add("", 0);

            while (stack.Count > 0)
            {
                var directory = stack.Pop();

                directory.EntriesIndex = (uint)entries.Count;
                directory.EntriesCount = (uint)directory.Directories.Count + (uint)directory.Files.Count;

                var theList = new List<IRageArchiveEntry7>();

                foreach (var xd in directory.Directories)
                {
                    if (!nameDict.ContainsKey(xd.Name))
                    {
                        nameDict.Add(xd.Name, (uint)nameOffset);
                        nameOffset += xd.Name.Length + 1;
                    }
                    xd.NameOffset = nameDict[xd.Name];

                    theList.Add(xd);
                }

                foreach (var xf in directory.Files)
                {
                    if (!nameDict.ContainsKey(xf.Name))
                    {
                        nameDict.Add(xf.Name, (uint)nameOffset);
                        nameOffset += xf.Name.Length + 1;
                    }
                    xf.NameOffset = nameDict[xf.Name];

                    theList.Add(xf);
                }

                theList.Sort(
                    delegate (IRageArchiveEntry7 a, IRageArchiveEntry7 b)
                    {
                        return string.CompareOrdinal(a.Name, b.Name);
                    }
                    );
                foreach (var xx in theList)
                    entries.Add(xx);
                theList.Reverse();
                foreach (var xx in theList)
                    if (xx is RageArchiveDirectory7)
                        stack.Push((RageArchiveDirectory7)xx);
            }

            foreach (var entry in entries)
                if (entry is RageArchiveResourceFile7)
                {
                    var resource = entry as RageArchiveResourceFile7;
                    if (resource.FileSize > 0xFFFFFF)
                    {
                        var buf = new byte[16];
                        buf[7] = (byte)((resource.FileSize >> 0) & 0xFF);
                        buf[14] = (byte)((resource.FileSize >> 8) & 0xFF);
                        buf[5] = (byte)((resource.FileSize >> 16) & 0xFF);
                        buf[2] = (byte)((resource.FileSize >> 24) & 0xFF);

                        if (writer.Length > 512 * resource.FileOffset)
                        {
                            writer.Position = 512 * resource.FileOffset;
                            writer.Write(buf);
                        }

                        resource.FileSize = 0xFFFFFF;
                    }
                }

            var ent_str = new MemoryStream();
            var ent_wr = new DataWriter(ent_str);
            foreach (var entry in entries)
                entry.Write(ent_wr);
            ent_str.Flush();

            var ent_buf = new byte[ent_str.Length];
            ent_str.Position = 0;
            ent_str.Read(ent_buf, 0, ent_buf.Length);

            if (Encryption == RageArchiveEncryption7.AES)
                ent_buf = AesEncryption.EncryptData(ent_buf, aesKey);
            if (Encryption == RageArchiveEncryption7.NG)
            {
                Encryption = RageArchiveEncryption7.None;
            }

            var n_str = new MemoryStream();
            var n_wr = new DataWriter(n_str);
            foreach (var entry in nameDict)
                n_wr.Write(entry.Key);
            var empty = new byte[16 - (n_wr.Length % 16)];
            n_wr.Write(empty);
            n_str.Flush();

            var n_buf = new byte[n_str.Length];
            n_str.Position = 0;
            n_str.Read(n_buf, 0, n_buf.Length);

            if (Encryption == RageArchiveEncryption7.AES)
                n_buf = AesEncryption.EncryptData(n_buf, aesKey);

            writer.Position = 0;
            writer.Write((uint)IDENT);
            writer.Write((uint)entries.Count);
            writer.Write((uint)n_buf.Length);

            switch (Encryption)
            {
                case RageArchiveEncryption7.None:
                    writer.Write((uint)0x04E45504F);
                    break;
                case RageArchiveEncryption7.AES:
                    writer.Write((uint)0x0ffffff9);
                    break;
                case RageArchiveEncryption7.NG:
                    writer.Write((uint)0x0fefffff);
                    break;
            }

            writer.Write(ent_buf);
            writer.Write(n_buf);

            BaseStream.Position = positionBackup;
        }

        public void Dispose()
        {
            if (BaseStream != null)
                BaseStream.Dispose();

            BaseStream = null;
            Root = null;
        }
    }

    public class RageArchiveDirectory7 : IRageArchiveEntry7
    {
        public uint NameOffset { get; set; }
        public uint EntriesIndex { get; set; }
        public uint EntriesCount { get; set; }

        public string Name { get; set; }
        public List<RageArchiveDirectory7> Directories = new List<RageArchiveDirectory7>();
        public List<IRageArchiveEntry7> Files = new List<IRageArchiveEntry7>();

        public void Read(DataReader reader)
        {
            this.NameOffset = reader.ReadUInt32();

            uint ident = reader.ReadUInt32();
            if (ident != 0x7FFFFF00)
                throw new Exception("Error in RPF7 directory entry.");

            this.EntriesIndex = reader.ReadUInt32();
            this.EntriesCount = reader.ReadUInt32();
        }

        public void Write(DataWriter writer)
        {
            writer.Write(this.NameOffset);
            writer.Write((uint)0x7FFFFF00);
            writer.Write(this.EntriesIndex);
            writer.Write(this.EntriesCount);
        }
    }

    public class RageArchiveBinaryFile7 : IRageArchiveFileEntry7
    {
        public uint NameOffset { get; set; }
        public uint FileSize { get; set; }
        public uint FileOffset { get; set; }
        public uint FileUncompressedSize { get; set; }
        public bool IsEncrypted { get; set; }

        public string Name { get; set; }

        public void Read(DataReader reader)
        {
            NameOffset = reader.ReadUInt16();

            var buf1 = reader.ReadBytes(3);
            FileSize = (uint)buf1[0] + (uint)(buf1[1] << 8) + (uint)(buf1[2] << 16);

            var buf2 = reader.ReadBytes(3);
            FileOffset = (uint)buf2[0] + (uint)(buf2[1] << 8) + (uint)(buf2[2] << 16);

            FileUncompressedSize = reader.ReadUInt32();

            var encFlag = reader.ReadUInt32();
            IsEncrypted = encFlag != 0;
        }

        public void Write(DataWriter writer)
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
    }

    public class RageArchiveResourceFile7 : IRageArchiveFileEntry7
    {
        public uint NameOffset { get; set; }
        public uint FileSize { get; set; }
        public uint FileOffset { get; set; }
        public uint SystemFlags { get; set; }
        public uint GraphicsFlags { get; set; }

        public string Name { get; set; }

        public void Read(DataReader reader)
        {
            NameOffset = reader.ReadUInt16();

            var buf1 = reader.ReadBytes(3);
            FileSize = (uint)buf1[0] + (uint)(buf1[1] << 8) + (uint)(buf1[2] << 16);

            var buf2 = reader.ReadBytes(3);
            FileOffset = ((uint)buf2[0] + (uint)(buf2[1] << 8) + (uint)(buf2[2] << 16)) & 0x7FFFFF;

            SystemFlags = reader.ReadUInt32();
            GraphicsFlags = reader.ReadUInt32();
        }

        public void Write(DataWriter writer)
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
                (byte)(((FileOffset >> 16) & 0xFF) | 0x80)
            };
            writer.Write(buf2);

            writer.Write(SystemFlags);
            writer.Write(GraphicsFlags);
        }
    }
}
