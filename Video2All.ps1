$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Get-ChildItem (Join-Path $dir "sorgenti") -Filter *.cs | ForEach-Object { $_.FullName }
Add-Type -Path $src -ReferencedAssemblies System.Windows.Forms, System.Drawing, System.Core, System.Security
[Program]::Run($dir)
