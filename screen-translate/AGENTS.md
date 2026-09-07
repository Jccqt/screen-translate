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

## Commits and Pull Requests

- Follow Conventional Commits for every commit: `<type>[!]: <description>`. Do not include a scope.
- Choose the type that matches the change, such as `feat`, `fix`, `docs`, `refactor`, `test`, `perf`, `build`, `ci`, or `chore`. Use a short, imperative description, for example `feat: support copying translated text` or `fix: preserve target language on reload`.
- Mark breaking changes with `!` before the colon or a `BREAKING CHANGE:` footer, and explain the impact and required migration in the commit body.
- Keep commit titles and descriptions short and clear.
- Use clear pull request titles and short descriptions that summarize the change and relevant validation.
- Do not include Codex as a commit co-author or add a `Co-authored-by` trailer attributing a commit to Codex.

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

## Requirement Development Iteration

For each development requirement, continuously follow this loop within the authorized scope:

`Define acceptance criteria -> inspect -> implement -> review -> validate -> fix and repeat -> complete`

1. **Define the requirement.** State the expected user-visible behavior and concrete acceptance criteria, including relevant failure and cancellation paths. Identify affected components and applicable Version 1.0 constraints. Resolve routine implementation choices independently; ask only when missing information prevents correct progress.
2. **Inspect and plan.** Read the relevant implementation, tests, and project guidance. Identify the smallest complete change, likely regression risks, and the checks needed to demonstrate each acceptance criterion.
3. **Implement.** Complete the behavior across the affected components, including error handling and resource cleanup. Add or update focused tests for non-UI logic and regressions. Preserve unrelated user changes and avoid expanding scope.
4. **Review the actual diff.** Check the implementation against every acceptance criterion. Look for correctness defects, regressions, async and cancellation issues, resource leaks, settings compatibility problems, and violations of offline-first or privacy-first behavior. Fix actionable findings before completion.
5. **Validate.** After relevant code changes, run `dotnet build` and `dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts`, plus focused checks appropriate to the change. Perform required UI and physical-desktop verification when the affected features require it. Inspect actual results; do not treat a command being started as a passing check.
6. **Repeat until ready.** If review or validation finds a defect, failed test, or unmet acceptance criterion, return to implementation, fix it, review the updated diff, and rerun the affected checks and required validation. Continue without asking for confirmation for routine fixes within scope. Do not stop at the first implementation or merely propose fixes that can be completed now. Once the completion criteria are met, finish rather than repeating unchanged checks indefinitely.
7. **Report completion or a concrete blocker.** Summarize the implemented behavior, review outcome, validation results, and any remaining limitations. If progress requires unavailable tools, credentials, hardware, a user decision, or approval, complete independent work first, then identify the exact blocker and what is needed to continue. Do not claim completion while required verification remains blocked.

### Iteration Record

Use this concise structure in the development plan or progress notes; update it as the work advances rather than creating a new repository file unless requested:

```text
Requirement: <behavior being implemented>
Acceptance criteria: <observable outcomes and relevant failure cases>
Iteration: <number>
Implementation: <changes made and affected components>
Review: <findings and their resolution, or no outstanding findings>
Validation:
  Automated: <commands and pass/fail/not-run results>
  Rendered UI: <checks and results, or not applicable with reason>
  Physical desktop: <checks and results, or not applicable with reason>
Remaining work or blockers: <specific items, or none>
Status: <in progress / blocked / complete>
```

### Completion Criteria

- All acceptance criteria are implemented and verified.
- The final diff has been reviewed and no known actionable defects introduced by the change remain unresolved.
- All required builds, tests, and applicable manual checks pass against the final implementation. Explain unrelated pre-existing failures explicitly; do not describe a failing suite as passing.
- No required checks are skipped or blocked. A check may be marked not applicable only with a reason tied to the change; documentation-only changes do not require a build or acceptance-harness run.
- The final report distinguishes automated, rendered-UI, and physical-desktop evidence. State that no defects were found in the performed review and checks when accurate; never claim that testing proves the absence of all bugs.
