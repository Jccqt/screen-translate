# Requirement 1.3: Target language

Requirement [1.4 Offline models](offline-models-1.4.md) adds package management, stricter structural checks and transaction-aware discovery. Its **Discovered** badge supersedes the historical **Installed** wording below; engine compatibility still requires actual loading.

Users can select the output language independently of model installation. Availability is checked for the selected source-to-target direction, and the selection and translation folder survive restart. Identical-language OCR results bypass translation and use the shared result and copy controls.

## Review of the updated requirement

The original implementation already provided documented output choices, stable-code persistence, direction-specific discovery, language/folder/explicit/focus refresh, and an identical-language **Not required** status. The updated requirement needs these additional behaviors:

| Updated acceptance rule | Change |
| --- | --- |
| Listed targets must not promise a compatible model for every pair | Explain this beside the language selectors; keep all eight choices available. |
| Refresh after installation or removal | Observe package files and root creation/replacement automatically, with periodic recovery scans. |
| Pending checks cannot confirm stale readiness | Invalidate the exposed model during OCR checks, validation, file changes, and translation scans; repeat scans interrupted by package writes and reject obsolete requests. |
| Explain unavailable or unmapped sources | Include missing saved-language, OCR scan/load failures, and unknown mapping explanations in the translation-model status. |
| Skip translation for identical languages and return recognized text | Add `TranslationProcessor`, `OcrResult`, and the shared `TranslationResultForm`; bypass both model discovery and the translation engine and preserve exact text and OCR region/confidence data. |

The existing project guidance remains applicable. No dependencies, models, downloads, telemetry, or text storage were added.

## Using the feature

1. Choose an installed **Source language (OCR)** and select the desired **Translate to** language.
2. Open **Offline models** and read the **Translation** status for the selected direction.
3. Use **Choose folder…** in that section to select a local directory containing extracted Argos package subdirectories.
4. Adding, modifying, or removing packages triggers a refresh automatically. **Refresh models** is also available. Changing either language or folder, or returning focus to the app, rechecks availability.

Output choices are Chinese (Simplified), English, Filipino, French, German, Japanese, Korean, and Spanish. Spanish preserves the previous UI default. These are output preferences, not a promise that every pair has an available model. Missing models do not disable the target selector or erase its selection.

Their stable persisted codes are `zh`, `en`, `fil`, `fr`, `de`, `ja`, `ko`, and `es`, respectively.

The default model directory is `%LOCALAPPDATA%\ScreenTranslate\translation-models`. Target settings are stored separately in `%LOCALAPPDATA%\ScreenTranslate\target-language.json`, preserving the existing OCR settings file. No downloads, external requests, screenshot storage, or telemetry are performed by this feature.

## Discovery contract

`ITranslationModelCatalog` keeps discovery replaceable. Its initial `ArgosTranslationModelCatalog` implementation reads direct-pair package metadata in immediate subdirectories of the selected folder. The format follows the [Argos package implementation](https://github.com/argosopentech/argos-translate/blob/master/argostranslate/package.py) and [CTranslate2 model structure](https://opennmt.net/CTranslate2/conversion.html).

Example layout:

```text
translation-models/
  translate-en_es/
    metadata.json
    sentencepiece.model
    model/
      model.bin
      config.json
      shared_vocabulary.json
```

Metadata must contain string `from_code` and `to_code` values. If `type` is present, it must be `translate`. A readable, nonempty `bpe.model` can replace `sentencepiece.model`. Separate readable, nonempty `source_vocabulary.json` and `target_vocabulary.json` can replace the shared vocabulary. Discovery checks readability and nonzero size of required files, and parses package metadata. It does not validate weight binaries, tokenizer contents, vocabulary contents, or runtime compatibility.

The package directory's name does not determine its language pair. An `.argosmodel` archive alone does not count as installed; its contents must already be extracted into a package subdirectory. Invalid metadata and missing, empty, or unreadable required files exclude that package and produce a skipped-package message. A missing root folder means no installed models; a folder-read failure produces **Cannot check**.

OCR codes are mapped to translation language codes before matching. Chinese variants and vertical OCR data have explicit mappings; unknown custom OCR codes produce an explanatory state. Direction matters: an English-to-Spanish package does not satisfy Spanish-to-English. Identical languages show **Not required**. An unavailable OCR source shows **Cannot check** with its reason; an unmapped source shows **Unknown source**. Pivot translation and multilingual package metadata are not implemented.

`MainForm.SelectedTargetLanguageCode`, `TranslationModelDirectory`, and `SelectedTranslationModel` expose the settings and discovered package. Scans run off the UI thread; pending OCR/translation checks and known OCR failures do not expose stale packages, and older scan results cannot replace newer results. Accessible status descriptions follow the visible state.

`TranslationModelMonitor` watches package contents recursively and the nearest existing parent nonrecursively. It detects initially missing roots and root renames without observing unrelated subtrees. A 300 ms UI timer handles notifications, failed subscriptions are retried, and a full scan every 30 seconds recovers from missed events. A scan interrupted by filesystem changes is repeated before confirming its result. Watchers and the timer are disposed with the main window.

**Installed** still means the documented local files were discovered, not that translation or engine loading has been verified. Actual inference and engine-load validation belong to the translation-engine requirement.

## Same-language result integration

The OCR/capture workflow can supply its in-memory `OcrResult` to `MainForm.ShowOcrResultAsync`. The source code comes from that recognition result, and the target is captured when processing starts. Later preference changes cannot relabel an existing result.

`TranslationProcessor` checks mapped language equality before touching the model catalog, model directory, or `ITranslationEngine`. An identical-language result preserves text, whitespace, line breaks, positions, and confidence data. Both **Copy original** and **Copy output** receive the same recognized text. An inaccessible translation folder does not block this path. Empty recognition, unsupported language codes, and cancellation are handled explicitly.

Different-language results use the same result contract and controls after the replaceable engine processes a direct model. The result window follows theme changes, reports recoverable clipboard failures, closes with **Close** or `Esc`, and is disposed when the main application exits. Text remains in memory; copying is explicit.

The repository still has no screen-region capture/recognition workflow or production translation backend. This change implements and tests the result integration point; it does not make the hotkey-to-OCR workflow available or claim end-to-end inference readiness. The main window retains its runtime-unavailable explanation until those separate requirements are implemented.

## Verification

Run from the application project directory on Windows with .NET 10:

```powershell
dotnet build
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts
```

Verified on 2026-09-06:

- Application and test builds: zero warnings and errors.
- Automated: The full CI script passed 356 assertions in each of Debug and Release with no skips, including genuine Tesseract model loading. Layout checks also cover a desktop-constrained launch height. Coverage includes existing OCR/settings/layout regressions plus automatic installation/removal/root replacement, focus invalidation, unavailable/unmapped/failed OCR sources, same-language engine/catalog bypass, exact result/copy text, clipboard failure/retry, theme changes, shutdown, and result layout at minimum width and synthetic 150% DPI. Translation discovery uses synthetic packages and different-language processing uses a fake engine.
- Rendered UI: inspected source-unavailable and unmapped states and the shared result window in Light/Dark themes, at minimum size, and at synthetic 150% DPI.
- Physical desktop: exercised both copy buttons using Windows input and verified exact fixture text on the real clipboard; `Esc` closed the result preview and its process exited normally.

Tests use temporary settings and synthetic discovery files, never the user's model installation. Renderings and local logs are written to the ignored `Tests/Artifacts` directory. The harness runs a real WinForms message loop, but its DPI-change notification is synthetic. Physical monitor transitions, native folder-picker interaction, and real-model inference were not verified. The Release CI script obtains and checksum-verifies its own isolated OCR test model; no model is bundled with the application.

For the isolated desktop result preview (copies only fixture text when a copy button is pressed):

```powershell
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- --result-preview
```
