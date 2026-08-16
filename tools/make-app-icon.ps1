# Rebuild src/Lones.SptManager.App/Assets/app.ico from assets/lones-spt-manager-icon.png
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $root "assets\lones-spt-manager-icon.png"
$icoPath = Join-Path $root "src\Lones.SptManager.App\Assets\app.ico"
$sizes = @(16, 24, 32, 48, 64, 128, 256)

$original = [System.Drawing.Bitmap]::FromFile($srcPath)
$side = [Math]::Max($original.Width, $original.Height)
$square = New-Object System.Drawing.Bitmap $side, $side, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$sg = [System.Drawing.Graphics]::FromImage($square)
$sg.Clear([System.Drawing.Color]::Transparent)
$sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
$sg.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
$sg.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
$sg.DrawImage(
    $original,
    [int](($side - $original.Width) / 2),
    [int](($side - $original.Height) / 2),
    $original.Width,
    $original.Height)
$sg.Dispose()
$original.Dispose()

$pngBlobs = New-Object System.Collections.Generic.List[byte[]]
foreach ($size in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.DrawImage($square, 0, 0, $size, $size)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}
$square.Dispose()

$count = $sizes.Count
$offset = 6 + (16 * $count)
$entries = New-Object System.Collections.Generic.List[byte]
foreach ($i in 0..($count - 1)) {
    $w = $sizes[$i]
    $blob = $pngBlobs[$i]
    $widthByte = if ($w -ge 256) { [byte]0 } else { [byte]$w }
    $entries.Add($widthByte)
    $entries.Add($widthByte)
    $entries.Add([byte]0)
    $entries.Add([byte]0)
    $entries.Add([byte]1)
    $entries.Add([byte]0)
    $entries.Add([byte]32)
    $entries.Add([byte]0)
    $entries.AddRange([BitConverter]::GetBytes([int]$blob.Length))
    $entries.AddRange([BitConverter]::GetBytes([int]$offset))
    $offset += $blob.Length
}

$ico = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ico
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$count)
$bw.Write($entries.ToArray())
foreach ($blob in $pngBlobs) { $bw.Write($blob) }
$bw.Flush()
New-Item -ItemType Directory -Force -Path (Split-Path $icoPath) | Out-Null
[System.IO.File]::WriteAllBytes($icoPath, $ico.ToArray())
$bw.Dispose()
$ico.Dispose()
Write-Output "Wrote $icoPath ($((Get-Item $icoPath).Length) bytes)"
