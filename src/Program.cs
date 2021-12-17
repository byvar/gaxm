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

            [Option('o', "output", Required = true, HelpText = "Directory to save files in.")]
            public string OutputDirectory { get; set; }

            [Option('l', "log", Required = false, HelpText = "Directory to store serializer logs in.")]
            public string LogDirectory { get; set; }

            [Option('x', "xmlog", Required = false, HelpText = "Directory to store XM logs in.")]
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

            int progressSize = 30;
            ProgressBar ProgressBarScan = new ProgressBar(progressSize);
            Console.WriteLine();

            Context.ConsoleLog logger = new Context.ConsoleLog();
            List<GAX2_Song> songs = new List<GAX2_Song>();

            using (Context context = new Context(basePath, log: false, verbose: false)) {
                context.AddFile(new MemoryMappedFile(context, filename, 0x08000000, Endian.Little));
                var basePtr = context.FilePointer(filename);
                var s = context.Deserializer;
                s.Goto(basePtr);

                // Scan ROM
                uint curPtr = 0;
                var len = s.CurrentLength - 0x200;
                while (curPtr < len) {
                    s.DoAt(basePtr + curPtr, () => {
                        context.Cache.Structs.Clear();
                        context.MemoryMap.ClearPointers();

                        try {
                            GAX2_Song Song = null;
                            Song = s.SerializeObject<GAX2_Song>(Song, name: nameof(Song));
                            if (Song.Name.Length <= 4 || !Song.Name.Contains("\" © ")) {
                                throw new BinarySerializableException(Song, $"Incorrect name: {Song.Name}");
                            }
                            logger.Log($"{Song.Offset}: {Song.ParsedName} - {Song.ParsedArtist}");
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
                        ProgressBarLog.Refresh((int)((i / (float)songs.Count) * progressSize), $"Logging {i}/{songs.Count}: {song.Name}");

                        using (Context context = new Context(basePath, log: Settings.Log, verbose: false)) {
                            Directory.CreateDirectory(Settings.LogDirectory);
                            context.Log.OverrideLogPath = Path.Combine(Settings.LogDirectory, $"{song.ParsedName}.txt");
                            context.AddFile(new MemoryMappedFile(context, filename, 0x08000000, Endian.Little));
                            var basePtr = context.FilePointer(filename);
                            var s = context.Deserializer;

                            // Re-read song. We could have just done this before,
                            // but we only want to log valid songs, so we do it after we've verified that it's valid
                            s.DoAt(song.Offset, () => {
                                GAX2_Song Song = null;
                                Song = s.SerializeObject<GAX2_Song>(Song, name: nameof(Song));
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
                    ProgressBarConvert.Refresh((int)((i / (float)songs.Count) * progressSize), $"Converting {i}/{songs.Count}: {song.Name}");
                    try {
                        GAXHelpers.ExportGAX(basePath, options.OutputDirectory, song, 2);
                    } catch {
                    }
                }
                ProgressBarConvert.Refresh(progressSize, $"Converting: Finished");
            }
        }
    }
}
