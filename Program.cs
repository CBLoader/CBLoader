using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Samples.Debugging.Native;
using System.Xml.Linq;
using System.Xml;
using ApplicationUpdate.Client;

namespace CharacterBuilderLoader
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                FileManager fm = new FileManager();
                bool loadExec = true;
                bool forcedReload = false;
                if (args != null && args.Length > 0)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "-e")
                            forcedReload = true;
                        else if (args[i] == "-n")
                            loadExec = false;
                        else if (args[i] == "-f")
                        {
                            if(args.Length > i+1)
                                fm.CustomFolders.Add(args[i++]);
                            else {
                                displayHelp();
                                return;
                            }
                        }
                        else
                        {
                            displayHelp();
                            return;
                        }
                    }
                }
                fm.ExtractAndMerge(forcedReload);

                if (loadExec)
                    ProcessManager.StartProcess();
            }
            catch (Exception e)
            {
                Exception current = e;
                int tabCount = 0;
                while (current != null)
                {
                    Console.WriteLine("".PadLeft(tabCount*3,' ') + e.Message);
                    current = e.InnerException;
                    tabCount++;
                }
                Console.ReadKey();
            }
        }

        private static void displayHelp()
        {
            Console.WriteLine("Usage: CBLoader.exe [-p] [-e] [-n] [-f customFolder]");
            Console.WriteLine("\t-e\tRe-Extract and Re-Merge the xml files");
            Console.WriteLine("\t-n\tDo not load the executable");
            Console.WriteLine("\t-f\tSpecifies a folder containing custom rules files. This switch can be specified multiple times");
            Console.WriteLine("\t-h\tDisplay this help.");
            Console.WriteLine("If the patched files do not exist, and -n is not specified, they will be created");
        }
    }
}
