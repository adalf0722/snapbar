# OCR dependencies

- Tesseract and the .NET wrapper come from [Tesseract 5.2.0 on NuGet](https://www.nuget.org/packages/Tesseract/5.2.0). The package includes the x64 Tesseract and Leptonica 1.82.0 native binaries. [Wrapper source and license](https://github.com/charlesw/tesseract)
- The .NET wrapper is copyright 2012–2022 Charles Weld and uses InteropDotNet, copyright 2014 Andrey Akinshin. The InteropDotNet MIT license is included as `INTEROPDOTNET-LICENSE.txt`.
- The English and Traditional Chinese model files are unchanged from [tessdata_fast 4.1.0](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0). The Apache 2.0 license is included as `TESSERACT-LICENSE.txt`.
- Leptonica's license is included as `LEPTONICA-LICENSE.txt`. [Leptonica 1.82.0 source](https://github.com/DanBloomberg/leptonica/tree/1.82.0)
- The packaged native binaries require the Microsoft Visual C++ x64 runtime. [Microsoft runtime documentation](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
