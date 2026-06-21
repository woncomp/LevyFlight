# System DPI scaling
$lp = (Get-ItemProperty 'HKCU:\Control Panel\Desktop\WindowMetrics' -ErrorAction SilentlyContinue).AppliedDPI
if (-not $lp) { $lp = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue).LogPixels }
$scale = [math]::Round($lp / 96 * 100)
Write-Output "AppliedDPI=$lp  scale=${scale}%"

# Per-monitor V2?
$v2 = (Get-ItemProperty 'HKCU:\Control Panel\Desktop' -ErrorAction SilentlyContinue).Win8DpiScaling
Write-Output "Win8DpiScaling=$v2"

# Icon dimensions
Add-Type -AssemblyName System.Drawing
foreach ($f in @('Resources\LevyFlightBrandIcon.png','Resources\BookmarkMenuIcon.png','Resources\LevyFlightWindowCommand.png')) {
  $i = [System.Drawing.Image]::FromFile((Resolve-Path $f))
  "{0}  {1}x{2}  {3}" -f $f, $i.Width, $i.Height, $i.PixelFormat
  $i.Dispose()
}
