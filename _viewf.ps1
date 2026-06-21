param([string]$f='_preview.png',[int]$cols=40)
Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path $f))
$w = $src.Width; $h = $src.Height
$rows = [int]($cols * $h / $w / 2)
$sx = $w / $cols; $sy = $h / $rows
$g = " .:-=+*#%@".ToCharArray()
for($r=0;$r -lt $rows;$r++){
  $line = ""
  for($c=0;$c -lt $cols;$c++){
    $x0=[int]($c*$sx); $x1=[int](($c+1)*$sx)
    $y0=[int]($r*$sy); $y1=[int](($r+1)*$sy)
    $sum=0;$cnt=0;$asum=0
    for($xx=$x0;$xx -lt $x1;$xx++){
      for($yy=$y0;$yy -lt $y1;$yy++){
        $p=$src.GetPixel($xx,$yy)
        $sum += ($p.R+$p.G+$p.B)/3
        $asum += $p.A
        $cnt++
      }
    }
    if($cnt -eq 0){$cnt=1}
    $a = $asum/$cnt; $v = $sum/$cnt
    if($a -lt 40){ $line += " " }
    else {
      $idx = [int]((255-$v)/255 * ($g.Length-1))
      $line += $g[[Math]::Max(0,[Math]::Min($g.Length-1,$idx))]
    }
  }
  Write-Output $line
}
$src.Dispose()
