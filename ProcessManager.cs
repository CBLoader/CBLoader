using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Samples.Debugging.Native;

namespace CharacterBuilderLoader
{
    public class ProcessManager
    {
        private static string LOADED_FILE = "D20RulesEngine.dll";
        private static string EXECUTABLE = "CharacterBuilder.exe";
        private static string EXECUTABLE_ARGS = "";

        public static void StartProcess()
        {
            //// start character builder
            PEFile file = new PEFile(LOADED_FILE);
            NativePipeline np = new NativePipeline();
            NativeDbgProcess proc = np.CreateProcessDebug(EXECUTABLE, EXECUTABLE_ARGS);
            while (true)
            {
                NativeEvent ne = np.WaitForDebugEventInfinite();
                ne.Process.HandleIfLoaderBreakpoint(ne);
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
                // if we found it, pat*ch. Otherwise, assume we've already patched
                if (patternIndex > -1)
                {
                    int RVA = def.GetByteOffset(file, patternIndex);
                    int memoryOffset = ev.Module.BaseAddress.ToInt32() + RVA;
                    int writtenBytes;
                    // change the push 1 to a push 0
                    byte[] data = new byte[] { 0x16 };
                    pmr.WriteProcessMemory(new IntPtr(memoryOffset), data, out writtenBytes);

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
                        RVA = file.GetFieldDataRVA(fieldNum);

                        memoryOffset = ev.Module.BaseAddress.ToInt32() + RVA + FileManager.FINAL_FILENAME.Length * 2;
                        // null out the end bytes of the string
                        string dstr = Encoding.Unicode.GetString(
                            pmr.ReadProcessMemory(new IntPtr(memoryOffset), (uint)FileManager.ENCRYPTED_FILENAME.Length * 2, out writtenBytes));
                        pmr.WriteProcessMemory(new IntPtr(memoryOffset), data, out writtenBytes);
                    }

                }
                else
                    Console.WriteLine("Could not find the code to patch. Perhaps the character builder has changed. Check for an update to cbloader.");
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
