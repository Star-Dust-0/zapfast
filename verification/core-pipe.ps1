# core-pipe.ps1 - exercises the fluentchatd JSON-RPC pipe without any account.
# Covers test matrix rows B1-B5 (docs/testing-v1.md).
$ErrorActionPreference = 'Stop'
$core = Join-Path $PSScriptRoot '..' 'target' 'release' 'fluentchatd.exe'
if (-not (Test-Path $core)) { $core = Join-Path $PSScriptRoot '..' 'publish' 'core' 'fluentchatd.exe' }
if (-not (Test-Path $core)) { throw "fluentchatd.exe not found" }

$env:FLUENTCHAT_ACCOUNT = '1'
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
$done = $false
$reader = Start-Job -ScriptBlock {
    param($proc)
    while (-not $proc.HasExited) {
        $line = $proc.StandardOutput.ReadLine()
        if ($null -eq $line) { break }
        if ($line.Length -gt 0) { $line }
    }
} -ArgumentList $p

function Send-Frame($obj) {
    $p.StandardInput.WriteLine(($obj | ConvertTo-Json -Compress -Depth 8))
    $p.StandardInput.Flush()
}
function Wait-Reply($id, $timeoutSec = 15) {
    $limit = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $limit) {
        foreach ($line in (Receive-Job $reader -Keep)) {
            $frame = $line | ConvertFrom-Json
            if ($null -ne $frame.id) { $script:responses[$frame.id] = $frame }
            elseif ($frame.method -eq 'event') { $null = $events.Add($frame.params.name) }
        }
        if ($responses.ContainsKey($id)) { return $responses[$id] }
        Start-Sleep -Milliseconds 200
    }
    throw "timeout waiting for reply id=$id"
}

$fails = 0
function Check($name, $cond) {
    if ($cond) { Write-Output "PASS $name" } else { $script:fails++; Write-Output "FAIL $name" }
}

# B1: ping
Send-Frame @{ jsonrpc = '2.0'; id = 1; method = 'ping' }
$r = Wait-Reply 1
Check 'B1 ping answers with protocol version' ($r.result.pong -eq $true -and $r.result.protocol -ge 1)

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

# B4: loadChat for an unknown chat answers (no hang)
Send-Frame @{ jsonrpc = '2.0'; id = 5; method = 'loadChat'; params = @{ chat = 'nobody@s.whatsapp.net' } }
$r = Wait-Reply 5 30
Check 'B4 loadChat answers for unknown chat' (($null -ne $r.result) -or ($null -ne $r.error))

# B5: shutdown exits promptly
Send-Frame @{ jsonrpc = '2.0'; id = 6; method = 'shutdown' }
$r = Wait-Reply 6
$exited = $p.WaitForExit(3000)
Check 'B5 shutdown exits within 3s' ($exited -eq $true)

if (-not $exited) { $p.Kill() }
Remove-Job $reader -Force -ErrorAction SilentlyContinue
if ($fails -gt 0) { throw "$fails core-pipe check(s) failed" }
Write-Output 'core-pipe: all checks passed'
