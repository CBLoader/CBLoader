﻿using System;
using System.Text;
using System.IO;
using System.Runtime.CompilerServices;

namespace CBLoader
{
    internal class LogRemoteReceiver : PersistantRemoteObject
    {
        private StreamWriter outStream;
        internal string LogFileLocation { get; private set; }
        internal bool ErrorLogged { get; set; } = false;
        internal bool VerboseMode { get; set; } = false;

        public LogRemoteReceiver(string logFileLocation)
        {
            this.LogFileLocation = logFileLocation;

            var file = File.Open(logFileLocation, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);

            try
            {
                this.outStream = new StreamWriter(file);
            }
            catch (Exception e)
            {
                Console.WriteLine(Log.FallbackFormatError("Failed to initialize logging!", e));
                Console.WriteLine();
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void WriteLogFile(string taggedMsg)
        {
            if (outStream == null) return;
            outStream.WriteLine(Utils.NormalizeLineEndings($"[{DateTime.Now}] {taggedMsg}"));
            outStream.Flush();
        }
    }

    public static class Log
    {
        internal static LogRemoteReceiver RemoteReceiver { get; private set; }
        private static string LogPrefix;

        internal static string LogFileLocation { get => RemoteReceiver.LogFileLocation; }
        internal static bool ErrorLogged { get => RemoteReceiver.ErrorLogged; }
        internal static bool VerboseMode { get => RemoteReceiver.VerboseMode;
                                           set => RemoteReceiver.VerboseMode = value; }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal static void InitLogging()
        {
            RemoteReceiver = new LogRemoteReceiver(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CBLoader.log"));
            LogPrefix = " ";
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        internal static void InitLoggingForChildDomain(LogRemoteReceiver remoteReceiver)
        {
            RemoteReceiver = remoteReceiver;
            LogPrefix = "*";
        }
        
        private static string ExceptionMessage(Exception e, string message, bool verbose)
        {
            message = message ?? "";
            if (e == null) return message;

            var sb = new StringBuilder();
            sb.Append(message);

            var current = e;
            var hasLastLine = message != "";
            while (current != null)
            {
                if (hasLastLine) sb.Append("\nCaused by: ");

                var excString = current.ToString().Trim();
                if (!verbose && current.Message != null && current.Message != "")
                    excString = excString.Split(new char[] { ':' }, 2)[1].Trim();
                sb.Append(excString);

                current = current.InnerException;
            }
            return sb.ToString();
        }
        private static void BaseLog(string tag, bool alwaysTag, bool printConsole, string message, Exception e)
        {
            if (printConsole)
                Console.WriteLine(ExceptionMessage(e, $"{(alwaysTag ? $"{tag}: " : "")}{message}", VerboseMode));
            RemoteReceiver.WriteLogFile(ExceptionMessage(e, $"{LogPrefix}{tag.PadRight(5)}: {message}", true));
        }

        internal static string FallbackFormatError(string msg = null, Exception e = null) =>
            ExceptionMessage(e, $"Error: {msg}", true);

        public static void Trace(string msg = null, Exception e = null) =>
            BaseLog("Trace", false, false, msg, e);
        public static void Debug(string msg = null, Exception e = null) =>
            BaseLog("Debug", false, VerboseMode, msg, e);
        public static void Info(string msg = null, Exception e = null) =>
            BaseLog("Info", false, true, msg, e);
        public static void Warn(string msg = null, Exception e = null) =>
            BaseLog("Warn", false, true, msg, e);
        public static void Error(string msg = null, Exception e = null)
        {
            RemoteReceiver.ErrorLogged = true;
            BaseLog("Error", true, true, msg, e);
            //Program.sentry.Capture(new SharpRaven.Data.SentryEvent(e) { Message = msg });
        }
    }
}
