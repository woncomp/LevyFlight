$vsc='C:\Program Files\Microsoft Visual Studio\2022\Community'
$files=Get-ChildItem $vsc -Recurse -Filter '*.imagemanifest' -ErrorAction SilentlyContinue
$out = New-Object System.Collections.ArrayList
foreach($f in $files){
  $lines=Get-Content $f.FullName
  for($i=0;$i -lt $lines.Count;$i++){
    if($lines[$i] -match '\.svg'){
      [void]$out.Add(('{0}:{1}  {2}' -f $f.Name,$i,$lines[$i].Trim()))
    }
  }
}
$out | Select-Object -First 25
