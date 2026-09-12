# Requirement 1.7 - Screen-Region Capture

The production hotkey workflow now validates the current OCR/translation runtime
and Windows display configuration before it opens selection. Missing or unreadable
OCR data, an unavailable translation runtime, unusable displays, and known model
problems remain on the retryable readiness surface instead of opening a selector.

`ScreenRegionCaptureService` reads the entire Windows virtual desktop in physical
pixels, including negative coordinates for monitors left of or above the primary
display. It records each monitor's bounds and effective DPI. Any bounds, monitor,
or DPI change between snapshot and confirmation cancels capture with a retryable
message. A timer and `WM_DISPLAYCHANGE` cover changes while selection is active.

The private desktop bitmap is taken before the selector window exists. Existing
result forms opt into Windows capture exclusion and are disposed by the shared
request gate first. Capture then yields for a short composition interval and uses
`DwmFlush` before `CopyFromScreen`. The user therefore selects from a frozen,
in-memory desktop image and neither the selection border nor a previous result
overlay can enter the bitmap passed to OCR. No screenshot or recognized text is
written to disk.

The borderless, topmost selector spans the virtual desktop. Dragging works in all
directions, continuously shows a normalized rectangle and physical desktop bounds,
and clamps pointer movement to the desktop. A click or collapsed drag stays in
selection and does not return pixels. Escape (and right-click) cancels without OCR.
Only a confirmed positive-area crop reaches `IOcrRecognizer`; capture pixels are
disposed as soon as recognition/translation finishes, fails, or is cancelled.

Tesseract recognition runs away from the UI thread, holds the configured model-use
lease until native work actually ends, and returns word bounds and confidence when
available. Source and target languages that are identical complete through the
existing no-model result path. Other directions remain blocked before capture until
the offline translation engine is available.

## Verification

Run `dotnet build` and
`dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts`.
The harness covers reverse and zero-area drags, live bounds, Escape, negative virtual
coordinates, mixed-DPI topology changes, snapshot disposal, capture-error feedback,
configuration-before-capture ordering, OCR dispatch count, and captured-pixel
cleanup. `Tests/Artifacts/screen-region-selection.png` is rendered selector evidence.

For an isolated physical-desktop check, run
`dotnet run --project Tests/ScreenTranslate.Tests.csproj -- --capture-preview`.
It registers Ctrl+Alt+F7 with temporary settings. The selector and GDI capture are
production components; OCR text is an explicit fixture, so this preview does not
verify Tesseract accuracy or translation. Check reverse drags, Escape, monitor
boundaries, spanning selections, and DPI transitions, then close the main window.

Secure desktops and capture-protected content remain unsupported. Windows does not
always expose protection as an error; detectable GDI failures are explained, while
protected surfaces may be returned by Windows as blank pixels.
