# Screen Translate Project Guidance

## Product

Screen Translate is an open-source, offline-first Windows screen translation application. Its Version 1.0 experience should resemble an on-demand Google Lens workflow:

`Press global hotkey -> select screen region -> run local OCR -> run local translation -> show translated overlay`

Do not require an internet connection after the necessary OCR and translation models have been installed.

## Version 1.0 Requirements

- Display the main WinForms interface when the application launches.
- Let the user select an installed OCR source language and a translation target language.
- Show whether the required local OCR and translation models are installed.
- Allow users to download, import, inspect, and remove models; show each model's size, source, and license.
- Support Light, Dark, and System themes and apply theme changes without restarting.
- Allow the user to configure a global translation hotkey. Reject invalid or conflicting shortcuts.
- When the hotkey is pressed, show a region-selection interface. Dragging selects the region; `Esc` cancels.
- Support Windows multi-monitor setups and common DPI scaling configurations.
- Use Tesseract for local OCR. Preserve recognized text positions and confidence data when available.
- Translate recognized text with an installed offline model through a replaceable translation-engine interface.
- Display translations in an always-on-top overlay positioned near or over the source text.
- Keep the overlay visible until dismissed with `Esc` or right-click.
- Allow copying the original and translated text.
- Provide clear errors for missing models, no detected text, OCR or translation failures, hotkey conflicts, and model-download failures.
- Persist languages, theme, hotkey, and model settings between sessions.

## Technical Constraints

- Target Windows 10 and Windows 11 on x64.
- Use .NET 10 and Windows Forms.
- Keep screen capture, OCR, translation, model management, settings, and overlay rendering as separate components.
- Define replaceable `IOcrEngine` and `ITranslationEngine` abstractions instead of coupling the UI to a particular implementation.
- Run OCR and translation asynchronously, support cancellation, and do not freeze the UI.
- Dispose of captured images and other native resources promptly.
- Aim to display a typical translation within approximately three seconds after engine warm-up.
- Keep screenshots and recognized text on the device. Do not store them unless the user explicitly requests a future feature that requires storage.
- Do not enable telemetry or remote data collection by default.
- Prefer Apache-2.0-compatible dependencies. Track third-party code and model licenses separately in project notices.
- Do not bundle or redistribute translation models until their individual licenses have been verified.

## Version 1.0 Non-Goals

- Continuous or real-time screen translation
- Automatic source-language detection
- Full-screen automatic text replacement
- Cloud translation providers
- Translation history
- Speech output
- Mobile or non-Windows versions

Treat these as future possibilities, not implicit implementation requirements.

## Development Guidance

- Preserve the offline-first and privacy-first behavior when making changes.
- Avoid expanding Version 1.0 beyond the requirements above without explicit user direction.
- Keep target-language selection available even when its model is missing, and persist the selection using stable language codes.
- Determine translation-model availability for the selected source-to-target direction. Recheck when either language or the model folder changes, on explicit refresh, and when the app regains focus. Treat identical languages as requiring no translation model.
- Keep model discovery separate from the UI and translation engine. Document the supported on-disk format; file discovery does not establish engine compatibility. Do not label unreadable or incomplete packages as installed, and distinguish scan errors from missing models.
- Preserve existing settings when adding new preferences. Show recoverable load/save errors without crashing or silently changing unrelated preferences.
- Build the project with `dotnet build` after relevant code changes.
- Add focused tests for non-UI logic and manually verify global hotkeys, region capture, overlays, multi-monitor behavior, and DPI scaling when those features are changed.
- Run the acceptance harness with `dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts`. Use temporary settings and model-discovery fixtures; never modify a user's installed models or imply fixtures verify actual translation. Report automated, rendered-UI, and physical-desktop verification separately.
