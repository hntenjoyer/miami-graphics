using System;
using System.IO;

namespace MiamiGraphics.Core.Injector
{
    public static class UpdateRpfLimits
    {
        public const long MeasuredOkBytes = 2250L * 1024 * 1024;

        public const long WarnBytes = 3500L * 1024 * 1024;

        public const long MeasuredDeadBytes = 5130L * 1024 * 1024;

        public static bool IsUpdateRpf(string path)
            => Path.GetFileName(path).Equals("update.rpf", StringComparison.OrdinalIgnoreCase);

        public static string Describe(string updateRpfPath)
        {
            long size;
            try
            {
                var fi = new FileInfo(updateRpfPath);
                if (!fi.Exists) return "";
                size = fi.Length;
            }
            catch { return ""; }

            string head = $"update.rpf: {size / (1024 * 1024):N0} МБ";
            if (size >= MeasuredDeadBytes)
                return head + " - ВЫШЕ ЗАМЕРЕННОГО ПОТОЛКА (5,13 ГБ): игра зависает на «Запуск игры»";
            if (size >= WarnBytes)
                return head + $" - выше порога предупреждения ({WarnBytes / (1024 * 1024):N0} МБ); " +
                       "проверено рабочим 2,25 ГБ, зависанием - 5,13 ГБ";
            return head;
        }
    }
}
