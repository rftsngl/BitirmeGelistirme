# Windows AI Assistant - release script
#
# Adimlar:
#   Test    - dotnet test + Release build
#   Package - publish + Inno Setup (.exe)
#   GitHub  - git tag + gh release create
#   All     - sirayla hepsi
#
# Ornekler:
#   .\scripts\release.ps1 -Step Test
#   .\scripts\release.ps1 -Step Package
#   .\scripts\release.ps1 -Step GitHub -PushMain
#   .\scripts\release.ps1 -Step All -Yes -PushMain

param(
    [ValidateSet("Test", "Package", "GitHub", "All")]
    [string]$Step = "All",

    [switch]$IncludeWhisperModel,
    [switch]$SkipZip,
    [switch]$PushMain,
    [switch]$ForceTag,
    [switch]$Yes
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot "src\WindowsAiAssistant.App\WindowsAiAssistant.App.csproj"
$versionFile = Join-Path $repoRoot "Directory.Build.props"
$installerDir = Join-Path $repoRoot "dist\installer"
$buildSetup = Join-Path $repoRoot "scripts\build-setup.ps1"

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-ProjectVersion {
    if (-not (Test-Path $versionFile)) {
        throw "Directory.Build.props bulunamadi: $versionFile"
    }

    $content = Get-Content $versionFile -Raw
    if ($content -match "<Version>([^<]+)</Version>") {
        return $Matches[1].Trim()
    }

    throw "Directory.Build.props icinde <Version> bulunamadi."
}

function Invoke-ReleaseTest {
    Write-Step "Adim 1/3 - Test ve Release build"

    Push-Location $repoRoot
    try {
        dotnet test
        if ($LASTEXITCODE -ne 0) { throw "dotnet test basarisiz (cikis: $LASTEXITCODE)." }

        dotnet build $project -c Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet build basarisiz (cikis: $LASTEXITCODE)." }
    }
    finally {
        Pop-Location
    }

    Write-Host "Test ve build tamam." -ForegroundColor Green
}

function Invoke-ReleasePackage {
    Write-Step "Adim 2/3 - Publish ve kurulum paketi"

    if (-not (Test-Path $buildSetup)) {
        throw "build-setup.ps1 bulunamadi: $buildSetup"
    }

    $packageArgs = @{}
    if ($IncludeWhisperModel) { $packageArgs.IncludeWhisperModel = $true }
    if ($SkipZip) { $packageArgs.SkipZip = $true }

    & $buildSetup @packageArgs
    if ($LASTEXITCODE -ne 0) { throw "Paketleme basarisiz." }

    $setup = Get-SetupExe
    Write-Host "Kurulum dosyasi hazir: $($setup.FullName)" -ForegroundColor Green
    return $setup
}

function Get-SetupExe {
    $setup = Get-ChildItem $installerDir -Filter "WindowsAiAssistant-Setup-*.exe" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $setup) {
        throw "Setup exe bulunamadi. Once -Step Package calistirin. Beklenen konum: $installerDir"
    }

    return $setup
}

function Invoke-ReleaseGitHub {
    param([string]$Version)

    Write-Step "Adim 3/3 - GitHub Release"

    $tag = "v$Version"
    $setup = Get-SetupExe
    $notesFile = Join-Path $repoRoot ".github\release-notes\v$Version.md"

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) bulunamadi. Kurun: https://cli.github.com/"
    }

    gh auth status 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "gh oturumu acik degil. Calistirin: gh auth login"
    }

    Push-Location $repoRoot
    try {
        if ($PushMain) {
            Write-Host "main dalina push ediliyor..."
            git push origin main
            if ($LASTEXITCODE -ne 0) { throw "git push origin main basarisiz." }
        }

        $tagExists = git tag -l $tag
        if ($tagExists -and -not $ForceTag) {
            throw "Tag '$tag' zaten var. Yeniden olusturmak icin -ForceTag kullanin."
        }
        if ($tagExists -and $ForceTag) {
            Write-Warning "Mevcut tag siliniyor: $tag"
            git tag -d $tag 2>$null
            git push origin ":refs/tags/$tag" 2>$null
        }

        git tag $tag
        if ($LASTEXITCODE -ne 0) { throw "git tag basarisiz." }

        git push origin $tag
        if ($LASTEXITCODE -ne 0) { throw "git push origin $tag basarisiz." }

        $ghArgs = @(
            "release", "create", $tag,
            $setup.FullName,
            "--title", "Windows AI Assistant $Version"
        )

        if (Test-Path $notesFile) {
            $ghArgs += "--notes-file"
            $ghArgs += $notesFile
        }
        else {
            Write-Warning "Release notlari bulunamadi: $notesFile - CHANGELOG.md kullaniliyor."
            $changelog = Join-Path $repoRoot "CHANGELOG.md"
            if (Test-Path $changelog) {
                $ghArgs += "--notes-file"
                $ghArgs += $changelog
            }
        }

        & gh @ghArgs
        if ($LASTEXITCODE -ne 0) { throw "gh release create basarisiz." }
    }
    finally {
        Pop-Location
    }

    Write-Host "GitHub Release olusturuldu: $tag" -ForegroundColor Green
}

$version = Get-ProjectVersion
Write-Host "Windows AI Assistant release - surum $version - adim: $Step"

switch ($Step) {
    "Test" {
        Invoke-ReleaseTest
    }
    "Package" {
        Invoke-ReleasePackage | Out-Null
    }
    "GitHub" {
        Invoke-ReleaseGitHub -Version $version
    }
    "All" {
        Invoke-ReleaseTest
        Invoke-ReleasePackage | Out-Null

        $doGitHub = $Yes
        if (-not $doGitHub) {
            Write-Host ""
            $answer = Read-Host "GitHub Release olusturulsun mu? (E/H)"
            $doGitHub = $answer -match "^[Ee]"
        }

        if ($doGitHub) {
            Invoke-ReleaseGitHub -Version $version
        }
        else {
            Write-Host "GitHub adimi atlandi. Manuel: .\scripts\release.ps1 -Step GitHub -PushMain"
        }
    }
}

Write-Host ""
Write-Host "Bitti." -ForegroundColor Green
