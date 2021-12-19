using BinarySerializer;
using BinarySerializer.GBA.Audio.GAX;
using CommandLine;
using Konsole;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace gaxm {
	class Program {
        public class Options {
            [Option('i', "input", Required = true, HelpText = "Input file to be processed.")]
            public string InputFile { get; set; }

            [Option('o', "output", Required = false, HelpText = "Directory to save files in. Set to the file's basename if not specified.")]
            public string OutputDirectory { get; set; }

            [Option('l', "log", Required = false, HelpText = "Directory to store serializer logs in. Logging disabled if not specified.")]
            public string LogDirectory { get; set; }

            [Option('x', "xmlog", Required = false, HelpText = "Directory to store XM logs in. Logging disabled if not specified.")]
            public string XMLogDirectory { get; set; }

            [Option('v', "verbose", Required = false, HelpText = "Set output to verbose messages.")]
            public bool Verbose { get; set; }

        }


        static void Main(string[] args) {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o => {
                       ParseROM(o);
                   });
        }


        private static void ParseROM(Options options) {
            Settings.Verbose = options.Verbose;
            Settings.Log = !string.IsNullOrEmpty(options.LogDirectory);
            Settings.LogDirectory = options.LogDirectory;
            Settings.XMLog = !string.IsNullOrEmpty(options.XMLogDirectory);
            Settings.XMLogDirectory = options.XMLogDirectory;

            string fullPath = Path.GetFullPath(options.InputFile);
            string basePath = Path.GetDirectoryName(fullPath);
            string filename = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(options.OutputDirectory)) {
                options.OutputDirectory = Path.GetFileNameWithoutExtension(fullPath);
            }

            int progressSize = 30;
            ProgressBar ProgressBarScan = new ProgressBar(progressSize);
            Console.WriteLine();

            Context.ConsoleLog logger = new Context.ConsoleLog();
            List<GAX3_Song> songs = new List<GAX3_Song>();

            using (Context context = new Context(basePath, log: false, verbose: false)) {
                context.AddFile(new MemoryMappedFile(context, filename, 0x08000000, Endian.Little));
                context.GetGAXSettings().EnableErrorChecking = true;
                var basePtr = context.FilePointer(filename);
                var s = context.Deserializer;
                s.Goto(basePtr);

                // Scan ROM 1
                long curPtr = 0;
                long len = s.CurrentLength - 16;
                while (curPtr < len) {
                    bool success = false;
                    s.DoAt(basePtr + curPtr, () => {
                        success = s.GetGAXSettings().SerializeVersion(s);
                    });
                    if (success) {
                        logger.Log(s.GetGAXSettings().MajorVersion);
                        break;
                    }
                    curPtr += 4;
                }

                 // Scan ROM
                curPtr = 0;
                len = s.CurrentLength - 200;
                while (curPtr < len) {
                    s.DoAt(basePtr + curPtr, () => {
                        context.Cache.Structs.Clear();
                        context.MemoryMap.ClearPointers();

                        try {
                            GAX3_Song Song = null;
                            Song = s.SerializeObject<GAX3_Song>(Song, name: nameof(Song));
                            if (Song.Info.Name.Length <= 4 || !Song.Info.Name.Contains("\" © ")) {
                                throw new BinarySerializableException(Song, $"Incorrect name: {Song.Info.Name}");
                            }
                            logger.Log($"{Song.Offset}: {Song.Info.ParsedName} - {Song.Info.ParsedArtist}");
                            songs.Add(Song);
                        } catch {
                        }

                    });
                    curPtr += 4;
                    //if (curPtr % (1 << 12) == 0) logger.Log($"{curPtr:X8}/{len:X8}");
                    if (curPtr % (1 << 16) == 0 || curPtr == len) ProgressBarScan.Refresh((int)((curPtr / (float)len) * progressSize), $"Scanning: {curPtr:X8}/{len:X8}");
                }
            };
            Console.WriteLine();

            if (songs.Count != 0) {
                if (Settings.Log) {
                    // Log song data
                    ProgressBar ProgressBarLog = new ProgressBar(progressSize);
                    Console.WriteLine();

                    // Create a separate log file for each song
                    for (int i = 0; i < songs.Count; i++) {
                        var song = songs[i];
                        ProgressBarLog.Refresh((int)((i / (float)songs.Count) * progressSize), $"Logging {i}/{songs.Count}: {song.Info.Name}");

                        using (Context context = new Context(basePath, log: Settings.Log, verbose: false)) {
                            Directory.CreateDirectory(Settings.LogDirectory);
                            context.Log.OverrideLogPath = Path.Combine(Settings.LogDirectory, $"{song.Info.ParsedName}.txt");
                            context.AddFile(new MemoryMappedFile(context, filename, 0x08000000, Endian.Little));
                            var basePtr = context.FilePointer(filename);
                            var s = context.Deserializer;

                            // Re-read song. We could have just done this before,
                            // but we only want to log valid songs, so we do it after we've verified that it's valid
                            s.DoAt(song.Offset, () => {
                                GAX3_Song Song = null;
                                Song = s.SerializeObject<GAX3_Song>(Song, name: nameof(Song));
                            });
                        };
                    }
                    ProgressBarLog.Refresh(progressSize, $"Logging: Finished");
                }


                // Convert songs
                ProgressBar ProgressBarConvert = new ProgressBar(progressSize);
                Console.WriteLine();

                for (int i = 0; i < songs.Count; i++) {
                    var song = songs[i];
                    ProgressBarConvert.Refresh((int)((i / (float)songs.Count) * progressSize), $"Converting {i}/{songs.Count}: {song.Info.Name}");
                    try {
                        GAXHelpers.ExportGAX(basePath, options.OutputDirectory, song, 2);
                    } catch(Exception ex) {
                        logger.LogError(ex.ToString());
                    }
                }
                ProgressBarConvert.Refresh(progressSize, $"Converting: Finished");
            }
        }
    }
}
