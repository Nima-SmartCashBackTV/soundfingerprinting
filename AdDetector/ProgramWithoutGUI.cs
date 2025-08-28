// using System;
// using System.IO;
// using System.Linq;
// using System.Threading.Tasks;
// using SoundFingerprinting;              // TrackInfo, builders
// using SoundFingerprinting.Audio;        // IAudioService
// using SoundFingerprinting.Emy;          // FFmpegAudioService
// using SoundFingerprinting.Builder;
// using SoundFingerprinting.Data;
// using SoundFingerprinting.InMemory;
// using SoundFingerprinting.Query;        // AVQueryResult, ResultEntry
// using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;  // dynamic loader
// using SoundFingerprinting.Strides;   

// internal class Program
// {

//     /* --------------------------------------------------------------
//        Project-root-relative folders
//     -------------------------------------------------------------- */
//     private static string AdsDir =>
//         "C:/small_data/ads_video";

//     private static string AudioDir =>
//         "C:/small_data/audio";

//     /* --------------------------------------------------------------
//        Initialise FFmpeg once and return the decoder
//     -------------------------------------------------------------- */
//     private static readonly IAudioService audio = InitFFmpeg();

//     private static IAudioService InitFFmpeg()
//     {
//         string ffmpegPath = Path.Combine(AppContext.BaseDirectory, "FFmpeg",
//                                          "bin",
//                                          Environment.Is64BitProcess ? "x64" : "x86");
//         DynamicallyLoadedBindings.LibrariesPath = ffmpegPath;
//         DynamicallyLoadedBindings.Initialize();   // loads avcodec-60.dll & friends
//         Console.WriteLine($"✓ FFmpeg DLLs loaded from {ffmpegPath}");
//         return new FFmpegAudioService();
//     }

//     /* -------------------------------------------------------------- */
//     private static readonly IModelService model = new InMemoryModelService();

//     private static async Task Main()
//     {
//         Console.OutputEncoding = System.Text.Encoding.UTF8;
//         await IndexAdsAsync();
//         await ScanAudioAsync();
//     }

//     private static async Task IndexAdsAsync()
//     {
//         int secondsToAnalyze = 2; // number of seconds to analyze from query file
//         int startAtSecond = 0; // start at the begining
//         foreach (var file in Directory.GetFiles(AdsDir, "*.*",
//                                                 SearchOption.AllDirectories))
//         {
//             var track = new TrackInfo(Guid.NewGuid().ToString(),
//                                       Path.GetFileNameWithoutExtension(file),
//                                       "Ad");

        
//             var hashes = await FingerprintCommandBuilder.Instance
//                 .BuildFingerprintCommand()
//                 .From(file, secondsToAnalyze, startAtSecond)
//                 .WithFingerprintConfig(config =>
//                 {
//                     config.Audio.SampleRate = 5512; // Set directly if available
//                     config.Audio.Stride = new IncrementalStaticStride(64);
//                     return config;
//                 })
//                 .UsingServices(audio)
//                 .Hash();

//             model.Insert(track, hashes);
//             Console.WriteLine($"   Indexed {track.Title}");
//             Console.WriteLine($"   {hashes.Count} hashes generated.");
//             // Console.WriteLine($"   {hashes.Max(h => h.Confidence):P2} max confidence.");
//             // Console.WriteLine($"   {hashes.Min(h => h.Confidence):P2} min confidence.");
//             Console.WriteLine($"   {hashes}");
//         }

//         Console.WriteLine($"✓ {model.GetTrackIds().Count()} ad(s) ready.\n");
//     }

//     private static async Task ScanAudioAsync()
//     {
//         Console.WriteLine("→ Scanning content …");
//         // int secondsToAnalyze = 1000; // number of seconds to analyze from query file
//         // int startAtSecond = 0; // start at the begining
//         foreach (var file in Directory.GetFiles(AudioDir, "*.*",
//                                                 SearchOption.AllDirectories))
//         {
//             AVQueryResult queryResult = await QueryCommandBuilder.Instance
//                                           .BuildQueryCommand()
//                                         //   .From(file, secondsToAnalyze, startAtSecond)
//                                           .From(file)
//                                           .UsingServices(model, audio)
//                                           .Query();

//             var hits = queryResult.Audio?.ResultEntries ?? Enumerable.Empty<ResultEntry>();

//             foreach (var hit in hits.Where(h => h.Confidence >= 0.20))
//             {
//                 TimeSpan when = TimeSpan.FromSeconds(hit.QueryMatchStartsAt);
//                 Console.WriteLine($"{Path.GetFileName(file)}  →  {hit.Track.Title} @ {when}");
//             }
//         }
//     }
// }