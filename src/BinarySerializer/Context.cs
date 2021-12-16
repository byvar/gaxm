using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BinarySerializer;
using ILogger = BinarySerializer.ILogger;

namespace gaxm {
    public class Context : BinarySerializer.Context {
        public Context(string basePath, bool log = true) : base(
            basePath: basePath, // Pass in the base path
            settings: new SerializerSettings(), // Pass in the settings
            serializerLog: log ? new SerializerLog() : null, // Use serializer log for logging to a file
            fileManager: null,
            logger: new ConsoleLog()) // Use console log
        {}

        public class SerializerSettings : ISerializerSettings {
            /// <summary>
            /// The default string encoding to use when none is specified
            /// </summary>
            public Encoding DefaultStringEncoding => Encoding.GetEncoding(437);

            /// <summary>
            /// Indicates if a backup file should be created when writing to a file
            /// </summary>
            public bool CreateBackupOnWrite => false;

            /// <summary>
            /// Indicates if pointers should be saved in the Memory Map for relocation
            /// </summary>
            public bool SavePointersForRelocation => false;

            /// <summary>
            /// Indicates if caching read objects should be ignored
            /// </summary>
            public bool IgnoreCacheOnRead => false;

            /// <summary>
            /// The pointer size to use when logging a <see cref="Pointer"/>. Set to <see langword="null"/> to dynamically determine the appropriate size.
            /// </summary>
            public PointerSize? LoggingPointerSize => PointerSize.Pointer32;
        }

        public class ConsoleLog : ILogger {
            public void Log(object log) => Console.WriteLine(log);
            public void LogWarning(object log) => Console.WriteLine(log);
            public void LogError(object log) => Console.WriteLine(log);
        }

        public class SerializerLog : ISerializerLog {
            public bool IsEnabled => gaxm.Settings.Log;

            private StreamWriter _logWriter;

            protected StreamWriter LogWriter => _logWriter ??= GetFile();

            public string OverrideLogPath { get; set; }
            public string LogFile => OverrideLogPath ?? gaxm.Settings.LogFile;
            public int BufferSize => 0x8000000; // 1 GB

            public StreamWriter GetFile() {
                return new StreamWriter(File.Open(LogFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8, BufferSize);
            }

            public void Log(object obj) {
                if (IsEnabled)
                    LogWriter.WriteLine(obj != null ? obj.ToString() : "");
            }

            public void Dispose() {
                _logWriter?.Dispose();
                _logWriter = null;
            }
        }
    }
}