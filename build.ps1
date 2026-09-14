$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root "src"
$dist = Join-Path $root "dist"
$assets = Join-Path $root "assets"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path $csc)) {
  throw "csc.exe introuvable: $csc"
}

New-Item -ItemType Directory -Path $dist, $assets -Force | Out-Null

# Icon (optional)
$iconPy = Join-Path $root "tools\make_icon.py"
if (Test-Path $iconPy) {
  & python $iconPy
}

$iconIco = Join-Path $assets "hdr2sdr.ico"
$win32icon = ""
if (Test-Path $iconIco) {
  $win32icon = "/win32icon:`"$iconIco`""
  Copy-Item $iconIco (Join-Path $dist "hdr2sdr.ico") -Force
}

$sources = Get-ChildItem $src -Filter "*.cs" | ForEach-Object { "`"$($_.FullName)`"" }
$out = Join-Path $dist "hdr2sdr.exe"

$args = @(
  "/nologo",
  "/target:winexe",
  "/platform:anycpu",
  "/optimize+",
  "/out:`"$out`"",
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Drawing.dll",
  "/reference:System.Windows.Forms.dll"
)
if ($win32icon) { $args += $win32icon }
$args += $sources

Write-Host "Compiling hdr2sdr..."
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)" }

# Silent launcher helpers for shortcuts / Autohotkey / Stream Deck
$helpers = Join-Path $dist "helpers"
New-Item -ItemType Directory -Path $helpers -Force | Out-Null

@"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$out"" toggle", 0, False
"@ | Set-Content -Path (Join-Path $helpers "toggle-hdr.vbs") -Encoding ASCII

@"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$out"" sdr up", 0, False
"@ | Set-Content -Path (Join-Path $helpers "sdr-up.vbs") -Encoding ASCII

@"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$out"" sdr down", 0, False
"@ | Set-Content -Path (Join-Path $helpers "sdr-down.vbs") -Encoding ASCII

@"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$out"" profile jour", 0, False
"@ | Set-Content -Path (Join-Path $helpers "profile-jour.vbs") -Encoding ASCII

@"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$out"" profile soir", 0, False
"@ | Set-Content -Path (Join-Path $helpers "profile-soir.vbs") -Encoding ASCII

@"
Set WshShell = CreateObject("WScript.Shell")
WshShell.Run """$out"" profile jeu", 0, False
"@ | Set-Content -Path (Join-Path $helpers "profile-jeu.vbs") -Encoding ASCII

Write-Host "OK -> $out"
Write-Host "Helpers -> $helpers"

# Start Menu shortcut (updated on every build)
$startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
New-Item -ItemType Directory -Path $startDir -Force | Out-Null
$lnkPath = Join-Path $startDir "hdr2sdr.lnk"
$wsh = New-Object -ComObject WScript.Shell
$lnk = $wsh.CreateShortcut($lnkPath)
$lnk.TargetPath = $out
$lnk.WorkingDirectory = $dist
$lnk.Description = "hdr2sdr - HDR / SDR / luminosité"
if (Test-Path $iconIco) { $lnk.IconLocation = "$iconIco,0" }
else { $lnk.IconLocation = "$out,0" }
$lnk.Save()
Write-Host "Start Menu -> $lnkPath"
