using System;
using System.IO;

namespace MiamiGraphics.Core.System
{

    public static class WorkDirDefaults
    {

        public static string ResultBaseDir { get; } = ResolveResultBaseDir();

        private static string ResolveResultBaseDir()
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "MiamiGraphics", "workdir");
            try { Directory.CreateDirectory(dir); }
            catch {  }
            return dir;
        }
    }
}
