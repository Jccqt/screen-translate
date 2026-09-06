# Requirement 1.2: Source language

## Revised requirement and implementation review

The previous implementation already discovered readable, nonempty language files, filtered auxiliary data, persisted source settings, and refreshed on launch, activation, and explicit refresh. The revised requirement exposed three gaps:

1. Missing saved languages became the first available language on General without a visible warning. The explanation lived on Offline models and disappeared after another refresh because the fallback was already saved.
2. An empty scan erased the saved preference. A temporarily locked individual model could also trigger a replacement, even though folder-level errors already preserved preferences.
3. Discovery was the only check. There was no Tesseract model-loading implementation to establish compatibility.

The selector now requires an explicit replacement when a saved source is unavailable. Its warning names the saved code beside **Read from**, in the readiness card, and on Offline models. Refresh and restart preserve the warning and preference until the user chooses another source or the saved data becomes available again. There is no automatic fallback for an existing preference. On first setup, with no saved code, the first available language is selected, displayed, and saved.

## Using the feature

1. Go to **Offline models → Choose folder…** in Text recognition and select your local `tessdata` directory.
2. Select the source in **General → Read from**. Only discovered OCR language files appear. The target selector remains independent.
3. In Offline models, use **Validate OCR data** to load the selected data with Tesseract. A successful load shows **Validated**; incompatible data shows **Load failed** with recovery instructions on both pages.
4. After installing, removing, or replacing files, use **Refresh languages**, or return focus to the app.

With no available languages, the selector and validation action are disabled and the interface explains where to locate `.traineddata` files. A folder scan error says it cannot read the folder rather than claiming models are absent. The saved preference survives missing folders, empty folders, locked files, and folder read errors.

If the source-settings file itself cannot be read, the application leaves the source unselected and keeps the recovery warning. Refresh retries reading the saved settings and restores the original folder and language when reading succeeds. It does not automatically save fallback settings or treat a settings file that disappears during recovery as a first launch. An explicit source or folder choice can replace unreadable settings.

Settings remain in `%LOCALAPPDATA%\ScreenTranslate\source-language.json`; the default data folder is `%LOCALAPPDATA%\ScreenTranslate\tessdata`. The feature does not download data automatically or capture/store screen content. Existing target, appearance, and shortcut settings are preserved.

## Discovery and engine contract

`OcrLanguageCatalog` lists readable, nonempty `.traineddata` files directly inside the selected folder. It tests readability with an actual read. Auxiliary `osd` and `equ`, empty/locked files, partial-download extensions, nested files, and Tesseract language expressions using `+` or `~` are excluded. Names preserve the exact filename stem, including custom codes and case. A saved code can match case-insensitively on Windows, but the exposed engine code is always the discovered spelling. No automatic language detection option is offered.

Discovery does not prove a file is internally valid. `IOcrEngine.ValidateLanguageAsync` is the replaceable model-loading boundary, implemented by `TesseractOcrEngine` using the Tesseract 5.2.0 package. The loader reopens the exact selected file, checks it is nonempty, holds it against writes/deletion during initialization, and passes the unchanged OCR code and explicit directory to native Tesseract. It rejects load errors instead of retrying another language. It disposes the temporary engine and file stream after validation. Runtime and data-load failures have separate recovery messages.

Validation runs off the UI thread. Selection changes and refreshes invalidate in-flight validation results. Closing cancels UI waiting; native initialization finishes on its worker and releases its resources because it cannot be interrupted safely. Completed failures are remembered by full model path for the current session: explicit refresh, activation refresh, a temporary scan omission, and switching languages do not erase them. Only a successful validation retry clears the remembered failure for that model; replacing files alone cannot establish engine compatibility. A refresh clears a previous success badge, so discovery cannot masquerade as fresh validation. Validation results are not persisted across application restarts.

The screen capture, recognition, translation, and overlay pipeline remains unimplemented in this build. Successful model loading does not mark the entire application ready. Future recognition must use the same load-time checks and exact selection; this revision verifies model loading, not OCR accuracy or end-to-end translation.

The format and installation instructions follow [Tesseract's official installation guidance](https://tesseract-ocr.github.io/tessdoc/Installation.html). Dependency and test-model licenses are recorded separately in [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## Acceptance verification

| Revised criterion | Evidence |
| --- | --- |
| User selects the language Tesseract recognizes | WinForms selection, settings round trip, exact code/directory forwarding, native loading with English and a mixed-case custom filename. |
| Only readable, nonempty local language data is selectable; no auxiliary data | Missing/empty folders, locked and zero-byte files, added/removed data, custom codes, case, partial files, nested data, auxiliary codes and language-expression tests. |
| Preserve engine code and validate at load | Real native model initialization plus rejection of corrupt, empty, locked, removed and replaced data; explicit folder overrides unrelated TESSDATA_PREFIX. |
| No languages disables selector and explains setup | WinForms disabled/empty state and visible General/Offline models guidance. |
| Missing saved language requires a new choice or visible fallback | Explicit replacement required; warning and saved code survive repeated refresh and restart; returning data restores the saved source. |
| Temporary folder-read failure preserves preference | Read-error UI and saved-settings assertions, followed by folder recovery and restored selection; separate locked-file recovery test. |
| No automatic detection required | No detection option; auxiliary data cannot be selected or loaded. |

Run on Windows x64 with the .NET 10 SDK:

```powershell
dotnet build
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts
```

The harness uses temporary settings/data and a real WinForms message loop. It also tests native rejection of invalid data without needing a real model. Genuine-model success tests are optional and print an explicit skip when `SCREEN_TRANSLATE_TEST_TESSDATA` is unset. To reproduce the full verification, first obtain the Apache-2.0 English model in the ignored artifact directory (the application itself does not perform this download):

```powershell
New-Item -ItemType Directory -Force Tests/Artifacts/ocr-validation-data
Invoke-WebRequest https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata -OutFile Tests/Artifacts/ocr-validation-data/eng.traineddata
# Expected SHA-256: 7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2
Get-FileHash Tests/Artifacts/ocr-validation-data/eng.traineddata -Algorithm SHA256
$env:SCREEN_TRANSLATE_TEST_TESSDATA = (Resolve-Path Tests/Artifacts/ocr-validation-data).Path
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts
```

The harness copies real OCR data into temporary folders before destructive fixture tests. It never modifies the supplied or installed model. Synthetic translation packages remain discovery fixtures, not translation evidence.

Verification is reported separately:

- **Automated (2026-09-06, after review fixes):** build passed with zero warnings/errors; all 293 acceptance assertions passed. New regressions were observed failing before their corresponding fixes. They cover repeated settings-read errors and recovery, missing/corrupt settings, explicit replacement, activation/explicit refresh after a native model failure, language changes, and successful revalidation. Genuine native OCR loading and the existing target-language/interface checks also passed. The full run used the verified English test model; no genuine-model tests were skipped. Output is in `Tests/Artifacts/acceptance.log`.
- **Rendered UI (2026-09-06):** inspected DrawToBitmap images of unavailable selection, invalid data, and successful validation, plus Light/Dark, minimum-width and synthetic 150% DPI checks. The final minimum-width layout also passed overflow checks. These are rendered WinForms surfaces, not physical-desktop screenshots.
- **Physical desktop:** not performed for this revision. Physical multi-monitor movement, hotkey-driven capture, and overlays were not changed and are not verified by these tests.
