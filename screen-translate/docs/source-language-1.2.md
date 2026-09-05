# Requirement 1.2: Source language

## Interface review

The original source selector offered a fixed list even with no OCR data installed. Its label, “Screen text”, did not clearly identify the OCR setting, and the choice was neither retained nor exposed as a Tesseract language code. The OCR status was always “Not installed”.

The settings layout also forced a minimum page width and placed fixed-width controls beyond the card at smaller window sizes. The sidebar brand was too wide for its available space.

This change labels the field “Source language (OCR)”, populates it from local files, reports empty/read-error states, updates the OCR installation indicator, and saves the choice. Settings sections stack their controls when needed, the page scrolls vertically, and the brand fits the sidebar.

Other existing UI gaps remain outside 1.2: Home/Models navigation and Manage models have no actions; theme buttons only change their selected appearance; the shortcut is a static label; target languages are still a fixed list. These controls do not yet implement their corresponding Version 1.0 requirements.

## Using the selector

1. Open Settings and choose **Choose OCR folder…**.
2. Select a local `tessdata` folder containing installed language files such as `eng.traineddata` or `jpn.traineddata`.
3. Select the source language. The choice and folder are saved immediately.
4. After adding or removing language files, click **Refresh languages** or return focus to the application.

The default folder is `%LOCALAPPDATA%\ScreenTranslate\tessdata`; configuration is stored in `%LOCALAPPDATA%\ScreenTranslate\source-language.json`. The application does not download models or retain screenshots/text for this feature.

Discovery includes readable, nonempty `.traineddata` files directly inside the selected folder. It excludes auxiliary `osd`/`equ` data, partial downloads, and combined-language names. Known codes have readable names; custom codes remain available under their file names. No automatic-detection option is provided. The folder follows [Tesseract's installation guidance](https://tesseract-ocr.github.io/tessdoc/Installation.html); auxiliary data and language variants are described in the [official data-file reference](https://tesseract-ocr.github.io/tessdoc/Data-Files.html).

When a saved language is unavailable, the first installed language in display order is selected and the change is explained. With no installed languages, the dropdown is empty and disabled. A folder read error also disables selection but preserves the saved preference. Save failures are shown in the interface.

## Acceptance verification

| Criterion | Implementation and verification |
| --- | --- |
| User can select the language Tesseract will recognize | Noneditable dropdown uses exact model codes; tests change the selection, verify the exposed code/directory, and reopen the form to check persistence. |
| Only installed OCR languages are selectable | Discovery tests cover missing/empty folders, added/removed files, locked and zero-byte files, custom names, partial files, and auxiliary data. WinForms tests verify refresh, fallback, empty state, and directory errors. |
| Automatic detection is not required | No detection option or detection logic; explicitly tested. |

Run from the application project directory on Windows with the .NET 10 SDK:

```powershell
dotnet build
dotnet build Tests/ScreenTranslate.Tests.csproj
dotnet Tests/bin/Debug/net10.0-windows/ScreenTranslate.Tests.dll Tests/Artifacts
```

The dependency-free test executable returns a nonzero exit code on failure. It uses temporary configuration and discovery fixtures, runs an actual WinForms message loop, and writes UI renderings to the ignored `Tests/Artifacts` directory. It does not change the user's application settings.

Verification completed: build with zero warnings/errors; 60 passing assertions; visual inspection of empty/installed states and default/minimum/wide layouts. A synthetic Windows DPI-change notification exercises the form's 150% layout handling. This is not a physical multi-monitor test; actual monitor transitions and font rendering still need manual verification on a Windows desktop.

There is no OCR engine or recognition pipeline in this repository yet. `MainForm.SelectedSourceLanguageCode` and `MainForm.OcrDataDirectory` expose the selection for that future integration. Selection/discovery tests use file fixtures, not recognition models; they do not establish that a model is internally valid or compatible with Tesseract. Actual recognition and model-load validation must be tested when the OCR engine is implemented.

## Suggested commit

```text
feat: select source language from installed OCR data

- Discover local Tesseract language files and exclude auxiliary data.
- Persist the selected OCR code and data directory across sessions.
- Add folder selection, refresh, installation status, and error states.
- Fix settings overflow and adapt sections to narrow windows and DPI changes.
- Add source-language acceptance tests and document remaining UI gaps.

Validation: dotnet build; 60 acceptance assertions; rendered UI review.
```
