# Third-party notices / 第三方聲明

SnapBar 的原創程式碼採用根目錄的 MIT `LICENSE`。第三方程式庫、模型與素材仍依各自條款提供，並不因 SnapBar 採 MIT 而變更授權。請在再散布時一併保留本文件與下列授權及聲明。免費散布也適用。

SnapBar includes the following third-party software and assets. Their original licenses, copyrights, disclaimers, and attribution notices remain applicable. Inclusion does not imply endorsement by their authors. This index supplements, and does not replace, the original license texts.

## Icons

- **Microsoft Fluent UI System Icons Regular**, commit `9eb184e96268d3293462f9d9d19225b4c7b07774`. Copyright (c) 2020 Microsoft Corporation. MIT License. The font is unmodified; `catalog.json` removes the upstream `ic_fluent_` prefix. See [license](Assets/FluentIcons/LICENSE.txt), [upstream notices](Assets/FluentIcons/NOTICE.txt), and [source](Assets/FluentIcons/SOURCE.md). In the source checkout these files are under `src/SnapBar.App/Assets/FluentIcons/`.

## OCR engine and language models

- **Tesseract .NET wrapper 5.2.0**, Copyright 2012–2022 Charles Weld (upstream README; NuGet metadata states 2012–2020). Apache License 2.0. [Upstream](https://github.com/charlesw/tesseract). Distributed binaries are unmodified.
- **Tesseract OCR 5.2.0**, Apache License 2.0. [Upstream source and copyright notices](https://github.com/tesseract-ocr/tesseract/tree/5.2.0). The unmodified x64 binary is supplied by the Tesseract 5.2.0 NuGet package.
- **tessdata_fast 4.1.0**, English and Traditional Chinese models (`eng.traineddata`, `chi_tra.traineddata`), Apache License 2.0. [Upstream](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0). Model files are unmodified.
- **InteropDotNet**, Copyright 2014 Andrey Akinshin. MIT License. Included by the wrapper. [Upstream](https://github.com/AndreyAkinshin/InteropDotNet).
- **Leptonica 1.82.0**, Copyright (C) 2001–2020 Leptonica. BSD-style license. [Upstream](https://github.com/DanBloomberg/leptonica/tree/1.82.0). The unmodified x64 binary is supplied by the Tesseract 5.2.0 NuGet package.

The Apache license, InteropDotNet license, Leptonica license, and provenance are in `OcrData/`. In the source checkout they are under `src/SnapBar.Windows.Ocr/OcrData/`.

## Image libraries inside the OCR native binaries

The following versions were reported by the bundled Leptonica DLL's `getImagelibVersions()` function. These libraries are included inside the native DLL even when SnapBar only supplies screen captures.

| Component | Version | License / notice |
| --- | --- | --- |
| GIFLIB | 5.2.1 | [MIT-style license](licenses/Ocr/GIFLIB-LICENSE.txt) |
| libjpeg-turbo, implementing the libjpeg 6b API | 2.1.4 | [BSD/IJG/zlib license overview](licenses/Ocr/LIBJPEG-TURBO-LICENSE.md), [IJG terms](licenses/Ocr/README.ijg) |
| libpng | 1.6.37 | [PNG Reference Library License](licenses/Ocr/LIBPNG-LICENSE.txt) |
| libtiff | 4.4.0 | [libtiff license](licenses/Ocr/LIBTIFF-LICENSE.txt), [per-file copyright and license notices](licenses/Ocr/LIBTIFF-SOURCE-NOTICES.txt) |
| zlib | 1.2.13 | [zlib notice and license](licenses/Ocr/ZLIB-README.txt) |

**This software is based in part on the work of the Independent JPEG Group.**

The native binary also contains libtiff LZMA codec implementation strings. This software includes code from XZ Utils <https://tukaani.org/xz/>. The liblzma source in the contemporaneous XZ Utils 5.2.x series is public domain; see [upstream licensing information](licenses/Ocr/LIBLZMA-COPYING.txt). The exact liblzma version is not exposed by this binary and is not asserted here. XZ command-line tools and their GPL/LGPL helper programs are not bundled with SnapBar.

Original downloaded texts and their SHA-256 hashes are recorded in `Licenses/Ocr/SOURCES.json`. In the source checkout, `Licenses/` corresponds to the `licenses/` directory. See `licenses/README.md` for verification and maintenance details.

## .NET runtime

Self-contained Windows packages include Microsoft .NET runtime and Windows Desktop runtime binaries. The resolved runtime-pack licenses are copied without modification into `Licenses/DotNet/<package-name>/LICENSE.txt`; supplied third-party notices are copied alongside them. `Licenses/DotNet/PACKAGES.txt` records the actual package versions. The Probe subdirectory contains its own runtime notices for its own runtime copies.

These generated files are present in self-contained published packages, not in framework-dependent build output or the source checkout. [Microsoft licensing information](https://github.com/dotnet/core/blob/main/license-information.md).

The Windows Desktop runtime pack does not supply a separate third-party notice file. Supplementary upstream [WPF notices](licenses/WindowsDesktop/WPF-THIRD-PARTY-NOTICES.txt) and [Windows Forms notices](licenses/WindowsDesktop/WINFORMS-THIRD-PARTY-NOTICES.txt) for .NET 10.0.13 are also retained, including the upstream zlib, Json.NET, and Ookii.Dialogs attributions. These upstream lists can cover components beyond the runtime subset used by SnapBar; they are retained without claiming that every listed component is used. Their sources and hashes are in `Licenses/WindowsDesktop/SOURCES.json`. Publishing with a different Windows Desktop runtime version requires reviewing and updating these supplementary notices.

## External prerequisites and user-supplied content

Microsoft Visual C++ x64 Redistributable is an external prerequisite obtained from Microsoft; its installer is not bundled. Node.js, Python, AI services, and StarDict dictionaries are optional, separately obtained dependencies or content. Their own terms apply. SnapBar does not include a dictionary database or grant rights to user-imported dictionaries, third-party services, or selected text.
