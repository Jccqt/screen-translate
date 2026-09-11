# Screen Translate Project Guidance

## Product and Scope

Screen Translate is an open-source, offline-first Windows app with this Version 1.0 workflow:

`Press global hotkey -> select screen region -> run local OCR -> run local translation -> show translated overlay`

Once its models are installed, this workflow must work offline.

### Version 1.0 behavior

- Launch into the main WinForms UI. Let users choose an installed OCR source and any translation target; persist languages, theme, hotkey, and model settings.
- Show local OCR and directional translation model status. Support model download, import, inspection, and removal, including size, source, and license.
- Apply Light, Dark, and System themes without restarting.
- Let users configure a global hotkey; reject invalid or conflicting shortcuts. It opens drag-to-select region capture; `Esc` cancels.
- Support Windows multi-monitor layouts and common DPI scaling.
- Use Tesseract for local OCR, retaining positions and confidence when available. Translate with an installed offline model through a replaceable engine.
- Show an always-on-top translation overlay near or over source text until `Esc` or right-click. Allow copying original and translated text.
- Clearly report missing models, no detected text, OCR or translation failures, hotkey conflicts, model-download failures, and recoverable settings errors.

### Non-goals

Unless explicitly requested as future work, exclude continuous translation, automatic source detection, full-screen replacement, cloud translation, history, speech, and non-Windows versions.

## Technical and Privacy Constraints

- Target Windows 10/11 x64 with .NET 10 and Windows Forms.
- Separate capture, OCR, translation, model management, settings, and overlays. Expose replaceable `IOcrEngine` and `ITranslationEngine` abstractions.
- Run OCR and translation asynchronously with cancellation, without blocking UI. Promptly dispose of images and native resources.
- Aim for a typical translation within three seconds after warm-up.
- Keep screenshots and recognized text on-device; store neither unless explicitly requested. Disable telemetry and remote collection by default.
- Prefer Apache-2.0-compatible dependencies. Track code and model licenses separately in project notices. Bundle or redistribute translation models only after license verification.

## Model and Settings Rules

- Keep the target language selectable when its model is missing and persist stable language codes.
- Determine availability for the source-to-target direction. Recheck after language or model-folder changes, refresh, and focus regain. Identical languages need no translation model.
- Keep discovery independent of UI and translation engines. Document the on-disk format; discovery does not prove compatibility. Treat unreadable or incomplete packages as invalid and distinguish scan errors from missing models.
- Preserve unrelated settings when adding preferences. Surface recoverable load/save errors without crashing or silently replacing valid values.

## Development Workflow

For code changes:

1. Define concise acceptance criteria, including relevant failure and cancellation paths. Inspect only applicable code, tests, and guidance. Make the smallest complete change without expanding Version 1.0 scope or disturbing unrelated work. Resolve routine choices independently; ask only when blocked by missing information.
2. Add focused non-UI tests for meaningful regression coverage, including applicable error, cancellation, and cleanup paths.
3. Review the final diff against acceptance criteria and applicable architecture, privacy, async, resource, settings, and compatibility rules. Fix in-scope findings.
4. Iterate with focused checks. On final code, run `dotnet build` and `dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts`. A started command is not a pass; inspect results. Rerun only after relevant changes or failures. Skip both for documentation-only changes.
5. For affected UI or desktop behavior, manually verify hotkeys, region capture, overlays, multi-monitor behavior, and DPI scaling as applicable. Use temporary settings and discovery fixtures; never modify installed user models or claim fixtures verify actual translation.
6. Fix in-scope failures and repeat affected checks. Complete independent work before reporting a blocker. Report changes, findings, limitations, blockers, and final automated, rendered-UI, and physical-desktop results as applicable. Explain unrelated failures; never claim completion with required checks blocked or failing.

## Commits and Pull Requests

- Use Conventional Commits without scopes: `<type>[!]: <short imperative description>`; choose from `feat`, `fix`, `docs`, `refactor`, `test`, `perf`, `build`, `ci`, or `chore`.
- Mark breaking changes with `!` or a `BREAKING CHANGE:` footer describing impact and migration. Keep commit and pull-request descriptions concise and include relevant validation.
- Never attribute a commit to Codex or add a Codex `Co-authored-by` trailer.
