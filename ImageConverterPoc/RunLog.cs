using System;
using System.IO;
using System.Text;

namespace ImageConverterPoc
{
    /// <summary>
    /// Mirrors conversion output to the console and appends to a UTF-8 log under <c>logs\conversion-yyyyMMdd.log</c> next to the host exe.
    /// </summary>
    internal static class RunLog
    {
        private static readonly object Gate = new object();
        private static string _logFilePath;

        internal static string LogFilePath
        {
            get
            {
                lock (Gate)
                    EnsureLogFilePathLocked();
                return _logFilePath;
            }
        }

        /// <summary>Start-of-run marker in the log file (and one console line with the log path).</summary>
        public static void BeginRun(string xmlPath, bool dryRun)
        {
            var fullXml = Path.GetFullPath(xmlPath);
            string path;
            lock (Gate)
            {
                EnsureLogFilePathLocked();
                path = _logFilePath;
                var banner =
                    $"{Environment.NewLine}{new string('=', 72)}{Environment.NewLine}" +
                    $"{Timestamp()}\t--- New conversion run ({(dryRun ? "dry-run" : "live")}) ---{Environment.NewLine}" +
                    $"{Timestamp()}\tTask file: {fullXml}{Environment.NewLine}" +
                    $"{new string('=', 72)}{Environment.NewLine}";
                File.AppendAllText(path, banner, Encoding.UTF8);
            }

            Console.WriteLine($"[ImageConverter] Conversion log: {path}");
        }

        public static void WriteLine(string message)
        {
            Console.WriteLine(message);
            AppendLineToFile(message);
        }

        public static void BlankLine()
        {
            Console.WriteLine();
            AppendRaw(Environment.NewLine);
        }

        /// <summary>File only (stack traces, bootstrap diagnostics).</summary>
        public static void Detail(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var prefixed = "[detail]\t" + message.Replace(Environment.NewLine, Environment.NewLine + "[detail]\t");
            AppendLineToFile(prefixed);
        }

        private static string Timestamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");

        private static void AppendLineToFile(string message)
        {
            AppendRaw($"{Timestamp()}\t{message}{Environment.NewLine}");
        }

        private static void AppendRaw(string text)
        {
            try
            {
                lock (Gate)
                {
                    EnsureLogFilePathLocked();
                    File.AppendAllText(_logFilePath, text, Encoding.UTF8);
                }
            }
            catch
            {
                // logging must never break conversion
            }
        }

        private static void EnsureLogFilePathLocked()
        {
            if (_logFilePath != null)
                return;

            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(dir);
            _logFilePath = Path.Combine(dir, $"conversion-{DateTime.Now:yyyyMMdd}.log");
        }
    }
}
