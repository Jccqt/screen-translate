#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
# Native Tesseract deliberately writes errors for invalid-model test cases.
# Check process exit codes explicitly instead of treating stderr as failure.
$PSNativeCommandUseErrorActionPreference = $false

if (-not $IsWindows -or -not [Environment]::Is64BitProcess) {
    throw 'CI requires Windows and x64 PowerShell with the .NET 10 SDK.'
}

$projectDirectory = Split-Path -Parent $PSScriptRoot
$artifactDirectory = Join-Path $projectDirectory "Tests/Artifacts/ci/$Configuration"
$modelDirectory = Join-Path $artifactDirectory 'ocr-validation-data'
$previousOcrData = $env:SCREEN_TRANSLATE_TEST_TESSDATA

Push-Location $projectDirectory
try {
    New-Item -ItemType Directory -Force $modelDirectory | Out-Null

    dotnet --info 2>&1 | Tee-Object -FilePath "$artifactDirectory/dotnet-info.log"
    if ($LASTEXITCODE -ne 0) { throw "dotnet --info failed ($LASTEXITCODE)." }

    # Restoring/building the harness also restores/builds its application reference.
    dotnet restore Tests/ScreenTranslate.Tests.csproj 2>&1 |
        Tee-Object -FilePath "$artifactDirectory/restore.log"
    if ($LASTEXITCODE -ne 0) { throw "Restore failed ($LASTEXITCODE)." }

    dotnet build Tests/ScreenTranslate.Tests.csproj --configuration $Configuration --no-restore 2>&1 |
        Tee-Object -FilePath "$artifactDirectory/build.log"
    if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

    # Apache-2.0 test data only; never bundled or uploaded as an artifact.
    # See THIRD-PARTY-NOTICES.md. Existing local test data must pass the same hash check.
    $modelPath = Join-Path $modelDirectory 'eng.traineddata'
    if (-not (Test-Path -LiteralPath $modelPath)) {
        Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/tesseract-ocr/tessdata_fast/4.1.0/eng.traineddata' `
            -OutFile $modelPath -MaximumRetryCount 3 -RetryIntervalSec 5 -TimeoutSec 120
    }
    $expectedHash = '7d4322bd2a7749724879683fc3912cb542f19906c83bcc1a52132556427170b2'
    if ((Get-FileHash -LiteralPath $modelPath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw "OCR test model checksum mismatch. Remove '$modelPath' and retry."
    }
    $env:SCREEN_TRANSLATE_TEST_TESSDATA = $modelDirectory

    # This is a custom executable harness, not a dotnet test / VSTest project.
    dotnet run --project Tests/ScreenTranslate.Tests.csproj --configuration $Configuration --no-build --no-restore -- $artifactDirectory 2>&1 |
        Tee-Object -FilePath "$artifactDirectory/acceptance.log"
    if ($LASTEXITCODE -ne 0) { throw "Acceptance harness failed ($LASTEXITCODE)." }

    $log = Get-Content -LiteralPath "$artifactDirectory/acceptance.log" -Raw
    if ($log -match '(?m)^SKIP:' -or $log -notmatch '(?m)^PASS: [1-9][0-9]* assertions,') {
        throw 'Acceptance harness must report passing assertions with no skipped tests.'
    }
}
finally {
    $env:SCREEN_TRANSLATE_TEST_TESSDATA = $previousOcrData
    Pop-Location
}
