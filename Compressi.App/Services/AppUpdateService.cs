using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compressi_App.Services;

public enum AppUpdateStatus
{
    UpToDate,
    Checking,
    Available,
    Downloading,
    Installing,
    Failed,
}

public sealed class AppUpdateInfo
{
    public required Version Version { get; init; }

    public required string DownloadUrl { get; init; }
}

public sealed class AppUpdateService
{
    private const string GitHubOwner = "thomasboyle";
    private const string GitHubRepo = "compressi";
    private const string SetupAssetName = "Compressi-Setup-x64.exe";
    // Focus-driven rechecks only (no timer). Keep this short so a release published
    // soon after the last check is still noticed; ETag makes repeats cheap.
    private static readonly TimeSpan MinRecheckInterval = TimeSpan.FromMinutes(15);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders =
        {
            { "User-Agent", "Compressi-Updater" },
            { "Accept", "application/vnd.github+json" },
        },
    };

    private readonly object _gate = new();
    private readonly string _cachePath;
    private UpdateCache _cache;
    private AppUpdateStatus _status = AppUpdateStatus.UpToDate;
    private AppUpdateInfo? _available;
    private double _downloadProgress;
    private int _lastRaisedProgressPercent = -1;

    public AppUpdateService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Compressi");
        Directory.CreateDirectory(folder);
        _cachePath = Path.Combine(folder, "update-cache.json");
        _cache = LoadCache(_cachePath);
        ApplyCacheLocked();
    }

    public event EventHandler? StateChanged;

    public AppUpdateStatus Status
    {
        get { lock (_gate) return _status; }
    }

    public AppUpdateInfo? AvailableUpdate
    {
        get { lock (_gate) return _available; }
    }

    public double DownloadProgress
    {
        get { lock (_gate) return _downloadProgress; }
    }

    public static Version GetLocalVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(version.Build, 0));
    }

    public async Task CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_status is AppUpdateStatus.Checking or AppUpdateStatus.Downloading or AppUpdateStatus.Installing)
            {
                return;
            }

            if (!force
                && _cache.LastCheckedUtc is not null
                && DateTime.UtcNow - _cache.LastCheckedUtc.Value < MinRecheckInterval)
            {
                return;
            }

            _status = AppUpdateStatus.Checking;
        }

        RaiseStateChanged();

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest");

            string? etag;
            lock (_gate)
            {
                etag = _cache.ETag;
            }

            if (!string.IsNullOrWhiteSpace(etag))
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", etag);
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
            {
                lock (_gate)
                {
                    _cache.LastCheckedUtc = DateTime.UtcNow;
                    SaveCache(_cachePath, _cache);
                    ApplyCacheLocked();
                }

                RaiseStateChanged();
                return;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("Could not parse GitHub release response.");

            var remoteVersion = ParseTagVersion(release.TagName)
                ?? throw new InvalidOperationException($"Unrecognized release tag '{release.TagName}'.");
            var downloadUrl =
                $"https://github.com/{GitHubOwner}/{GitHubRepo}/releases/latest/download/{SetupAssetName}";

            lock (_gate)
            {
                _cache = new UpdateCache
                {
                    ETag = response.Headers.ETag?.Tag,
                    LastCheckedUtc = DateTime.UtcNow,
                    LatestVersion = remoteVersion.ToString(),
                    DownloadUrl = downloadUrl,
                    DismissedVersion = _cache.DismissedVersion,
                };
                SaveCache(_cachePath, _cache);
                ApplyCacheLocked();
            }

            RaiseStateChanged();
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                ApplyCacheLocked();
            }

            RaiseStateChanged();
        }
        catch
        {
            lock (_gate)
            {
                if (_available is not null)
                {
                    _status = AppUpdateStatus.Available;
                }
                else
                {
                    _status = AppUpdateStatus.Failed;
                }
            }

            RaiseStateChanged();
        }
    }

    public async Task DownloadAndInstallAsync(CancellationToken cancellationToken = default)
    {
        AppUpdateInfo update;
        lock (_gate)
        {
            update = _available ?? throw new InvalidOperationException("No update is available.");
            if (_status is AppUpdateStatus.Downloading or AppUpdateStatus.Installing)
            {
                return;
            }

            _status = AppUpdateStatus.Downloading;
            _downloadProgress = 0;
            _lastRaisedProgressPercent = -1;
        }

        RaiseStateChanged();

        var tempDir = Path.Combine(Path.GetTempPath(), "CompressiUpdate");
        Directory.CreateDirectory(tempDir);
        var setupPath = Path.Combine(tempDir, SetupAssetName);

        try
        {
            using var response = await _http.GetAsync(
                update.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(setupPath, FileMode.Create, FileAccess.Write, FileShare.None, 82_000, true);

            var buffer = new byte[82_000];
            long readTotal = 0;
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                readTotal += read;
                if (total is not > 0)
                {
                    continue;
                }

                var pct = (int)Math.Clamp(100.0 * readTotal / total.Value, 0, 100);
                bool raise;
                lock (_gate)
                {
                    _downloadProgress = pct;
                    raise = pct != _lastRaisedProgressPercent;
                    if (raise)
                    {
                        _lastRaisedProgressPercent = pct;
                    }
                }

                if (raise)
                {
                    RaiseStateChanged();
                }
            }

            lock (_gate)
            {
                _status = AppUpdateStatus.Installing;
                _downloadProgress = 100;
            }

            RaiseStateChanged();

            if (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = setupPath,
                    Arguments = "/VERYSILENT /CLOSEAPPLICATIONS /NORESTART /SUPPRESSMSGBOXES",
                    UseShellExecute = true,
                }) is null)
            {
                throw new InvalidOperationException("Could not start the updater.");
            }
        }
        catch (OperationCanceledException)
        {
            lock (_gate)
            {
                _status = AppUpdateStatus.Available;
                _downloadProgress = 0;
            }

            RaiseStateChanged();
            throw;
        }
        catch
        {
            lock (_gate)
            {
                _status = AppUpdateStatus.Failed;
                _downloadProgress = 0;
            }

            RaiseStateChanged();
            throw;
        }
    }

    public void DismissAvailableUpdate()
    {
        lock (_gate)
        {
            if (_available is null)
            {
                return;
            }

            _cache.DismissedVersion = _available.Version.ToString();
            SaveCache(_cachePath, _cache);
            _available = null;
            _status = AppUpdateStatus.UpToDate;
        }

        RaiseStateChanged();
    }

    private void ApplyCacheLocked()
    {
        var local = GetLocalVersion();
        if (!Version.TryParse(_cache.LatestVersion, out var remote)
            || string.IsNullOrWhiteSpace(_cache.DownloadUrl)
            || remote <= local
            || (Version.TryParse(_cache.DismissedVersion, out var dismissed) && dismissed >= remote))
        {
            _available = null;
            _status = AppUpdateStatus.UpToDate;
            return;
        }

        _available = new AppUpdateInfo
        {
            Version = remote,
            DownloadUrl = _cache.DownloadUrl!,
        };
        _status = AppUpdateStatus.Available;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static Version? ParseTagVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out var version)
            ? new Version(version.Major, version.Minor, Math.Max(version.Build, 0))
            : null;
    }

    private static UpdateCache LoadCache(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new UpdateCache();
            }

            return JsonSerializer.Deserialize<UpdateCache>(File.ReadAllText(path), JsonOptions) ?? new UpdateCache();
        }
        catch
        {
            return new UpdateCache();
        }
    }

    private static void SaveCache(string path, UpdateCache cache)
    {
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(cache, JsonOptions));
        }
        catch
        {
            // Best effort.
        }
    }

    private sealed class UpdateCache
    {
        public string? ETag { get; set; }

        public DateTime? LastCheckedUtc { get; set; }

        public string? LatestVersion { get; set; }

        public string? DownloadUrl { get; set; }

        public string? DismissedVersion { get; set; }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}
