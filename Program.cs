using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.IO;
using ApplicationUpdate.Client;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Samples.Debugging.Native;
using System.Xml.Linq;
using System.Xml;

namespace CharacterBuilderLoader
{
    class Program
    {
        private static string ENCRYPTED_FILENAME = "combined.dnd40.encrypted";
        private static string CORE_FILENAME = "combined.dnd40.main";
        private static string PART_FILENAME = "combined.dnd40.part";
        private static string FINAL_FILENAME = "combined.dnd40";
        private static string LOADED_FILE = "D20RulesEngine.dll";
        private static string EXECUTABLE = "CharacterBuilder.exe";
        private static Guid applicationID = new Guid("2a1ddbc4-4503-4392-9548-d0010d1ba9b1");
        static void Main(string[] args)
        {
            try
            {
                bool usePatchedFiles = true;
                bool loadExec = true;
                if (args != null && args.Length > 0)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        if (args[i] == "-e")
                            extractFile();
                        else if (args[i] == "-n")
                            loadExec = false;
                        else
                        {
                            displayHelp();
                            return;
                        }
                    }
                }
                if (loadExec)
                    startCB(usePatchedFiles);
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
            Console.WriteLine("Usage: CBLoader.exe [-p] [-e] [-n] [-f]");
            Console.WriteLine("\t-e\tRe-Extract the xml file");
            Console.WriteLine("\t-n\tDo not load the executable");
            Console.WriteLine("\t-h\tDisplay this help.");
            Console.WriteLine("If the patched .dll does not exist, and -n is not specified, they will be created");
        }

        private static void startCB(bool usePatchedDLL)
        {
            if (usePatchedDLL)
            {
                if (!File.Exists(CORE_FILENAME) || File.GetLastWriteTime(ENCRYPTED_FILENAME) > File.GetLastWriteTime(CORE_FILENAME))
                    extractFile();
                mergeFiles();
            }
            //// start character builder
            PEFile file = new PEFile(LOADED_FILE);
            NativePipeline np = new NativePipeline();
            NativeDbgProcess proc = np.CreateProcessDebug(EXECUTABLE, "");
            while (true)
            {
                NativeEvent ne = np.WaitForDebugEventInfinite();
                if (ne.EventCode == NativeDebugEventCode.LOAD_DLL_DEBUG_EVENT)
                {
                    DllBaseNativeEvent ev = (DllBaseNativeEvent)ne;
                    if (ev.Module.Name.Contains(LOADED_FILE))
                    {
                        patchMemory(file, ev, (uint)proc.Id);
                        np.ContinueEvent(ne);
                        np.Detach(proc);
                        break;
                    }
                }
                np.ContinueEvent(ne);
            }
        }

        private static void mergeFiles()
        {
            if (!File.Exists(CORE_FILENAME))
                throw new Exception("Error, could not find file: " + CORE_FILENAME);
            if (!File.Exists(PART_FILENAME))
            {
                File.Copy(CORE_FILENAME, FINAL_FILENAME, true);
                return;
            }
            if (File.Exists(FINAL_FILENAME))
            {
                DateTime touched = File.GetLastWriteTime(FINAL_FILENAME);
                if (touched >= File.GetLastWriteTime(CORE_FILENAME) && touched >= File.GetLastWriteTime(PART_FILENAME))
                    return;
            }
            XDocument main = (XDocument)XDocument.Load(CORE_FILENAME);
            XDocument part = (XDocument)XDocument.Load(PART_FILENAME);
            // replace all main elements with part elements having the same internal-id
            foreach (XElement customRule in part.Root.Descendants(XName.Get("RulesElement")))
            {
                string id = getID(customRule);
                if (id != null)
                {
                    XElement el = main.Root.Descendants(XName.Get("RulesElement")).FirstOrDefault(xe => getID(xe) == id);
                    if (el == null)
                        main.Root.Add(customRule);
                    else
                        el.ReplaceWith(customRule);
                }
            }
            main.Save(FINAL_FILENAME);
        }

        private static string getID(XElement customRule)
        {
            XAttribute attrib = customRule.Attribute(XName.Get("internal-id"));
            string id = null;
            if (attrib != null)
            {
                id = attrib.Value;
                //try to find this id in main
            }
            return id;
        }

        private static void extractFile()
        {

            using (StreamReader sr = new StreamReader(CommonMethods.GetDecryptedStream(applicationID, ENCRYPTED_FILENAME)))
            {
                using (StreamWriter sw = new StreamWriter(CORE_FILENAME))
                {
                    sw.Write(sr.ReadToEnd());
                }
            }
            if (!File.Exists(PART_FILENAME))
            {
                using (StreamWriter sw = new StreamWriter(PART_FILENAME))
                {
                    sw.WriteLine("<D20Rules game-system=\"D&amp;D4E\">");
                    sw.WriteLine("</D20Rules>");
                }
            }
        }

        private static void patchMemory(PEFile file, DllBaseNativeEvent ev, uint processID)
        {
            ProcessMemoryReader pmr = new ProcessMemoryReader();
            pmr.ReadProcessID = processID;
            pmr.OpenProcess();
            try
            {

                MethodDef def = file.FindCodeForMethodName("D20RulesEngine.LoadRulesDatabase");
                MethodDef def2 = file.FindCodeForMethodName("D20RulesEngine.LoadRulesFile");

                // we're looking for where LoadRulesDatabase calls LoadRules File using a 'true' boolean to indicate using an encrypted file
                // we're just changing that true to a false
                byte[] pattern = new byte[6];
                // Push 1 onto the stack
                pattern[0] = 0x17;
                // Call operation
                pattern[1] = 0x28;
                Array.ConstrainedCopy(file.GetMetaDataToken(def2), 0, pattern, 2, 4);
                int patternIndex = findPatternIndex(def, pattern);
                // if we found it, patch. Otherwise, assume we've already patched
                if (patternIndex > -1)
                {
                    int RVA = def.GetByteOffset(file, patternIndex);
                    int memoryOffset = ev.Module.BaseAddress.ToInt32() + RVA;
                    int writtenBytes;
                    // change the push 1 to a push 0
                    byte[] data = new byte[] { 0x16 };
                    pmr.WriteProcessMemory(new IntPtr(memoryOffset), data, out writtenBytes);

                    // now, modify the filename that is loaded from ENCRYPTED_FILENAME to FINAL_FILENAME (final filename is a substring of encrypted filename)
                    int excessBytes = (ENCRYPTED_FILENAME.Length - FINAL_FILENAME.Length)*2;
                    // this should already be initialized to 0's
                    data = new byte[excessBytes];
                    
                    // find the location of the filename and move to the spot that we want to null out
                    // TODO: remove this hardcoded string location
                    memoryOffset = ev.Module.BaseAddress.ToInt32() + 0x411D4 + FINAL_FILENAME.Length * 2;
                    // null out the end bytes of the string
                    pmr.WriteProcessMemory(new IntPtr(memoryOffset), data, out writtenBytes);
                }
                else
                    Console.WriteLine("This file already appears to be patched, or the code has changed");
            }
            finally
            {
                pmr.CloseHandle();
            }
        }

        private static int findPatternIndex(MethodDef def, byte[] pattern)
        {
            for (int i = 0; i < def.Method.Code.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (def.Method.Code[i + j] != pattern[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }

    }
}
