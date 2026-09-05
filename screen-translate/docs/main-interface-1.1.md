# Requirement 1.1: Main interface

## Review and changes

The previous implementation launched `MainForm` with `Application.Run`, exposed working language/folder selectors, and displayed separate discovery badges. It lacked overall translation readiness. Appearance only highlighted a button, the shortcut was a static label, navigation/model-management buttons had no actions, and pending discovery work had no cancellation token.

The revised implementation keeps the visible main window as the application's lifetime owner:

| Acceptance criterion | Implementation and evidence |
| --- | --- |
| Display the main interface at launch | Existing `Application.Run(new MainForm())` entry point retained. The WinForms harness checks visibility and message-loop exit on close. |
| Access source, target, appearance, global shortcut, and offline-model settings | General contains source/target selection, appearance, and the shortcut editor. Offline models contains both folder/refresh controls. Models navigation and Manage models focus OCR recovery when no source is installed, or translation-model settings otherwise. Navigation stays visible when scrolling. Appearance applies immediately. Apply validates and registers captured shortcut keys. |
| Show checking, ready, or action required, with a reason | The header displays the configured languages and a reason. Overall readiness combines OCR and directional translation discovery, runtime availability, and shortcut availability. Pending scans invalidate readiness. A separate evaluator covers all three states. |
| Keep configuration available with missing or invalid models | Target, appearance, shortcut, model folders, and refresh remain enabled. The OCR selector remains disabled only when it has no installed language to offer or is being checked; its folder/refresh recovery controls remain usable. Tests cover empty, missing, incomplete, unreadable, and invalid-directory cases. |
| Closing exits, cancels work, dismisses selection/overlays, and unregisters hotkey | `ApplicationLifetime` cancels active work and disposes the hotkey registration. MainForm disposes its owned windows, even if a child attempts to cancel closing. Closing or disposing prevents new scans and late windows. No tray/background mode is introduced. |

## Current runtime boundary

This repository does not implement OCR inference, translation inference, region selection, or a translation overlay yet. Discovered files are **not** proof of usable OCR/translation models. For a pair with all expected local files, the real interface therefore displays **Action required** and explains that screen translation is unavailable in this build while configuration remains usable. It never displays Ready merely because discovery fixtures or model-shaped files exist. Identical languages still need a working OCR runtime.

The Ready evaluator branch is tested with explicit logic fixtures, not real engines. When engine requirements are implemented, replace the runtime blocker only with actual engine validation and execution availability. Model downloads/import/removal and model license inspection remain part of model management; the main interface currently provides access to local folder selection and refresh.

The global shortcut is registered while the main interface is open. Until the workflow exists, pressing it brings the main interface forward to explain readiness. Invalid/conflicting edits retain the previous registration and saved shortcut. Accepted keys are Ctrl and/or Alt plus a letter, digit, or F key, optionally with Shift; Windows registration rejects unavailable/reserved combinations.

Future selection and overlay forms must use `MainForm.ShowTranslationWindow` (or the same main-window ownership), and active pipeline work must observe `MainForm.WorkCancellationToken`. Catalog implementations receive cancellation tokens; UI waits also cancel promptly, and stale results cannot update a closed form.

## Preferences and privacy

Appearance and shortcut preferences are stored separately in `%LOCALAPPDATA%\ScreenTranslate\interface.json`. Existing source/target settings files are preserved. Invalid individual interface preferences recover independently, and load/save failures are shown in the main interface. System theme follows the Windows app theme, including preference-change notifications and focus refresh. Theme subscriptions are removed on shutdown.

No screenshots, recognized text, translation history, model downloads, telemetry, or network calls are added. No model weights or third-party dependencies are bundled.

## Verification — 2026-09-05

```powershell
dotnet build
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts
```

- **Automated:** 199 assertions pass, covering existing language/model regression tests, readiness state/reasons, isolated preferences, configuration without valid models, shortcut editing/conflicts, cancellation, owned-window cleanup, and message-loop exit. Native Windows registration tests confirm conflict rejection and hotkey release. Redesign tests cover all everyday settings visible without scrolling at the default size, persistent navigation, accessible selection states, recoverable error visibility, and proportional typography/dropdown scaling with restoration to 100%. All settings and model-discovery fixtures are temporary; they do not touch user settings or installed models.
- **Rendered UI:** inspected missing-model, runtime-unavailable, invalid-directory, settings-error, light/dark, minimum-width, and synthetic 150% DPI renderings. Bounds checks cover readiness, language controls, shortcut controls, and model-folder actions. Artifacts are written under ignored `Tests/Artifacts`. See [UI redesign](ui-redesign.md) for references and design decisions.
- **Physical desktop:** manual keypresses from another application, native folder-picker interaction, real monitor transitions, and physical DPI changes were not verified. Native desktop automation is unavailable in this session. Selection/overlay shutdown tests use owned window fixtures, not an implemented capture/translation workflow. Real-model translation has not been tested.

The automated acceptance scope of 1.1 is covered. End-to-end translation readiness and actual selection/overlay behavior depend on their separate runtime requirements.
