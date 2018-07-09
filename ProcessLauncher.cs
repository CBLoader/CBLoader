using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CharacterBuilderLoader
{
    internal sealed class TargetDomainReturnData : MarshalByRefObject
    {
        internal bool ErrorLogged { get; set; }
    }

    [Serializable]
    internal sealed class TargetDomainCallback
    {
        private string rootDirectory;
        private byte[] assembly;

        private string logFile;
        private bool verbose;

        private TargetDomainReturnData returnData;

        public bool ErrorLogged { get { return returnData.ErrorLogged; } }

        public TargetDomainCallback(string rootDirectory, byte[] assembly)
        {
            this.rootDirectory = rootDirectory;
            this.assembly = assembly;

            this.logFile = Log.LogFile;
            this.verbose = Log.VerboseMode;

            this.returnData = new TargetDomainReturnData();
        }

        public void InitLogging()
        {
            Log.InitLoggingForChildDomain(logFile, verbose);
        }

        public void GetReturnData()
        {
            returnData.ErrorLogged = Log.ErrorLogged;
        }

        public Assembly ResolveAssembly(Object sender, ResolveEventArgs ev)
        {
            Log.Debug("Handling ResolveAssembly event for " + ev.Name);

            string name = ev.Name;
            if (name.Contains(",")) name = name.Split(',')[0].Trim();

            if (name == "ApplicationUpdate.Client")
            {
                Log.Debug(" - Using patched ApplicationUpdate.Client.dll");
                return Assembly.Load(assembly);
            }

            var appPath = Path.Combine(rootDirectory, name + ".dll");
            if (File.Exists(appPath))
            {
                Log.Debug(" - Found assembly at " + appPath);
                return Assembly.LoadFrom(appPath);
            }

            var exePath = Path.Combine(rootDirectory, name + ".exe");
            if (File.Exists(exePath))
            {
                Log.Debug(" - Found assembly at " + exePath);
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

            // Redirect "combined.dnd40.encrypted" to redirectPath.
            il.InsertBefore(head, il.Create(OpCodes.Ldarg_1));
            il.InsertBefore(head, il.Create(OpCodes.Ldstr, "combined.dnd40.encrypted"));
            il.InsertBefore(head, il.Create(OpCodes.Call,
                method.Module.ImportReference(typeof(String).GetMethod("op_Equality"))));
            il.InsertBefore(head, il.Create(OpCodes.Brfalse_S, head));
            il.InsertBefore(head, il.Create(OpCodes.Ldstr, redirectPath));
            il.InsertBefore(head, il.Create(OpCodes.Starg, 1));
            il.InsertBefore(head, il.Create(OpCodes.Ldstr, "Hooking CommonMethods.GetDecryptedStream filename."));
            il.InsertBefore(head, il.Create(OpCodes.Call,
                    method.Module.ImportReference(typeof(Log).GetMethod("Debug", new Type[] { typeof(String) }))));
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

        public static bool RetrieveErrorLogged()
        {
            return Log.ErrorLogged;
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
            appDomain.DoCallBack(() => { }); // Ensure CBLoader.exe is loaded in the target domain.
            appDomain.ClearPrivatePath();
            appDomain.AppendPrivatePath("<|>"); // An invalid path. Seems required to make it not search the current directory.
#pragma warning restore CS0618

            Log.Debug("Loading CharacterBuilder.exe");
            var callback = new TargetDomainCallback(rootDirectory, assembly);
            appDomain.DoCallBack(callback.InitLogging);
            appDomain.AssemblyResolve += callback.ResolveAssembly;
            appDomain.ExecuteAssemblyByName("CharacterBuilder-cleaned", null, args);
            appDomain.DoCallBack(callback.GetReturnData);

            if (callback.ErrorLogged) Log.ErrorLogged = true;
        }
    }
}
 