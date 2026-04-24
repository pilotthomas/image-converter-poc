using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ImageMagick;
using Microsoft.Win32;

namespace ImageConverterPoc
{
    /// <summary>
    /// Must run before any <see cref="MagickImage"/> / <see cref="MagickImageCollection"/> use.
    /// ImageMagick resolves the PDF delegate on first init; if that happens during test seeding, a later
    /// <see cref="MagickNET.SetGhostscriptDirectory"/> has no effect. Also enforces a working GS install (exe + DLL) and PATH.
    /// </summary>
    internal static class MagickGhostscriptBootstrap
    {
        private static readonly object Gate = new object();
        private static bool _configured;
        private static bool _magickInitialized;
        private static string _resolvedGsExe;
        private static string _resolvedGsBin;

        /// <summary>
        /// Populated when <see cref="ConfigureOnce"/> finishes without resolving Ghostscript (for clearer PDF errors).
        /// </summary>
        internal static string LastResolutionFailureHint { get; private set; }

        /// <summary>Returns a verified Ghostscript CLI path after <see cref="ConfigureOnce"/>.</summary>
        internal static bool TryGetGhostscriptExecutable(out string exePath, out string binDirectory)
        {
            ConfigureOnce();
            exePath = _resolvedGsExe;
            binDirectory = _resolvedGsBin;
            return !string.IsNullOrEmpty(exePath) && File.Exists(exePath);
        }

        public static void ConfigureOnce()
        {
            if (_configured)
                return;

            lock (Gate)
            {
                if (_configured)
                    return;

                string applied = null;

                foreach (var label in new[] { "IMAGE_CONVERTER_GHOSTSCRIPT", "MAGICK_GHOSTSCRIPT_PATH" })
                {
                    var dir = NormalizeDirectoryHint(Environment.GetEnvironmentVariable(label));
                    if (dir != null && TryApplyBinDirectory(dir, label, ref applied))
                        break;
                }

                if (applied == null)
                    TryApplyFromOptionalConfigFile(ref applied);

                if (applied == null)
                {
                    foreach (var bin in CollectStandardGhostscriptBins())
                    {
                        if (TryApplyBinDirectory(bin, "auto-detect", ref applied))
                            break;
                    }
                }

                if (applied != null)
                {
                    LastResolutionFailureHint = null;
                    EnsureMagickInitializedOnce();
                }
                else
                {
                    LastResolutionFailureHint =
                        "Magick.NET does not include Ghostscript; PDF pages are rasterized by calling Ghostscript separately. "
                        + "This process did not find a working Ghostscript under IMAGE_CONVERTER_GHOSTSCRIPT / MAGICK_GHOSTSCRIPT_PATH, "
                        + "GhostscriptBin.txt next to this program, Program Files\\gs\\*, registry (GPL Ghostscript), PATH, or where.exe. "
                        + "Install the current 64-bit Windows build from https://ghostscript.com/releases/gsdnld.html "
                        + "(default location is something like C:\\Program Files\\gs\\gs10.xx.x\\bin), "
                        + "then set environment variable IMAGE_CONVERTER_GHOSTSCRIPT to that bin folder, "
                        + "or create GhostscriptBin.txt in the same folder as this exe with that path on a single line, and restart.";
                }

                _configured = true;
            }
        }

        private static void EnsureMagickInitializedOnce()
        {
            if (_magickInitialized)
                return;

            try
            {
                MagickNET.Initialize();
            }
            catch (Exception ex)
            {
                RunLog.Detail($"MagickNET.Initialize: {ex.Message}");
            }

            _magickInitialized = true;
        }

        private static bool TryApplyFromOptionalConfigFile(ref string applied)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var fileName in new[] { "GhostscriptBin.txt", "IMAGE_CONVERTER_GHOSTSCRIPT.txt" })
            {
                var fullPath = Path.Combine(baseDir, fileName);
                if (!File.Exists(fullPath))
                    continue;

                string firstLine;
                try
                {
                    firstLine = File.ReadAllLines(fullPath)
                        .Select(l => l.Trim())
                        .FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#", StringComparison.Ordinal) && !l.StartsWith(";", StringComparison.Ordinal));
                }
                catch (Exception ex)
                {
                    RunLog.Detail($"Could not read {fileName}: {ex.Message}");
                    continue;
                }

                if (string.IsNullOrEmpty(firstLine))
                    continue;

                var dir = NormalizeDirectoryHint(firstLine);
                if (dir != null && TryApplyBinDirectory(dir, fileName, ref applied))
                    return true;
            }

            return false;
        }

        private static string NormalizeDirectoryHint(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim().Trim('"');
            if (string.Equals(Path.GetFileName(raw), "gswin64c.exe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileName(raw), "gswin32c.exe", StringComparison.OrdinalIgnoreCase))
                raw = Path.GetDirectoryName(raw) ?? raw;

            return raw;
        }

        private static bool TryApplyBinDirectory(string binDirectory, string sourceLabel, ref string applied)
        {
            if (string.IsNullOrWhiteSpace(binDirectory) || !Directory.Exists(binDirectory))
                return false;

            binDirectory = Path.GetFullPath(binDirectory);
            if (!HasGhostscriptCli(binDirectory))
            {
                RunLog.Detail($"No gswin64c.exe / gswin32c.exe in: {binDirectory}");
                return false;
            }

            if (!SmokeTestGhostscript(binDirectory))
            {
                RunLog.Detail($"Ghostscript smoke test failed: {binDirectory}");
                return false;
            }

            try
            {
                MagickNET.SetGhostscriptDirectory(binDirectory);
            }
            catch (Exception ex)
            {
                RunLog.Detail($"SetGhostscriptDirectory failed: {ex.Message}");
                return false;
            }

            Environment.SetEnvironmentVariable("MAGICK_GHOSTSCRIPT_PATH", binDirectory, EnvironmentVariableTarget.Process);
            PrependProcessPath(binDirectory);

            _resolvedGsBin = binDirectory;
            _resolvedGsExe = File.Exists(Path.Combine(binDirectory, "gswin64c.exe"))
                ? Path.Combine(binDirectory, "gswin64c.exe")
                : Path.Combine(binDirectory, "gswin32c.exe");

            applied = binDirectory;
            RunLog.Detail($"Ghostscript configured ({sourceLabel}): {binDirectory}");
            return true;
        }

        private static bool HasGhostscriptCli(string bin)
        {
            return File.Exists(Path.Combine(bin, "gswin64c.exe"))
                   || File.Exists(Path.Combine(bin, "gswin32c.exe"));
        }

        private static bool SmokeTestGhostscript(string binDirectory)
        {
            var exe = File.Exists(Path.Combine(binDirectory, "gswin64c.exe"))
                ? Path.Combine(binDirectory, "gswin64c.exe")
                : Path.Combine(binDirectory, "gswin32c.exe");

            // Prefer nullpage; some installs reject it — fall back to pngalpha → NUL (proves gsdll load + device init).
            return RunGhostscriptSmoke(exe, binDirectory, "-dBATCH -dNOPAUSE -dQUIET -sDEVICE=nullpage -c quit")
                   || RunGhostscriptSmoke(exe, binDirectory, "-dBATCH -dNOPAUSE -dQUIET -sDEVICE=pngalpha -sOutputFile=NUL -c quit");
        }

        private static bool RunGhostscriptSmoke(string exe, string workingDirectory, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(exe)
                {
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workingDirectory
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return false;
                    if (!p.WaitForExit(8000))
                    {
                        try
                        {
                            p.Kill();
                        }
                        catch
                        {
                            // ignore
                        }

                        return false;
                    }

                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                RunLog.Detail($"Ghostscript smoke test exception: {ex.Message}");
                return false;
            }
        }

        private static void PrependProcessPath(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            var normalized = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process)
                          ?? Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
                          ?? string.Empty;

            var parts = current.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Any(p => string.Equals(Path.GetFullPath(p), normalized, StringComparison.OrdinalIgnoreCase)))
                return;

            Environment.SetEnvironmentVariable(
                "PATH",
                normalized + Path.PathSeparator + current,
                EnvironmentVariableTarget.Process);
        }

        private static IReadOnlyList<string> CollectStandardGhostscriptBins()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Array.Empty<string>();

            var bestVersionByBin = new Dictionary<string, Version>(StringComparer.OrdinalIgnoreCase);

            AddRegistryBins(bestVersionByBin);
            AddProgramFilesBins(bestVersionByBin);
            AddPathExecutableBins(bestVersionByBin);
            AddWhereExecutableBins(bestVersionByBin);

            return bestVersionByBin
                .OrderByDescending(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();
        }

        private static void UpsertBin(Dictionary<string, Version> sink, string binDirectory, Version version)
        {
            if (string.IsNullOrEmpty(binDirectory))
                return;

            binDirectory = Path.GetFullPath(binDirectory);
            if (!Directory.Exists(binDirectory))
                return;

            if (sink.TryGetValue(binDirectory, out var existing))
            {
                if (version > existing)
                    sink[binDirectory] = version;
            }
            else
            {
                sink[binDirectory] = version;
            }
        }

        private static void AddRegistryBins(Dictionary<string, Version> sink)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
                {
                    using (var root = RegistryKey.OpenBaseKey(hive, view))
                    {
                        AddBinsFromGplRegistry(root, @"SOFTWARE\GPL Ghostscript", sink);
                        AddBinsFromGplRegistry(root, @"SOFTWARE\Artifex\GPL Ghostscript", sink);
                    }
                }
            }
        }

        private static void AddBinsFromGplRegistry(RegistryKey root, string relativePath, Dictionary<string, Version> sink)
        {
            using (var gpl = root.OpenSubKey(relativePath))
            {
                if (gpl == null)
                    return;

                foreach (var versionName in gpl.GetSubKeyNames())
                {
                    using (var verKey = gpl.OpenSubKey(versionName))
                    {
                        if (verKey == null)
                            continue;

                        string binDir = null;
                        var dllPath = verKey.GetValue("GS_DLL") as string;
                        if (!string.IsNullOrWhiteSpace(dllPath))
                            binDir = Path.GetDirectoryName(dllPath);
                        else
                        {
                            var installDir = verKey.GetValue("Install_Dir") as string
                                             ?? verKey.GetValue("InstallDir") as string;
                            if (!string.IsNullOrWhiteSpace(installDir))
                            {
                                installDir = installDir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                binDir = Path.Combine(installDir, "bin");
                            }
                        }

                        if (string.IsNullOrEmpty(binDir))
                            continue;

                        var v = Version.TryParse(versionName, out var parsed) ? parsed : new Version(0, 0);
                        UpsertBin(sink, binDir, v);
                    }
                }
            }
        }

        private static void AddPathExecutableBins(Dictionary<string, Version> sink)
        {
            foreach (var scope in new[]
                     {
                         EnvironmentVariableTarget.Process,
                         EnvironmentVariableTarget.User,
                         EnvironmentVariableTarget.Machine
                     })
            {
                string path;
                try
                {
                    path = Environment.GetEnvironmentVariable("PATH", scope);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrEmpty(path))
                    continue;

                foreach (var segment in path.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var dir = segment.Trim().Trim('"');
                    if (File.Exists(Path.Combine(dir, "gswin64c.exe"))
                        || File.Exists(Path.Combine(dir, "gswin32c.exe")))
                        UpsertBin(sink, dir, new Version(0, 0));
                }
            }
        }

        private static void AddWhereExecutableBins(Dictionary<string, Version> sink)
        {
            var whereExe = Path.Combine(Environment.SystemDirectory, "where.exe");
            if (!File.Exists(whereExe))
                return;

            foreach (var name in new[] { "gswin64c.exe", "gswin32c.exe" })
            {
                try
                {
                    var psi = new ProcessStartInfo(whereExe, name)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = Environment.SystemDirectory
                    };

                    using (var p = Process.Start(psi))
                    {
                        if (p == null)
                            continue;

                        var stdout = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(4000);
                        if (p.ExitCode != 0)
                            continue;

                        foreach (var line in stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var full = line.Trim().Trim('"');
                            if (full.Length == 0 || !full.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                                continue;

                            var binDir = Path.GetDirectoryName(full);
                            if (!string.IsNullOrEmpty(binDir))
                                UpsertBin(sink, binDir, new Version(0, 0));
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        private static void AddProgramFilesBins(Dictionary<string, Version> sink)
        {
            foreach (var programFiles in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                         Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                     })
            {
                if (string.IsNullOrEmpty(programFiles))
                    continue;

                var gsRoot = Path.Combine(programFiles, "gs");
                if (!Directory.Exists(gsRoot))
                    continue;

                foreach (var editionDir in Directory.EnumerateDirectories(gsRoot))
                {
                    var bin = Path.Combine(editionDir, "bin");
                    var folderName = Path.GetFileName(editionDir);
                    var v = ParseGhostscriptFolderVersion(folderName);
                    UpsertBin(sink, bin, v);
                }
            }
        }

        private static Version ParseGhostscriptFolderVersion(string folderName)
        {
            if (string.IsNullOrEmpty(folderName) || !folderName.StartsWith("gs", StringComparison.OrdinalIgnoreCase))
                return new Version(0, 0);

            var tail = folderName.Substring(2);
            return Version.TryParse(tail, out var parsed) ? parsed : new Version(0, 0);
        }
    }
}
