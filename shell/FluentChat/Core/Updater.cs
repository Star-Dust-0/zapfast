using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FluentChat.Core;

public sealed class UpdateInfo
{
    public string Version { get; set; } = "";
    public string InstallerUrl { get; set; } = "";
    public string InstallerName { get; set; } = "";
    public long Size { get; set; }
    public string? Sha256 { get; set; }
    public string ReleasePage { get; set; } = "";
}

/// <summary>
/// In-app updates (owner convention): check GitHub releases from inside the
/// app, download the installer with verification, then run it - the running
/// instance closes through the FluentChat.Quit handshake and the installer
/// upgrades in place. No browser step.
/// </summary>
public static class Updater
{
    public const string Repo = "Star-Dust-0/zapfast";
    public const string LatestReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
    public const string LatestReleasePage = "https://github.com/" + Repo + "/releases/latest";

    private static string TempDir => Path.Combine(Path.GetTempPath(), "FluentChat-update");

    public static string CurrentVersion =>
        typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static bool IsTrustedSource(Uri? uri)
    {
        if (uri == null || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }
        return uri.Host == "github.com"
            || uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancel = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentChat/" + CurrentVersion);
        using JsonDocument doc = JsonDocument.Parse(await http.GetStringAsync(LatestReleaseApi, cancel));
        JsonElement root = doc.RootElement;
        string tag = root.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() ?? "" : "";
        string version = tag.TrimStart('v', 'V');
        if (version.Length == 0 || !IsNewer(version, CurrentVersion))
        {
            return null;
        }
        string? url = null;
        string name = "";
        long size = 0;
        if (root.TryGetProperty("assets", out JsonElement assets))
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                string assetName = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "" : "";
                if (assetName.EndsWith("-installer-win-x64.exe", StringComparison.OrdinalIgnoreCase))
                {
                    url = asset.TryGetProperty("browser_download_url", out JsonElement u) ? u.GetString() : null;
                    name = assetName;
                    size = asset.TryGetProperty("size", out JsonElement sz) ? sz.GetInt64() : 0;
                    break;
                }
            }
        }
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || !IsTrustedSource(uri))
        {
            return null;
        }
        return new UpdateInfo
        {
            Version = version,
            InstallerUrl = url,
            InstallerName = name,
            Size = size,
            ReleasePage = LatestReleasePage,
        };
    }

    public static bool IsNewer(string candidate, string current)
    {
        return System.Version.TryParse(candidate, out System.Version? next)
            && System.Version.TryParse(current, out System.Version? now)
            && next > now;
    }

    public static void Verify(string path, UpdateInfo info)
    {
        long length = new FileInfo(path).Length;
        if (info.Size > 0 ? length != info.Size : length < 1_000_000)
        {
            throw new InvalidDataException("Installer size does not match the release.");
        }
        using FileStream stream = File.OpenRead(path);
        if (stream.ReadByte() != 'M' || stream.ReadByte() != 'Z')
        {
            throw new InvalidDataException("Downloaded file is not a Windows installer.");
        }
        if (info.Sha256 != null)
        {
            stream.Position = 0;
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!hash.Equals(info.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Installer checksum does not match the release.");
            }
        }
    }

    public static async Task<string> DownloadAsync(UpdateInfo info, Action<double> progress, CancellationToken cancel)
    {
        if (info.InstallerName != Path.GetFileName(info.InstallerName)
            || !info.InstallerName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Unexpected installer name.");
        }
        if (!Uri.TryCreate(info.InstallerUrl, UriKind.Absolute, out Uri? uri) || !IsTrustedSource(uri))
        {
            throw new InvalidOperationException("Untrusted download source.");
        }
        Directory.CreateDirectory(TempDir);
        foreach (string old in Directory.GetFiles(TempDir))
        {
            try { File.Delete(old); } catch { }
        }
        string target = Path.Combine(TempDir, info.InstallerName);
        string part = target + ".part";
        try
        {
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FluentChat/" + CurrentVersion);
            using HttpResponseMessage response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancel);
            response.EnsureSuccessStatusCode();
            if (!IsTrustedSource(response.RequestMessage?.RequestUri))
            {
                throw new InvalidOperationException("Download was redirected to an untrusted host.");
            }
            long total = response.Content.Headers.ContentLength ?? info.Size;
            await using Stream input = await response.Content.ReadAsStreamAsync(cancel);
            await using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920];
            long done = 0;
            while (true)
            {
                int read = await input.ReadAsync(buffer, cancel);
                if (read <= 0)
                {
                    break;
                }
                await output.WriteAsync(buffer.AsMemory(0, read), cancel);
                done += read;
                if (total > 0)
                {
                    progress(Math.Min(100.0, done * 100.0 / total));
                }
            }
            output.Dispose();
            await Task.Run(() => Verify(part, info), cancel);
            File.Move(part, target, overwrite: true);
            return target;
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
    }

    public static void LaunchInstaller(string path)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
}
