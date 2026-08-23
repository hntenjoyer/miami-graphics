using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using RageLib.Archives;
using RageLib.GTA5.ArchiveWrappers;
using RageLib.GTA5.Cryptography;

namespace MiamiGraphics.RpfShrink;

internal static class Program
{
    private const string KitStamp = "v1";
    private const int MaxNestDepth = 8;

    private static bool _compress = true;
    private static bool _verbose;
    private static string _kitDir = "";
    private static readonly List<string> _tempDirs = new();

    private static int _entries;
    private static int _nested;
    private static int _nestedFailed;
    private static int _nestedKept;
    private static int _compressedCount;
    private static bool _verify = true;

    private static int Main(string[] argv)
    {
        int code = Run(argv);

        if (OwnsConsole())
        {
            Console.WriteLine();
            Console.WriteLine("Нажми Enter, чтобы закрыть...");
            try { Console.ReadLine(); } catch { }
        }
        return code;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint count);

    private static bool OwnsConsole()
    {
        try
        {
            var buf = new uint[8];
            uint n = GetConsoleProcessList(buf, (uint)buf.Length);
            return n <= 1;
        }
        catch { return false; }
    }

    private static int Run(string[] argv)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.WriteLine("RPF Shrink — пересборка rpf без пустот + сжатие + ArchiveFix");
        Console.WriteLine();

        string? input = null;
        string? outDir = null;
        string? logicalName = null;
        bool inPlace = false;

        for (int i = 0; i < argv.Length; i++)
        {
            var a = argv[i];
            switch (a.ToLowerInvariant())
            {
                case "--no-compress": _compress = false; continue;
                case "--no-verify": _verify = false; continue;
                case "-v":
                case "--verbose": _verbose = true; continue;
                case "--inplace":
                case "--replace": inPlace = true; continue;
                case "--as":
                    if (i + 1 >= argv.Length) { Console.WriteLine("--as требует имя файла"); return 2; }
                    logicalName = argv[++i];
                    continue;
                case "-h":
                case "--help":
                case "/?":
                    Help(); return 0;
            }
            if (a.StartsWith("-")) { Console.WriteLine($"Неизвестный ключ: {a}"); Help(); return 2; }
            if (input is null) input = a;
            else if (outDir is null) outDir = a;
        }

        if (input is null) { Help(); return 2; }

        input = Path.GetFullPath(input);
        if (!File.Exists(input))
        {
            Console.WriteLine($"Файл не найден: {input}");
            return 2;
        }

        logicalName ??= Path.GetFileName(input);
        outDir = inPlace
            ? Path.Combine(Path.GetTempPath(), "rpfshrink_final_" + Guid.NewGuid().ToString("N")[..8])
            : Path.GetFullPath(outDir ?? Path.Combine(Path.GetDirectoryName(input)!, "rpfshrink_out"));

        var sw = Stopwatch.StartNew();
        long sizeBefore = new FileInfo(input).Length;

        try
        {
            ExtractKit();
            GTA5Constants.LoadFromPath(_kitDir);
            int keys = GTA5Constants.PC_NG_KEYS?.Count(k => k is { Length: > 0 }) ?? 0;
            if (keys == 0) { Console.WriteLine("Ключи GTA5 не загрузились — дальше идти нельзя."); return 3; }
            if (_verbose) Console.WriteLine($"[kit] {_kitDir} — NG-ключей: {keys}");

            Console.WriteLine($"Вход:  {input}  ({Mb(sizeBefore)})");
            Console.WriteLine($"Режим: {(_compress ? "пересборка + сжатие" : "только пересборка (без сжатия)")}");
            Console.WriteLine();

            string built = ShrinkToTemp(input, logicalName, 0);
            long sizeAfter = new FileInfo(built).Length;

            if (_verify)
            {
                Console.WriteLine();
                Console.WriteLine("Сверка содержимого...");
                if (!Verify(input, built, logicalName))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("СВЕРКА НЕ ПРОШЛА — результат НЕ сохранён.");
                    Console.ResetColor();
                    return 4;
                }
            }

            string final = Path.Combine(outDir, Path.GetFileName(input));

            if (inPlace)
            {
                string bak = input + ".bak";
                if (File.Exists(bak)) File.Delete(bak);
                File.Move(input, bak);
                File.Move(built, input);
                final = input;
                Console.WriteLine();
                Console.WriteLine($"Оригинал сохранён: {bak}");
            }
            else
            {
                Directory.CreateDirectory(outDir);
                if (File.Exists(final)) File.Delete(final);
                File.Move(built, final);
            }

            sw.Stop();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Готово: {final}");
            Console.ResetColor();
            Console.WriteLine($"  было:    {Mb(sizeBefore)}");
            Console.WriteLine($"  стало:   {Mb(sizeAfter)}   ({Delta(sizeBefore, sizeAfter)})");
            Console.WriteLine($"  записей: {_entries}, вложенных rpf: {_nested}" +
                              (_nestedKept > 0 ? $" (оставлено как есть: {_nestedKept})" : "") +
                              (_nestedFailed > 0 ? $" (не пересобрано: {_nestedFailed})" : "") +
                              (_compress ? $", пережато: {_compressedCount}" : ""));
            Console.WriteLine($"  время:   {sw.Elapsed.TotalSeconds:F1} c");

            if (sizeAfter >= sizeBefore)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine();
                Console.WriteLine("Выигрыша нет: архив уже упакован плотно, пустот в нём не нашлось.");
                Console.WriteLine("Содержимое сверено и идентично — файл рабочий, просто не меньше исходного.");
                Console.ResetColor();
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine($"ОШИБКА: {ex.GetType().Name}: {ex.Message}");
            Console.ResetColor();
            if (_verbose) Console.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            foreach (var d in _tempDirs)
                try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
        }
    }

    private static string ShrinkToTemp(string srcPath, string logicalName, int depth)
    {
        string dir = NewTempDir();
        string outPath = Path.Combine(dir, logicalName);

        var fs = new FileStream(srcPath, FileMode.Open, FileAccess.Read);
        RageArchiveWrapper7 src;
        try { src = RageArchiveWrapper7.Open(fs, logicalName, leaveOpen: false); }
        catch { fs.Dispose(); throw; }

        using (src)
        {
            var dst = RageArchiveWrapper7.Create(outPath);
            try
            {
                dst.archive_.Encryption = RageLib.GTA5.Archives.RageArchiveEncryption7.None;

                CopyDirectory(src.Root, dst.Root, depth);

                dst.FileName = logicalName;
                dst.Flush();
            }
            finally { dst.Dispose(); }
        }

        if (!ArchiveFix(outPath))
            throw new Exception($"ArchiveFix не отработал на '{logicalName}' — архив остался без валидного NG-checksum");

        return outPath;
    }

    private static void CopyDirectory(IArchiveDirectory src, IArchiveDirectory dst, int depth)
    {
        foreach (var d in src.GetDirectories())
        {
            var nd = dst.CreateDirectory();
            nd.Name = d.Name;
            CopyDirectory(d, nd, depth);
        }

        foreach (var f in src.GetFiles())
        {
            _entries++;

            if (f is IArchiveBinaryFile bin)
            {
                if (bin.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && depth < MaxNestDepth)
                    CopyNestedRpf(bin, dst, depth);
                else
                    CopyBinary(bin, dst);
            }
            else if (f is IArchiveResourceFile res)
            {
                var nf = dst.CreateResourceFile();
                nf.Name = res.Name;
                using var ms = new MemoryStream();
                res.Export(ms);
                ms.Position = 0;
                nf.Import(ms);
            }
        }
    }

    private static void CopyNestedRpf(IArchiveBinaryFile bin, IArchiveDirectory dst, int depth)
    {
        byte[] raw = RealBytes(bin);
        byte[] outBytes;

        try
        {
            string tmpDir = NewTempDir();
            string tmpIn = Path.Combine(tmpDir, bin.Name);
            File.WriteAllBytes(tmpIn, raw);

            string shrunk = ShrinkToTemp(tmpIn, bin.Name, depth + 1);
            byte[] rebuilt = File.ReadAllBytes(shrunk);
            _nested++;

            const int MinNestedRpfBytes = 512;
            if (rebuilt.LongLength < raw.LongLength && rebuilt.LongLength >= MinNestedRpfBytes)
            {
                outBytes = rebuilt;
                if (_verbose || depth == 0)
                    Console.WriteLine($"{Indent(depth)}[rpf] {bin.Name}: {Mb(raw.LongLength)} → {Mb(rebuilt.LongLength)}  ({Delta(raw.LongLength, rebuilt.LongLength)})");
            }
            else
            {
                outBytes = raw;
                _nestedKept++;
                if (_verbose || depth == 0)
                    Console.WriteLine($"{Indent(depth)}[rpf] {bin.Name}: {Mb(raw.LongLength)} — пересборка не выиграла ({Delta(raw.LongLength, rebuilt.LongLength)}), оставляю оригинал");
            }
        }
        catch (Exception ex)
        {
            _nestedFailed++;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{Indent(depth)}[rpf] {bin.Name}: не пересобран ({ex.Message}) — копирую как есть");
            Console.ResetColor();
            outBytes = raw;
        }

        var nf = dst.CreateBinaryFile();
        nf.Name = bin.Name;
        using var ms = new MemoryStream(outBytes);
        nf.Import(ms);
        nf.IsCompressed = false;
        nf.IsEncrypted = false;
        nf.UncompressedSize = outBytes.LongLength;
    }

    private static void CopyBinary(IArchiveBinaryFile bin, IArchiveDirectory dst)
    {
        byte[] stored = StoredBytes(bin);
        byte[] real = RealBytes(bin);

        byte[] payload = stored;
        bool compressed = bin.IsCompressed;
        bool encrypted = bin.IsEncrypted;
        long uncompressed = bin.UncompressedSize;

        if (real.LongLength < payload.LongLength)
        {
            payload = real;
            compressed = false;
            encrypted = false;
            uncompressed = real.LongLength;
        }

        if (_compress && real.Length > 64)
        {
            byte[] def = Deflate(real);
            if (def.LongLength < payload.LongLength)
            {
                payload = def;
                compressed = true;
                encrypted = false;
                uncompressed = real.LongLength;
                _compressedCount++;
            }
        }

        var nf = dst.CreateBinaryFile();
        nf.Name = bin.Name;
        using var ms = new MemoryStream(payload);
        nf.Import(ms);
        nf.IsCompressed = compressed;
        nf.IsEncrypted = encrypted;
        nf.UncompressedSize = uncompressed;
    }

    private static byte[] StoredBytes(IArchiveBinaryFile bin)
    {
        using var ms = new MemoryStream();
        bin.Export(ms);
        return ms.ToArray();
    }

    private static byte[] RealBytes(IArchiveBinaryFile bin)
    {
        byte[] buf = StoredBytes(bin);

        if (bin.IsEncrypted)
        {
            uint hash = GTA5Hash.CalculateHash(bin.Name);
            uint idx = (hash + (uint)bin.UncompressedSize + (101 - 40)) % 0x65;
            byte[]? key = GTA5Constants.PC_NG_KEYS is { } ks && ks.Length > idx ? ks[idx] : null;
            if (key is null || key.Length == 0)
                throw new InvalidOperationException($"нет NG-ключа для записи {bin.Name} (idx={idx})");
            buf = GTA5Crypto.Decrypt(buf, key);
        }

        if (bin.IsCompressed)
        {
            using var inf = new DeflateStream(new MemoryStream(buf), CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            inf.CopyTo(outMs);
            return outMs.ToArray();
        }

        return buf;
    }

    private static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var def = new DeflateStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            def.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static bool Verify(string srcPath, string outPath, string logicalName)
    {
        var a = FingerprintArchive(srcPath, logicalName);
        var b = FingerprintArchive(outPath, logicalName);

        var missing = a.Keys.Where(k => !b.ContainsKey(k)).ToList();
        var extra = b.Keys.Where(k => !a.ContainsKey(k)).ToList();
        var changed = a.Keys.Where(k => b.TryGetValue(k, out var h) && h != a[k]).ToList();

        if (missing.Count == 0 && extra.Count == 0 && changed.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  OK — {a.Count} записей совпали побайтово");
            Console.ResetColor();
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  РАСХОЖДЕНИЯ: пропало {missing.Count}, лишних {extra.Count}, изменилось {changed.Count}");
        Console.ResetColor();
        foreach (var k in missing.Take(15)) Console.WriteLine($"    [нет]      {k}");
        foreach (var k in extra.Take(15)) Console.WriteLine($"    [лишний]   {k}");
        foreach (var k in changed.Take(15)) Console.WriteLine($"    [изменён]  {k}");
        return false;
    }

    private static SortedDictionary<string, string> FingerprintArchive(string path, string logicalName)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        RageArchiveWrapper7 arc;
        try { arc = RageArchiveWrapper7.Open(fs, logicalName, leaveOpen: false); }
        catch { fs.Dispose(); throw; }

        using (arc) FingerprintDir(arc.Root, "", map, 0);
        return map;
    }

    private static void FingerprintDir(IArchiveDirectory dir, string prefix,
                                       SortedDictionary<string, string> map, int depth)
    {
        foreach (var d in dir.GetDirectories())
            FingerprintDir(d, prefix + d.Name.ToLowerInvariant() + "/", map, depth);

        foreach (var f in dir.GetFiles())
        {
            string path = prefix + f.Name.ToLowerInvariant();

            if (f is IArchiveBinaryFile bin)
            {
                byte[] real = RealBytes(bin);

                if (bin.Name.EndsWith(".rpf", StringComparison.OrdinalIgnoreCase) && depth < MaxNestDepth)
                {
                    try
                    {
                        using var ms = new MemoryStream(real);
                        using var sub = RageArchiveWrapper7.Open(ms, bin.Name, leaveOpen: true);
                        FingerprintDir(sub.Root, path + ":/", map, depth + 1);
                        continue;
                    }
                    catch {}
                }

                map[path] = Sha(real);
            }
            else if (f is IArchiveResourceFile res)
            {
                using var ms = new MemoryStream();
                res.Export(ms);
                map[path] = Sha(ms.ToArray());
            }
        }
    }

    private static string Sha(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));

    private static bool ArchiveFix(string rpfPath)
    {
        string exe = Path.Combine(_kitDir, "ArchiveFix.exe");
        if (!File.Exists(exe)) { Console.WriteLine("[ArchiveFix] exe не распаковался"); return false; }

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"\"{exe}\" \"{rpfPath}\" < NUL\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _kitDir,
        };

        try
        {
            using var p = Process.Start(psi);
            if (p is null) return false;

            var log = new StringBuilder();
            p.OutputDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (log) log.AppendLine(e.Data); };
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            long size = 0;
            try { size = new FileInfo(rpfPath).Length; } catch { }
            int timeoutMs = (int)Math.Clamp(size / (5L * 1024 * 1024) * 1000, 60_000, 600_000);

            if (!p.WaitForExit(timeoutMs))
            {
                Console.WriteLine($"[ArchiveFix] таймаут {timeoutMs / 1000}s на {Path.GetFileName(rpfPath)}");
                try { p.Kill(entireProcessTree: true); } catch { }
                return false;
            }

            string text;
            lock (log) text = log.ToString();

            if (text.Contains("0 cryptokeys", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Error loading crypto keys", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[ArchiveFix] ключи не найдены рядом с exe ({_kitDir})");
                return false;
            }

            if (p.ExitCode != 0)
            {
                Console.WriteLine($"[ArchiveFix] exit {p.ExitCode}: {text.Trim()}");
                return false;
            }

            if (_verbose) Console.WriteLine($"[ArchiveFix] ok: {Path.GetFileName(rpfPath)}");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ArchiveFix] {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void ExtractKit()
    {
        var asm = Assembly.GetExecutingAssembly();
        _kitDir = Path.Combine(Path.GetTempPath(), $"rpfshrink_kit_{KitStamp}");
        Directory.CreateDirectory(_kitDir);

        foreach (string res in asm.GetManifestResourceNames())
        {
            if (!res.StartsWith("kit.", StringComparison.Ordinal)) continue;
            string target = Path.Combine(_kitDir, res["kit.".Length..]);

            using Stream? rs = asm.GetManifestResourceStream(res);
            if (rs is null) continue;

            if (File.Exists(target) && new FileInfo(target).Length == rs.Length) continue;

            using var fsOut = File.Create(target);
            rs.CopyTo(fsOut);
        }
    }

    private static string NewTempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "rpfshrink_" + Guid.NewGuid().ToString("N")[..12]);
        Directory.CreateDirectory(d);
        _tempDirs.Add(d);
        return d;
    }

    private static string Indent(int depth) => new(' ', depth * 2);

    private static string Mb(long bytes) =>
        bytes >= 1024L * 1024
            ? $"{bytes / 1024.0 / 1024.0:F2} МБ"
            : $"{bytes / 1024.0:F1} КБ";

    private static string Delta(long before, long after)
    {
        if (before <= 0) return "—";
        double pct = (before - after) * 100.0 / before;
        return pct >= 0 ? $"-{pct:F1}%" : $"+{-pct:F1}%";
    }

    private static void Help()
    {
        Console.WriteLine("""
            Пересобирает rpf: убирает пустоты, сжимает записи, рекурсивно
            обрабатывает все вложенные rpf, чинит хеши через ArchiveFix.

            Использование:
              rpfshrink <файл.rpf> [папка-выхода] [ключи]

            Ключи:
              --inplace       заменить исходный файл (оригинал → <имя>.rpf.bak)
              --no-compress   только убрать пустоты, ничего не пережимать
              --no-verify     пропустить побайтовую сверку содержимого (быстрее)
              --as <имя>      открыть NG-архив под другим именем
                              (нужно, если файл переименован: NG-ключ зависит от имени)
              -v              подробный лог
              -h              эта справка

            По умолчанию результат кладётся в <папка-исходника>\rpfshrink_out\
            ПОД ТЕМ ЖЕ ИМЕНЕМ — переименовывать нельзя, NG-шифрование
            привязано к имени файла.

            Примеры:
              rpfshrink "C:\mods\dlc.rpf"
              rpfshrink "C:\mods\dlc.rpf" "D:\out"
              rpfshrink "C:\mods\dlc.rpf" --inplace
            """);
    }
}
