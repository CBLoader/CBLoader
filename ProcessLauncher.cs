using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace CharacterBuilderLoader
{
    [Serializable]
    internal sealed class AssemblyResolver
    {
        private string rootDirectory;
        private byte[] assembly;
        private bool verbose;

        public AssemblyResolver(string rootDirectory, byte[] assembly, bool verbose)
        {
            this.rootDirectory = rootDirectory;
            this.assembly = assembly;
            this.verbose = verbose;
        }

        public Assembly ResolveAssembly(Object sender, ResolveEventArgs ev)
        {
            if (verbose) Console.WriteLine("AssemblyResolver: Handling ResolveAssembly event for " + ev.Name);

            string name = ev.Name;
            if (name.Contains(",")) name = name.Split(',')[0].Trim();

            if (name == "ApplicationUpdate.Client")
            {
                if (verbose) Console.WriteLine("AssemblyResolver:  - Using patched ApplicationUpdate.Client.dll");
                return Assembly.Load(assembly);
            }

            var appPath = Path.Combine(rootDirectory, name + ".dll");
            if (File.Exists(appPath))
            {
                if (verbose) Console.WriteLine("AssemblyResolver:  - Found assembly at " + appPath);
                return Assembly.LoadFrom(appPath);
            }

            var exePath = Path.Combine(rootDirectory, name + ".exe");
            if (File.Exists(exePath))
            {
                if (verbose) Console.WriteLine("AssemblyResolver:  - Found assembly at " + exePath);
                return Assembly.LoadFrom(exePath);
            }

            return null;
        }
    }

    public sealed class ProcessLauncher
    {
        private static NamedPermissionSet FULL_TRUST = new NamedPermissionSet("FullTrust");
        private static string GET_ALGORITHM_WITH_KEY =
            "System.Security.Cryptography.SymmetricAlgorithm ApplicationUpdate.Client.CommonMethods::GetAlgorithmWithKey(System.Guid,System.Guid)";
        private static string GET_DECRYPTED_STREAM =
            "System.IO.Stream ApplicationUpdate.Client.CommonMethods::GetDecryptedStream(System.Guid,System.String)";
        private static string KEY_STORE =
            "ApplicationUpdate.Client.IKeyInformationStore ApplicationUpdate.Client.CommonMethods::KeyStore";
        private static string GET_KEY_BLOB =
            "System.Byte[] ApplicationUpdate.Client.IKeyInformationStore::GetKeyBlob(System.Guid,System.Guid)";

        private static void RedirectPath(MethodDefinition method, string redirectPath)
        {
            var il = method.Body.GetILProcessor();

            var head = method.Body.Instructions[0];

            il.InsertBefore(head, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(head, il.Create(OpCodes.Ldstr, "combined.dnd40.encrypted"));
            il.InsertBefore(head, il.Create(OpCodes.Call,
                method.Module.ImportReference(typeof(String).GetMethod("op_Equality"))));
            il.InsertBefore(head, il.Create(OpCodes.Brfalse, head));
            il.InsertBefore(head, il.Create(OpCodes.Ldstr, redirectPath));
            il.InsertBefore(head, il.Create(OpCodes.Starg, 1));
            if (Log.VerboseMode)
            {
                il.InsertBefore(head, il.Create(OpCodes.Ldstr, "ApplicationUpdate.Client: Merged rules path injected."));
                il.InsertBefore(head, il.Create(OpCodes.Call,
                        method.Module.ImportReference(typeof(Console).GetMethod("WriteLine", new Type[] { typeof(String) }))));
            }

            method.Body.OptimizeMacros();
        }

        private static void InjectUpdateKey(MethodDefinition method)
        {
            var il = method.Body.GetILProcessor();

            Instruction virtualCallStart = null, virtualCallEnd = null;
            for (int i = 0; i < method.Body.Instructions.Count - 3; i++)
            {
                if (method.Body.Instructions[i + 0].OpCode == OpCodes.Ldsfld &&
                    ((FieldReference) method.Body.Instructions[i + 0].Operand).FullName == KEY_STORE &&
                    method.Body.Instructions[i + 1].OpCode == OpCodes.Ldarg_0 &&
                    method.Body.Instructions[i + 2].OpCode == OpCodes.Ldarg_1 &&
                    method.Body.Instructions[i + 3].OpCode == OpCodes.Callvirt &&
                    ((MethodReference) method.Body.Instructions[i + 3].Operand).FullName == GET_KEY_BLOB)
                {
                    virtualCallStart = method.Body.Instructions[i + 0];
                    virtualCallEnd   = method.Body.Instructions[i + 3];
                }
            }
            if (virtualCallStart == null) throw new Exception("Cannot find GetAlgorithmWithKey patch point.");

            var uuid_temp = new VariableDefinition(method.Module.ImportReference(typeof(Guid)));
            method.Body.Variables.Add(uuid_temp);

            var uuid_ctor = method.Module.ImportReference(typeof(Guid).GetConstructor(new Type[] { typeof(string) }));
            var uuid_eq = method.Module.ImportReference(typeof(Guid).GetMethod("op_Equality"));
            var from_base64 = method.Module.ImportReference(typeof(Convert).GetMethod("FromBase64String"));

            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldarg_0));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldloca, uuid_temp));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldstr, CryptoUtils.CB_APP_ID.ToString()));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Call, uuid_ctor));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldloc, uuid_temp));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Call, uuid_eq));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Brfalse, virtualCallStart));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldloca, uuid_temp));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldstr, CryptoUtils.INJECT_UPDATE_ID.ToString()));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Call, uuid_ctor));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldloc, uuid_temp));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Call, uuid_eq));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Brfalse, virtualCallStart));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldstr, CryptoUtils.INJECT_UPDATE_KEY.ToString()));
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Call, from_base64));
            if (Log.VerboseMode)
            {
                il.InsertBefore(virtualCallStart, il.Create(OpCodes.Ldstr, "ApplicationUpdate.Client: Custom key injected."));
                il.InsertBefore(virtualCallStart, il.Create(OpCodes.Call,
                        method.Module.ImportReference(typeof(Console).GetMethod("WriteLine", new Type[] { typeof(String) }))));
            }
            il.InsertBefore(virtualCallStart, il.Create(OpCodes.Br, virtualCallEnd.Next));
            method.Body.OptimizeMacros();
        }

        private static byte[] PatchApplicationUpdate(Stream unpatchedData, string redirectPath)
        {
            var assembly = AssemblyDefinition.ReadAssembly(unpatchedData);
            Trace.Assert(assembly.FullName.StartsWith("ApplicationUpdate.Client, "),
                            "ApplicationUpdate.Client.dll does not seem to contain the correct assembly!");

            var commonMethodsDef = assembly.MainModule.Types
                .First(t => t.FullName == "ApplicationUpdate.Client.CommonMethods");

            Log.Debug(" - Injecting update key into GetAlgorithmWithKey");
            InjectUpdateKey(commonMethodsDef.Methods.First(m => m.FullName == GET_ALGORITHM_WITH_KEY));

            Log.Debug(" - Injecting combined rules location into GetDecryptedStream");
            RedirectPath(commonMethodsDef.Methods.First(m => m.FullName == GET_DECRYPTED_STREAM), redirectPath);

            var debug_out = File.Open("ApplicationUpdate.Client-patched.dll", FileMode.Create);
            assembly.Write(debug_out);
            assembly.Dispose();

            var patchedData = new MemoryStream();
            assembly.Write(patchedData);
            return patchedData.ToArray();
        }

        public static void StartProcess(string rootDirectory, string[] args, string redirectPath)
        {
            redirectPath = Path.GetFullPath(redirectPath);

            Log.Debug("Creating application domain.");
            var setup = new AppDomainSetup();
            setup.ApplicationBase = rootDirectory;
            setup.DisallowCodeDownload = true;
            setup.PrivateBinPathProbe = "true";
            var appDomain = AppDomain.CreateDomain("D&D 4E Character Builder", null, setup, FULL_TRUST);

            Log.Debug("Patching ApplicationUpdate.Client.dll");
            byte[] assembly;
            using (var stream = File.Open(Path.Combine(rootDirectory, "ApplicationUpdate.Client.dll"), FileMode.Open))
                assembly = PatchApplicationUpdate(stream, redirectPath);

#pragma warning disable CS0618
            // Though these methods are obsolete, they are the only option I've found for doing this.
            appDomain.AppendPrivatePath(AppDomain.CurrentDomain.BaseDirectory);
            appDomain.AssemblyResolve += new AssemblyResolver(rootDirectory, assembly, Log.VerboseMode).ResolveAssembly;
            appDomain.ClearPrivatePath();
            appDomain.AppendPrivatePath(":::"); // A path that cannot possibly exist.
#pragma warning restore CS0618

            foreach (var asm in appDomain.GetAssemblies()) Log.Debug("Init assemblies: " + asm.ToString());

            Log.Debug("Loading CharacterBuilder.exe");
            var thread = new Thread(() => appDomain.ExecuteAssemblyByName("CharacterBuilder", null, args));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
