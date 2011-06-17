using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace CharacterBuilderLoader
{
    public static class Log
    {
        static Log()
        {
            LogFile = FileManager.BasePath + "/log.txt";
            if (File.Exists(LogFile))
                File.WriteAllText(LogFile, "");  // Resets the contents without actually deleting the file.
        }
        public static string LogFile { get; private set; }
        public static bool ErrorLogged { get; private set; }
        public static bool VerboseMode { get; set; }
        public static void Debug(string msg)
        {
            if (VerboseMode)
            {
                Console.WriteLine("Debug: " + msg);
                writeToFile("Debug: " + msg);
            }
        }

        public static void Info(string msg)
        {
            Console.WriteLine(msg);
            writeToFile("INFO: " + msg);
        }

        public static void Error(string msg)
        {
            ErrorLogged = true;
            string taggedMsg = "ERROR: " + msg;
            Console.WriteLine(taggedMsg);
            writeToFile(taggedMsg);
        }

        private static void writeToFile(string taggedMsg)
        {
            using (StreamWriter sw = new StreamWriter(
                new FileStream(LogFile, FileMode.Append)))
            {
                sw.WriteLine(DateTime.Now.ToString() + " - " + taggedMsg);
            }
        }

        public static void Error(string msg, Exception e)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(msg);
            Exception current = e;
            int tabCount = 0;
            while (current != null)
            {
                sb.Append("Inner Exception: ");
                sb.AppendLine("".PadLeft(tabCount * 3, ' ') + current.Message);
                if (VerboseMode)
                {
                    sb.AppendLine("".PadLeft(tabCount * 3, ' ') + current.StackTrace);
                    sb.AppendLine("".PadLeft(80, '-'));
                    sb.AppendLine();
                }

                current = current.InnerException;
                tabCount++;
            }
            Error(sb.ToString());
        }
    }
}
