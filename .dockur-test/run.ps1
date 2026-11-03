# ============================================================================
# windows-vm-test guest script - Fluent Chat, runs INSIDE the Windows 11 VM.
# Proves what a headless runner cannot: real GUI launch in demo mode (no
# account), light/dark themes, forced-RTL layout, installer behaviour, and
# data-directory creation. The app zip + installer are staged in the shared
# folder by the workflow (no release download).
# ============================================================================
$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
function Shot($n){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bm=New-Object System.Drawing.Bitmap $b.Width,$b.Height; $g=[System.Drawing.Graphics]::FromImage($bm); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $bm.Save("\\host.lan\Data\$n.png"); "shot $n $($b.Width)x$($b.Height)" }
function KillApp(){ Get-Process FluentChat,fluentchatd -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
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

# --- C1: launch, light theme (VM default) ---
Start-Process $exe.FullName
Start-Sleep 45
$ws = New-Object -ComObject WScript.Shell
$ws.SendKeys('{ESC}'); Start-Sleep 2
$ws.SendKeys('{ESC}'); Start-Sleep 15
Get-Process FluentChat,fluentchatd -ErrorAction SilentlyContinue | % { "proc: $($_.Name) id=$($_.Id)" }
Shot 'app-light'

# --- C2: dark theme ---
KillApp; Start-Sleep 3
New-Item 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Force | Out-Null
Set-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' AppsUseLightTheme 0
Set-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' SystemUsesLightTheme 0
Start-Process $exe.FullName
Start-Sleep 30
Shot 'app-dark'

# --- C3: forced RTL layout ---
KillApp; Start-Sleep 3
$env:FLUENTCHAT_RTL='1'
Start-Process $exe.FullName
Start-Sleep 30
Shot 'app-rtl'
Remove-Item Env:FLUENTCHAT_RTL
KillApp; Start-Sleep 3

# --- C4: data directories created by the core ---
Get-ChildItem "$env:APPDATA","$env:LOCALAPPDATA" -Directory -ErrorAction SilentlyContinue | ? { $_.Name -match 'fluent|Fluent' } | % { "datadir: $($_.FullName)"; Get-ChildItem $_.FullName -Recurse -ErrorAction SilentlyContinue | select -First 10 | % { "  $($_.FullName)" } }

# --- C5: installer: silent install, installed app launches ---
$setup = Get-ChildItem '\\host.lan\Data\' -Filter '*installer*.exe' | select -First 1
if ($setup) {
  "installer: $($setup.Name)"
  Start-Process $setup.FullName -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES' -Wait
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
