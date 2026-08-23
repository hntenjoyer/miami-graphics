using RageLib.Archives;
using RageLib.GTA5.Archives;
using RageLib.GTA5.ArchiveWrappers;

namespace MiamiGraphics.DlcLab;

public static class Packer
{
    public sealed record Stats(int Dirs, int Files, int Rpfs, int Resources, long Size);

    public static Stats Pack(string srcFolder, string outRpf, bool verbose = false)
    {
        if (!Directory.Exists(srcFolder))
            throw new DirectoryNotFoundException($"Source folder not found: {srcFolder}");

        var outDir = Path.GetDirectoryName(Path.GetFullPath(outRpf));
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        if (File.Exists(outRpf)) File.Delete(outRpf);

        int dirs = 0, files = 0, rpfs = 0, resources = 0;

        var arc = RageArchiveWrapper7.Create(outRpf);
        try
        {
            void PackDir(string fsDir, IArchiveDirectory arcDir)
            {
                foreach (var file in Directory.GetFiles(fsDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(file);
                    byte[] raw = File.ReadAllBytes(file);
                    bool isRsc7 = raw.Length >= 4 && raw[0] == 0x52 && raw[1] == 0x53 && raw[2] == 0x43;
                    bool isRpf = name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase);

                    if (isRsc7 && !isRpf)
                    {
                        var f = arcDir.CreateResourceFile();
                        f.Name = name;
                        f.Import(new MemoryStream(raw));
                        resources++;
                    }
                    else
                    {
                        var f = arcDir.CreateBinaryFile();
                        f.Name = name;
                        f.IsCompressed = !isRpf;
                        f.IsEncrypted = false;
                        f.UncompressedSize = raw.Length;
                        f.Import(new MemoryStream(raw));
                        if (isRpf) rpfs++;
                    }
                    files++;
                    if (verbose) Console.WriteLine($"  + {name} ({raw.Length:N0}b{(isRpf ? ", rpf/uncompressed" : isRsc7 ? ", resource" : "")})");
                }

                foreach (var sub in Directory.GetDirectories(fsDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var d = arcDir.CreateDirectory();
                    d.Name = Path.GetFileName(sub);
                    dirs++;
                    PackDir(sub, d);
                }
            }

            PackDir(srcFolder, arc.Root);

            arc.archive_.Encryption = RageArchiveEncryption7.None;
            arc.Flush();
        }
        finally
        {
            arc.Dispose();
        }

        return new Stats(dirs, files, rpfs, resources, new FileInfo(outRpf).Length);
    }
}
