param(
    [string]$Configuration = "Release",
    [string]$RuntimeIdentifier = "win-x64",
    [switch]$IncludeWhisperModel,
    [switch]$SkipInno,
    [switch]$SkipPublish,
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot "src\WindowsAiAssistant.App\WindowsAiAssistant.App.csproj"
$publishDir = Join-Path $repoRoot "dist\publish"
$installerDir = Join-Path $repoRoot "dist\installer"
$iconScript = Join-Path $repoRoot "scripts\generate-app-icons.ps1"
$issFile = Join-Path $repoRoot "installer\WindowsAiAssistant.iss"

Write-Host "==> Uygulama ikonlari olusturuluyor..."
& powershell -NoProfile -ExecutionPolicy Bypass -File $iconScript -ProjectDir (Split-Path $project -Parent)

if (-not $SkipPublish) {
    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    Write-Host "==> dotnet publish ($Configuration, $RuntimeIdentifier, self-contained)..."
    $publishArgs = @(
        "publish", $project,
        "-c", $Configuration,
        "-r", $RuntimeIdentifier,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:PublishReadyToRun=false",
        "-p:WindowsAppSDKSelfContained=true",
        "-o", $publishDir
    )

    if (-not $IncludeWhisperModel) {
        $publishArgs += "-p:SkipWhisperModelOnPublish=true"
    }

    dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish basarisiz."
    }
}

if (-not (Test-Path (Join-Path $publishDir "WindowsAiAssistant.App.exe"))) {
    throw "Publish ciktisi bulunamadi: $publishDir"
}

$zipPath = Join-Path $repoRoot "dist\WindowsAiAssistant-$Configuration-$RuntimeIdentifier.zip"
if (-not $SkipZip) {
    Write-Host "==> ZIP paketi olusturuluyor: $zipPath"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
} else {
    Write-Host "==> ZIP atlandi (-SkipZip)."
}

if ($SkipInno) {
    if (-not $SkipZip) {
        Write-Host "Kurulum tamamlandi (Inno Setup atlandi). ZIP: $zipPath"
    } else {
        Write-Host "Publish tamamlandi (Inno Setup atlandi). Cikti: $publishDir"
    }
    exit 0
}

$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    Write-Warning "Inno Setup 6 bulunamadi. Yalnizca ZIP olusturuldu: $zipPath"
    Write-Warning "Inno Setup kurduktan sonra tekrar calistirin: .\scripts\build-setup.ps1"
    exit 0
}

New-Item -ItemType Directory -Force -Path $installerDir | Out-Null
Write-Host "==> Inno Setup derleniyor..."
& $iscc $issFile
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup derlemesi basarisiz."
}

$setupExe = Get-ChildItem $installerDir -Filter "WindowsAiAssistant-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ""
Write-Host "Kurulum basariyla hazirlandi."
Write-Host "  Setup : $($setupExe.FullName)"
if (-not $SkipZip) {
    Write-Host "  ZIP   : $zipPath"
}
