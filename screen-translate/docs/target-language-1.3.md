# Requirement 1.3: Target language

Users can select the output language independently of model installation, and the application indicates whether local model files are installed for the selected source-to-target direction. The selection and translation folder survive restart.

## AGENTS.md review

The existing guidance already defines the offline-first workflow, component boundaries, model licensing restrictions, and Version 1.0 scope. Its language/model requirements needed more precise acceptance rules. Added guidance covers:

- Stable target codes and selection when models are missing.
- Direction-specific installation checks, refresh triggers, and identical-language behavior.
- Separation of file discovery from engine compatibility, and clear missing/read-error states.
- Preservation of existing settings and recoverable settings errors.
- The runnable acceptance-test command, isolated fixtures, and honest reporting of verification limits.

## Using the feature

1. Choose an installed **Source language (OCR)** and select the desired **Translate to** language.
2. Read the translation-model message below the OCR folder controls. The **Offline models** section also shows the current installation state.
3. Use **Choose translation folder…** to select a local directory containing extracted Argos package subdirectories.
4. Use **Refresh models** after adding or removing packages. Changing either language or returning focus to the app also rechecks availability.

Output choices are Chinese (Simplified), English, Filipino, French, German, Japanese, Korean, and Spanish. Spanish preserves the previous UI default. These are output preferences, not a promise that every pair has an available model. Missing models do not disable the target selector or erase its selection.

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

OCR codes are mapped to translation language codes before matching. Chinese variants and vertical OCR data have explicit mappings; unknown custom OCR codes produce an explanatory state. Direction matters: an English-to-Spanish package does not satisfy Spanish-to-English. Identical languages show **Not required**. No OCR source shows **Select source**. Pivot translation and multilingual package metadata are not implemented.

`MainForm.SelectedTargetLanguageCode`, `TranslationModelDirectory`, and `SelectedTranslationModel` expose the settings and discovered package for future integration. Scans run off the UI thread; pending scans do not expose stale packages, and older scan results cannot replace newer results.

There is no translation engine in this repository yet. **Installed** means the documented local files were discovered, not that translation or engine loading has been verified. No models or third-party code were bundled. Actual inference and engine-load validation belong to the translation-engine requirement.

## Verification

Run from the application project directory on Windows with .NET 10:

```powershell
dotnet build
dotnet build Tests/ScreenTranslate.Tests.csproj
dotnet Tests/bin/Debug/net10.0-windows/ScreenTranslate.Tests.dll Tests/Artifacts
```

Verified on 2026-09-05:

- Application and test builds: zero warnings and errors.
- Acceptance harness: 116 passing assertions, including existing OCR regressions.
- Model tests: direction matching, absent/incomplete/locked/removed files, metadata errors, tokenizer and vocabulary alternatives, source-code mapping, same-language behavior, and directory errors.
- Settings and WinForms tests: immediate persistence, restart, missing models, changing languages/folders, save failures, invalidating cached availability, and overlapping scan ordering.
- Rendered UI inspection: installed, missing, folder-error, minimum-width, and synthetic 150% DPI states. No horizontal overflow in the tested layouts.

Tests use temporary settings and synthetic discovery files, never the user's model installation. Renderings are written to the ignored `Tests/Artifacts` directory. The harness runs a real WinForms message loop, but its DPI-change notification is synthetic. Physical monitor transitions, native folder-picker interaction, and real-model translation were not manually verified.
