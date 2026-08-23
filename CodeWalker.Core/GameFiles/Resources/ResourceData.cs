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

//shamelessly stolen and mangled

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CodeWalker.GameFiles
{

    public class ResourceDataReader : DataReader
    {
        public bool IsGen9 = RpfManager.IsGen9;

        private const long SYSTEM_BASE = 0x50000000;
        private const long GRAPHICS_BASE = 0x60000000;

        private Stream systemStream;
        private Stream graphicsStream;

        public RpfResourceFileEntry FileEntry { get; set; }

        public Dictionary<long, IResourceBlock> blockPool = new Dictionary<long, IResourceBlock>();
        public Dictionary<long, object> arrayPool = new Dictionary<long, object>();

        public override long Length
        {
            get
            {
                return -1;
            }
        }

        public override long Position
        {
            get;
            set;
        }

        public ResourceDataReader(Stream systemStream, Stream graphicsStream, Endianess endianess = Endianess.LittleEndian)
            : base((Stream)null, endianess)
        {
            this.systemStream = systemStream;
            this.graphicsStream = graphicsStream;
        }

        public ResourceDataReader(RpfResourceFileEntry resentry, byte[] data, Endianess endianess = Endianess.LittleEndian)
            : base((Stream)null, endianess)
        {
            FileEntry = resentry;
            var systemSize = resentry.SystemSize;
            var graphicsSize = resentry.GraphicsSize;

            this.systemStream = new MemoryStream(data, 0, systemSize);
            this.graphicsStream = new MemoryStream(data, systemSize, graphicsSize);
            Position = 0x50000000;
        }

        public ResourceDataReader(int systemSize, int graphicsSize, byte[] data, Endianess endianess = Endianess.LittleEndian)
            : base((Stream)null, endianess)
        {
            this.systemStream = new MemoryStream(data, 0, systemSize);
            this.graphicsStream = new MemoryStream(data, systemSize, graphicsSize);
            Position = 0x50000000;
        }

        protected override byte[] ReadFromStream(int count, bool ignoreEndianess = false)
        {
            if ((Position & SYSTEM_BASE) == SYSTEM_BASE)
            {

                systemStream.Position = Position & ~0x50000000;

                var buffer = new byte[count];
                systemStream.Read(buffer, 0, count);

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    Array.Reverse(buffer);
                }

                Position = systemStream.Position | 0x50000000;
                return buffer;

            }
            if ((Position & GRAPHICS_BASE) == GRAPHICS_BASE)
            {

                graphicsStream.Position = Position & ~0x60000000;

                var buffer = new byte[count];
                graphicsStream.Read(buffer, 0, count);

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    Array.Reverse(buffer);
                }

                Position = graphicsStream.Position | 0x60000000;
                return buffer;
            }
            throw new Exception("illegal position!");
        }

        public T ReadBlock<T>(params object[] parameters) where T : IResourceBlock, new()
        {
            var usepool = !typeof(IResourceNoCacheBlock).IsAssignableFrom(typeof(T));
            if (usepool)
            {
                if (blockPool.ContainsKey(Position))
                {
                    var block = blockPool[Position];
                    if (block is T tblk)
                    {
                        Position += block.BlockLength;
                        return tblk;
                    }
                    else
                    {
                        usepool = false;
                    }
                }
            }

            var result = new T();

            if (result is IResourceXXSystemBlock)
            {
                result = (T)((IResourceXXSystemBlock)result).GetType(this, parameters);
            }

            if (result == null)
            {
                return default(T);
            }

            if (usepool)
            {
                blockPool[Position] = result;
            }

            result.Read(this, parameters);

            return result;
        }

        public T ReadBlockAt<T>(ulong position, params object[] parameters) where T : IResourceBlock, new()
        {
            if (position != 0)
            {
                var positionBackup = Position;

                Position = (long)position;
                var result = ReadBlock<T>(parameters);
                Position = positionBackup;

                return result;
            }
            else
            {
                return default(T);
            }
        }

        public T[] ReadBlocks<T>(ulong[] pointers) where T : IResourceBlock, new()
        {
            if (pointers == null) return null;
            var count = pointers.Length;
            var items = new T[count];
            for (int i = 0; i < count; i++)
            {
                items[i] = ReadBlockAt<T>(pointers[i]);
            }
            return items;
        }

        public byte[] ReadBytesAt(ulong position, uint count, bool cache = true)
        {
            long pos = (long)position;
            if ((pos <= 0) || (count == 0)) return null;
            var posbackup = Position;
            Position = pos;
            var result = ReadBytes((int)count);
            Position = posbackup;
            if (cache) arrayPool[(long)position] = result;
            return result;
        }
        public ushort[] ReadUshortsAt(ulong position, uint count, bool cache = true)
        {
            if ((position <= 0) || (count == 0)) return null;

            var result = new ushort[count];
            var length = count * 2;
            byte[] data = ReadBytesAt(position, length, false);
            Buffer.BlockCopy(data, 0, result, 0, (int)length);

            if (cache) arrayPool[(long)position] = result;

            return result;
        }
        public short[] ReadShortsAt(ulong position, uint count, bool cache = true)
        {
            if ((position <= 0) || (count == 0)) return null;
            var result = new short[count];
            var length = count * 2;
            byte[] data = ReadBytesAt(position, length, false);
            Buffer.BlockCopy(data, 0, result, 0, (int)length);

            if (cache) arrayPool[(long)position] = result;

            return result;
        }
        public uint[] ReadUintsAt(ulong position, uint count, bool cache = true)
        {
            if ((position <= 0) || (count == 0)) return null;

            var result = new uint[count];
            var length = count * 4;
            byte[] data = ReadBytesAt(position, length, false);
            Buffer.BlockCopy(data, 0, result, 0, (int)length);

            if (cache) arrayPool[(long)position] = result;

            return result;
        }
        public ulong[] ReadUlongsAt(ulong position, uint count, bool cache = true)
        {
            if ((position <= 0) || (count == 0)) return null;

            var result = new ulong[count];
            var length = count * 8;
            byte[] data = ReadBytesAt(position, length, false);
            Buffer.BlockCopy(data, 0, result, 0, (int)length);

            if (cache) arrayPool[(long)position] = result;

            return result;
        }
        public float[] ReadFloatsAt(ulong position, uint count, bool cache = true)
        {
            if ((position <= 0) || (count == 0)) return null;

            var result = new float[count];
            var length = count * 4;
            byte[] data = ReadBytesAt(position, length, false);
            Buffer.BlockCopy(data, 0, result, 0, (int)length);

            if (cache) arrayPool[(long)position] = result;

            return result;
        }
        public T[] ReadStructsAt<T>(ulong position, uint count, bool cache = true)
        {
            if ((position <= 0) || (count == 0)) return null;

            uint structsize = (uint)Marshal.SizeOf(typeof(T));
            var length = count * structsize;
            byte[] data = ReadBytesAt(position, length, false);

            var result = new T[count];
            GCHandle handle = GCHandle.Alloc(result, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            Marshal.Copy(data, 0, h, (int)length);
            handle.Free();

            if (cache) arrayPool[(long)position] = result;

            return result;
        }
        public T[] ReadStructs<T>(uint count)
        {
            uint structsize = (uint)Marshal.SizeOf(typeof(T));
            var result = new T[count];
            var length = count * structsize;
            byte[] data = ReadBytes((int)length);

            GCHandle handle = GCHandle.Alloc(result, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            Marshal.Copy(data, 0, h, (int)length);
            handle.Free();

            return result;
        }

        public T ReadStruct<T>() where T : struct
        {
            uint structsize = (uint)Marshal.SizeOf(typeof(T));
            var length = structsize;
            byte[] data = ReadBytes((int)length);
            GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
            var h = handle.AddrOfPinnedObject();
            var result = Marshal.PtrToStructure<T>(h);
            handle.Free();
            return result;
        }

        public T ReadStructAt<T>(long position) where T : struct
        {
            if ((position <= 0)) return default(T);
            var posbackup = Position;
            Position = (long)position;
            var result = ReadStruct<T>();
            Position = posbackup;
            return result;
        }

        public string ReadStringAt(ulong position)
        {
            long newpos = (long)position;
            if ((newpos <= 0)) return null;
            var lastpos = Position;
            Position = newpos;
            var result = ReadString();
            Position = lastpos;
            arrayPool[newpos] = result;
            return result;
        }

    }

    public class ResourceDataWriter : DataWriter
    {
        public bool IsGen9 = false;

        private const long SYSTEM_BASE = 0x50000000;
        private const long GRAPHICS_BASE = 0x60000000;

        private Stream systemStream;
        private Stream graphicsStream;

        public override long Length
        {
            get
            {
                return -1;
            }
        }

        public override long Position
        {
            get;
            set;
        }

        public ResourceDataWriter(Stream systemStream, Stream graphicsStream, Endianess endianess = Endianess.LittleEndian)
            : base((Stream)null, endianess)
        {
            this.systemStream = systemStream;
            this.graphicsStream = graphicsStream;
        }

        protected override void WriteToStream(byte[] value, bool ignoreEndianess = true)
        {
            if ((Position & SYSTEM_BASE) == SYSTEM_BASE)
            {

                systemStream.Position = Position & ~SYSTEM_BASE;

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    var buf = (byte[])value.Clone();
                    Array.Reverse(buf);
                    systemStream.Write(buf, 0, buf.Length);
                }
                else
                {
                    systemStream.Write(value, 0, value.Length);
                }

                Position = systemStream.Position | 0x50000000;
                return;

            }
            if ((Position & GRAPHICS_BASE) == GRAPHICS_BASE)
            {

                graphicsStream.Position = Position & ~GRAPHICS_BASE;

                if (!ignoreEndianess && (Endianess == Endianess.BigEndian))
                {
                    var buf = (byte[])value.Clone();
                    Array.Reverse(buf);
                    graphicsStream.Write(buf, 0, buf.Length);
                }
                else
                {
                    graphicsStream.Write(value, 0, value.Length);
                }

                Position = graphicsStream.Position | 0x60000000;
                return;
            }

            throw new Exception("illegal position!");
        }

        public void WriteBlock(IResourceBlock value)
        {
            value.Write(this);
        }

        public void WriteStruct<T>(T val) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(val, ptr, true);
            Marshal.Copy(ptr, arr, 0, size);
            Marshal.FreeHGlobal(ptr);
            Write(arr);
        }
        public void WriteStructs<T>(T[] val) where T : struct
        {
            if (val == null) return;
            foreach (var v in val)
            {
                WriteStruct(v);
            }
        }

        public void WritePadding(int alignment)
        {
            var pad = ((alignment - (Position % alignment)) % alignment);
            if (pad > 0) Write(new byte[pad]);
        }

        public void WriteUlongs(ulong[] val)
        {
            if (val == null) return;
            foreach (var v in val)
            {
                Write(v);
            }
        }

    }

    public interface IResourceBlock
    {
        long FilePosition { get; set; }

        long BlockLength { get; }
        long BlockLength_Gen9 { get; }

        void Read(ResourceDataReader reader, params object[] parameters);

        void Write(ResourceDataWriter writer, params object[] parameters);
    }

    public interface IResourceSystemBlock : IResourceBlock
    {
        Tuple<long, IResourceBlock>[] GetParts();

        IResourceBlock[] GetReferences();
    }

    public interface IResourceXXSystemBlock : IResourceSystemBlock
    {
        IResourceSystemBlock GetType(ResourceDataReader reader, params object[] parameters);
    }

    public interface IResourceGraphicsBlock : IResourceBlock
    { }

    public interface IResourceNoCacheBlock : IResourceBlock
    { }

    [TypeConverter(typeof(ExpandableObjectConverter))] public abstract class ResourceSystemBlock : IResourceSystemBlock
    {
        private long position;

        public virtual long FilePosition
        {
            get
            {
                return position;
            }
            set
            {
                position = value;
                foreach (var part in GetParts())
                {
                    part.Item2.FilePosition = value + part.Item1;
                }
            }
        }

        public abstract long BlockLength
        {
            get;
        }
        public virtual long BlockLength_Gen9 => BlockLength;

        public abstract void Read(ResourceDataReader reader, params object[] parameters);

        public abstract void Write(ResourceDataWriter writer, params object[] parameters);

        public virtual Tuple<long, IResourceBlock>[] GetParts()
        {
            return new Tuple<long, IResourceBlock>[0];
        }

        public virtual IResourceBlock[] GetReferences()
        {
            return new IResourceBlock[0];
        }
    }

    public abstract class ResourecTypedSystemBlock : ResourceSystemBlock, IResourceXXSystemBlock
    {
        public abstract IResourceSystemBlock GetType(ResourceDataReader reader, params object[] parameters);
    }

    public abstract class ResourceGraphicsBlock : IResourceGraphicsBlock
    {
        public virtual long FilePosition
        {
            get;
            set;
        }

        public abstract long BlockLength
        {
            get;
        }
        public virtual long BlockLength_Gen9 => BlockLength;

        public abstract void Read(ResourceDataReader reader, params object[] parameters);

        public abstract void Write(ResourceDataWriter writer, params object[] parameters);
    }

}
