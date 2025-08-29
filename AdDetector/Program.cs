using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SoundFingerprinting;              // TrackInfo, builders
using SoundFingerprinting.Audio;        // IAudioService
using SoundFingerprinting.Emy;          // FFmpegAudioService
using SoundFingerprinting.Builder;
using SoundFingerprinting.Data;
using SoundFingerprinting.InMemory;
using SoundFingerprinting.Query;        // AVQueryResult, ResultEntry
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;  // dynamic loader
using SoundFingerprinting.Strides;
using SoundFingerprinting.Media;
using System.Text.Json;

internal class Program
{

    /* --------------------------------------------------------------
       Project-root-relative folders
    -------------------------------------------------------------- */
    private static string AdsDir =>
        "C:/small_data/ads_video";

    private static string VideoDir =>
        "C:/small_data/videos";

    /* --------------------------------------------------------------
       Initialise FFmpeg once and return the decoder
    -------------------------------------------------------------- */
    private static readonly IMediaService mediaService = InitFFmpeg();

    private static IMediaService InitFFmpeg()
    {
        string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg",
                                         "bin",
                                         Environment.Is64BitProcess ? "x64" : "x86");
        DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
        DynamicallyLoadedBindings.Initialize();   // loads avcodec-60.dll & friends
        Console.WriteLine($"✓ FFmpeg DLLs loaded from {ffmpegPath}");
        return new FFmpegAudioService();
    }

    /* -------------------------------------------------------------- */
    private static readonly IModelService model = new InMemoryModelService();

    private static async Task Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        await IndexAdsAsync();
        await ScanVideoAsync();
    }

    private static async Task IndexAdsAsync()
    {
        double secondsToAnalyze = 2; // number of seconds to analyze from query file
        double startAtSecond = 0; // start at the begining
        foreach (var file in Directory.GetFiles(AdsDir, "*.*",
                                                SearchOption.AllDirectories))
        {
            var track = new TrackInfo(
                Guid.NewGuid().ToString(),
                Path.GetFileNameWithoutExtension(file),
                "Ad",
                MediaType.Audio | MediaType.Video // Fix: set both audio and video
                // MediaType.Video
            );

            var hashes = await FingerprintCommandBuilder.Instance
                .BuildFingerprintCommand()
                .From(file, secondsToAnalyze, startAtSecond, MediaType.Audio | MediaType.Video)
                // .WithFingerprintConfig(config =>
                // {
                //     // config.Video.FrameRate = 30; // Set directly if available
                //     // config.Video.Stride = new IncrementalStaticStride(64);
                //     return config;
                // })
                .UsingServices(mediaService)
                .Hash();

            model.Insert(track, hashes);
            Console.WriteLine($"   Indexed {track.Title}");
            Console.WriteLine($"   {hashes.Count} hashes generated.");
            // Console.WriteLine($"   {hashes.Max(h => h.Confidence):P2} max confidence.");
            // Console.WriteLine($"   {hashes.Min(h => h.Confidence):P2} min confidence.");
            Console.WriteLine($"   {hashes}");
        }

        Console.WriteLine($"✓ {model.GetTrackIds().Count()} ad(s) ready.\n");
    }

    private static async Task ScanVideoAsync()
    {
        Console.WriteLine("→ Scanning content …");
        // int secondsToAnalyze = 1000; // number of seconds to analyze from query file
        // int startAtSecond = 0; // start at the begining
        foreach (var file in Directory.GetFiles(VideoDir, "*.*",
                                                SearchOption.AllDirectories))
        {
            Console.WriteLine($"→ Scanning {Path.GetFileName(file)}");
            AVQueryResult queryResult = await QueryCommandBuilder.Instance
                                          .BuildQueryCommand()
                                        //   .From(file, secondsToAnalyze, startAtSecond)
                                          .From(file, MediaType.Audio | MediaType.Video)
                                          .UsingServices(model, mediaService)
                                          .Query();

            var videoHits = queryResult.Video?.ResultEntries ?? Enumerable.Empty<ResultEntry>();
            var audioHits = queryResult.Audio?.ResultEntries ?? Enumerable.Empty<ResultEntry>();
            foreach (var hit in audioHits.Where(h => h.Confidence >= 0.20))
            {
                TimeSpan when = TimeSpan.FromSeconds(hit.QueryMatchStartsAt);
                Console.WriteLine($"{Path.GetFileName(file)}  →  {hit.Track.Title} @ {when}");
            }
            foreach (var hit in videoHits.Where(h => h.Confidence >= 0.20))
            {
                TimeSpan when = TimeSpan.FromSeconds(hit.QueryMatchStartsAt);
                Console.WriteLine($"{Path.GetFileName(file)}  →  {hit.Track.Title} @ {when}");
            }
        }
    }
}