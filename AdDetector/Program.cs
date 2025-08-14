using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using SoundFingerprinting;              // TrackInfo, builders
using SoundFingerprinting.Audio;        // IAudioService
using SoundFingerprinting.Emy;          // FFmpegAudioService
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;        // AVQueryResult, ResultEntry
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;  // dynamic loader
using SoundFingerprinting.Strides;
using LibVLCSharp.Shared;            // media playback

internal class Program
{
    /* --------------------------------------------------------------
       Project-root-relative folders
    -------------------------------------------------------------- */
    private static string AdsDir =>
        "C:/small_data/ads";

    private static string FingerprintsDir =>
        "C:/small_data/fingerprints"; // where hashes are stored between runs

    /* --------------------------------------------------------------
       Initialise FFmpeg once and return the decoder
    -------------------------------------------------------------- */
    private static readonly IAudioService audio = InitFFmpeg();

    private static IAudioService InitFFmpeg()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg",
                                         "bin",
                                         Environment.Is64BitProcess ? "x64" : "x86");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();   // loads avcodec-60.dll & friends
        Console.WriteLine($"✓ FFmpeg DLLs loaded from {ffmpegPath}");
        return new FFmpegAudioService();
    }

    private static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Core.Initialize();

        Console.WriteLine("1) Index ads\n2) Detect media");
        Console.Write("Select option: ");
        var option = Console.ReadLine();
        switch (option)
        {
            case "1":
                await IndexAdsAsync();
                break;
            case "2":
                await DetectAsync();
                break;
            default:
                Console.WriteLine("Unknown option");
                break;
        }
    }

    private static async Task IndexAdsAsync()
    {
        var model = new InMemoryModelService();

        foreach (var file in Directory.GetFiles(AdsDir, "*.*",
                                                SearchOption.AllDirectories))
        {
            var track = new TrackInfo(Guid.NewGuid().ToString(),
                                      Path.GetFileNameWithoutExtension(file),
                                      "Ad");
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
            Console.WriteLine($"   Indexed {track.Title}");
            Console.WriteLine($"   {hashes.Count} hashes generated.");
        }

        Directory.CreateDirectory(FingerprintsDir);
        model.Snapshot(FingerprintsDir);
        Console.WriteLine($"✓ {model.GetTrackIds().Count()} ad(s) ready and stored.\n");
    }

    private static async Task DetectAsync()
    {
        if (!Directory.Exists(FingerprintsDir))
        {
            Console.WriteLine("No fingerprints found. Run indexing first.");
            return;
        }

        var model = new InMemoryModelService(FingerprintsDir);

        Console.WriteLine("Select detection mode:\n1) Audio file\n2) Video playback");
        Console.Write("Mode: ");
        var mode = Console.ReadLine();

        Console.Write("Path to media file: ");
        var file = (Console.ReadLine() ?? string.Empty).Trim('"');
        if (!File.Exists(file))
        {
            Console.WriteLine("File not found");
            return;
        }

        switch (mode)
        {
            case "1":
                await DetectAudioAsync(model, file);
                break;
            case "2":
                await DetectVideoAsync(model, file);
                break;
            default:
                Console.WriteLine("Unknown mode");
                break;
        }
    }

    private static async Task DetectAudioAsync(IModelService model, string file)
    {
        AVQueryResult queryResult = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(file)
            .UsingServices(model, audio)
            .Query();

        var hits = queryResult.Audio?.ResultEntries
            ?.Where(h => h.Confidence >= 0.20) ?? Enumerable.Empty<ResultEntry>();

        if (!hits.Any())
        {
            Console.WriteLine("No match found.");
            return;
        }

        foreach (var hit in hits)
        {
            var duration = TimeSpan.FromSeconds(hit.Track.Length);
            Console.WriteLine($"Match: {hit.Track.Title} - {duration:mm\\:ss}");
        }
    }

    private static async Task DetectVideoAsync(IModelService model, string file)
    {
        AVQueryResult queryResult = await QueryCommandBuilder.Instance
            .BuildQueryCommand()
            .From(file)
            .UsingServices(model, audio)
            .Query();

        var hits = queryResult.Audio?.ResultEntries ?? Enumerable.Empty<ResultEntry>();
        await PlayWithNotificationsAsync(file, hits);
    }

    private static async Task PlayWithNotificationsAsync(string file, IEnumerable<ResultEntry> hits)
    {
        using var libVLC = new LibVLC();
        using var media = new Media(libVLC, file, FromType.FromPath);
        using var player = new MediaPlayer(libVLC);
        player.Play(media);

        var playbackStart = DateTime.UtcNow;

        foreach (var hit in hits.Where(h => h.Confidence >= 0.20))
        {
            _ = Task.Run(async () =>
            {
                var delay = TimeSpan.FromSeconds(hit.QueryMatchStartsAt);
                var wait = delay - (DateTime.UtcNow - playbackStart);
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait);
                }

                var duration = TimeSpan.FromSeconds(hit.Track.Length);
                player.SetMarqueeString(MediaPlayerMarqueeOption.Text,
                    $"{hit.Track.Title} - {duration:mm\\:ss}");
                player.SetMarqueeInt(MediaPlayerMarqueeOption.Timeout,
                    (int)duration.TotalMilliseconds);
                player.SetMarqueeInt(MediaPlayerMarqueeOption.Size, 30);
                player.SetMarqueeInt(MediaPlayerMarqueeOption.Position,
                    (int)MediaPlayerMarqueePosition.Top);
                player.SetMarqueeInt(MediaPlayerMarqueeOption.Enable, 1);
            });
        }

        var tcs = new TaskCompletionSource();
        player.EndReached += (_, _) => tcs.SetResult();
        await tcs.Task;
    }
}
