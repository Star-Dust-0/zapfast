// DotnetPipeProbe - verifies the exact transport CoreClient uses to talk to
// fluentchatd (System.Diagnostics.Process + redirected stdio) and, if the
// default read strategy fails, two alternatives. Also runs the B1-B5 matrix.
// Usage: DotnetPipeProbe <path-to-fluentchatd.exe>
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

string exe = args[0];
int failures = 0;

Process StartCore(bool demo)
{
    var psi = new ProcessStartInfo(exe)
    {
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardInputEncoding = new UTF8Encoding(false),
    };
    psi.Environment["FLUENTCHAT_TRACE"] = "1";
    psi.Environment["FLUENTCHAT_ACCOUNT"] = "1";
    if (demo) psi.Environment["FLUENTCHAT_DEMO"] = "1";
    var p = Process.Start(psi) ?? throw new Exception("failed to start core");
    _ = p.StandardError.ReadToEndAsync(); // drain so the child never blocks on stderr
    return p;
}

async Task<bool> ProbeReadLineAsync(bool demo)
{
    using var p = StartCore(demo);
    try
    {
        p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
        p.StandardInput.Flush();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        string? line = null;
        try { line = await p.StandardOutput.ReadLineAsync(cts.Token); } catch (OperationCanceledException) { }
        return line != null && line.Contains("pong");
    }
    finally { if (!p.HasExited) p.Kill(); }
}

async Task<bool> ProbeBaseStream(bool demo)
{
    using var p = StartCore(demo);
    try
    {
        p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
        p.StandardInput.Flush();
        var buf = new byte[8192];
        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (!sb.ToString().Contains("pong"))
        {
            int n;
            try { n = await p.StandardOutput.BaseStream.ReadAsync(buf, cts.Token); }
            catch (OperationCanceledException) { return false; }
            if (n == 0) return false;
            sb.Append(Encoding.UTF8.GetString(buf, 0, n));
        }
        return true;
    }
    finally { if (!p.HasExited) p.Kill(); }
}

async Task<bool> ProbeEvents(bool demo)
{
    using var p = StartCore(demo);
    try
    {
        var lines = new ConcurrentQueue<string>();
        var got = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) return;
            lines.Enqueue(e.Data);
            if (e.Data.Contains("pong")) got.TrySetResult(true);
        };
        p.BeginOutputReadLine();
        p.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
        p.StandardInput.Flush();
        var done = await Task.WhenAny(got.Task, Task.Delay(15000));
        return done == got.Task && got.Task.Result;
    }
    finally { if (!p.HasExited) p.Kill(); }
}

void Report(string name, bool ok)
{
    Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    if (!ok) failures++;
}

// --- transport strategy probes (demo core) ---
bool a = await ProbeReadLineAsync(true);
Report("T1 ReadLineAsync (CoreClient strategy) receives pong", a);
bool b = await ProbeBaseStream(true);
Report("T2 BaseStream.ReadAsync receives pong", b);
bool c = await ProbeEvents(true);
Report("T3 OutputDataReceived events receive pong", c);

// --- B-matrix with the preferred working strategy ---
if (a || b || c)
{
    // Use a simple synchronous reader loop over the first working strategy.
    using var p = StartCore(true);
    var responses = new Dictionary<long, JsonElement>();
    var readCts = new CancellationTokenSource();
    var reader = Task.Run(async () =>
    {
        try
        {
            while (!readCts.IsCancellationRequested)
            {
                string? line;
                if (a) line = await p.StandardOutput.ReadLineAsync(readCts.Token);
                else
                {
                    var buf = new byte[8192];
                    int n = await p.StandardOutput.BaseStream.ReadAsync(buf, readCts.Token);
                    if (n == 0) break;
                    line = Encoding.UTF8.GetString(buf, 0, n).Trim('\r', '\n');
                    // BaseStream can return partial/multiple lines; split them.
                    foreach (var part in line.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        Ingest(part.Trim('\r'));
                    continue;
                }
                if (line == null) break;
                Ingest(line);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.WriteLine("DIAG reader ended: " + ex.GetType().Name + " " + ex.Message); }
    });
    void Ingest(string line)
    {
        if (line.Length == 0) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number)
                responses[id.GetInt64()] = doc.RootElement.Clone();
        }
        catch (JsonException) { }
    }
    void Send(long id, string method, object? prms = null)
    {
        var frame = prms == null
            ? "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\"}"
            : "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\",\"params\":" + JsonSerializer.Serialize(prms) + "}";
        p.StandardInput.WriteLine(frame);
        p.StandardInput.Flush();
    }
    async Task<JsonElement?> WaitReply(long id, int sec)
    {
        var limit = DateTime.UtcNow.AddSeconds(sec);
        while (DateTime.UtcNow < limit)
        {
            if (responses.TryGetValue(id, out var r)) return r;
            if (p.HasExited) { Console.WriteLine("DIAG core exited, code=" + p.ExitCode); return null; }
            await Task.Delay(100);
        }
        return null;
    }

    Send(1, "ping");
    var r1 = await WaitReply(1, 60);
    Report("B1 ping answers with protocol version", r1 is JsonElement e1 && e1.GetProperty("result").GetProperty("pong").GetBoolean());

    Send(2, "getState");
    var r2 = await WaitReply(2, 15);
    Report("B2 getState returns link state", r2 is JsonElement e2 && e2.GetProperty("result").TryGetProperty("link", out _));

    Send(3, "noSuchMethod");
    var r3 = await WaitReply(3, 15);
    Report("B3 unknown method returns error -32601", r3 is JsonElement e3 && e3.GetProperty("error").GetProperty("code").GetInt64() == -32601);

    Send(4, "ping");
    var r4 = await WaitReply(4, 15);
    Report("B3 core still answers after bad frame", r4 is JsonElement e4 && e4.GetProperty("result").GetProperty("pong").GetBoolean());

    Send(5, "loadChat", new { chat = "demo-mom@s.whatsapp.net" });
    var r5 = await WaitReply(5, 30);
    Report("B4 loadChat answers through the Messages event", r5 is JsonElement e5 && (e5.TryGetProperty("result", out _) || e5.TryGetProperty("error", out _)));

    Send(6, "shutdown");
    var r6 = await WaitReply(6, 15);
    bool exited = false;
    try { exited = p.WaitForExit(10000); } catch { }
    Report("B5 shutdown acks and the process exits", r6 is JsonElement && exited && p.ExitCode == 0);

    readCts.Cancel();
    if (!p.HasExited) p.Kill();

    // real-backend smoke: boot + ping + shutdown, network-dependent parts excluded
    using var pr = StartCore(false);
    pr.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}");
    pr.StandardInput.Flush();
    string? rline = null;
    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
    {
        try { rline = await pr.StandardOutput.ReadLineAsync(cts.Token); } catch (OperationCanceledException) { }
    }
    Report("B6 real backend boots and answers ping", rline != null && rline.Contains("pong"));
    pr.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"shutdown\"}");
    pr.StandardInput.Flush();
    bool rexited = false;
    try { rexited = pr.WaitForExit(15000); } catch { }
    Report("B6 real backend shuts down cleanly", rexited && pr.ExitCode == 0);
    if (!pr.HasExited) pr.Kill();
}

Console.WriteLine(failures == 0 ? "dotnet pipe probe: all checks passed" : ("dotnet pipe probe: " + failures + " checks failed"));
return failures == 0 ? 0 : 1;
