﻿using BinarySerializer;
using BinarySerializer.Audio;
using BinarySerializer.GBA.Audio.GAX;
using System.Collections.Generic;
using System.IO;

namespace gaxm
{
    public static class GAXHelpers 
    {


        public static void ExportSample(string basePath, string directory, string filename, byte[] data, uint sampleRate, ushort channels) {
            // Create the directory
            Directory.CreateDirectory(directory);

            // Create WAV data
            var formatChunk = new WAVFormatChunk() {
                ChunkHeader = "fmt ",
                FormatType = 1,
                ChannelCount = channels,
                SampleRate = sampleRate,
                BitsPerSample = 8,
            };

            var wav = new WAV {
                Magic = "RIFF",
                FileTypeHeader = "WAVE",
                Chunks = new WAVChunk[]
                {
                            formatChunk,
                            new WAVChunk()
                            {
                                ChunkHeader = "data",
                                Data = data
                            }
                }
            };

            formatChunk.ByteRate = (formatChunk.SampleRate * formatChunk.BitsPerSample * formatChunk.ChannelCount) / 8;
            formatChunk.BlockAlign = (ushort)((formatChunk.BitsPerSample * formatChunk.ChannelCount) / 8);

            // Get the output path
            var outputFilePath = Path.Combine(directory, filename + ".wav");

            // Create and open the output file
            using (var outputStream = File.Create(outputFilePath)) {
                // Create a context
                using (var wavContext = new Context(basePath, log: false)) {
                    // Create a key
                    const string wavKey = "wav";

                    // Add the file to the context
                    wavContext.AddFile(new StreamFile(wavContext, wavKey, outputStream));

                    // Write the data
                    FileFactory.Write<WAV>(wavKey, wav, wavContext);
                }
            }
        }

        public static void ExportGAX(string basePath, string mainDirectory, GAX2_Song song, ushort channels) {

            Directory.CreateDirectory(mainDirectory); 
            for (int i = 0; i < song.Samples.Length; i++) {
                var e = song.Samples[i];
                string outPath = Path.Combine(mainDirectory, "samples", $"{song.ParsedName} - {song.ParsedArtist}");
                ExportSample(basePath, outPath, $"{i}_{e.SampleOffset.StringAbsoluteOffset}", e.Sample, 15769, channels);
            }
            var h = song;
            if (h.SampleRate == 0) return;


            GAX2_XMWriter xmw = new GAX2_XMWriter();
            Directory.CreateDirectory(Path.Combine(mainDirectory, "xm"));

            XM xm = xmw.ConvertToXM(h);

            // Get the output path
            var outputFilePath = Path.Combine(mainDirectory, "xm", $"{h.ParsedName}.xm");

            // Create and open the output file
            using (var outputStream = File.Create(outputFilePath)) {
                // Create a context
                using (var xmContext = new Context(basePath, log: Settings.XMLog)) {
                    if (Settings.XMLog) {
                        Directory.CreateDirectory(Settings.XMLogDirectory);
                        xmContext.Log.OverrideLogPath = Path.Combine(Settings.XMLogDirectory, $"{h.ParsedName}.txt");
                    }
                    // Create a key
                    string xmKey = $"{h.ParsedName}.xm";

                    // Add the file to the context
                    xmContext.AddFile(new StreamFile(xmContext, xmKey, outputStream));

                    // Write the data
                    FileFactory.Write<XM>(xmKey, xm, xmContext);
                }
            }

        }

    }

    public class GAXInfo {
        public uint MusicCount { get; set; }
        public uint FXCount { get; set; }
        public uint MusicOffset { get; set; }
        public uint FXOffset { get; set; }
    }
}