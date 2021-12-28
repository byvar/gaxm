using BinarySerializer;
using BinarySerializer.GBA.Audio.GAX;
using CommandLine;
using Konsole;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

            // TODO: Allow option for providing the address of a single song, and another option for setting the major GAX version
        }


        static void Main(string[] args) {
            Parser.Default.ParseArguments<Options>(args)
                   .WithParsed<Options>(o => {
                       ParseROM(o);
                   });
        }

        private const int progressSize = 50;
        private const int progressTextWidth = 40;

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

            int gaxVersion = 3;
            GAX_Settings gaxSettings = null;
            Context.ConsoleLog logger = new Context.ConsoleLog();
            List<IGAX_Song> songs = new List<IGAX_Song>();

            using (Context context = new Context(basePath, log: false, verbose: false)) {
                context.AddFile(new MemoryMappedFile(context, filename, 0x08000000, Endian.Little));
                context.GetGAXSettings().EnableErrorChecking = true;
                gaxSettings = context.GetGAXSettings();
                Pointer basePtr = context.FilePointer(filename);
                BinaryDeserializer s = context.Deserializer;
                s.Goto(basePtr);

                // Scan ROM for pointers
                Dictionary<Pointer, List<int>> pointers = FindPointers(s, basePtr);

                // Scan ROM for the version
                gaxVersion = FindVersion(s, pointers, logger, gaxSettings) ?? gaxVersion;

                FastScan(s, pointers, logger, songs);

                if (songs.Count > 0) {
                    var distinctPtrs = songs.Select(sng => sng.Info.InstrumentSetPointer).Distinct().ToArray();
                    foreach (var instrSetPtr in distinctPtrs) {
                        FastScan_FindUnused(s, pointers, logger, songs, instrSetPtr, basePtr);
                    }
                }
            };

            if (songs.Count != 0) {
                // Prevent duplicate names
                var groups = songs.GroupBy(sng => sng.Info.ParsedName);
                foreach (var group in groups) {
                    if (group.Count() > 1) {
                        foreach (var song in group) {
                            song.Info.ParsedName = $"{song.Offset.StringAbsoluteOffset}_{song.Info.ParsedName}";
                        }
                    }
                }

                if (Settings.Log) {
                    // Log song data
                    ProgressBar ProgressBarLog = new ProgressBar(progressSize, progressTextWidth);
                    Console.WriteLine();

                    // Create a separate log file for each song
                    for (int i = 0; i < songs.Count; i++) {
                        var song = songs[i];
                        ProgressBarLog.Refresh((int)((i / (float)songs.Count) * progressSize), $"Logging {i}/{songs.Count}: {song.Info.ParsedName}");

                        using (Context context = new Context(basePath, log: Settings.Log, verbose: true)) {
                            context.SetGAXSettings(gaxSettings);
                            Directory.CreateDirectory(Settings.LogDirectory);
                            ((Context.SerializerLog)context.Log).OverrideLogPath = Path.Combine(Settings.LogDirectory, $"{song.Info.ParsedName}.txt");
                            context.AddFile(new MemoryMappedFile(context, filename, 0x08000000, Endian.Little));
                            var basePtr = context.FilePointer(filename);
                            var s = context.Deserializer;

                            // Re-read song. We could have just done this before,
                            // but we only want to log valid songs, so we do it after we've verified that it's valid
                            s.DoAt(song.Offset, () => {
                                IGAX_Song Song = null;
                                switch (gaxVersion) {
                                    case 1:
                                    case 2:
                                        Song = s.SerializeObject<GAX2_Song>(default, name: nameof(Song));
                                        break;
                                    case 3:
                                        Song = s.SerializeObject<GAX3_Song>(default, name: nameof(Song));
                                        break;
                                }
                            });
                        };
                    }
                    ProgressBarLog.Refresh(progressSize, $"Logging: Finished");
                }


                // Convert songs
                ProgressBar ProgressBarConvert = new ProgressBar(progressSize, progressTextWidth);
                Console.WriteLine();

                for (int i = 0; i < songs.Count; i++) {
                    var song = songs[i];
                    ProgressBarConvert.Refresh((int)((i / (float)songs.Count) * progressSize), $"Converting {i}/{songs.Count}: {song.Info.ParsedName}");
                    try {
                        GAXHelpers.ExportGAX(basePath, options.OutputDirectory, song, 2);
                    } catch(Exception ex) {
                        logger.LogError(ex.ToString());
                    }
                }
                ProgressBarConvert.Refresh(progressSize, $"Converting: Finished");
            }
        }

        private static void FastScan(SerializerObject s, Dictionary<Pointer, List<int>> pointers, Context.ConsoleLog logger, List<IGAX_Song> songs) {
            ProgressBar ProgressBarGaxScan = new ProgressBar(progressSize, progressTextWidth);
            Console.WriteLine();

            int pointerIndex = 0;
            float pointersCountFloat = pointers.Count;
            int gaxVersion = s.GetGAXSettings().MajorVersion;
            var context = s.Context;

            foreach (Pointer p in pointers.Keys) {
                s.DoAt(p, () =>
                {
                    context.Cache.Structs.Clear();
                    context.MemoryMap.ClearPointers();

                    try {
                        IGAX_Song Song = null;
                        switch (gaxVersion) {
                            case 1:
                            case 2:
                                Song = s.SerializeObject<GAX2_Song>(default, name: nameof(Song));
                                break;
                            case 3:
                                Song = s.SerializeObject<GAX3_Song>(default, name: nameof(Song));
                                break;
                        }
                        if (Song.Info.Name.Length <= 4 || !Song.Info.Name.Contains("\" © ")) {
                            throw new Exception($"{Song.Offset}: Incorrect name: {Song.Info.Name}");
                        }
                        logger.Log($"{Song.Offset}: {Song.Info.ParsedName} - {Song.Info.ParsedArtist}");
                        songs.Add(Song);
                    } catch {
                    }

                });

                pointerIndex++;

                if (pointerIndex % 16 == 0 || pointerIndex == pointers.Count)
                    ProgressBarGaxScan.Refresh((int)(pointerIndex / pointersCountFloat * progressSize), $"Scanning: {pointerIndex}/{pointers.Count}");
            }
            Console.WriteLine();
        }

        private static void FastScan_FindUnused(SerializerObject s, Dictionary<Pointer, List<int>> pointers, Context.ConsoleLog logger, List<IGAX_Song> songs, Pointer instrumentSetPointer, Pointer basePtr) {
            ProgressBar ProgressBarGaxScan = new ProgressBar(progressSize, progressTextWidth);
            Console.WriteLine();

            int pointerIndex = 0;
            float pointersCountFloat = pointers[instrumentSetPointer].Count;
            int gaxVersion = s.GetGAXSettings().MajorVersion;
            var context = s.Context;

            var instrSetPtrOffsetInGAX_SongInfo = 16;
            if (s.GetGAXSettings().MajorVersion < 2 && !(s.GetGAXSettings().MinorVersion == 99 && s.GetGAXSettings().MinorVersionAdd?.ToLower() == "f")) {
                instrSetPtrOffsetInGAX_SongInfo -= 4;
            }

            foreach (var ptr in pointers[instrumentSetPointer]) {
                var songInfoPtr = basePtr + ptr - instrSetPtrOffsetInGAX_SongInfo;
                switch (gaxVersion) {
                    case 1:
                    case 2:
                        if (!pointers.ContainsKey(songInfoPtr)) break;
                        foreach (var ptr2 in pointers[songInfoPtr]) {
                            var soundHandlerPtr = basePtr + ptr2 - 0x18;
                            if (!pointers.ContainsKey(soundHandlerPtr)) continue;
                            foreach (var ptr3 in pointers[soundHandlerPtr]) {
                                var gax2SongPtr = basePtr + ptr3 - 0x8;
                                if(pointers.ContainsKey(gax2SongPtr) || songs.Any(sng => sng.Offset == gax2SongPtr)) continue;

                                s.DoAt(gax2SongPtr, () => {
                                    context.Cache.Structs.Clear();
                                    context.MemoryMap.ClearPointers();

                                    try {
                                        IGAX_Song Song = s.SerializeObject<GAX2_Song>(default, name: nameof(Song));
                                        if (Song.Info.Name.Length <= 4 || !Song.Info.Name.Contains("\" © ")) {
                                            throw new Exception($"{Song.Offset}: Incorrect name: {Song.Info.Name}");
                                        }
                                        logger.Log($"{Song.Offset}: {Song.Info.ParsedName} - {Song.Info.ParsedArtist}");
                                        songs.Add(Song);
                                    } catch {
                                    }
                                });
                            }
                        }
                        break;
                    case 3:
                        var gax3SongPtr = songInfoPtr;
                        if (pointers.ContainsKey(gax3SongPtr)) break;
                        s.DoAt(gax3SongPtr, () => {
                            context.Cache.Structs.Clear();
                            context.MemoryMap.ClearPointers();

                            try {
                                IGAX_Song Song = s.SerializeObject<GAX3_Song>(default, name: nameof(Song));
                                if (Song.Info.Name.Length <= 4 || !Song.Info.Name.Contains("\" © ")) {
                                    throw new Exception($"{Song.Offset}: Incorrect name: {Song.Info.Name}");
                                }
                                logger.Log($"{Song.Offset}: {Song.Info.ParsedName} - {Song.Info.ParsedArtist}");
                                songs.Add(Song);
                            } catch {
                            }
                        });
                        break;
                }

                pointerIndex++;

                ProgressBarGaxScan.Refresh((int)(pointerIndex / pointersCountFloat * progressSize), $"Scanning Unreferenced: {pointerIndex}/{pointers[instrumentSetPointer].Count}");
            }
            Console.WriteLine();
        }

        private static Dictionary<Pointer, List<int>> FindPointers(SerializerObject s, Pointer basePtr)
        {
            ProgressBar progressBarPointerScan = new ProgressBar(progressSize, progressTextWidth);
            Console.WriteLine();

            long len = s.CurrentLength - 4;
            float lenFloat = len;
            int curPtr = 0;

            // Find all pointers in the ROM and attempt to find the GAX structs from those
            var pointers = new Dictionary<Pointer, List<int>>();

            while (curPtr < len) {
                Pointer p = s.DoAt(basePtr + curPtr, () => s.SerializePointer(default, allowInvalid: true));

                if (p != null) {
                    if (!pointers.ContainsKey(p)) pointers[p] = new List<int>();
                    pointers[p].Add(curPtr);
                }

                curPtr += 4;

                if (curPtr % (1 << 16) == 0 || curPtr == len)
                    progressBarPointerScan.Refresh((int)((curPtr / lenFloat) * progressSize), $"Scanning pointers: {curPtr:X8}/{len:X8}");
            }

            return pointers;
        }

        private static int? FindVersion(SerializerObject s, Dictionary<Pointer, List<int>> pointers, ILogger logger, GAX_Settings gaxSettings)
        {
            ProgressBar progressBarVersionScan = new ProgressBar(progressSize, progressTextWidth);
            Console.WriteLine();

            bool success = false;

            int pointerIndex = 0;
            float pointersCountFloat = pointers.Count;

            foreach (Pointer p in pointers.Keys)
            {
                s.DoAt(p, () => success = s.GetGAXSettings().SerializeVersion(s));

                pointerIndex++;

                if (pointerIndex == pointers.Count || success) {
                    progressBarVersionScan.Refresh(progressSize, "Version scan: Finished");
                } else if (pointerIndex % 16 == 0) {
                    progressBarVersionScan.Refresh((int)(pointerIndex / pointersCountFloat * progressSize), $"Version scan: {pointerIndex}/{pointers.Count}");
                }

                if (success)
                {
                    int gaxVersion = gaxSettings.MajorVersion;
                    logger.Log($"{p}:");
                    logger.Log(gaxSettings.FullVersionString);
                    logger.Log($"Parsed GAX version: {gaxVersion}");
                    Console.WriteLine();
                    return gaxVersion;
                }
            }

            logger.Log($"GAX version string not found. Assuming GAX version {gaxSettings.MajorVersion}");
            Console.WriteLine();

            return null;
        }
    }
}
