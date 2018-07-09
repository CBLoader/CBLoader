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
        private static string GET_DECRYPTED_STREAM =
            "System.IO.Stream ApplicationUpdate.Client.CommonMethods::GetDecryptedStream(System.Guid,System.String)";

        private static void RedirectPath(MethodDefinition method, string redirectPath)
        {
            var il = method.Body.GetILProcessor();

            var head = method.Body.Instructions[0];

            // Redirect "combined.dnd40.encrypted" elsewhere.
            //
            // roughly:
            // if (filename == "combined.dnd40.encrypted") filename = redirectPath;
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

        private static byte[] PatchApplicationUpdate(string filename, string redirectPath)
        {
            var assembly = AssemblyDefinition.ReadAssembly(filename);
            Trace.Assert(assembly.FullName.StartsWith("ApplicationUpdate.Client, "),
                            "ApplicationUpdate.Client.dll does not seem to contain the correct assembly!");

            var commonMethodsDef = assembly.MainModule.Types
                .First(t => t.FullName == "ApplicationUpdate.Client.CommonMethods");

            Log.Debug(" - Injecting combined rules location into GetDecryptedStream");
            RedirectPath(commonMethodsDef.Methods.First(m => m.FullName == GET_DECRYPTED_STREAM), redirectPath);

            var debug_out = File.Open("ApplicationUpdate.Client-patched.dll", FileMode.Create);
            assembly.Write(debug_out);
            debug_out.Dispose();

            var patchedData = new MemoryStream();
            assembly.Write(patchedData);
            return patchedData.ToArray();
        }
        
        public static void StartProcess(string rootDirectory, string[] args, string redirectPath)
        {
            redirectPath = Path.GetFullPath(redirectPath);

            Log.Debug("Patching ApplicationUpdate.Client.dll");
            var assembly = PatchApplicationUpdate(Path.Combine(rootDirectory, "ApplicationUpdate.Client.dll"), redirectPath);

            Log.Debug("Creating application domain.");
            var setup = new AppDomainSetup();
            setup.ApplicationBase = rootDirectory;
            setup.DisallowCodeDownload = true;
            setup.DisallowPublisherPolicy = true;
            setup.PrivateBinPathProbe = "true";
            var appDomain = AppDomain.CreateDomain("D&D 4E Character Builder", null, setup, FULL_TRUST);

#pragma warning disable CS0618
            // Though these methods are obsolete, they are the only option I've found for doing this.
            // This ensures CBLoader.exe is on the resolution path so the resolver can be loaded.
            appDomain.AppendPrivatePath(AppDomain.CurrentDomain.BaseDirectory);
            appDomain.AssemblyResolve += new AssemblyResolver(rootDirectory, assembly, Log.VerboseMode).ResolveAssembly;
            appDomain.ClearPrivatePath();
            appDomain.AppendPrivatePath("<|>"); // An invalid path. Seems required to make it not search the current directory.
#pragma warning restore CS0618

            Log.Debug("Loading CharacterBuilder.exe");
            var thread = new Thread(() => appDomain.ExecuteAssemblyByName("CharacterBuilder-cleaned", null, args));
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
 