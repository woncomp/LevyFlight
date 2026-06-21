# Render a WPF XAML vector icon (Canvas root) to PNG at a given size.
param(
  [Parameter(Mandatory=$true)][string]$File,
  [Parameter(Mandatory=$true)][string]$Out,
  [int]$Size = 256
)
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Xaml

$xaml = Get-Content -Raw -LiteralPath $File
$reader = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
$canvas = [System.Windows.Markup.XamlReader]::Load($reader)

$scale = $Size / 16.0
$canvas.RenderTransform = [System.Windows.Media.ScaleTransform]::new($scale, $scale)
$canvas.RenderTransformOrigin = [System.Windows.Point]::new(0, 0)
$canvas.Measure([System.Windows.Size]::new($Size, $Size))
$canvas.Arrange([System.Windows.Rect]::new(0, 0, $Size, $Size))
$canvas.UpdateLayout()

$rtb = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($Size, $Size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
$rtb.Render($canvas)

$enc = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
$enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
$outDir = Split-Path $Out -Parent
if ([string]::IsNullOrEmpty($outDir)) { $outDir = (Get-Location).Path }
if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
$outPath = (Get-Item -LiteralPath $outDir).FullName + '\' + (Split-Path $Out -Leaf)
$fs = [System.IO.File]::Create($outPath)
$enc.Save($fs)
$fs.Close()
Write-Output "rendered $Out"
