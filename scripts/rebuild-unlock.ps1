# Antivirus DLL engeli veya kilit sonrasi temiz build.
# Agent.dll elle olusturulmaz; dotnet build kaynak koddan uretir.
param(
    [string]$Configuration = 'Debug',
    [switch]$UseAvSafeOutput
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Write-Host "Calisan uygulama sonlandiriliyor..."
taskkill /F /IM WindowsAiAssistant.App.exe /T 2>$null | Out-Null

Write-Host "Dotnet build sunucusu kapatiliyor..."
dotnet build-server shutdown 2>$null | Out-Null

if ($UseAvSafeOutput) {
    $env:ANTIVIRUS_SAFE_BUILD = '1'
    Write-Host "ANTIVIRUS_SAFE_BUILD=1 (cikti: $env:LOCALAPPDATA\WindowsAiAssistant\msbuild\)"
}
else {
    # Bitdefender Desktop/bin altinda .dll yazimini engelliyorsa otomatik guvenli moda gec.
    $probeDir = Join-Path $root 'src\WindowsAiAssistant.Agent\bin\Debug\net8.0-windows10.0.19041.0'
    $probeDll = Join-Path $probeDir 'av-probe.dll'
    New-Item -ItemType Directory -Path $probeDir -Force -ErrorAction SilentlyContinue | Out-Null
    $blocked = $false
    try {
        [IO.File]::WriteAllBytes($probeDll, [byte[]](0x4D, 0x5A))
        Remove-Item -LiteralPath $probeDll -Force -ErrorAction SilentlyContinue
    }
    catch {
        $blocked = $true
    }

    if ($blocked) {
        $env:ANTIVIRUS_SAFE_BUILD = '1'
        Write-Host "Antivirus .dll yazimini engelliyor; ANTIVIRUS_SAFE_BUILD=1 otomatik acildi."
        Write-Host "Kalici cozum: Bitdefender'da $root klasorunu istisnaya alin."
    }
}

Write-Host "Yeniden derleniyor..."
Push-Location (Join-Path $root 'src\WindowsAiAssistant.App')
try {
    dotnet build -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Build basarisiz. Antivirus guvenli modu zorla:" -ForegroundColor Yellow
        Write-Host "  `$env:ANTIVIRUS_SAFE_BUILD='1'; dotnet build" -ForegroundColor Yellow
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

if ($env:ANTIVIRUS_SAFE_BUILD -eq '1') {
    Write-Host "Cikti konumu: $env:LOCALAPPDATA\WindowsAiAssistant\msbuild\out\"
    Write-Host "Calistirmak icin: cd src\WindowsAiAssistant.App; `$env:ANTIVIRUS_SAFE_BUILD='1'; dotnet run"
}

Write-Host "Tamam."
