using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluentChat.Core;

/// <summary>
/// Owns the fluentchatd child process and speaks newline-delimited
/// JSON-RPC 2.0 with it over stdio. Protocol: docs/fluent-chat-core-protocol.md.
/// </summary>
public sealed class CoreClient : IDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private long _nextId;
    private int _disposed;

    /// <summary>(event name, data) for every core event, on a background thread.</summary>
    public event Action<string, JsonElement>? CoreEvent;

    /// <summary>Raised when the core process exits unexpectedly.</summary>
    public event Action<int>? CoreExited;

    private CoreClient(Process process)
    {
        _process = process;
        _stdin = process.StandardInput;
        _ = Task.Run(ReadLoop);
        // Always drain stderr. Trace output must never fill a pipe and stall
        // the daemon while the UI is waiting for a protocol response.
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) System.Diagnostics.Debug.WriteLine("core: " + e.Data);
        };
        process.BeginErrorReadLine();
        _ = Task.Run(() =>
        {
            try
            {
                process.WaitForExit();
                if (_disposed == 0)
                {
                    CoreExited?.Invoke(process.ExitCode);
                }
            }
            catch
            {
            }
        });
    }

    public static string CoreExePath =>
        Path.Combine(AppContext.BaseDirectory, "core", "fluentchatd.exe");

    public static CoreClient? Start()
    {
        string exe = CoreExePath;
        if (!File.Exists(exe))
        {
            return null;
        }
        var info = new ProcessStartInfo(exe)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        if (!process.Start())
        {
            return null;
        }
        return new CoreClient(process);
    }

    public Task<JsonElement> RequestAsync(string method, object? parameters = null)
    {
        long id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        var frame = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params = parameters ?? new { },
        };
        string line = JsonSerializer.Serialize(frame);
        lock (_stdin)
        {
            _stdin.WriteLine(line);
            _stdin.Flush();
        }
        return tcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }

    private async Task ReadLoop()
    {
        // Read the raw stream, not StreamReader.ReadLineAsync: against this
        // child, ReadLineAsync never completes on Windows (verified by the
        // DotnetPipeProbe CI gate) while BaseStream reads work. Decode UTF-8
        // incrementally so multi-byte characters can span reads, split on
        // newlines, and dispatch one frame per line.
        var buffer = new byte[8192];
        Decoder decoder = Encoding.UTF8.GetDecoder();
        var chars = new char[8192];
        var pendingText = new StringBuilder();
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                int read = await _process.StandardOutput.BaseStream
                    .ReadAsync(buffer, _stop.Token)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }
                int used = decoder.GetChars(buffer, 0, read, chars, 0);
                pendingText.Append(chars, 0, used);
                int start = 0;
                for (int i = 0; i < pendingText.Length; i++)
                {
                    if (pendingText[i] != '\n')
                    {
                        continue;
                    }
                    string line = pendingText.ToString(start, i - start).TrimEnd('\r');
                    start = i + 1;
                    DispatchLine(line);
                }
                if (start > 0)
                {
                    pendingText.Remove(0, start);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
        }
        foreach (System.Collections.Generic.KeyValuePair<long, TaskCompletionSource<JsonElement>> pair in _pending)
        {
            pair.Value.TrySetException(new IOException("core closed"));
        }
        _pending.Clear();
    }

    private void DispatchLine(string line)
    {
        if (line.Length == 0)
        {
            return;
        }
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }
        using (doc)
        {
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("id", out JsonElement idEl) && idEl.ValueKind == JsonValueKind.Number)
            {
                long id = idEl.GetInt64();
                if (_pending.TryRemove(id, out TaskCompletionSource<JsonElement>? tcs))
                {
                    if (root.TryGetProperty("error", out JsonElement error))
                    {
                        string message = error.TryGetProperty("message", out JsonElement m)
                            ? m.GetString() ?? "core error"
                            : "core error";
                        tcs.TrySetException(new InvalidOperationException(message));
                    }
                    else
                    {
                        tcs.TrySetResult(root.GetProperty("result").Clone());
                    }
                }
            }
            else if (root.TryGetProperty("method", out JsonElement methodEl)
                && methodEl.GetString() == "event"
                && root.TryGetProperty("params", out JsonElement evtParams))
            {
                string name = evtParams.TryGetProperty("name", out JsonElement n)
                    ? n.GetString() ?? ""
                    : "";
                JsonElement data = evtParams.TryGetProperty("data", out JsonElement d)
                    ? d.Clone()
                    : default;
                try
                {
                    CoreEvent?.Invoke(name, data);
                }
                catch
                {
                }
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        try
        {
            _stop.Cancel();
            lock (_stdin)
            {
                _stdin.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":-1,\"method\":\"shutdown\"}");
                _stdin.Flush();
            }
            if (!_process.WaitForExit(3000))
            {
                _process.Kill();
            }
        }
        catch
        {
        }
        _process.Dispose();
        _stop.Dispose();
    }
}
