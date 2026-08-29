[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\..\src\CodexHp.App\Assets')
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.Drawing.Common

function New-RoundedRectanglePath {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.RectangleF]$Bounds,

        [Parameter(Mandatory)]
        [single]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($Bounds.Left, $Bounds.Top, $diameter, $diameter, 180, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Top, $diameter, $diameter, 270, 90)
    $path.AddArc($Bounds.Right - $diameter, $Bounds.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Bounds.Left, $Bounds.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-ScaledPngFrame {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Bitmap]$Master,

        [Parameter(Mandatory)]
        [int]$Size
    )

    $frame = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($frame)
    try {
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
        $graphics.DrawImage($Master, 0, 0, $Size, $Size)

        $memory = [System.IO.MemoryStream]::new()
        try {
            $frame.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
            return $memory.ToArray()
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $graphics.Dispose()
        $frame.Dispose()
    }
}

function Write-MultiSizeIcon {
    param(
        [Parameter(Mandatory)]
        [System.Drawing.Bitmap]$Master,

        [Parameter(Mandatory)]
        [string]$Path
    )

    $sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
    $frames = @(
        foreach ($size in $sizes) {
            [pscustomobject]@{
                Size = $size
                Data = New-ScaledPngFrame -Master $Master -Size $size
            }
        }
    )

    $fileStream = [System.IO.File]::Create($Path)
    $writer = [System.IO.BinaryWriter]::new($fileStream)
    try {
        $writer.Write([uint16]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]$frames.Count)

        $offset = 6 + (16 * $frames.Count)
        foreach ($frame in $frames) {
            $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
            $writer.Write([byte]$dimension)
            $writer.Write([byte]$dimension)
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([uint16]1)
            $writer.Write([uint16]32)
            $writer.Write([uint32]$frame.Data.Length)
            $writer.Write([uint32]$offset)
            $offset += $frame.Data.Length
        }

        foreach ($frame in $frames) {
            $writer.Write([byte[]]$frame.Data)
        }
    }
    finally {
        $writer.Dispose()
        $fileStream.Dispose()
    }
}

$package = Get-AppxPackage -Name 'OpenAI.Codex' |
    Sort-Object -Property Version -Descending |
    Select-Object -First 1
if ($null -eq $package) {
    throw 'The installed OpenAI.Codex Windows package was not found.'
}

$sourcePath = Join-Path `
    $package.InstallLocation `
    'assets\Square44x44Logo.targetsize-256_altform-unplated.png'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
    throw "The official 256px Codex icon asset was not found: $sourcePath"
}

$resolvedOutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
[System.IO.Directory]::CreateDirectory($resolvedOutputDirectory) | Out-Null
$pngPath = Join-Path $resolvedOutputDirectory 'CodexHp.png'
$icoPath = Join-Path $resolvedOutputDirectory 'CodexHp.ico'

$source = [System.Drawing.Bitmap]::new($sourcePath)
$master = [System.Drawing.Bitmap]::new(
    256,
    256,
    [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [System.Drawing.Graphics]::FromImage($master)
try {
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    $platePath = New-RoundedRectanglePath `
        -Bounds ([System.Drawing.RectangleF]::new(8, 8, 240, 240)) `
        -Radius 48
    $plateBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#18181C'))
    try {
        $graphics.FillPath($plateBrush, $platePath)
    }
    finally {
        $plateBrush.Dispose()
        $platePath.Dispose()
    }

    $graphics.DrawImage(
        $source,
        [System.Drawing.Rectangle]::new(16, -6, 224, 224),
        0,
        0,
        $source.Width,
        $source.Height,
        [System.Drawing.GraphicsUnit]::Pixel)

    $gaugeBorderPath = New-RoundedRectanglePath `
        -Bounds ([System.Drawing.RectangleF]::new(22, 199, 212, 38)) `
        -Radius 10
    $gaugeBorderBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    try {
        $graphics.FillPath($gaugeBorderBrush, $gaugeBorderPath)
    }
    finally {
        $gaugeBorderBrush.Dispose()
        $gaugeBorderPath.Dispose()
    }

    $gaugeTrackPath = New-RoundedRectanglePath `
        -Bounds ([System.Drawing.RectangleF]::new(27, 204, 202, 28)) `
        -Radius 6
    $gaugeTrackBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#3E3E44'))
    try {
        $graphics.FillPath($gaugeTrackBrush, $gaugeTrackPath)
    }
    finally {
        $gaugeTrackBrush.Dispose()
        $gaugeTrackPath.Dispose()
    }

    $gaugeFillPath = New-RoundedRectanglePath `
        -Bounds ([System.Drawing.RectangleF]::new(27, 204, 170, 28)) `
        -Radius 6
    $gaugeFillBrush = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#DC4856'))
    try {
        $graphics.FillPath($gaugeFillBrush, $gaugeFillPath)
    }
    finally {
        $gaugeFillBrush.Dispose()
        $gaugeFillPath.Dispose()
    }

    $master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-MultiSizeIcon -Master $master -Path $icoPath
}
finally {
    $graphics.Dispose()
    $master.Dispose()
    $source.Dispose()
}

Write-Output "SourcePackage=$($package.PackageFullName)"
Write-Output "SourceAsset=$sourcePath"
Write-Output "Png=$pngPath"
Write-Output "Ico=$icoPath"
