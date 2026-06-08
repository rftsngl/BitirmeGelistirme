param(
    [string]$ProjectDir = (Join-Path $PSScriptRoot "..\src\WindowsAiAssistant.App"),
    [string]$SourceImage = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$assetsDir = Join-Path $ProjectDir "Assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

if ([string]::IsNullOrWhiteSpace($SourceImage)) {
    $SourceImage = Join-Path $assetsDir "app-logo-source.png"
}

if (-not (Test-Path $SourceImage)) {
    throw "Logo kaynagi bulunamadi: $SourceImage"
}

function New-SquareBitmap([System.Drawing.Image]$source, [int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.Clear([System.Drawing.Color]::FromArgb(26, 26, 46))
        $graphics.DrawImage($source, 0, 0, $size, $size)
        return $bitmap
    }
    finally {
        $graphics.Dispose()
    }
}

function Save-Png([System.Drawing.Image]$image, [string]$path, [int]$size) {
    $bitmap = New-SquareBitmap $image $size
    try {
        $bitmap.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally {
        $bitmap.Dispose()
    }
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeIcon {
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern bool DestroyIcon(IntPtr handle);
}
"@

function Save-Ico([System.Drawing.Image]$image, [string]$path) {
    $bitmap = New-SquareBitmap $image 256
    try {
        $handle = $bitmap.GetHicon()
        try {
            $icon = [System.Drawing.Icon]::FromHandle($handle)
            $saved = New-Object System.Drawing.Icon $icon, 256, 256
            $stream = [System.IO.File]::Create($path)
            try {
                $saved.Save($stream)
            }
            finally {
                $stream.Dispose()
                $saved.Dispose()
            }
        }
        finally {
            [NativeIcon]::DestroyIcon($handle) | Out-Null
        }
    }
    finally {
        $bitmap.Dispose()
    }
}

$source = [System.Drawing.Image]::FromFile((Resolve-Path $SourceImage))
try {
    Save-Png $source (Join-Path $assetsDir "Square44x44Logo.scale-200.png") 88
    Save-Png $source (Join-Path $assetsDir "Square150x150Logo.scale-200.png") 300
    Save-Png $source (Join-Path $assetsDir "StoreLogo.png") 50
    Save-Png $source (Join-Path $assetsDir "SplashScreen.scale-200.png") 620
    Save-Png $source (Join-Path $assetsDir "Wide310x150Logo.scale-200.png") 620
    Save-Ico $source (Join-Path $assetsDir "AppIcon.ico")
}
finally {
    $source.Dispose()
}

Write-Host "Uygulama ikonlari olusturuldu: $assetsDir"
