# Build-time helper: ensures ggml-medium.bin exists under the App project.
# Antivirus tools may quarantine this file; re-copy from repo or re-run this script.
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = 'Stop'

$modelDir = Join-Path $ProjectDir 'Assets\Models\whisper'
$modelPath = Join-Path $modelDir 'ggml-medium.bin'
$tempPath = "$modelPath.download"
$url = 'https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-medium.bin'
$minValidBytes = 500MB

function Get-SizeMb([long]$bytes) {
    [math]::Round($bytes / 1MB, 1)
}

if (Test-Path -LiteralPath $modelPath) {
    $size = (Get-Item -LiteralPath $modelPath).Length
    if ($size -ge $minValidBytes) {
        Write-Host "Whisper medium model mevcut ($(Get-SizeMb $size) MB): $modelPath"
        exit 0
    }

    Write-Warning "Eksik veya bozuk Whisper modeli yeniden indirilecek."
    Remove-Item -LiteralPath $modelPath -Force
}

if (-not (Test-Path -LiteralPath $modelDir)) {
    New-Item -ItemType Directory -Path $modelDir -Force | Out-Null
}

if (Test-Path -LiteralPath $tempPath) {
    Remove-Item -LiteralPath $tempPath -Force
}

Write-Host "Whisper medium model indiriliyor (~1,5 GB)..."
Write-Host "Kaynak: $url"

try {
    # curl.exe ships with Windows 10+ and is less likely to be blocked than custom download code.
    $curl = Get-Command curl.exe -ErrorAction Stop
    & $curl.Source -fL --retry 3 --retry-delay 5 -o $tempPath $url
    if ($LASTEXITCODE -ne 0) {
        throw "curl cikis kodu: $LASTEXITCODE"
    }

    $size = (Get-Item -LiteralPath $tempPath).Length
    if ($size -lt $minValidBytes) {
        throw "Indirilen dosya beklenenden kucuk ($size bayt)."
    }

    Move-Item -LiteralPath $tempPath -Destination $modelPath -Force
    Write-Host "Whisper medium model indirildi ($(Get-SizeMb $size) MB): $modelPath"
    exit 0
}
catch {
    if (Test-Path -LiteralPath $tempPath) {
        Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
    }

    Write-Error @"
Whisper modeli hazirlanamadi: $_

Model zaten varsa build devam edebilir. Antivirus (ornegin Bitdefender) bu scripti veya .bin dosyasini engellemis olabilir.
Cozum: Bitdefender'da proje klasorunu istisnaya alin veya modeli elle koyun:
  $modelPath
"@
    exit 1
}
