# Main interface redesign

## Online references

- [Microsoft: Guidelines for app settings](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) — focused setting groups, bounded reading width, immediate preference changes, and cards with controls aligned beside their descriptions.
- [Fluent 2: Layout](https://fluent2.microsoft.design/layout) — proximity, consistent spacing, responsive alignment, and deliberate content density.
- [Fluent 2: Typography](https://fluent2.microsoft.design/typography) — a clear type hierarchy using Windows-native Segoe typography.
- [PowerToys Text Extractor](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor) — a related screen-text utility whose language and activation-shortcut preferences are central to configuration.

These informed the interaction and visual design. No screenshots, third-party UI assets, or dependency code were copied or bundled. The implementation remains .NET 10 Windows Forms.

## Result

The former wide sidebar and single oversized settings surface are replaced with a small brand bar, persistent General/Offline models navigation, and focused cards. A default 1040 × 840 client area shows all everyday settings without scrolling; narrower/shorter windows scroll vertically. Content has a 960 logical-pixel maximum width, with aligned margins that do not jump when scrollbars appear.

General contains the language pair, appearance, and global shortcut. Offline models contains separate OCR and translation cards with discovery status, model paths, and folder/refresh actions. Long paths are ellipsized with their full value available in a tooltip. Settings errors remain visible on General even when their detailed controls live on the model page.

One readiness banner explains the next necessary action. It links to model configuration from General and offers Refresh setup on the model page. Unavailable translation is described in user-facing language; files found on disk still cannot produce a false Ready state.

Themed native dropdowns preserve keyboard navigation and accessibility. Buttons share sizing, hover/press feedback, and visible keyboard focus. Theme choices and navigation expose checked/selected states to assistive technology. Light/Dark/System retain persistence and immediate updates. Typography, dropdown item height, and layout follow DPI changes; fonts are cached by DPI and released on shutdown.

## Verification

`dotnet build` succeeds without warnings or errors. The acceptance harness passes 199 assertions, including prior functional regressions and new visual/interaction invariants. Rendered WinForms images were inspected for both themes, missing/invalid models, preference recovery, minimum-width layout, and synthetic 150% DPI. The harness also checks return to 100% without accumulated font scaling. Temporary fixtures are not real translation models.

Physical monitor transitions, native folder-picker interaction, and manual hotkey presses from other apps were not verified. These are distinct from the automated native hotkey registration tests and rendered UI checks.

Representative generated previews (under the ignored `Tests/Artifacts` folder):

- `redesign-general-light.png`
- `main-dark-missing.png`
- `main-runtime-unavailable.png`
- `redesign-models-light.png`
- `redesign-settings-error.png`
- `scaled-150-percent.png`
