# Requirement 1.5 — Theme

The General page offers System, Light and Dark. Selection applies immediately and
persists in the existing interface preferences without changing the shortcut or
language/model preferences. A checkmark, border and accessible checked state
identify the selected theme. Status and errors retain explanatory text.

System reads Windows' **application** light/dark preference (`AppsUseLightTheme`),
including at launch and when returning to System. `WindowsSystemThemeSource`
listens for Windows preference notifications; the main window dispatches updates
to its UI thread. Explicit Light and Dark ignore those notifications. Missing or
unreadable Windows preference values fall back to Light. Shutdown unsubscribes
the event source and ignores queued callbacks. Settings failures retain the
session's appearance and show a recoverable message.

The main window propagates colors recursively through owned windows. Result,
model manager, download review, package help and removal confirmation use the
shared `ThemedForm`; nested modal windows inherit the owner's theme before their
first display. New app dialogs should derive from this class and specify their
owner. Translation windows should be opened through `ShowTranslationWindow`.
Native title bars use the Windows dark appearance attribute when supported.
Windows file/folder pickers retain Windows' own appearance.

Buttons preserve keyboard focus indicators and readable disabled text. Language
dropdowns use a contrasting selection outline; model inventory adds an outline
and checkmark without replacing native keyboard or accessibility behavior.
The Light accent was darkened to meet 4.5:1 text contrast on the selected theme's
background. Light model-warning text also meets that threshold. Appearance and
navigation fills stay stable on hover; primary-button hover/press colors preserve
text contrast.

## Verification

- Automated: `dotnet build` and
  `dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts`.
  Final run: build passed with zero warnings/errors; 724 assertions passed.
  Theme coverage includes both System values, explicit overrides, missing/invalid
  registry values, recoverable registry errors, persistence, save failure,
  worker-thread notifications, nested modal loops, new/open owned windows,
  confirmation outcomes, shutdown cleanup, text contrast and selection-border
  contrast. Tests use temporary preferences and model fixtures.
- Rendered UI: inspect `Tests/Artifacts/theme-*.png`, model manager selection
  renders, and the existing minimum-size/150% DPI result and manager renders.
  Rendered title bars can differ from the physical DWM title bar.
- Physical desktop (Windows 11): checked immediate Light/Dark changes with an
  open result, result dismissal with Escape, Dark dropdown selection/focus,
  model-manager and nested download/help readability, and a real Windows
  Dark → Light → Dark change with two modal dialogs open and the main owner
  disabled. The original Windows Dark preference was restored. No downloads or
  installed-model changes were made.

For an isolated manual session, run
`dotnet run --project Tests/ScreenTranslate.Tests.csproj -- --theme-preview`.
Ctrl+R opens a result containing synthetic text; its copy action deliberately
reports a fixture failure without touching the clipboard. The preview uses the
real Windows theme source, a fake shortcut registration, temporary preferences
and empty model folders. Close the main window to clean up the fixtures.

Region capture, selection UI and the actual translated overlay are not present
in this build. Their readability must be checked when implemented; a generic
owned-window fixture does not establish overlay readability over screen content.
This change does not alter hotkey handling, screen geometry, monitor placement
or DPI handling. Physical multi-monitor/DPI verification of those features is
outside this theme change. Optional genuine OCR checks still require
`SCREEN_TRANSLATE_TEST_TESSDATA`; discovery fixtures never verify translation.
