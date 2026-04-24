# image-converter-poc
Proof-of-concept .NET 4.7.2 image batch converter using Magick.NET and Ghostscript, driven by ConversionTask20.Test.xml (Nuance-style tasks → TIFF G4/LZW and minimal PDF).
# Image Converter POC — Engineering Observations

**Document type:** Technical observations (proof of concept)  
**Last reviewed:** April 2026  

---

## 1. Executive summary

This POC replaces **Nuance IproPlus (COM)**–based image conversion with **ImageMagick via Magick.NET** (`Magick.NET-Q16-AnyCPU`), driven by the same style of task list as production: **`ConversionTask20.xml`** (`ArrayOfConversionTask20` / `ConversionTask20` elements).

**What aligns well with production usage**

- Task shape matches production XML: source folder, destination folder, optional archive, flags, and `ImgFormat` strings such as `FF_TIFG4`, `FF_TIFLZW`, `FF_PDF_MIN`.
- Production `ConversionTask20.xml` (sample bundled at repo root) uses **only those three** `ImgFormat` values on **live** tasks; the POC parser recognizes all three.
- Folder-based processing, optional decollate (per-page outputs into a timestamped subfolder), optional unique filenames, optional delete-after-success, and optional archive copy behavior are implemented in the runner.

**What does not align with the full legacy Nuance surface**

- Legacy `ConversionFormat` / `IMF_FORMAT` exposed **many** formats (PDF MRC, JBIG2, JPEG2000, PackBits, etc.). The POC implements **three** logical outputs only (Group 4 TIFF, LZW TIFF, and a minimal PDF path).
- Output bytes and PDF/TIFF internals will **not** match Nuance bit-for-bit; see **Format fidelity** (Section 8).
- **Ghostscript** is required for robust PDF read/write in many environments; it is **not** bundled inside Magick.NET (the POC documents env var / `GhostscriptBin.txt`).

**Verdict:** The POC is a **credible replacement path for the subset of formats and behaviors actually used** in the analyzed production task file. It is **not** a drop-in reimplementation of the entire Nuance format matrix.

**Benchmark note (Apr 2026):** A controlled comparison using `Nunance-Test-Log.txt`, local `Converted\` outputs, `conversion-20260417.log`, and POC `TestData\*\Out` is documented in **Section 14 — Nuance vs Magick POC benchmark**.

---

## 2. Purpose and scope

| In scope | Out of scope (for this POC) |
|----------|-----------------------------|
| Batch conversion from **directories** listed in XML | Windows Service hosting, scheduling, or multi-instance orchestration |
| Read: TIFF, PDF, PNG, JPEG, BMP, GIF, WebP (extension filter) | Nuance COM licensing, `Engine.Init`, or IproPlus APIs |
| Write: Group 4 TIFF, LZW TIFF, PDF (JPEG-compressed raster path) | Full parity with every `IMF_FORMAT` / MRC / JBIG2 mode |
| Optional auto-orient, despeckle, deskew (document heuristic) | OCR, forms, or proprietary Nuance document workflows |
| Decollate, archive copies, delete source | Centralized logging to NLog/Splunk (console / `RunLog` style only) |

---

## 3. Architecture (logical)

```text
┌─────────────────────────┐
│ ConversionTask20.xml    │
│ (tasks + ImgFormat)     │
└───────────┬─────────────┘
            │ XmlSerializer → List<ConversionTask20>
            ▼
┌─────────────────────────┐
│ TaskPathNormalizer      │  Relative paths resolved vs XML file directory
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ ConversionRunner        │
│  • Validate task        │
│  • Parse ImgFormat      │
│  • Enumerate inputs     │
│  • MagickImageCollection│
│  • Per-page processing  │
│  • Write + Archive      │
└───────────┬─────────────┘
            ▼
┌─────────────────────────┐
│ ImageMagick / Ghostscript│  Q16 AnyCPU Magick.NET; GS for PDF stack
└─────────────────────────┘
```

**Entry points**

| Host | Role |
|------|------|
| `ImageConverterPoc` (exe) | CLI: `--dry-run`, `--test`, `--seed-test-assets`, optional path to XML |
| `ImageConverterTestConsole` (exe) | Interactive menu: seed test data, run test XML, dry-run bundled production XML, custom path |

---

## 4. Technology stack

| Component | Version / notes |
|-----------|-------------------|
| Target framework | **.NET Framework 4.7.2** (`net472`) |
| Image library | **Magick.NET-Q16-AnyCPU** (e.g. 14.12.0 in csproj) |
| PDF stack | **Ghostscript** expected on machine; bootstrap via `MagickGhostscriptBootstrap` + `ConversionRunner.ConfigureMagickEnvironment()` |
| Task format | XML: `ArrayOfConversionTask20` root, repeated `ConversionTask20` children |

---

## 5. Task model (`ConversionTask20`)

**Deserialized properties (POC)**

| Property | Used by runner | Notes |
|----------|----------------|--------|
| `SourcePath` | Yes | Must exist as a **directory**; files picked by extension |
| `DestinationPath` | Yes | Created if missing (non–dry-run) |
| `ArchivePath` | Yes | Optional; if set, originals + converted copies duplicated under archive |
| `ImgFormat` | Yes | Parsed by `ImageFormatKindParser` |
| `CanDeleteSourceFile` | Yes | Deletes source after successful conversion when true |
| `CreateUniqueName` | Yes | Appends GUID to output basename when true |
| `Decollate` | Yes | When true, one file per page under a new timestamped subfolder |
| `AutoRotate` | Yes | When true, enables auto-orient + optional deskew/despeckle pipeline |
| `Error` | **No** | Present in some production XML (`<Error/>`); deserialized but **never read** |

**Important:** `XmlSerializer` ignores XML comments. Large portions of production files may be **commented-out tasks**; those do not load as tasks.

---

## 6. Supported `ImgFormat` values (POC vs legacy enum)

Legacy production code mapped a wide **`ConversionFormat`** enum to **`IMF_FORMAT`** (BMP, DCX, GIF, JBIG2, JPEG, JPEG2000, many PDF variants including MRC, many TIFF variants, PNG, etc.).

**This POC only maps:**

| `ImgFormat` (XML) | Internal enum | Output behavior (summary) |
|-------------------|---------------|---------------------------|
| `FF_TIFG4` | `TiffGroup4` | Bilevel + Group 4 compression (with quantization path) |
| `FF_TIFLZW` | `TiffLzw` | LZW TIFF; alpha removed against white background |
| `FF_PDF_MIN` | `PdfMinimal` | PDF written with JPEG-style compression (quality fixed in code) |

**Any other `ImgFormat` string** → `Unknown` → task skipped with a log line.

**Observation on production `ConversionTask20.xml` (repo sample):** Active tasks use **only** `FF_TIFG4`, `FF_TIFLZW`, and `FF_PDF_MIN`. Other enum values appear only in **comments** (e.g. historical `FF_TIFJPGNEW`, `FF_TIFPB`) or documentation comments inside tasks—not as live configuration.

---

## 7. Input discovery and filtering

- Inputs are files under `SourcePath` whose extension is in a fixed allowlist (TIFF, PDF, common raster formats).
- There is **no** recursive subdirectory scan in the described runner flow (top-level enumeration only—confirm in `ConversionRunner` if behavior changes).
- Tasks whose `SourcePath` is not reachable (`Directory.Exists` false) are **skipped** (typical when running on a workstation against production UNC paths).

---

## 8. Format fidelity vs Nuance (detailed observation)

**Definition:** Fidelity means matching not only file extension but **color depth, compression, TIFF tags/metadata, and PDF construction** (image streams, compression, structure).

| Topic | Nuance (legacy) | This POC |
|-------|-----------------|----------|
| TIFF G4 | Engine-defined bilevel / G4 pipeline | Explicit quantization to bilevel + `Group4` compression in Magick |
| TIFF LZW | Engine-defined LZW TIFF | LZW + explicit alpha flattening to white |
| PDF “MIN” / related | Nuance `FF_PDF_*` family (multiple quality/MRC modes) | Single “minimal PDF” path: raster PDF with **JPEG compression** at a **fixed quality** (not equivalent to Nuance `FF_PDF_LOSSLESS` or MRC variants) |
| Pixel-perfect output | Proprietary | **Will differ** from Nuance outputs; validation should use **samples + acceptance criteria**, not byte comparison |
| COM-specific quirks | e.g. tolerating certain TIFF tag errors while still emitting a file | Magick may fail or behave differently on malformed inputs |

**Recommendation:** For go-live, define **golden-file tests** per `ImgFormat` and document type (scan vs photo), and involve stakeholders who care about **archival/legal** rendering.

---

## 9. Image processing pipeline (behavioral)

When `AutoRotate` is true:

1. **`AutoOrient()`** — EXIF-based orientation (analogous in spirit to “auto rotation” in consumer imaging; not identical to Nuance’s document workflow rotation).
2. **Conditional document cleanup** — `Despeckle()` and `Deskew()` run only when heuristics suggest a **monochrome / document-like** page and output format is TIFF or PDF path (see `ShouldApplyDocumentCleanup`). Color photos are largely protected from aggressive deskew/despeckle.

**Decollate**

- When true, outputs go under `DestinationPath\<yyyyMMdd_HHmmss>\` with per-page filenames.

**Multi-page**

- Multi-page sources collapse to **one output file** when the resolved output paths are identical (multi-page TIFF/PDF); otherwise per-page writes apply.

---

## 10. Operational observations

**UNC paths and dry runs**

- Bundled production XML uses **UNC paths** (e.g. `\\spis110\...`). On machines without access, tasks are skipped as “Source not reachable.” The test console explicitly offers **dry-run against bundled production** to validate parsing without I/O.

**Ghostscript**

- PDF conversion reliability often depends on Ghostscript installation and path discovery (`IMAGE_CONVERTER_GHOSTSCRIPT` or `GhostscriptBin.txt` beside the exe). Operations should treat GS as a **first-class deployment dependency** wherever PDF is in scope.

**Archiving**

- When `ArchivePath` is set, behavior includes copying **source** and **converted** artifacts into the archive folder (see `ArchiveResults` implementation for exact naming).

**Deletion**

- `CanDeleteSourceFile` deletes the **original source file** after success; this matches high-risk production behavior—test with dry-run and backups first.

---

## 13. Appendix — key source files

| File | Purpose |
|------|---------|
| `ImageConverterPoc/ConversionRunner.cs` | Core conversion, Magick env, archive/delete |
| `ImageConverterPoc/ConversionTask20.cs` | XML contract / DTO |
| `ImageConverterPoc/ImageFormatKind.cs` | Parsed formats + parser |
| `ImageConverterPoc/TaskPathNormalizer.cs` | Relative path resolution |
| `ImageConverterPoc/Program.cs` | CLI |
| `ImageConverterTestConsole/Program.cs` | Interactive test harness |
| `ConversionTask20.xml` (repo root) | Production-style task list (linked into build output) |
| `ImageConverterPoc/ConversionTask20.Test.xml` | Local test tasks |

---