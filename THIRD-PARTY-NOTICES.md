# Third-party software

Sweetch uses MIT-licensed code (copyright notices are preserved in [LICENSE](LICENSE)) and the following software. Each component keeps its own license.

| Component | Used for | License |
|---|---|---|
| [Magick.NET](https://github.com/dlemstra/Magick.NET) / ImageMagick | Images | Apache-2.0 / ImageMagick License |
| [PDFtoImage](https://github.com/sungaila/PDFtoImage) / PDFium / SkiaSharp | PDF → images | MIT / BSD-3-Clause / MIT |
| [PdfPig](https://github.com/UglyToad/PdfPig) | PDF text | Apache-2.0 |
| [PDFsharp](https://github.com/empira/PDFsharp) 6.2 | Merge, split, rotate PDF | MIT |
| [ClosedXML](https://github.com/ClosedXML/ClosedXML) | XLSX | MIT |
| [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) | XLS | MIT |
| [SharpCompress](https://github.com/adamhathcock/sharpcompress) | RAR, 7z, TAR… | MIT |
| [LibVLC 3.0](https://www.videolan.org/vlc/libvlc.html) (included in `libvlc/`, playback plugins only) / [LibVLCSharp](https://code.videolan.org/videolan/LibVLCSharp) | Player | LGPL-2.1-or-later – source: https://code.videolan.org/videolan/vlc/-/tree/3.0.x, https://code.videolan.org/videolan/LibVLCSharp |
| [FFmpeg](https://ffmpeg.org) (Windows: downloaded on demand, “essentials” build by [gyan.dev](https://www.gyan.dev/ffmpeg/builds/)) | Audio and video | GPL-3.0 – source: https://github.com/FFmpeg/FFmpeg |
| [Pandoc 3.12.1](https://pandoc.org) (included as a separate executable in `tools/`) | Documents | GPL-2.0-or-later – source: https://github.com/jgm/pandoc/tree/3.12.1 |
| [LibreOffice](https://www.libreoffice.org) (optional, installed separately) | Office files → PDF | MPL-2.0 |

Pandoc is bundled from the unmodified official Windows distribution. Its `COPYING.rtf`, `COPYRIGHT.txt` and `MANUAL.html` are included in `tools/`. Source release: https://hackage.haskell.org/package/pandoc-3.12.1. The Windows ZIP is verified against SHA-256 `6ef431cb20b2c24a0371fc19200dc49d452be54b1b5c9239f3462841ef53e587` before packaging.

LibVLC is bundled unmodified from the official `VideoLAN.LibVLC.Windows` package as separate DLLs that Sweetch loads at run time, so they can be replaced with another LibVLC 3.0 build. Only the plugins needed to play local files are included.

FFmpeg is downloaded on demand into `%LOCALAPPDATA%\Sweetch\tools`, checked against its published SHA-256. LibreOffice is installed separately. These components run as separate programs; Sweetch invokes them through their command-line interfaces.
