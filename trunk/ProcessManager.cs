using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Samples.Debugging.Native;
using System.IO;
using System.Diagnostics;

namespace CharacterBuilderLoader
{
    public class ProcessManager
    {
        private static string LOADED_FILE = "D20RulesEngine.dll";
        private static string LOADED_FILE_BAK = LOADED_FILE + ".bak";
        private static string EXECUTABLE = "CharacterBuilder.exe";
        private static string EXECUTABLE_ARGS = "";


        /// <summary>
        /// Patches the on-disk copy of LOADED_FILE
        /// </summary>
        public static void StartProcessAndPatchFile()
        {
            Log.Debug("About to patch: " + LOADED_FILE);
            patchFile();
            Process.Start(EXECUTABLE, EXECUTABLE_ARGS);
            
        }

        /// <summary>
        /// Starts the characterbuilder and modifies the in-memory representation of LOADED_FILE to
        /// support unencrypted data. 
        /// </summary>
        /// <param name="patchFile"></param>
        public static void StartProcessAndPatchMemory()
        {
            Log.Debug("About to start loading character builder.");
            //// start character builder
            NativePipeline np = new NativePipeline();
            Log.Debug("Creating process and attaching debugger");
            NativeDbgProcess proc = np.CreateProcessDebug(EXECUTABLE, EXECUTABLE_ARGS);
            while (true)
            {
                Log.Debug("Waiting for next event");
                NativeEvent ne = np.WaitForDebugEventInfinite();
                Log.Debug(ne.ToString());
                ne.Process.HandleIfLoaderBreakpoint(ne);
                if (ne.EventCode == NativeDebugEventCode.LOAD_DLL_DEBUG_EVENT)
                {
                    DllBaseNativeEvent ev = (DllBaseNativeEvent)ne;
                    if (ev.Module.Name.Contains(LOADED_FILE))
                    {
                        patchMemory(ev, (uint)proc.Id);
                        np.ContinueEvent(ne);
                        np.Detach(proc);
                        break;
                    }
                }
                np.ContinueEvent(ne);
            }
        }

        private static void patchFile()
        {
                patch(CopyIfNecessary,
                    (file, RVA) => file.FindSectionForRVA(RVA).CalculateFileOffset(RVA),
                    writeToFile);
            
        }

        private static void CopyIfNecessary()
        {
            if (!File.Exists(LOADED_FILE_BAK))
                File.Copy(LOADED_FILE, LOADED_FILE_BAK, true);
        }

        private static void writeToFile(int offset, byte[] data)
        {
            using (FileStream sw = new FileStream(LOADED_FILE, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                sw.Position = offset;
                sw.Write(data, 0, data.Length);
            }
        }

        private static void patchMemory(DllBaseNativeEvent ev, uint processID)
        {
            ProcessMemoryReader pmr = new ProcessMemoryReader();
            pmr.ReadProcessID = processID;
            pmr.OpenProcess();
            try
            {
                int writtenBytes;
                patch(null,
                    (file, RVA) => ev.Module.BaseAddress.ToInt32() + RVA,
                    (offset,data) => pmr.WriteProcessMemory(new IntPtr(offset), data, out writtenBytes));
            }
            finally
            {
                pmr.CloseHandle();
            }
        }
        private static void patch(Action patchNeeded,
            Func<PEFile,int,int> RVAToOffset, Action<int,byte[]> writeData)
        {

                PEFile file = new PEFile(LOADED_FILE);
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
                    if(patchNeeded != null)
                        patchNeeded();
                    int offset = RVAToOffset(file, def.GetByteOffset(file, patternIndex));
                    // change the push 1 to a push 0
                    byte[] data = new byte[] { 0x16 };
                    writeData(offset, data);
                    // now, modify the filename that is loaded from ENCRYPTED_FILENAME to FINAL_FILENAME (final filename is a substring of encrypted filename)
                    int excessBytes = (FileManager.ENCRYPTED_FILENAME.Length - FileManager.FINAL_FILENAME.Length) * 2;
                    // this should already be initialized to 0's
                    data = new byte[excessBytes];

                    // find the location of the filename and move to the spot that we want to null out
                    // check that we have a ldsfld
                    if (def.Method.Code[patternIndex - 5] == 0x7f)
                    {
                        // read the int, shift off the table number
                        int fieldNum =
                            BitConverter.ToInt32(def.Method.Code, patternIndex - 4)
                            << 8 >> 8;
                        int RVA = file.GetFieldDataRVA(fieldNum);

                        offset = RVAToOffset(file, RVA) + FileManager.FINAL_FILENAME.Length * 2;
                        // null out the end bytes of the string
                        writeData(offset, data);
                    }

                }
                else
                    Log.Debug("Could Not patch file: It is likely already patched.");
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
