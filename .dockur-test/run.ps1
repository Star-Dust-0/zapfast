# ============================================================================
# windows-vm-test guest script - Fluent Chat, runs INSIDE the Windows 11 VM.
# Proves: GUI launch in demo mode (no account), light/dark themes, forced-RTL
# layout, installer behaviour, data dirs. Captures crash evidence when the
# app dies: app crash.log, WER Application Error entries, core stderr.
# ============================================================================
$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
function Shot($n){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bm=New-Object System.Drawing.Bitmap $b.Width,$b.Height; $g=[System.Drawing.Graphics]::FromImage($bm); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $bm.Save("\\host.lan\Data\$n.png"); "shot $n $($b.Width)x$($b.Height)" }
function KillApp(){ Get-Process FluentChat,fluentchatd -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
function Dump-Crash($tag){
  $log = "$env:APPDATA\FluentChat\crash.log"
  if (Test-Path $log) { "crashlog[$tag]:"; Get-Content $log | select -Last 30 | % { "  $_" }; Copy-Item $log "\\host.lan\Data\crash-$tag.log" -Force } else { "crashlog[$tag]: none" }
  $wer = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'} -MaxEvents 3 -ErrorAction SilentlyContinue
  $wer | % { "WER: $($_.TimeCreated) $($_.Message.Substring(0,[Math]::Min(600,$_.Message.Length)))" }
}
Shot 'desktop'

# --- stage the portable build ---
$zip = Get-ChildItem '\\host.lan\Data\' -Filter '*portable*.zip' | select -First 1
if (-not $zip) { "ERROR: no portable zip staged"; exit 1 }
Expand-Archive $zip.FullName C:\app -Force
$exe = Get-ChildItem C:\app -Recurse -Filter FluentChat.exe | select -First 1
$core = Get-ChildItem C:\app -Recurse -Filter fluentchatd.exe | select -First 1
"exe: $($exe.FullName)"; "core: $($core.FullName)"
if (-not $exe -or -not $core) { "ERROR: portable payload incomplete"; exit 1 }

$env:FLUENTCHAT_DEMO='1'
$env:FLUENTCHAT_TRACE='1'

function Launch-And-Watch($tag, $waitSec){
  Start-Process $exe.FullName
  $alive = $false
  for ($i=0; $i -lt [int]($waitSec/5); $i++) {
    Start-Sleep 5
    $proc = Get-Process FluentChat -ErrorAction SilentlyContinue
    if ($proc) { $alive = $true }
    elseif ($i -gt 1 -and -not $alive) { "launch[$tag]: app died after ~$($i*5)s"; break }
  }
  if ($alive) { "launch[$tag]: FluentChat process alive after ${waitSec}s" }
  else { "launch[$tag]: NO FluentChat process"; Dump-Crash $tag }
  $ws = New-Object -ComObject WScript.Shell
  $ws.SendKeys('{ESC}'); Start-Sleep 2
  $ws.SendKeys('{ESC}'); Start-Sleep 3
  Shot "app-$tag"
}

# --- C1: launch, light theme (VM default) ---
Launch-And-Watch 'light' 45

# --- C2: dark theme ---
KillApp; Start-Sleep 3
New-Item 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Force | Out-Null
Set-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' AppsUseLightTheme 0
Set-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' SystemUsesLightTheme 0
Launch-And-Watch 'dark' 30

# --- C3: forced RTL layout ---
KillApp; Start-Sleep 3
$env:FLUENTCHAT_RTL='1'
Launch-And-Watch 'rtl' 30
Remove-Item Env:FLUENTCHAT_RTL
KillApp; Start-Sleep 3

# --- C4: data directories created by the core ---
Get-ChildItem "$env:APPDATA","$env:LOCALAPPDATA" -Directory -ErrorAction SilentlyContinue | ? { $_.Name -match 'fluent|Fluent' } | % { "datadir: $($_.FullName)"; Get-ChildItem $_.FullName -Recurse -ErrorAction SilentlyContinue | select -First 10 | % { "  $($_.FullName)" } }
Copy-Item "$env:APPDATA\FluentChat\crash.log" '\\host.lan\Data\crash-latest.log' -Force -ErrorAction SilentlyContinue

# --- C5: installer: silent install with a timeout, installed app launches ---
$setup = Get-ChildItem '\\host.lan\Data\' -Filter '*installer*.exe' | select -First 1
if ($setup) {
  "installer: $($setup.Name)"
  $job = Start-Job { param($s) Start-Process $s -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES','/LOG=C:\inno-install.log' -Wait } -ArgumentList $setup.FullName
  if (Wait-Job $job -Timeout 300) { "installer finished" } else { "installer TIMED OUT after 300s"; Stop-Job $job; Get-Process *setup* -ErrorAction SilentlyContinue | Stop-Process -Force }
  Remove-Job $job -Force
  if (Test-Path C:\inno-install.log) { Get-Content C:\inno-install.log -Tail 15 | % { "inno: $_" }; Copy-Item C:\inno-install.log '\\host.lan\Data\' -Force }
  $installed = 'C:\Program Files\FluentChat\FluentChat.exe'
  "installed exists: $(Test-Path $installed)"
  if (Test-Path $installed) {
    Start-Process $installed
    Start-Sleep 30
    Get-Process FluentChat,fluentchatd -ErrorAction SilentlyContinue | % { "proc-installed: $($_.Name)" }
    Shot 'app-installed'
    KillApp
  }
} else { "ERROR: no installer staged" }
"run.ps1 done"
