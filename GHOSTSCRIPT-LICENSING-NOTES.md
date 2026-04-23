# Ghostscript licensing notes (for this project)

**This document is not legal advice.** It summarizes where licensing is defined and what your team should verify before production use. Only your counsel or Artifex can tell you which license applies to your exact deployment.

## How this codebase uses Ghostscript

- **Magick.NET / ImageMagick** do not bundle Ghostscript.
- PDF input is converted by **running the Ghostscript CLI** (`gswin64c.exe` on 64-bit Windows) from a known `bin` folder (see `GhostscriptPdfRasterizer`, `MagickGhostscriptBootstrap`).
- That means **Ghostscript’s license terms apply** to your production deployment of Ghostscript, independent of Magick.NET’s Apache 2.0 license.

## Official sources (read these first)

| Topic | URL |
|--------|-----|
| Ghostscript → Artifex licensing entry point | https://ghostscript.com/licensing |
| Artifex licensing overview (AGPL vs commercial) | https://www.artifex.com/licensing |
| GNU AGPL v3 (full license text) | https://www.gnu.org/licenses/agpl-3.0.html |

## Two distribution channels (high level)

### 1. AGPL / “community” build (no fee from Artifex)

- What you download from the **“Ghostscript AGPL Release”** column on [Ghostscript downloads](https://ghostscript.com/releases/gsdnld.html) is governed by the **GNU AGPLv3** (as Artifex describes on their site).
- AGPL is **not** “no obligations.” Artifex’s own licensing page states conditions that matter for **server / SaaS** style use, including language about **AGPL-compliant environments** and **source disclosure** when you cannot meet AGPL terms (see [Artifex licensing](https://www.artifex.com/licensing)).
- **If you cannot satisfy AGPL** for your production architecture (e.g. proprietary app, customer-hosted conversion service), Artifex states that **a commercial license is required** (same page).

### 2. Commercial license (paid, from Artifex)

- Use this path when you need **commercial terms**, **support**, and **no AGPL-driven disclosure obligations** on your proprietary code, as described on [Artifex licensing](https://www.artifex.com/licensing).
- Pricing and contract shape are **per use case** (OEM, subscription, internal, etc.); contact Artifex for a quote.

## Practical checklist before “thousands of files / day” production

1. **Decide AGPL vs commercial** with whoever owns legal/compliance at your company (especially if the converter is **customer-facing**, **multi-tenant**, or **ships with closed-source product**).
2. **Document** which Ghostscript build you install (version, AGPL vs commercial installer) and where it runs (VM image, container base layer, etc.).
3. **Track upgrades** — Ghostscript version bumps should stay aligned with your compliance record.
4. **If commercial:** start at [Artifex licensing](https://www.artifex.com/licensing) or their sales/contact flow and keep the agreement on file.

## Magick.NET (separate from Ghostscript)

- Magick.NET is licensed under **Apache License 2.0** (see the [Magick.NET repository](https://github.com/dlemstra/Magick.NET)).
- That does **not** remove Ghostscript’s separate obligations when you use Ghostscript for PDF.

---

*Last updated from public pages as of the project maintainer’s snapshot; always follow the current text on the official URLs above.*
