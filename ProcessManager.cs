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
        public static string EXECUTABLE_ARGS = "";


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
            // hack: This magic number is just some empty space I found. 
            patch((file, def, def2, patternIndex) => performDiskPatch(file, def, patternIndex, 0x000D31F0));
        }

        private static void CopyIfNecessary()
        {
            if (!File.Exists(LOADED_FILE_BAK))
                File.Copy(LOADED_FILE, LOADED_FILE_BAK, true);
        }


        private static void patchMemory(DllBaseNativeEvent ev, uint processID)
        {
            ProcessMemoryReader pmr = new ProcessMemoryReader();
            pmr.ReadProcessID = processID;
            pmr.OpenProcess();
            try
            {
                patch((file, def,def2,patternIndex) => performMemoryPatch(file,def,patternIndex,pmr,ev));
            }
            finally
            {
                pmr.CloseHandle();
            }

        }

        private static byte[] pattern = new byte[] {
                    0x11, 0x07,     // ldloc.s flag2
                    0x2D, 0x0e,     // brtrue.s 0x10
                    0x02,           // ldarg.0   
                    0x7F, 255,255,255,255,  // ldsflda <unknown>
                    0x17,           // ldc.i4.1
                    0x28, 0,0,0,0   // call <unknown, to be filled>
                };

        private static byte[] newCode = new byte[] {
            0x02,           // ldarg.0
            0x20, 0,0,0,0,  // ldc.i4 <address of filename string, to be filled>
            0xD3,           // conv.i
            0x16,           // ldc.i4.1
            0x00,0x00,0x00  // nops to fill space            
        }; 
        private static void patch(Action<PEFile,MethodDef,MethodDef,int> performPatch)
        {
                PEFile file = new PEFile(LOADED_FILE);
                MethodDef def = file.FindCodeForMethodName("D20RulesEngine.LoadRulesDatabase");
                MethodDef def2 = file.FindCodeForMethodName("D20RulesEngine.LoadRulesFile");

                // We're going to replace the arguments from Loadrulesdatabase to loadrulesfile
                // the 2nd arg will be the filename we injected into memory
                // the 3rd arg will be changed from true to false
               
                Array.ConstrainedCopy(file.GetMetaDataToken(def2), 0, pattern, 12, 4);
                int patternIndex = findPatternIndex(def, pattern);
                // if we found it, patch. Otherwise, assume we've already patched
                if (patternIndex > -1)
                {
                    performPatch(file, def, def2, patternIndex);                   
                }
                else
                    Log.Debug("Could Not patch file: It is likely already patched.");
        }

        private static void performDiskPatch(PEFile file, MethodDef def, int patternIndex, int fileAddress)
        {
            CopyIfNecessary();

            // read the int, shift off the table number
            int fieldNum =
                  BitConverter.ToInt32(def.Method.Code, patternIndex + 6)
                  << 8 >> 8;
            using (FileStream sw = new FileStream(LOADED_FILE, FileMode.Open, FileAccess.Write, FileShare.Read))
            {
                // change the push 1 to a push 0
                int RVA = def.GetByteOffset(file, patternIndex) + 10;
                byte[] data = new byte[] { 0x16 };
                sw.Position = file.FindSectionForRVA(RVA).CalculateFileOffset(RVA);
                sw.Write(data, 0, data.Length);


                // write the new location to the metadata folder
                data = new byte[] { getByte(fileAddress, 0), getByte(fileAddress, 1), getByte(fileAddress, 2), getByte(fileAddress, 3) };
                FieldDef fd = file.GetField(fieldNum);
                RVA =
                    file.FindSectionForFileOffset((int)fd.MetaDataFileLocation)
                        .CalculateRVA((int)fd.MetaDataFileLocation);
                sw.Position = file.FindSectionForRVA(RVA).CalculateFileOffset(RVA);
                sw.Write(data, 0, data.Length);

                // write the string to an empty file location
                RVA = fileAddress;
                data = Encoding.Unicode.GetBytes(FileManager.MergedPath);
                sw.Position = file.FindSectionForRVA(RVA).CalculateFileOffset(RVA);
                sw.Write(data, 0, data.Length);
            }
        }

        private static void performMemoryPatch(PEFile file, MethodDef def, int patternIndex, ProcessMemoryReader pmr,DllBaseNativeEvent ev)
        {
            IntPtr address =
                Utils.VirtualAllocEx(ev.Process.UnsafeHandle, IntPtr.Zero,
                (uint)Encoding.Unicode.GetBytes(FileManager.MergedPath).Length,
                 AllocationType.Reserve | AllocationType.Commit | AllocationType.TopDown, MemoryProtection.ReadWrite);
            int fileAddress = address.ToInt32();
            // set the location for our new filename string
            int offset = ev.Module.BaseAddress.ToInt32() + def.GetByteOffset(file, patternIndex);
            newCode[2] = getByte(fileAddress, 0);
            newCode[3] = getByte(fileAddress, 1);
            newCode[4] = getByte(fileAddress, 2);
            newCode[5] = getByte(fileAddress, 3);
            int writtenBytes;
            pmr.WriteProcessMemory(new IntPtr(offset), newCode, out writtenBytes);
            // write the new filename string
            pmr.WriteProcessMemory(new IntPtr(fileAddress),  Encoding.Unicode.GetBytes(FileManager.MergedPath), out writtenBytes);
        }

        private static byte getByte(int i, int num)
        {
            int bits = num*8;
            return (byte)((i & (0xFF << bits)) >> bits);
        }
        private static int findPatternIndex(MethodDef def, byte[] pattern)
        {
            for (int i = 0; i < def.Method.Code.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < pattern.Length; j++)
                {
                    if (def.Method.Code[i + j] != pattern[j] && pattern[j] != 255)
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
