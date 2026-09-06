# Main interface redesign

Updated September 6, 2026 using the approved Capture Reply icon.

## Online references

- [Microsoft: Guidelines for app settings](https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings) — focused setting groups, bounded reading width, immediate preference changes, and cards with controls aligned beside their descriptions.
- [Fluent 2: Layout](https://fluent2.microsoft.design/layout) — proximity, consistent spacing, responsive alignment, and deliberate content density.
- [Fluent 2: Typography](https://fluent2.microsoft.design/typography) — a clear type hierarchy using Windows-native Segoe typography.
- [PowerToys Text Extractor](https://learn.microsoft.com/en-us/windows/powertoys/text-extractor) — a related screen-text utility whose language and activation-shortcut preferences are central to configuration.

These references informed the earlier interaction and visual design. This pass uses the supplied icon masters as its visual reference. No screenshots, third-party UI assets, or dependency code were copied or bundled. The implementation remains .NET 10 Windows Forms.

## Result

The interface takes its charcoal and teal directly from the icon masters. The supplied frame-and-bubble mark replaces the placeholder character in the header, switching between light and dark artwork with the theme. The original multi-resolution ICO is embedded in the executable and assigned to the main window. All artwork is embedded and disposed with the window; no loose image files are needed at runtime.

The brand bar, persistent General/Offline models navigation, and language pair establish the hierarchy. A default 1000 × 800 client area shows all everyday settings without scrolling; narrower/shorter windows scroll vertically. Content has a 960 logical-pixel maximum width, with aligned margins that do not jump when scrollbars appear.

General contains the language pair and a shared preferences panel, with the shortcut followed by appearance. A thin divider separates the rows. Small corner radii, Segoe UI typography, plain surfaces and one teal action keep the interface restrained. Offline models contains separate OCR and translation cards with discovery status, model paths, and folder/refresh actions. Long paths are ellipsized with their full value available in a tooltip. Settings errors remain visible on General even when their detailed controls live on the model page.

One readiness banner explains the next necessary action. It links to model configuration from General and offers Refresh setup on the model page. Unavailable translation is described in user-facing language; files found on disk still cannot produce a false Ready state.

Themed native dropdowns preserve keyboard navigation and accessibility. Buttons share sizing, hover/press feedback, and visible keyboard focus. Theme choices and navigation expose checked/selected states to assistive technology. Light/Dark/System retain persistence and immediate updates. Typography, dropdown item height, and layout follow DPI changes; fonts are cached by DPI and released on shutdown.

## Verification

`dotnet build` succeeds without warnings or errors. `dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts` passes 203 assertions, including prior functional regressions and checks of the supplied header artwork in both themes and the embedded window icon. The extracted 32-pixel icon from the executable matches the supplied ICO pixel for pixel. SHA-256 comparisons confirm all 24 copied artwork files are unchanged from the source folder.

Rendered WinForms images were inspected for both themes, missing/invalid models, preference recovery, minimum-width layout, and synthetic 150% DPI. Corrected default-size scrolling and preference-panel corner clipping during review. The harness also checks return to 100% without accumulated font scaling. Temporary fixtures are not real translation models and never modify installed user models.

Physical monitor transitions, native folder-picker interaction, and manual hotkey presses from other apps were not verified. These are distinct from the automated native hotkey registration tests and rendered UI checks. Previews use WinForms DrawToBitmap, not physical-desktop screenshots.

## Review judgment

The result is a cohesive desktop utility: the icon, action color and selected states agree, the language pair is easy to scan, and grouping preferences reduces competing boxes. Both themes retain readable text and explicit setup errors. The restrained layout avoids gradients, decorative illustrations and oversized headings.

Accepted limitations: the Windows title bar and scrollbars retain system rendering, which can look lighter than dark content. Model management needs vertical scrolling at the default window height. The translation runtime is still absent in this build; the status explicitly says so and never treats discovery fixtures as working translation engines.

Representative generated previews (under the ignored `Tests/Artifacts` folder):

- `redesign-general-light.png`
- `main-dark-missing.png`
- `main-runtime-unavailable.png`
- `redesign-models-light.png`
- `redesign-settings-error.png`
- `scaled-150-percent.png`
