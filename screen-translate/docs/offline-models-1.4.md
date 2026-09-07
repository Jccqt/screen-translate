# Requirement 1.4: Offline models

Open **Offline models**, choose the OCR and translation storage folders, then open **Manage OCR…** or **Manage translation…**. The manager lists local models, exposes their metadata, imports packages, downloads a user-reviewed HTTPS source, validates OCR, and removes identified model files after confirmation. Existing language and folder settings retain their separate stores and stable language codes.

## Engine support and offline boundaries

Tesseract 5, supplied by the existing Tesseract .NET package, loads one local source-language `.traineddata` file. Imports must pass actual Tesseract initialization before publication. The default download form offers English `tessdata_fast` 4.1.0 with its Apache-2.0 license and pinned SHA-256. Other traineddata files can be imported, or downloaded by entering their publisher's final URL and metadata. Changing the default source clears its original license, version, size and checksum rather than attributing them to another model.

Translation management supports the direct-pair Argos package format established in requirement 1.3. A package's `from_code` → `to_code` determines its direction; reverse, pivot and multilingual inference are not implied. Output preferences remain `zh`, `en`, `fil`, `fr`, `de`, `ja`, `ko`, `es`, with no promise of a model for every pair.

**This repository still has no production translation runtime. No translation language pair is engine-validated or usable for inference in this build.** The manager reports discovered packages and explicitly explains why translation validation is unavailable. Adding inference remains the separate translation-engine requirement; this change does not install Python, Argos, CTranslate2, or network-dependent tokenizer/sentence-splitting runtimes behind the user's back. `ITranslationEngine` remains replaceable. A downloaded translation package is not a promise that this build can run it.

All archive contents, including supplied tokenizer and auxiliary sentence-splitting resources, are staged and installed together. No model manager, OCR validation, discovery, or translation-processing code silently downloads dependencies on first use. Native runtime prerequisites remain application prerequisites; missing runtime libraries produce the existing repair error. Nothing captures or stores screenshots/recognized text, enables telemetry, or bundles translation weights.

## Accepted formats

OCR imports accept a single `<language>.traineddata` file. Language filenames use ASCII letters, digits, underscores or hyphens, start with a letter, and are at most 40 characters. Auxiliary `osd` and `equ` data and Tesseract multi-language expressions are not source-language packages. The complete file must load with Tesseract.

Translation imports accept ZIP archives with a `.argosmodel` or `.zip` extension, containing one package directly at the archive root or under one wrapper directory:

```text
metadata.json
sentencepiece.model
model/
  model.bin
  config.json
  shared_vocabulary.json
```

Metadata must contain valid string `from_code` and `to_code` language codes. If `type` is present it must equal `translate`. Nonempty `bpe.model` may substitute for `sentencepiece.model`. Both `model/source_vocabulary.json` and `model/target_vocabulary.json` may substitute for the shared vocabulary. Required JSON must parse as an object or array. Required files must be readable and nonempty. These are structural checks, not binary engine validation. Include all additional resources required by the package in the archive.

Already-extracted package subdirectories remain discoverable in the configured translation folder. An archive alone is not an installation. The format follows the [Argos package contract](https://github.com/argosopentech/argos-translate/blob/master/argostranslate/package.py) and [CTranslate2 model directory contract](https://opennmt.net/CTranslate2/conversion.html); neither dependency is added to this application.

Archives reject traversal, absolute paths, drive paths, NTFS alternate streams, reserved Windows device names, trailing-dot/space aliases, case-insensitive duplicate paths, symbolic/reparse links, and reserved manager paths. Limits are 8 GiB per download/extracted package and 20,000 archive entries. Metadata is limited to 1 MiB, management receipts to 16 MiB, and each required vocabulary/configuration JSON file to 64 MiB. Optional empty files are allowed. Linked model storage paths are deliberately unsupported; choose the actual local directory. Unknown formats and incomplete packages are rejected before publication.

## Metadata and state

Inspection displays the model identifier/name, purpose, language/direction, available version, local location, state, size of identified files, source and license. Imported metadata absent from the package is **Unknown**. A filename/directory name serves as the local identifier if no display name is provided. An arbitrary filename never supplies a guessed license, source or version. Licenses in custom download forms are user-supplied disclosures, not endorsements by the application.

States distinguish missing, checking, discovered, validated, invalid and read errors. OCR validation failures remain visible across manager refreshes. **Validated** is shown only following successful actual OCR loading; refresh conservatively returns to discovery. Translation packages are always unvalidated in this build. Main-window discovery badges now say **Discovered**.

Downloads show the final source, license, SHA-256 when supplied, and download size before **Start download**. Unknown download sizes are explicitly **Unknown**; installed disk size is calculated after installation and is separate from compressed/download bytes. The network is accessed only after the user starts. Transfers show byte progress (or an indeterminate bar where size is unknown), support cancellation and explicit retry from the beginning. HTTP redirects are rejected to avoid silently switching to a source the user did not review. Supply the final HTTPS publisher URL. Known published sizes and supplied trusted SHA-256 checksums must match before installation.

`<language>.traineddata.screen-translate.json` and package-local `.screen-translate.json` receipts retain reviewed provenance and installed-file inventories. These files are management metadata, not language settings. They never make a package engine-validated.

## Transactions and removal

Downloads, extraction and loading happen in a fresh `.st-<guid>` directory on the chosen volume. Discovery excludes these directories and takes a shared read lease; publication and engine work exclude mutation with a named cross-process semaphore. OCR validation and translation processing hold their leases until actual worker completion, even if their UI callers stop awaiting them. The UI reports when an active operation blocks a change. Future engines must retain this lifetime rule around any model-backed work.

Publication is the final, short, non-cancellable operation. Existing files move to a transaction backup and are restored on publication failure. Failed/cancelled staging leaves the existing usable model intact. Replacement of a translation folder with unidentified files is blocked, preserving those files. OCR replacement touches only the specific traineddata file and its receipt. A rollback failure preserves the hidden transaction directory and backups for recovery; do not delete a `preserve-backups` transaction until it has been inspected. Failed cleanup also leaves excluded transaction files rather than reporting them as installed. Resume and crash-recovery automation are not implemented.

Removal asks for confirmation naming the model, affected language/direction, local location and identified file count. The default is **No**. It moves only the current identified files to a private transaction area and rolls back if a file cannot move. It never recursively deletes a selected package or user-selected parent folder. For unmanaged extracted packages, only known format files and recognized LICENSE/README files are identified; unrelated contents remain. For managed packages, the receipt also identifies the extra files installed from the archive. Empty known directories may be removed nonrecursively. Stale inventories and paths outside the storage root are rejected.

After the manager closes, OCR and translation availability refresh without erasing language preferences. Existing translation watchers and focus refresh still detect external changes. Storage-folder selection and settings persistence continue to use the earlier requirement implementations.

## Verification

Run on Windows x64 with .NET 10:

```powershell
dotnet build
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts
```

Set `SCREEN_TRANSLATE_TEST_TESSDATA` to an isolated folder containing genuine `eng.traineddata` to include real OCR-loading checks. `scripts/Invoke-CI.ps1` obtains and checksum-verifies that isolated fixture. Tests never modify a user's installed models. Translation and HTTP fixtures test management behavior only, not inference.

The acceptance harness covers unsafe archives, missing/invalid files, unknown provenance, disk/download size disclosure, checksum mismatches, HTTP failures, cancellation and retry, explicit replacement, rollback after locked-file removal, in-use exclusion through worker completion, staging invisibility, unrelated-file preservation, and genuine OCR usability after failed replacement. It also exercises WinForms inventory/details, unavailable translation validation, download disclosure, source-edit metadata reset, minimum layouts, Light/Dark rendering, and synthetic 150% DPI.

For an isolated interactive OCR manager (temporary model files are removed when the preview exits):

```powershell
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- --models-preview
```

Automated, rendered-UI and physical-desktop results are recorded separately in the PR. Real translation inference, physical multi-monitor transitions, and real translation-model compatibility are outside this verification; no synthetic fixture establishes those capabilities.

Verified on 2026-09-07:

- Automated: `dotnet build` passed with zero warnings/errors. The required Debug acceptance command and Release CI script each passed 445 assertions, with genuine OCR fixtures and no skips.
- Rendered UI: inspected the manager in Light/Dark, at minimum size and synthetic 150% DPI, plus the download disclosure dialog. Metadata remains scrollable and actions remain accessible.
- Physical desktop: an isolated preview was launched, but computer-use app approval timed out before control inspection. Native interaction, real network transfer through the UI, and physical monitor transitions remain unverified. Automated transfer tests use an injected HTTP handler.
