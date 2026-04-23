using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ImageConverterPoc
{
    /// <summary>
    /// Renders PDF pages outside ImageMagick so we always invoke Ghostscript by full path from a known bin directory.
    /// Uses one Ghostscript process per page so large scans show progress and a single bad page cannot block the whole file indefinitely.
    /// </summary>
    internal static class GhostscriptPdfRasterizer
    {
        /// <summary>Hard cap to avoid runaway page loops on damaged PDFs.</summary>
        private const int MaxPdfPages = 500;

        /// <summary>Per-page budget (large single-page engineering drawings can be slow).</summary>
        private const int PerPageTimeoutMs = 180000;

        public static bool TryRasterizePdf(string sourcePdfPath, int dpi, out string workDirectory, out IReadOnlyList<string> pagePngPaths, out string errorMessage)
        {
            workDirectory = null;
            pagePngPaths = null;
            errorMessage = null;

            MagickGhostscriptBootstrap.ConfigureOnce();
            if (!MagickGhostscriptBootstrap.TryGetGhostscriptExecutable(out var gsExe, out var gsBin))
            {
                var detail = MagickGhostscriptBootstrap.LastResolutionFailureHint;
                errorMessage = string.IsNullOrEmpty(detail)
                    ? "Ghostscript is not available. Set IMAGE_CONVERTER_GHOSTSCRIPT to your Ghostscript bin folder or install Ghostscript from https://ghostscript.com/releases/gsdnld.html"
                    : detail;
                return false;
            }

            workDirectory = Path.Combine(Path.GetTempPath(), "imgconv-gs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDirectory);

            var workPdf = Path.Combine(workDirectory, "input.pdf");
            try
            {
                File.Copy(sourcePdfPath, workPdf, overwrite: true);
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                TryDeleteWorkDirectory(workDirectory);
                workDirectory = null;
                return false;
            }

            var workPdfFull = Path.GetFullPath(workPdf);
            var collected = new List<string>(32);

            try
            {
                for (var page = 1; page <= MaxPdfPages; page++)
                {
                    var outPng = Path.Combine(workDirectory, $"pg-{page:D4}.png");
                    if (File.Exists(outPng))
                    {
                        try
                        {
                            File.Delete(outPng);
                        }
                        catch
                        {
                            // ignore
                        }
                    }

                    RunLog.WriteLine($"  [PDF page {page}] {Path.GetFileName(sourcePdfPath)}");
                    RunLog.Detail($"Ghostscript dpi={dpi} page={page}: {sourcePdfPath}");

                    var ran = TryRunGhostscriptSinglePage(gsExe, gsBin, workPdfFull, page, dpi, outPng, out var timedOut, out var gsExit);

                    if (!ran)
                    {
                        if (timedOut)
                        {
                            errorMessage = $"Ghostscript timed out on page {page} of '{Path.GetFileName(sourcePdfPath)}' (limit {PerPageTimeoutMs / 1000}s per page).";
                            TryDeleteWorkDirectory(workDirectory);
                            workDirectory = null;
                            return false;
                        }

                        if (page == 1)
                        {
                            errorMessage =
                                $"Ghostscript failed on first page (exit {gsExit}). The PDF may be corrupt, encrypted, or unsupported.";
                            TryDeleteWorkDirectory(workDirectory);
                            workDirectory = null;
                            return false;
                        }

                        // Later page: treat as end of document (or benign GS quirk).
                        break;
                    }

                    if (!File.Exists(outPng))
                    {
                        if (page == 1)
                        {
                            errorMessage = "Ghostscript reported success but produced no image for page 1.";
                            TryDeleteWorkDirectory(workDirectory);
                            workDirectory = null;
                            return false;
                        }

                        break;
                    }

                    collected.Add(outPng);
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                TryDeleteWorkDirectory(workDirectory);
                workDirectory = null;
                return false;
            }

            if (collected.Count == 0)
            {
                errorMessage = "Ghostscript produced no PNG pages.";
                TryDeleteWorkDirectory(workDirectory);
                workDirectory = null;
                return false;
            }

            pagePngPaths = collected;
            return true;
        }

        private static bool TryRunGhostscriptSinglePage(
            string gsExe,
            string gsBin,
            string workPdfFullPath,
            int pageNumber,
            int dpi,
            string outputPngFullPath,
            out bool timedOut,
            out int exitCode)
        {
            timedOut = false;
            exitCode = -1;

            var pdfArg = QuoteGhostscriptFileArg(ToGhostscriptSlashes(workPdfFullPath));
            var outArg = QuoteGhostscriptFileArg(ToGhostscriptSlashes(Path.GetFullPath(outputPngFullPath)));

            var args =
                "-dQUIET -dSAFER -dBATCH -dNOPAUSE -dNOPROMPT -dMaxBitmap=500000000 -dAlignToPixels=0 -dGridFitTT=2 " +
                "-sDEVICE=pngalpha -dTextAlphaBits=4 -dGraphicsAlphaBits=4 " +
                $"-r{dpi}x{dpi} -dPrinted=false -dUseCropBox " +
                $"-dFirstPage={pageNumber} -dLastPage={pageNumber} " +
                $"-sOutputFile={outArg} -f {pdfArg}";

            try
            {
                var psi = new ProcessStartInfo(gsExe)
                {
                    Arguments = args,
                    WorkingDirectory = gsBin,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false
                };

                using (var p = Process.Start(psi))
                {
                    if (p == null)
                        return false;

                    if (!p.WaitForExit(PerPageTimeoutMs))
                    {
                        timedOut = true;
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

                    exitCode = p.ExitCode;
                    return exitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void TryDeleteWorkDirectory(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return;

            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }

        private static string ToGhostscriptSlashes(string path) => Path.GetFullPath(path).Replace('\\', '/');

        private static string QuoteGhostscriptFileArg(string pathWithForwardSlashes)
        {
            if (pathWithForwardSlashes.IndexOf(' ') >= 0
                || pathWithForwardSlashes.IndexOf('(') >= 0
                || pathWithForwardSlashes.IndexOf(')') >= 0)
                return "\"" + pathWithForwardSlashes.Replace("\"", "\\\"") + "\"";

            return pathWithForwardSlashes;
        }
    }
}
