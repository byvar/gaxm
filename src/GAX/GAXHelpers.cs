﻿using BinarySerializer;
using BinarySerializer.Audio;
using BinarySerializer.Audio.GBA.GAX;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace gaxm
{
    public static class GAXHelpers 
    {


        public static void ExportSample(string basePath, string directory, string filename, byte[] data, uint sampleRate, ushort channels) {
            // Create the directory
            Directory.CreateDirectory(directory);

            // Create WAV data
            var wav = new WAV();
            var fmt = wav.Format;
            fmt.FormatType = 1;
            fmt.ChannelCount = channels;
            fmt.SampleRate = sampleRate;
            fmt.BitsPerSample = 8;
            wav.Data.Data = data;

            fmt.ByteRate = (fmt.SampleRate * fmt.BitsPerSample * fmt.ChannelCount) / 8;
            fmt.BlockAlign = (ushort)((fmt.BitsPerSample * fmt.ChannelCount) / 8);

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
                    FileFactory.Write<WAV>(wavContext, wavKey, wav);
                }
            }
        }

        public static void ExportGAX(string basePath, string mainDirectory, IGAX_Song song, ushort channels) {

            Directory.CreateDirectory(mainDirectory); 
            for (int i = 0; i < song.Info.Samples.Length; i++) {
                var e = song.Info.Samples[i];
                if(e.SampleOffset == null) continue;
                string outPath = Path.Combine(mainDirectory, "samples", $"{song.Info.ParsedName} - {song.Info.ParsedArtist}");
                if (song.Info.Context.GetGAXSettings().MajorVersion < 3) {
                    ExportSample(basePath, outPath, $"{i}_{e.SampleOffset.StringAbsoluteOffset}", e.SampleSigned.Select(b => (byte)(b+128)).ToArray(), 15769, channels);
                } else {
                    ExportSample(basePath, outPath, $"{i}_{e.SampleOffset.StringAbsoluteOffset}", e.SampleUnsigned, 15769, channels);
                }
            }
            var h = song;
            if (h.Info.SampleRate == 0 && h.Info.Context.GetGAXSettings().MajorVersion >= 2) return;


            GAX_XMWriter xmw = new GAX_XMWriter();
            Directory.CreateDirectory(Path.Combine(mainDirectory, "xm"));

            XM xm = xmw.ConvertToXM(h);

            // Get the output path
            var outputFilePath = Path.Combine(mainDirectory, "xm", $"{h.Info.ParsedName}.xm");

            // Create and open the output file
            using (var outputStream = File.Create(outputFilePath)) {
                // Create a context
                using (var xmContext = new Context(basePath, log: Settings.XMLog)) {
                    if (Settings.XMLog) {
                        Directory.CreateDirectory(Settings.XMLogDirectory);
                        ((Context.SimpleSerializerLogger)xmContext.SerializerLogger).OverrideLogPath = Path.Combine(Settings.XMLogDirectory, $"{h.Info.ParsedName}.txt");
                    }
                    // Create a key
                    string xmKey = $"{h.Info.ParsedName}.xm";

                    // Add the file to the context
                    xmContext.AddFile(new StreamFile(xmContext, xmKey, outputStream));

                    // Write the data
                    FileFactory.Write<XM>(xmContext, xmKey, xm);
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