param([string]$In, [string]$Out)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $In))
$w = $src.Width; $h = $src.Height
$bg = New-Object System.Drawing.Bitmap $w, $h
$g = [System.Drawing.Graphics]::FromImage($bg)
$g.Clear([System.Drawing.Color]::FromArgb(245,245,245))
$g.DrawImage($src, 0, 0, $w, $h)
$g.Dispose()
$outDir = Split-Path $Out -Parent
if ([string]::IsNullOrEmpty($outDir)) { $outDir = (Get-Location).Path }
$bg.Save((Get-Item -LiteralPath $outDir).FullName + '\' + (Split-Path $Out -Leaf), [System.Drawing.Imaging.ImageFormat]::Png)
$bg.Dispose(); $src.Dispose()
Write-Output "composed $Out"
