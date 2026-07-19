using System.Collections.Concurrent;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage.Streams;

namespace Compressi_App.Services.UiSounds;

public static class UiSoundService
{
    /// <summary>Peak loudness at 100% volume, relative to the original (pre-slider) level.</summary>
    public const float MaxVolumeMultiplier = 3f;

    private const int DefaultVolumePercent = 50;

    private static readonly ConcurrentDictionary<UiSoundName, byte[]> Cache = new();
    private static readonly object PlayGate = new();
    private static MediaPlayer? _player;
    private static InMemoryRandomAccessStream? _currentStream;
    private static int _enabled = 1;
    private static int _volumePercent = DefaultVolumePercent;

    public static bool IsEnabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    /// <summary>0–100. 50% matches the original level; 100% is <see cref="MaxVolumeMultiplier"/>× louder.</summary>
    public static int VolumePercent
    {
        get => Volatile.Read(ref _volumePercent);
        set => Volatile.Write(ref _volumePercent, Math.Clamp(value, 0, 100));
    }

    public static void Play(UiSoundName sound)
    {
        if (!IsEnabled || VolumePercent <= 0)
        {
            return;
        }

        _ = PlayAsync(sound);
    }

    public static void Warmup()
    {
        _ = Task.Run(static () =>
        {
            foreach (UiSoundName sound in Enum.GetValues<UiSoundName>())
            {
                try
                {
                    Cache.GetOrAdd(sound, static name => UiSoundSynthesizer.RenderWav(name));
                }
                catch
                {
                    // Ignore warmup failures.
                }
            }

            // Touch MediaPlayer once off the hot path so first Play avoids construction latency.
            try
            {
                lock (PlayGate)
                {
                    EnsurePlayer_NoLock();
                }
            }
            catch
            {
                // Ignore warmup failures.
            }
        });
    }

    /// <summary>
    /// Maps slider percent to gain relative to the original level:
    /// 0 → 0, 50 → 1×, 100 → 3×.
    /// </summary>
    internal static double RelativeGain(int volumePercent)
    {
        var percent = Math.Clamp(volumePercent, 0, 100);
        if (percent <= DefaultVolumePercent)
        {
            return percent / (double)DefaultVolumePercent;
        }

        return 1.0 + (MaxVolumeMultiplier - 1.0) * ((percent - DefaultVolumePercent) / (double)DefaultVolumePercent);
    }

    private static async Task PlayAsync(UiSoundName sound)
    {
        try
        {
            var volumePercent = VolumePercent;
            if (volumePercent <= 0)
            {
                return;
            }

            var wav = Cache.GetOrAdd(sound, static name => UiSoundSynthesizer.RenderWav(name));
            var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(wav.AsBuffer());
            stream.Seek(0);

            var playerVolume = RelativeGain(volumePercent) / MaxVolumeMultiplier;

            lock (PlayGate)
            {
                var player = EnsurePlayer_NoLock();
                DisposeCurrentStream_NoLock();
                _currentStream = stream;
                player.Volume = Math.Clamp(playerVolume, 0.0, 1.0);
                player.Source = MediaSource.CreateFromStream(stream, "audio/wav");
                player.Play();
            }
        }
        catch
        {
            // Playback is best-effort; never break UI interactions.
        }
    }

    private static MediaPlayer EnsurePlayer_NoLock()
    {
        if (_player is not null)
        {
            return _player;
        }

        var player = new MediaPlayer
        {
            AudioCategory = MediaPlayerAudioCategory.SoundEffects,
            IsLoopingEnabled = false,
        };
        player.MediaEnded += OnMediaEnded;
        player.MediaFailed += OnMediaFailed;
        _player = player;
        return player;
    }

    private static void OnMediaEnded(MediaPlayer sender, object args)
    {
        lock (PlayGate)
        {
            if (!ReferenceEquals(sender, _player))
            {
                return;
            }

            sender.Source = null;
            DisposeCurrentStream_NoLock();
        }
    }

    private static void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        lock (PlayGate)
        {
            if (!ReferenceEquals(sender, _player))
            {
                return;
            }

            sender.Source = null;
            DisposeCurrentStream_NoLock();
        }
    }

    private static void DisposeCurrentStream_NoLock()
    {
        if (_currentStream is null)
        {
            return;
        }

        try
        {
            _currentStream.Dispose();
        }
        catch
        {
            // Ignore dispose races.
        }

        _currentStream = null;
    }
}
