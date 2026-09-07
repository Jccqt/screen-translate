# Third-party code

The source-language validation feature uses the NuGet package **Tesseract 5.2.0**, from [Charles Weld's Tesseract wrapper](https://github.com/charlesw/tesseract). Its package metadata declares Apache-2.0. The package supplies the native Windows Tesseract and Leptonica libraries; the application targets x64.

| Component | License | License text |
| --- | --- | --- |
| Tesseract .NET wrapper 5.2.0 | Apache-2.0 | `Notices/Tesseract-wrapper-LICENSE.txt` |
| Tesseract native OCR engine, supplied as `tesseract50.dll` | Apache-2.0 | `Notices/Tesseract-LICENSE.txt` |
| Leptonica 1.82.0 | BSD-2-Clause | `Notices/Leptonica-LICENSE.txt` |

License texts are copied into build and publish output. The wrapper's .NET Standard dependency on System.Reflection.Emit is supplied by Microsoft under MIT via NuGet. Native runtime installation may require Microsoft's Visual C++ x64 redistributable, as described in the [wrapper's installation instructions](https://github.com/charlesw/tesseract/blob/master/ReadMe.md).

# Model licenses (separate from code)

No OCR or translation weights are bundled by this change. User-installed model licenses must be checked individually before redistribution.

For acceptance verification only, we downloaded `eng.traineddata` from [tessdata_fast, tag 4.1.0](https://github.com/tesseract-ocr/tessdata_fast/tree/4.1.0), licensed under [Apache-2.0](https://github.com/tesseract-ocr/tessdata_fast/blob/4.1.0/LICENSE). It is held in ignored `Tests/Artifacts/ocr-validation-data` and copied into temporary test folders before testing. SHA-256: `7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2`.

Synthetic translation-package fixtures do not contain translation weights and do not verify actual translation.

Requirement 1.4's download-review form also offers the same upstream English OCR model as an optional, user-initiated download. Its URL, Apache-2.0 license, version and pinned SHA-256 are shown before starting. No weights are bundled. Custom downloads require users to review their individual publisher's license and checksum; imported absent metadata is labeled Unknown. No translation-model license is inferred from the Argos or CTranslate2 code licenses.
