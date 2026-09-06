# Continuous integration

The repository-level `.github/workflows/ci.yml` runs on every push (all branches
and tags), and when a pull request is opened, reopened, or updated. It also
supports manual dispatch. There are no branch/path filters or concurrency
cancellation rules that would skip a newer push's run. A push tests its tip;
a pull request tests GitHub's proposed merge with the base branch. Commits that
exist only locally do not trigger GitHub Actions.

## Architecture and test entry point

The application lives in `screen-translate/`, one directory below the Git root.
It targets `net10.0-windows`, Windows Forms, and x64. `Program.cs` launches
`MainForm`; partial form files handle layout, interface preferences, and the two
language selectors. `Settings/` persists independent preferences; `Ocr/` handles
discovery and native Tesseract model validation through `IOcrEngine`;
`Translation/` discovers directional Argos packages; `Interface/` contains
readiness, hotkeys, lifetime management, and themed controls.

`Tests/ScreenTranslate.Tests.csproj` is an executable with a project reference to
the application. Its STA entry point runs assertions, native hotkey registration
checks, and real WinForms message loops, renders controls with `DrawToBitmap`,
and exits nonzero on failure. It is **not** a VSTest/xUnit project: `dotnet test`
does not run this acceptance suite.

Capture, OCR recognition, translation inference, and translated overlays are
still future runtime work. Passing this suite verifies the implemented features,
not the complete Version 1.0 translation workflow.

## CI checks

Both Debug and Release run independently on `windows-2025` x64, with a 20-minute
job timeout and `fail-fast: false`. This Windows Server runner supports the
native tests but is not evidence of physical Windows 10/11 desktop coverage.
`setup-dotnet` installs .NET 10; the application's `global.json` restricts SDK
selection to stable .NET 10 feature bands.

Each job invokes `scripts/Invoke-CI.ps1`, which:

1. Restores and builds the harness and its application project reference.
2. Downloads the Apache-2.0 English `tessdata_fast` 4.1.0 test model and verifies
   its SHA-256 against the value in `THIRD-PARTY-NOTICES.md`. A local rerun reuses
   the file only if its checksum matches; delete a mismatched test file to retry
   the download.
3. Sets `SCREEN_TRANSLATE_TEST_TESSDATA` for genuine native model-loading tests.
   The harness copies this model into temporary fixtures before mutation tests.
4. Runs the acceptance executable using `dotnet run`, without rebuilding.
   Nonzero exit codes, skipped tests, and missing passing summaries fail CI.
   Expected native error messages from invalid-model tests do not themselves
   indicate failure.
5. Restores the caller's OCR-test environment variable and working directory.

The workflow uploads only logs and rendered PNGs, even after failure, with
14-day retention. Downloaded model weights, user settings, and installed models
are not uploaded. All test artifacts live in the ignored
`Tests/Artifacts/ci/<configuration>/` directory. Network access is used for CI
setup, NuGet restore, and the test-model download; application behavior remains
offline-first. Translation packages in tests are discovery fixtures only.

The workflow uses read-only repository permissions, disables persistent checkout
credentials, pins actions to commit SHAs, and needs no repository secrets. It
uses `pull_request`, so fork contributions can be tested without privileged
`pull_request_target` execution.

## Reproduce locally

Run from the application directory in x64 PowerShell 7 on Windows with .NET 10
and the native Visual C++ runtime required by Tesseract:

```powershell
./scripts/Invoke-CI.ps1 -Configuration Debug
./scripts/Invoke-CI.ps1 -Configuration Release
```

The original developer command remains available:

```powershell
dotnet build
dotnet run --project Tests/ScreenTranslate.Tests.csproj -- Tests/Artifacts
```

Unlike the CI script, that bare harness command skips genuine-model success
tests unless `SCREEN_TRANSLATE_TEST_TESSDATA` is set. See
[source language verification](source-language-1.2.md) for manual model setup.

To require CI before merging, configure the repository's branch protection or
ruleset to require `Windows x64 (Debug)` and `Windows x64 (Release)` after their
first GitHub run. Defining a workflow alone does not enforce merge protection.

## Initial local verification

Verified locally on 2026-09-06, Windows x64, .NET SDK 10.0.400:

- **Automated:** the exact CI script passed in both Debug and Release: 293
  assertions per configuration, zero skipped tests, and zero build warnings or
  errors. Both runs downloaded and checksum-verified the genuine English model.
  Actionlint 1.7.12 and PowerShell syntax validation passed. An isolated script
  probe confirmed success plus rejection of SDK-info, restore, build, and
  harness exit failures, skipped tests, missing summaries, corrupt test data,
  and download failures. All nine probe scenarios preserved the caller's
  working directory and OCR-test environment setting. Probe commands used
  stubs; they verify CI error handling, not application behavior.
- **Rendered UI:** the harness generated PNGs and passed layout assertions in
  both configurations. Inspected the Release genuine-model success rendering
  and corrupt-model error rendering at synthetic 150% DPI.
- **Physical desktop:** not performed. Physical keypress delivery, monitor
  movement, region capture, and translated overlays are not verified by CI.
- **GitHub-hosted execution:** not run from this local change. Event delivery,
  runner provisioning, and artifact upload remain to be confirmed after the
  workflow is pushed to GitHub.

Local build/harness evidence is under `Tests/Artifacts/ci/Debug/` and
`Tests/Artifacts/ci/Release/`; the failure-probe report is
`Tests/Artifacts/ci-script-verification.log`.

Workflow behavior follows GitHub's [workflow syntax reference](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax),
the official [setup-dotnet action](https://github.com/actions/setup-dotnet), and
[upload-artifact action](https://github.com/actions/upload-artifact).

## Hosted desktop-height failure and correction

The first [master run](https://github.com/Jccqt/screen-translate/actions/runs/34029898733)
built successfully and passed native OCR checks in both configurations, then
failed `Everyday settings need no scrolling at the default window size`.
Windows can constrain the launch window to a smaller desktop working area;
the test incorrectly required no scrolling even when the viewport was shorter
than the intended design height.

Reproduced that exact failure locally on master with a 788-pixel outer window
height at 96 DPI. The test now retains the intended-height content-fit check
and allows scrolling only below that viewport height. It exercises both launch
and constrained sizes, and logs window, client, viewport, content, and DPI values.
The application and workflow behavior are unchanged.

- **Automated:** `dotnet build` and both CI configurations passed with zero
  warnings/errors; 305 assertions passed per configuration with no skips.
  The constrained-size regression failed before the assertion correction.
- **Rendered UI:** inspected `desktop-constrained-launch.png` from Release;
  language, shortcut, and appearance controls remain visible and the shorter
  viewport scrolls for the remaining content.
- **Physical desktop:** not tested; the regression resizes the test form and
  does not change the user's display configuration.
- **Hosted evidence:** the same test correction in commit `3046f06` on
  `codex/update-target-language-1.3` already passed both configurations and
  artifact uploads in [GitHub Actions](https://github.com/Jccqt/screen-translate/actions/runs/34039460584).
  Its application into the local master checkout still needs to be committed
  and pushed before master receives a new hosted result.
