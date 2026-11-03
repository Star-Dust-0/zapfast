# ============================================================================
# windows-vm-test guest script v3 - Fluent Chat, runs INSIDE the Windows 11 VM.
# Launch-crash bisect: light1 (fresh) -> light2 (relaunch, same theme) ->
# dark (theme change) -> rtl (RTL on light theme, isolated from dark).
# Per-phase crash attribution: crash.log is reset before every launch and WER
# entries are filtered to events after that launch started.
# ============================================================================
$ErrorActionPreference='Continue'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing,UIAutomationClient,UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class WindowTest {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, UIntPtr e);
}
'@
$Share='\\host.lan\Data'
$Log="$env:APPDATA\FluentChat\crash.log"

function Shot($n){ $b=[System.Windows.Forms.Screen]::PrimaryScreen.Bounds; $bm=New-Object System.Drawing.Bitmap $b.Width,$b.Height; $g=[System.Drawing.Graphics]::FromImage($bm); $g.CopyFromScreen($b.Location,[System.Drawing.Point]::Empty,$b.Size); $bm.Save("$Share\$n.png"); "shot $n $($b.Width)x$($b.Height)" }
function KillApp(){ Get-Process FluentChat,fluentchatd -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue }
function Reset-CrashLog(){ if (Test-Path $Log) { Remove-Item $Log -Force } }
function Dump-Crash($tag, $since){
  if (Test-Path $Log) { "crashlog[$tag]:"; Get-Content $Log | select -First 120 | % { "  $_" }; Copy-Item $Log "$Share\crash-$tag.log" -Force } else { "crashlog[$tag]: none" }
  $wer = Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Application Error'; StartTime=$since} -ErrorAction SilentlyContinue
  $wer | % { "WER[$tag]: $($_.TimeCreated) $($_.Message.Substring(0,[Math]::Min(500,$_.Message.Length)))" }
}
function Set-Theme([bool]$light){
  New-Item 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' -Force | Out-Null
  $v = 0; if ($light) { $v = 1 }
  Set-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' AppsUseLightTheme $v
  Set-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize' SystemUsesLightTheme $v
}
function Test-Launch($tag, $waitSec, $path){
  if (-not $path) { $path = $exe.FullName }
  KillApp; Start-Sleep 2; Reset-CrashLog
  $t0 = Get-Date
  $p = Start-Process $path -PassThru
  $seen = $false; $crashed = $false
  for ($i=1; $i -le [int]($waitSec/5); $i++) {
    Start-Sleep 5
    $proc = Get-Process FluentChat -ErrorAction SilentlyContinue
    if ($proc) { $seen = $true }
    elseif ($seen) { "launch[$tag]: died after ~$($i*5)s"; $crashed = $true; break }
    elseif ($i -ge 3) { "launch[$tag]: never appeared"; $crashed = $true; break }
  }
  if (-not $crashed) {
    $proc = Get-Process FluentChat -ErrorAction SilentlyContinue | select -First 1
    "launch[$tag]: alive after ${waitSec}s hwnd=$($proc.MainWindowHandle) title='$($proc.MainWindowTitle)'"
    $ws = New-Object -ComObject WScript.Shell
    $ws.SendKeys('{ESC}'); Start-Sleep 1
    [WindowTest]::ShowWindow($proc.MainWindowHandle,3) | Out-Null
    [WindowTest]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
    Start-Sleep 1
    [WindowTest]::SetCursorPos(200,25) | Out-Null
    [WindowTest]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    [WindowTest]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
    $ws.SendKeys('{ESC}'); Start-Sleep 3
    "process[$tag]: CPU=$($proc.CPU) workingSet=$($proc.WorkingSet64)"
    Get-Process fluentchatd -ErrorAction SilentlyContinue | % { "core[$tag]: pid=$($_.Id) CPU=$($_.CPU) workingSet=$($_.WorkingSet64)" }
    try {
      $root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
      $elements = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants,[System.Windows.Automation.Condition]::TrueCondition)
      "UIA[$tag]: $($elements.Count) descendants"
      $elements | select -First 100 | % { "UIA[$tag]: $($_.Current.ControlType.ProgrammaticName) name='$($_.Current.Name)' id='$($_.Current.AutomationId)' rect=$($_.Current.BoundingRectangle)" }
    } catch { "UIA[$tag] error: $_" }
  } else { Start-Sleep 3; Dump-Crash $tag $t0 }
  Shot "app-$tag"
  if (-not $crashed) { Test-Interactions $tag $proc.MainWindowHandle }
}
function Get-Element($h,$id){
  $r=[System.Windows.Automation.AutomationElement]::FromHandle($h)
  $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
  return $r.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$c)
}
function Click-Element($e){
  if (-not $e) { throw 'Missing UI element' }
  $r=$e.Current.BoundingRectangle
  [WindowTest]::SetCursorPos([int]($r.X+$r.Width/2),[int]($r.Y+$r.Height/2)) | Out-Null
  [WindowTest]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
  [WindowTest]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
  Start-Sleep 1
}
function Chat-Count($h){
  $list=Get-Element $h 'ChatList'
  $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)
  return $list.FindAll([System.Windows.Automation.TreeScope]::Children,$c).Count
}
function Assert-Count($tag,$h,$expected){
  $actual=Chat-Count $h
  if ($actual -eq $expected) { "PASS[$tag]: chat count $actual" } else { "FAIL[$tag]: expected $expected chats, got $actual" }
}
function Test-Interactions($tag,$h){
  try {
    if ($tag -eq 'light1') {
      Assert-Count 'all' $h 7
      $search=Get-Element $h 'SearchBox'
      $value=$search.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
      $value.SetValue('Sarah'); Start-Sleep 2
      Assert-Count 'search-Sarah' $h 1
      Shot 'search-Sarah'
      $value.SetValue(''); Start-Sleep 1
      Click-Element (Get-Element $h 'ChipUnread'); Assert-Count 'unread' $h 3
      Click-Element (Get-Element $h 'ChipGroups'); Assert-Count 'groups' $h 2
      Click-Element (Get-Element $h 'ChipFavorites'); Assert-Count 'favorites' $h 0
      Click-Element (Get-Element $h 'ChipAll'); Assert-Count 'all-reset' $h 7
    }
    $list=Get-Element $h 'ChatList'
    $c=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty,[System.Windows.Automation.ControlType]::ListItem)
    $first=$list.FindFirst([System.Windows.Automation.TreeScope]::Children,$c)
    Click-Element $first; Start-Sleep 3
    $composer=Get-Element $h 'Composer'
    if (-not $composer) { throw 'Conversation did not open' }
    Shot "chat-$tag"
    $send=Get-Element $h 'SendButton'
    "send-empty[$tag]: enabled=$($send.Current.IsEnabled)"
    $value=$composer.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value.SetValue("Offline UI test $tag"); Start-Sleep 1
    "send-filled[$tag]: enabled=$($send.Current.IsEnabled)"
    Click-Element $send; Start-Sleep 3
    "composer-after-send[$tag]: '$($value.Current.Value)'"
    $root=[System.Windows.Automation.AutomationElement]::FromHandle($h)
    $tc=New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty,"Offline UI test $tag")
    $echo=$root.FindFirst([System.Windows.Automation.TreeScope]::Descendants,$tc)
    if ($echo) { "PASS[$tag]: demo message echo rendered" } else { "FAIL[$tag]: demo echo missing" }
    Shot "sent-$tag"
    if ($tag -eq 'light1') {
      $p=Get-Process FluentChat | select -First 1
      $before=$p.TotalProcessorTime.TotalMilliseconds
      Start-Sleep 5; $p.Refresh()
      "idle-sample: CPU ms over 5sec=$($p.TotalProcessorTime.TotalMilliseconds-$before) workingSet=$($p.WorkingSet64)"
    }
  } catch { "FAIL[interactions-$tag]: $_" }
}

Shot 'desktop'

# --- stage the portable build ---
$zip = Get-ChildItem $Share -Filter '*portable*.zip' | select -First 1
if (-not $zip) { "ERROR: no portable zip staged"; exit 1 }
Expand-Archive $zip.FullName C:\app -Force
$exe = Get-ChildItem C:\app -Recurse -Filter FluentChat.exe | select -First 1
$core = Get-ChildItem C:\app -Recurse -Filter fluentchatd.exe | select -First 1
"exe: $($exe.FullName)"; "core: $($core.FullName)"
if (-not $exe -or -not $core) { "ERROR: portable payload incomplete"; exit 1 }

$env:FLUENTCHAT_DEMO='1'
$env:FLUENTCHAT_TRACE='1'

# Standalone core check on a clean VM, without depending on shell navigation.
$pi = New-Object System.Diagnostics.ProcessStartInfo
$pi.FileName=$core.FullName; $pi.UseShellExecute=$false; $pi.CreateNoWindow=$true
$pi.RedirectStandardInput=$true; $pi.RedirectStandardOutput=$true; $pi.RedirectStandardError=$true
$cp = New-Object System.Diagnostics.Process; $cp.StartInfo=$pi
try {
  $cp.Start() | Out-Null
  $cp.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"ping"}')
  $cp.StandardInput.Flush(); Start-Sleep 3
  if ($cp.HasExited) { "standalone-core: EXIT $($cp.ExitCode) stderr=$($cp.StandardError.ReadToEnd())" }
  else {
    $bytes=New-Object byte[] 8192
    $read=$cp.StandardOutput.BaseStream.ReadAsync($bytes,0,$bytes.Length)
    if ($read.Wait(5000)) { "standalone-core: $([System.Text.Encoding]::UTF8.GetString($bytes,0,$read.Result))" }
    else { "standalone-core: TIMEOUT" }
    $cp.Kill()
  }
} catch { "standalone-core: $_" }

# --- P1: fresh launch, light theme (VM default) ---
Test-Launch 'light1' 40

# --- P2: relaunch, theme unchanged (bisects crash: relaunch vs dark theme) ---
Test-Launch 'light2' 25

# --- P3: dark theme ---
Set-Theme $false
Test-Launch 'dark' 25

# --- P4: forced RTL layout on light theme (isolates RTL from dark) ---
Set-Theme $true
$env:FLUENTCHAT_RTL='1'
Test-Launch 'rtl' 25
Remove-Item Env:FLUENTCHAT_RTL
KillApp; Start-Sleep 2

# --- C4: data directories created by the app/core ---
Get-ChildItem "$env:APPDATA","$env:LOCALAPPDATA" -Directory -ErrorAction SilentlyContinue | ? { $_.Name -match 'fluent|Fluent' } | % { "datadir: $($_.FullName)"; Get-ChildItem $_.FullName -Recurse -ErrorAction SilentlyContinue | select -First 10 | % { "  $($_.FullName)" } }

# --- C5: installer: copy local, silent install with a timeout, then launch ---
$setup = Get-ChildItem $Share -Filter '*installer*.exe' | select -First 1
if ($setup) {
  "installer: $($setup.Name)"
  New-Item C:\install -ItemType Directory -Force | Out-Null
  Copy-Item $setup.FullName C:\install\setup.exe -Force
  "installer copied local"
  $job = Start-Job { Start-Process C:\install\setup.exe -ArgumentList '/VERYSILENT','/NORESTART','/SUPPRESSMSGBOXES','/LOG=C:\inno-install.log' -Wait }
  if (Wait-Job $job -Timeout 600) { "installer finished" } else { "installer TIMED OUT after 600s"; Stop-Job $job; Get-Process *setup* -ErrorAction SilentlyContinue | Stop-Process -Force }
  Remove-Job $job -Force
  if (Test-Path C:\inno-install.log) { Get-Content C:\inno-install.log -Tail 20 | % { "inno: $_" }; Copy-Item C:\inno-install.log "$Share\" -Force }
  $installed = 'C:\Program Files\FluentChat\FluentChat.exe'
  "installed exists: $(Test-Path $installed)"
  if (Test-Path $installed) {
    Test-Launch 'installed' 25 $installed
  }
} else { "ERROR: no installer staged" }
"run.ps1 done"
