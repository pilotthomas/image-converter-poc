using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using ImageMagick;

namespace ImageConverterPoc
{
    public sealed class ConversionRunner
    {
        private static bool _printedGhostscriptSetupTip;

        /// <summary>
        /// Call once at process startup before any Magick types are used (including <see cref="TestDataSeeder"/>).
        /// </summary>
        public static void ConfigureMagickEnvironment()
        {
            MagickGhostscriptBootstrap.ConfigureOnce();

            if (_printedGhostscriptSetupTip)
                return;

            if (MagickGhostscriptBootstrap.TryGetGhostscriptExecutable(out _, out _))
                return;

            _printedGhostscriptSetupTip = true;
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            RunLog.BlankLine();
            RunLog.WriteLine("[ImageConverter] PDF conversion needs Ghostscript (it is not part of Magick.NET).");
            RunLog.WriteLine("  Install: https://ghostscript.com/releases/gsdnld.html (64-bit Windows, e.g. under C:\\Program Files\\gs\\...\\bin)");
            RunLog.WriteLine("  Then either set environment variable IMAGE_CONVERTER_GHOSTSCRIPT to that bin folder, or create:");
            RunLog.WriteLine("    " + Path.Combine(exeDir, "GhostscriptBin.txt"));
            RunLog.WriteLine("  with a single line: the full path to the Ghostscript bin folder. Restart the app after changing env or the file.");
            RunLog.BlankLine();
        }

        private static readonly string[] SourceExtensions =
        {
            ".tif", ".tiff", ".pdf", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp"
        };

        public IList<ConversionTask20> LoadTasks(string xmlPath)
        {
            var serializer = new XmlSerializer(typeof(ConversionTask20Document));
            using (var stream = File.OpenRead(xmlPath))
            {
                var doc = (ConversionTask20Document)serializer.Deserialize(stream);
                return doc?.Tasks ?? new List<ConversionTask20>();
            }
        }

        public void RunAll(string xmlPath, bool dryRun)
        {
            RunLog.BeginRun(xmlPath, dryRun);
            if (dryRun)
                RunLog.WriteLine("Dry run: no files will be read, written, archived, or deleted.");

            ConfigureMagickEnvironment();

            var taskFileDirectory = Path.GetDirectoryName(Path.GetFullPath(xmlPath));
            if (string.IsNullOrEmpty(taskFileDirectory))
                taskFileDirectory = AppDomain.CurrentDomain.BaseDirectory;

            var tasks = LoadTasks(xmlPath);
            foreach (var task in tasks)
            {
                TaskPathNormalizer.Apply(task, taskFileDirectory);

                if (!IsRunnableTask(task))
                {
                    if (!string.IsNullOrWhiteSpace(task?.SourcePath) || !string.IsNullOrWhiteSpace(task?.DestinationPath))
                        RunLog.WriteLine("[Skip] Task has no SourcePath or DestinationPath.");
                    continue;
                }

                var format = ImageFormatKindParser.Parse(task.ImgFormat);
                if (format == ImageFormatKind.Unknown)
                {
                    RunLog.WriteLine($"[Skip] Unknown ImgFormat '{task.ImgFormat}' for source '{task.SourcePath}'.");
                    continue;
                }

                if (!Directory.Exists(task.SourcePath))
                {
                    RunLog.WriteLine($"[Skip] Source not reachable: {task.SourcePath}");
                    continue;
                }

                RunTask(task, format, dryRun);
            }
        }

        private static bool IsRunnableTask(ConversionTask20 task)
        {
            return !string.IsNullOrWhiteSpace(task.SourcePath)
                   && !string.IsNullOrWhiteSpace(task.DestinationPath);
        }

        private void RunTask(ConversionTask20 task, ImageFormatKind format, bool dryRun)
        {
            RunLog.WriteLine($"--- Task: {task.SourcePath} -> {task.DestinationPath} ({format}, Decollate={task.Decollate}) ---");

            if (!dryRun)
            {
                Directory.CreateDirectory(task.DestinationPath);
                if (!string.IsNullOrWhiteSpace(task.ArchivePath))
                    Directory.CreateDirectory(task.ArchivePath);
            }

            var files = Directory.EnumerateFiles(task.SourcePath)
                .Where(f => SourceExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .ToList();

            if (files.Count == 0)
            {
                RunLog.WriteLine("  (no matching files)");
                return;
            }

            foreach (var sourceFile in files)
            {
                try
                {
                    ProcessFile(task, format, sourceFile, dryRun);
                }
                catch (Exception ex)
                {
                    var msg = ex.Message;
                    if (IsLikelyGhostscriptPdfFailure(msg)
                        && msg.IndexOf("IMAGE_CONVERTER_GHOSTSCRIPT", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        msg += " Install 64-bit Ghostscript, or set environment variable IMAGE_CONVERTER_GHOSTSCRIPT to its bin folder (containing gswin64c.exe and gsdll64.dll).";
                    }

                    RunLog.WriteLine($"  [Error] {sourceFile}: {msg}");
                    RunLog.Detail(ex.ToString());
                }
            }
        }

        private void ProcessFile(ConversionTask20 task, ImageFormatKind format, string sourceFile, bool dryRun)
        {
            if (dryRun)
            {
                RunLog.WriteLine($"  [DryRun] Would convert: {sourceFile}");
                return;
            }

            RunLog.WriteLine($"  [Start] {Path.GetFileName(sourceFile)}");

            using (var images = new MagickImageCollection())
            {
                string gsWorkDir = null;
                try
                {
                    if (IsPdfPath(sourceFile))
                    {
                        if (GhostscriptPdfRasterizer.TryRasterizePdf(
                                sourceFile,
                                150,
                                out gsWorkDir,
                                out var rasterPages,
                                out var gsError))
                        {
                            foreach (var png in rasterPages)
                                images.Add(new MagickImage(png));
                        }
                        else
                        {
                            throw new InvalidOperationException(gsError ?? "Ghostscript PDF rasterization failed.");
                        }
                    }
                    else
                    {
                        images.Read(sourceFile);
                    }
                }
                finally
                {
                    if (gsWorkDir != null)
                        GhostscriptPdfRasterizer.TryDeleteWorkDirectory(gsWorkDir);
                }

                if (images.Count == 0)
                    return;

                for (var i = 0; i < images.Count; i++)
                    ApplyPageProcessing(images[i], task.AutoRotate, format);

                var paths = BuildOutputPaths(task, format, sourceFile, images.Count);
                var convertedPaths = new List<string>();

                var singleMultiPageOutput = paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1 && images.Count > 1;
                if (singleMultiPageOutput)
                {
                    WriteMultiPageFile(images, paths[0], format);
                    convertedPaths.Add(paths[0]);
                }
                else
                {
                    for (var i = 0; i < images.Count; i++)
                    {
                        WritePage(images[i], paths[i], format);
                        convertedPaths.Add(paths[i]);
                    }
                }

                ArchiveResults(task, sourceFile, convertedPaths);

                if (task.CanDeleteSourceFile)
                    File.Delete(sourceFile);
            }

            RunLog.WriteLine($"  [OK] {Path.GetFileName(sourceFile)}");
        }

        private static bool IsPdfPath(string path) =>
            Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);

        private static bool IsLikelyGhostscriptPdfFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
                return false;

            return message.IndexOf("gswin64c", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("gswin32c", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("ghostscript", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("ExecuteGhostscriptCommand", StringComparison.OrdinalIgnoreCase) >= 0
                   || message.IndexOf("Ghostscript exited", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void ApplyPageProcessing(IMagickImage<ushort> page, bool autoPipeline, ImageFormatKind outputFormat)
        {
            if (!autoPipeline)
                return;

            page.AutoOrient();

            // Despeckle + deskew are for scanned documents. On full-color photos they smear detail and look awful.
            if (!ShouldApplyDocumentCleanup(page, outputFormat))
                return;

            page.Despeckle();

            try
            {
                page.Deskew(new Percentage(40));
            }
            catch
            {
                // Deskew can fail on non-document images; continue without failing the job.
            }
        }

        /// <summary>
        /// Heuristic: only run speckle removal / deskew when the page already looks like a monochrome document.
        /// </summary>
        private static bool ShouldApplyDocumentCleanup(IMagickImage<ushort> page, ImageFormatKind outputFormat)
        {
            if (outputFormat != ImageFormatKind.TiffGroup4
                && outputFormat != ImageFormatKind.TiffLzw
                && outputFormat != ImageFormatKind.PdfMinimal)
                return false;

            // Despeckle + deskew scale poorly on very large rasters (e.g. wide PDF pages at 150 dpi); can look "frozen" for minutes.
            const long maxPixelsForDocumentCleanup = 25_000_000L;
            if ((long)page.Width * page.Height > maxPixelsForDocumentCleanup)
                return false;

            switch (page.ColorType)
            {
                case ColorType.Bilevel:
                case ColorType.Grayscale:
                case ColorType.Palette:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Group 4 TIFF is 1-bit. Forcing bilevel without dithering destroys continuous-tone (photo) images.
        /// </summary>
        private static void PrepareForTiffGroup4(IMagickImage<ushort> page)
        {
            page.Alpha(AlphaOption.Remove);

            if (page.ColorType != ColorType.Bilevel)
                page.ColorSpace = ColorSpace.Gray;

            try
            {
                var quantize = new QuantizeSettings
                {
                    Colors = 2,
                    DitherMethod = DitherMethod.FloydSteinberg
                };
                page.Quantize(quantize);
            }
            catch
            {
                page.ColorType = ColorType.Bilevel;
                page.Depth = 1;
            }

            page.ColorType = ColorType.Bilevel;
            page.Depth = 1;
            page.Settings.Compression = CompressionMethod.Group4;
        }

        /// <summary>
        /// Lossless LZW TIFF (production FF_TIFLZW): keeps color/grayscale; flattens alpha to white for broad TIFF compatibility.
        /// </summary>
        private static void PrepareForTiffLzw(IMagickImage<ushort> page)
        {
            if (page.HasAlpha)
            {
                page.BackgroundColor = MagickColors.White;
                page.Alpha(AlphaOption.Remove);
            }

            page.Settings.Compression = CompressionMethod.LZW;
        }

        private static List<string> BuildOutputPaths(ConversionTask20 task, ImageFormatKind format, string sourceFile, int pageCount)
        {
            var baseName = Path.GetFileNameWithoutExtension(sourceFile);
            var ext = format == ImageFormatKind.PdfMinimal ? ".pdf" : ".tif";
            var uniqueInfix = task.CreateUniqueName ? $"_{Guid.NewGuid():N}" : string.Empty;

            if (task.Decollate)
            {
                var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var folder = Path.Combine(task.DestinationPath, stamp);
                Directory.CreateDirectory(folder);

                var paths = new List<string>(pageCount);
                for (var p = 0; p < pageCount; p++)
                {
                    var name = $"{baseName}{uniqueInfix}_p{p + 1:D4}{ext}";
                    paths.Add(Path.Combine(folder, name));
                }

                return paths;
            }

            if (pageCount == 1)
            {
                var single = BuildSingleDestinationPath(task, baseName, uniqueInfix, ext);
                return new List<string> { single };
            }

            var combined = BuildSingleDestinationPath(task, baseName, uniqueInfix, ext);
            return Enumerable.Repeat(combined, pageCount).ToList();
        }

        private static string BuildSingleDestinationPath(ConversionTask20 task, string baseName, string uniqueInfix, string ext)
        {
            var name = $"{baseName}{uniqueInfix}{ext}";
            return Path.Combine(task.DestinationPath, name);
        }

        private static void WritePage(IMagickImage<ushort> page, string path, ImageFormatKind format)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            if (format == ImageFormatKind.TiffGroup4)
            {
                PrepareForTiffGroup4(page);
                page.Write(path, MagickFormat.Tiff);
            }
            else if (format == ImageFormatKind.TiffLzw)
            {
                PrepareForTiffLzw(page);
                page.Write(path, MagickFormat.Tiff);
            }
            else
            {
                page.Settings.Compression = CompressionMethod.JPEG;
                page.Quality = 88;
                page.Write(path, MagickFormat.Pdf);
            }
        }

        private static void WriteMultiPageFile(MagickImageCollection images, string path, ImageFormatKind format)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            if (format == ImageFormatKind.TiffGroup4)
            {
                foreach (IMagickImage<ushort> page in images)
                    PrepareForTiffGroup4(page);

                images.Write(path, MagickFormat.Tiff);
            }
            else if (format == ImageFormatKind.TiffLzw)
            {
                foreach (IMagickImage<ushort> page in images)
                    PrepareForTiffLzw(page);

                images.Write(path, MagickFormat.Tiff);
            }
            else
            {
                foreach (IMagickImage<ushort> page in images)
                {
                    page.Settings.Compression = CompressionMethod.JPEG;
                    page.Quality = 88;
                }

                images.Write(path, MagickFormat.Pdf);
            }
        }

        private static void ArchiveResults(ConversionTask20 task, string sourceFile, IReadOnlyList<string> convertedPaths)
        {
            if (string.IsNullOrWhiteSpace(task.ArchivePath) || convertedPaths == null || convertedPaths.Count == 0)
                return;

            var archiveName = Path.GetFileName(sourceFile);
            if (string.IsNullOrEmpty(archiveName))
                return;

            var destOriginal = Path.Combine(
                task.ArchivePath,
                $"{Path.GetFileNameWithoutExtension(archiveName)}_src{Path.GetExtension(archiveName)}");
            File.Copy(sourceFile, destOriginal, overwrite: true);

            foreach (var convertedPath in convertedPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var destConverted = Path.Combine(task.ArchivePath, Path.GetFileName(convertedPath));
                File.Copy(convertedPath, destConverted, overwrite: true);
            }
        }
    }
}
