# Requirement 1.6 - Global Shortcut

The General page captures Ctrl or Alt with a letter, top-row number, or F1-F24
(except F12); Shift is optional. Escape cancels an edit and explains that it is
reserved for cancellation and dismissal. Windows-key chords, unmodified keys,
unsupported keys, and Windows-reserved F12 are rejected with an explanation.
Tab and Shift+Tab retain navigation behavior.

`GlobalShortcut` owns a private Windows message window and registers with
`MOD_NOREPEAT`. Replacement registers a second ID before releasing the first.
Messages must match both the active registration ID and its key/modifier data;
queued messages for a different previous shortcut cannot trigger new work.
The optional save callback runs only after successful registration. If saving
fails, the candidate is unregistered and the previous registration remains live.
The interface updates its configured shortcut only after both steps succeed.
Startup failures leave General and its shortcut editor visible; invalid edits,
conflicts, and write failures explain whether a previous shortcut is still active.
Language/model settings and appearance are preserved.

`TranslationRequestGate` drops overlapping requests without cancellation or a
queue. Both the global shortcut and `ShowOcrResultAsync` use it. During a workflow,
selection feedback remains with the selector and OCR/translation stages show an
owned, topmost progress window with Cancel and Escape. Cancellation retains the
guard until the operation returns. Late progress callbacks are ignored, and exit
cancels work and disposes owned windows. An accepted new request disposes existing
result windows before starting selection. An unavailable workflow leaves the
existing result intact and opens setup. Failures remain visible across model
refreshes until the next shortcut attempt.

The translation processor waits for the actual engine task after cancellation.
An engine that ignores cancellation leaves "Cancelling..." visible and continues
to hold the request guard; its eventual result is discarded. Only a fresh press
after it finishes can start another operation.

Drawing resources remain alive until all owned windows and main controls are
disposed, so shutdown-triggered repaints cannot use already-disposed fonts.

## Runtime Boundary

`ITranslationWorkflow` is the integration contract for selection, capture, OCR,
and translation. It receives a snapshot of both languages and model directories,
the owning form, a progress reporter, and cancellation. Implementations must
validate actual engine compatibility and own/dispose capture resources. A null
result represents cancelled selection.

There is no production region-selection/capture workflow or translation runtime
in this build. The default application retains its explicit unavailable message.
The acceptance harness supplies a controlled workflow to verify shortcut
dispatch, busy-state behavior, cancellation, and result replacement; this does
not verify real OCR or translation. End-to-end acceptance of those behaviors
remains pending the production workflow and physical capture/overlay checks.

## Verification

Run `dotnet build` and
`dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts`.
The final build passed with zero warnings/errors, and the harness passed 817
assertions. Unhandled UI exceptions now fail the harness instead of opening a
blocking runtime dialog.
The harness uses temporary preferences and discovery fixtures. Native checks
cover registration, conflict rollback, save rollback, replacement, and disposal.
Controlled workflow checks cover all three busy stages, no queued replay,
cancellation (including an engine that ignores it), failure recovery, shutdown,
and replacing a visible result.

Rendered evidence is in `Tests/Artifacts/shortcut-*.png`, including startup
failure, reserved-key errors, conflict feedback at simulated 150% DPI, and
translation progress. These are renders, not physical multi-monitor evidence.

For an isolated desktop session, run
`dotnet run --project Tests/ScreenTranslate.Tests.csproj -- --shortcut-preview`.
It registers Ctrl+Alt+F9 with real Windows APIs and uses temporary settings and
discovery fixtures. Pressing it from another ordinary application opens General
with the unavailable-runtime explanation. Close the main window to clean up.

Physical desktop verification on Windows 11 confirmed native Ctrl+Alt+F9 delivery
from an active Notepad window in the initial session and from GitHub Desktop on
the resumed final build (2026-09-11): Screen Translate came forward and switched
from Models to General. The final build also rejected Ctrl+F12 while preserving
Ctrl+Alt+F9, restored the configured chord on Escape, and supported Tab followed
by Enter to apply through the keyboard. The isolated preview closed successfully
and released its registration. Live selection, processing, and
overlay replacement cannot yet be physically verified without the runtime.
Screen geometry and monitor placement are not changed here; real multi-monitor
and mixed-DPI capture/overlay acceptance must accompany that implementation.
Optional genuine OCR checks require `SCREEN_TRANSLATE_TEST_TESSDATA` and are not
established by the fixtures used for this requirement.
