using System;
using System.IO;
using System.IO.Compression;
using RageLib.Archives;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.Core.Parser
{
    public static class RpfRealBytes
    {
        public static byte[] Get(IArchiveFile file)
        {
            if (file is IArchiveBinaryFile binFile)
            {
                using var ms = new MemoryStream();
                binFile.Export(ms);
                byte[] buf = ms.ToArray();

                if (binFile.IsEncrypted)
                {
                    var hash = GTA5Hash.CalculateHash(binFile.Name);
                    var keyIdx = (hash + (uint)binFile.UncompressedSize + (101 - 40)) % 0x65;
                    var key = GTA5Constants.PC_NG_KEYS != null && GTA5Constants.PC_NG_KEYS.Length > keyIdx
                        ? GTA5Constants.PC_NG_KEYS[keyIdx]
                        : null;
                    if (key == null || key.Length == 0)
                        throw new InvalidOperationException(
                            $"Нет NG-ключа для записи {binFile.Name} (idx={keyIdx}) - ключи не загружены (GTA5Constants.LoadFromPath)?");
                    buf = GTA5Crypto.Decrypt(buf, key);
                }

                if (binFile.IsCompressed)
                {
                    using var def = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    def.CopyTo(outMs);
                    return outMs.ToArray();
                }
                return buf;
            }

            using (var ms = new MemoryStream())
            {
                file.Export(ms);
                byte[] data = ms.ToArray();
                if (data.Length >= 4 && data[0] == 0x52 && data[1] == 0x53 && data[2] == 0x43 && data[3] == 0x07)
                    data[3] = 0x37;
                return data;
            }
        }
    }
}
