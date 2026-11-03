# core-pipe.ps1 - exercises the fluentchatd JSON-RPC pipe without any account.
# Covers test matrix rows B1-B5 (docs/testing-v1.md).
# -Demo: run against FLUENTCHAT_DEMO=1 (offline backend, fictional data).
# -Smoke: only B1 (ping/boot) and B5 (shutdown) - for the real backend, where
#         link state and chat loads depend on the network.
param([switch]$Demo, [switch]$Smoke)
$ErrorActionPreference = 'Stop'
$core = Join-Path $PSScriptRoot '..' 'target' 'release' 'fluentchatd.exe'
if (-not (Test-Path $core)) { $core = Join-Path $PSScriptRoot '..' 'publish' 'core' 'fluentchatd.exe' }
if (-not (Test-Path $core)) { throw "fluentchatd.exe not found" }

$env:FLUENTCHAT_ACCOUNT = '1'
$env:FLUENTCHAT_TRACE = '1'
if ($Demo) { $env:FLUENTCHAT_DEMO = '1' }
$p = New-Object System.Diagnostics.Process
$p.StartInfo.FileName = $core
$p.StartInfo.RedirectStandardInput = $true
$p.StartInfo.RedirectStandardOutput = $true
$p.StartInfo.RedirectStandardError = $true
$p.StartInfo.UseShellExecute = $false
$p.StartInfo.CreateNoWindow = $true
$null = $p.Start()

$responses = @{}
$events = New-Object System.Collections.ArrayList
$script:readTask = $p.StandardOutput.ReadLineAsync()
$script:errTask = $p.StandardError.ReadToEndAsync()

function Send-Frame($obj) {
    $p.StandardInput.WriteLine(($obj | ConvertTo-Json -Compress -Depth 8))
    $p.StandardInput.Flush()
}
function Dump-Core-State($why) {
    Write-Output "DIAG $why"
    Write-Output ("DIAG process exited: " + $p.HasExited + ($(if ($p.HasExited) { " code=" + $p.ExitCode } else { "" })))
    if ($p.HasExited) {
        $err = $script:errTask.GetAwaiter().GetResult()
        if ($err) { Write-Output "DIAG --- core stderr ---"; Write-Output $err; Write-Output "DIAG --- end stderr ---" }
    }
}
function Wait-Reply($id, $timeoutSec = 30) {
    $limit = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $limit) {
        if ($p.HasExited) { Dump-Core-State "core exited while waiting for id=$id"; throw "core exited while waiting for reply id=$id" }
        if ($script:readTask.Wait(200)) {
            $line = $script:readTask.Result
            if ($null -eq $line) { Dump-Core-State "stdout closed while waiting for id=$id"; throw "core closed stdout while waiting for reply id=$id" }
            $script:readTask = $p.StandardOutput.ReadLineAsync()
            if ($line.Length -gt 0) {
                $frame = $line | ConvertFrom-Json
                if ($null -ne $frame.id) { $responses[$frame.id] = $frame }
                elseif ($frame.method -eq 'event') { $null = $events.Add($frame.params.name) }
            }
        }
        if ($responses.ContainsKey($id)) { return $responses[$id] }
    }
    Dump-Core-State "timeout waiting for id=$id"
    throw "timeout waiting for reply id=$id"
}

$fails = 0
function Check($name, $cond) {
    if ($cond) { Write-Output "PASS $name" } else { $script:fails++; Write-Output "FAIL $name" }
}

try {
    # B1: ping (first answer doubles as the boot check: process starts,
    # data dirs are created, the backend thread and runtime come up).
    Send-Frame @{ jsonrpc = '2.0'; id = 1; method = 'ping' }
    $r = Wait-Reply 1 60
    Check 'B1 ping answers with protocol version' ($r.result.pong -eq $true -and $r.result.protocol -ge 1)

    if (-not $Smoke) {
        # B2: getState before any link
        Send-Frame @{ jsonrpc = '2.0'; id = 2; method = 'getState' }
        $r = Wait-Reply 2
        Check 'B2 getState returns link state' ($null -ne $r.result.link.state)

        # B3: unknown method -> JSON-RPC error, core alive afterwards
        Send-Frame @{ jsonrpc = '2.0'; id = 3; method = 'noSuchMethod' }
        $r = Wait-Reply 3
        Check 'B3 unknown method returns error -32601' ($r.error.code -eq -32601)
        Send-Frame @{ jsonrpc = '2.0'; id = 4; method = 'ping' }
        $r = Wait-Reply 4
        Check 'B3 core still answers after bad frame' ($r.result.pong -eq $true)

        # B4: loadChat answers through the Messages event (no hang)
        Send-Frame @{ jsonrpc = '2.0'; id = 5; method = 'loadChat'; params = @{ chat = 'nobody@s.whatsapp.net' } }
        $r = Wait-Reply 5 30
        Check 'B4 loadChat answers for unknown chat' (($null -ne $r.result) -or ($null -ne $r.error))
    }

    # B5: clean shutdown
    Send-Frame @{ jsonrpc = '2.0'; id = 6; method = 'shutdown' }
    $r = Wait-Reply 6
    $p.WaitForExit(10000) | Out-Null
    Check 'B5 shutdown acks and the process exits' (($null -ne $r.result) -and $p.HasExited -and $p.ExitCode -eq 0)
}
finally {
    if (-not $p.HasExited) { $p.Kill() }
    $err = $script:errTask.GetAwaiter().GetResult()
    if ($err) { Write-Output "--- core stderr ---"; Write-Output $err; Write-Output "--- end stderr ---" }
}

if ($fails -gt 0) { throw "$fails core pipe checks failed" }
Write-Output "core pipe: all checks passed"
