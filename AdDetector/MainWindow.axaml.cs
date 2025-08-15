using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using LibVLCSharp.Avalonia;
using LibVLCSharp.Shared;
using SoundFingerprinting;
using SoundFingerprinting.Audio;
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.Emy;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;
using SoundFingerprinting.Strides;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;

namespace AdDetector;

public partial class MainWindow : Window
{
    private readonly IAudioService audio;
    private readonly string FingerprintsDir = Path.Combine(AppContext.BaseDirectory, "fingerprints");
    private LibVLC? libVLC;
    private MediaPlayer? player;

    public MainWindow()
    {
        InitializeComponent();
        Core.Initialize();
        audio = InitFFmpeg();
        indexButton.Click += async (_, _) => await IndexFingerprintsAsync();
        detectButton.Click += async (_, _) => await DetectVideoAsync();
        exitButton.Click += (_, _) => Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        player?.Dispose();
        libVLC?.Dispose();
        base.OnClosed(e);
    }

    private static IAudioService InitFFmpeg()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg", "bin", Environment.Is64BitProcess ? "x64" : "x86");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();
        return new FFmpegAudioService();
    }

    private async Task IndexFingerprintsAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Select ads folder" };
        var folder = await dialog.ShowAsync(this);
        if (string.IsNullOrEmpty(folder)) return;
        var model = new InMemoryModelService();
        foreach (var file in Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories))
        {
            var track = new TrackInfo(Guid.NewGuid().ToString(), Path.GetFileNameWithoutExtension(file), "Ad");
            var hashes = await FingerprintCommandBuilder.Instance
                .BuildFingerprintCommand()
                .From(file)
                .WithFingerprintConfig(config =>
                {
                    config.Audio.SampleRate = 5512;
                    config.Audio.Stride = new IncrementalStaticStride(64);
                    return config;
                })
                .UsingServices(audio)
                .Hash();
            model.Insert(track, hashes);
        }
        Directory.CreateDirectory(FingerprintsDir);
        model.Snapshot(FingerprintsDir);
        summary.Text = $"Indexed {model.GetTrackIds().Count()} ad(s).";
        exitButton.IsVisible = true;
    }

    private async Task DetectVideoAsync()
    {
        if (!Directory.Exists(FingerprintsDir))
        {
            summary.Text = "No fingerprints found. Update first.";
            return;
        }
        var dialog = new OpenFileDialog { Title = "Select video file", AllowMultiple = false };
        var files = await dialog.ShowAsync(this);
        var file = files?.FirstOrDefault();
        if (string.IsNullOrEmpty(file)) return;
        var model = new InMemoryModelService(FingerprintsDir);
        AVQueryResult queryResult = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(file)
            .UsingServices(model, audio)
            .Query();
        var hits = queryResult.Audio?.ResultEntries ?? Enumerable.Empty<ResultEntry>();
        PlayWithNotifications(file, hits);
    }

    private void PlayWithNotifications(string file, IEnumerable<ResultEntry> hits)
    {
        libVLC ??= new LibVLC();
        player?.Dispose();
        player = new MediaPlayer(libVLC);
        videoView.MediaPlayer = player;
        using var media = new Media(libVLC, file, FromType.FromPath);
        player.Play(media);
        var playbackStart = DateTime.UtcNow;
        foreach (var hit in hits.Where(h => h.Confidence >= 0.20))
        {
            _ = Task.Run(async () =>
            {
                var delay = TimeSpan.FromSeconds(hit.QueryMatchStartsAt);
                var wait = delay - (DateTime.UtcNow - playbackStart);
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait);
                var duration = TimeSpan.FromSeconds(hit.Track.Length);
                player.SetMarqueeString(VideoMarqueeOption.Text, $"{hit.Track.Title} - {duration:mm\\:ss}");
                player.SetMarqueeInt(VideoMarqueeOption.Timeout, (int)duration.TotalMilliseconds);
                player.SetMarqueeInt(VideoMarqueeOption.Size, 30);
                player.SetMarqueeInt(VideoMarqueeOption.Position, 2);
                player.SetMarqueeInt(VideoMarqueeOption.Enable, 1);
            });
        }
    }
}
