Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path 'Resources/BookmarkMenuIcon.png'))
$w=$src.Width; $h=$src.Height
# Print raw alpha + dominant color per pixel, 16x16.
for($y=0;$y -lt $h;$y++){
  $line=""
  for($x=0;$x -lt $w;$x++){
    $p=$src.GetPixel($x,$y)
    if($p.A -lt 40){ $line += "  " }
    else {
      $key = ("{0:X2}{1:X2}{2:X2}" -f $p.R,$p.G,$p.B)
      $line += ($key + " ")
    }
  }
  Write-Output ("{0,2}: {1}" -f $y,$line)
}
$src.Dispose()
