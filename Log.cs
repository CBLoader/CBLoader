using System;
using System.Text;
using System.IO;

namespace CBLoader
{
    internal class LogRemoteReceiver : MarshalByRefObject
    {
        private StreamWriter outStream;
        internal string LogFileLocation { get; private set; }
        internal bool ErrorLogged { get; set; } = false;
        internal bool VerboseMode { get; set; } = false;

        public LogRemoteReceiver(string logFileLocation)
        {
            this.LogFileLocation = logFileLocation;

            var file = File.Open(logFileLocation, FileMode.Create, FileAccess.ReadWrite, FileShare.Read);
            this.outStream = new StreamWriter(file);
        }

        public void WriteLogFile(string taggedMsg)
        {
            outStream.WriteLine(DateTime.Now.ToString() + " - " + taggedMsg);
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

        internal static void InitLogging()
        {
            RemoteReceiver = new LogRemoteReceiver(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CBLoader.log"));
            LogPrefix = " ";
        }
        internal static void InitLoggingForChildDomain(LogRemoteReceiver remoteReceiver)
        {
            RemoteReceiver = remoteReceiver;
            LogPrefix = "*";
        }

        public static void Trace(string msg)
        {
            RemoteReceiver.WriteLogFile(LogPrefix + "Trace: " + msg);
        }

        public static void Debug(string msg)
        {
            if (VerboseMode) Console.WriteLine(msg);
            RemoteReceiver.WriteLogFile(LogPrefix + "Debug: " + msg);
        }

        public static void Info(string msg)
        {
            Console.WriteLine(msg);
            RemoteReceiver.WriteLogFile(LogPrefix + "Info : " + msg);
        }

        public static void Error(string msg)
        {
            RemoteReceiver.ErrorLogged = true;
            Console.WriteLine("ERROR: " + msg);
            RemoteReceiver.WriteLogFile(LogPrefix + "ERROR: " + msg);
        }

        private static string ExceptionMessage(Exception e, bool verbose)
        {
            StringBuilder sb = new StringBuilder();
            Exception current = e;
            while (current != null)
            {
                if (current != e)
                    sb.Append("Caused by: ");
                var header = !verbose || current.GetType() == typeof(Exception) ? "" : current.GetType().FullName + ": ";
                sb.AppendLine(header + current.Message);
                if (verbose) sb.AppendLine(current.StackTrace);

                current = current.InnerException;
                if (verbose && current != null)
                    sb.AppendLine();
            }
            return sb.ToString();
        }

        public static void Error(string msg, Exception e)
        {
            RemoteReceiver.ErrorLogged = true;
            Console.WriteLine("ERROR: " + msg + "\n" + ExceptionMessage(e, VerboseMode));
            RemoteReceiver.WriteLogFile(LogPrefix + "ERROR: " + msg + "\n" + ExceptionMessage(e, true));
        }
    }
}
