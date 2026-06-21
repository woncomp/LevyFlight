Add-Type -AssemblyName System.Drawing
$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path 'Resources/BookmarkMenuIcon.png'))
$cols = 32
$rows = $cols   # square cells: image is square
$sx = $src.Width / $cols; $sy = $src.Height / $rows
$g = " .:-=+*#%@".ToCharArray()
for($r=0;$r -lt $rows;$r++){
  $line = ""
  for($c=0;$c -lt $cols;$c++){
    $x0=[int]($c*$sx); $x1=[int](($c+1)*$sx); if($x1 -gt $src.Width){$x1=$src.Width}
    $y0=[int]($r*$sy); $y1=[int](($r+1)*$sy); if($y1 -gt $src.Height){$y1=$src.Height}
    $rs=0;$gs=0;$bs=0;$cnt=0;$asum=0
    for($xx=$x0;$xx -lt $x1;$xx++){
      for($yy=$y0;$yy -lt $y1;$yy++){
        $p=$src.GetPixel($xx,$yy)
        $rs+=$p.R; $gs+=$p.G; $bs+=$p.B; $asum+=$p.A; $cnt++
      }
    }
    if($cnt -eq 0){$cnt=1}
    $a=$asum/$cnt
    if($a -lt 40){ $line += " " }
    else {
      $R=[int]($rs/$cnt); $G=[int]($gs/$cnt); $B=[int]($bs/$cnt)
      $v=($R+$G+$B)/3
      # mark with @ for dark-green, % for bright-green
      if($G -gt 140 -and $R -lt 120){ $ch="%" }     # bright
      elseif($G -gt 90 -and $R -lt 90){ $ch="#" }    # mid
      elseif($G -gt 60 -and $R -lt 70){ $ch="*" }    # dark
      else { $idx=[int]((255-$v)/255*($g.Length-1)); $ch=$g[[Math]::Max(0,[Math]::Min($g.Length-1,$idx))] }
      $line += $ch
    }
  }
  Write-Output $line
}
$src.Dispose()
