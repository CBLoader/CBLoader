using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;
using System.Threading;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CharacterBuilderLoader
{
    [Serializable]
    internal sealed class AssemblyResolver
    {
        private string rootDirectory;
        private byte[] assembly;

        public AssemblyResolver(string rootDirectory, byte[] assembly)
        {
            this.rootDirectory = rootDirectory;
            this.assembly = assembly;
        }

        public Assembly ResolveAssembly(Object sender, ResolveEventArgs ev)
        {
            string name = ev.Name;
            if (name.Contains(",")) name = name.Split(',')[0].Trim();

            Console.WriteLine("AssemblyResolve for " + name);

            if (name == "ApplicationUpdate.Client") return Assembly.Load(assembly);

            var appPath = Path.Combine(rootDirectory, name + ".dll");
            if (File.Exists(appPath)) return Assembly.LoadFrom(appPath);

            var exePath = Path.Combine(rootDirectory, name + ".exe");
            if (File.Exists(exePath)) return Assembly.LoadFrom(exePath);

            return null;
        }
    }

    public sealed class ProcessLauncher
    {
        private static NamedPermissionSet FULL_TRUST = new NamedPermissionSet("FullTrust");

        private static void AddMethodTracer(MethodDefinition method)
        {
            var il = method.Body.GetILProcessor();

            var ldstr = il.Create(OpCodes.Ldstr, method.Name);
            var call = il.Create(OpCodes.Call,
                method.Module.ImportReference(
                    typeof(Console).GetMethod("WriteLine", new[] { typeof(string) })));

            il.InsertBefore(method.Body.Instructions[0], ldstr);
            il.InsertAfter(method.Body.Instructions[0], call);
        }

        private static byte[] PatchApplicationUpdate(Stream unpatchedData)
        {
            var assembly = AssemblyDefinition.ReadAssembly(unpatchedData);
            Trace.Assert(assembly.FullName.StartsWith("ApplicationUpdate.Client, "),
                            "ApplicationUpdate.Client.dll does not seem to contain the correct assembly!");

            var commonMethodsDef = assembly.MainModule.Types
                .First(t => t.FullName == "ApplicationUpdate.Client.CommonMethods");

            foreach (var method in commonMethodsDef.Methods)
            {
                Log.Debug(method.FullName);
                AddMethodTracer(method);
            }

            var debug_out = File.Open("ApplicationUpdate.Client-patched.dll", FileMode.Create);
            assembly.Write(debug_out);
            assembly.Dispose();

            var patchedData = new MemoryStream();
            assembly.Write(patchedData);
            return patchedData.ToArray();
        }

        public static void StartProcess(string rootDirectory, string[] args)
        {
            Log.Debug("Creating application domain.");
            var setup = new AppDomainSetup();
            setup.ApplicationBase = rootDirectory;
            setup.DisallowCodeDownload = true;
            setup.PrivateBinPathProbe = "true";
            var appDomain = AppDomain.CreateDomain("D&D 4E Character Builder", null, setup, FULL_TRUST);

            Log.Debug("Patching ApplicationUpdate.Client.dll");
            byte[] assembly;
            using (var stream = File.Open(Path.Combine(rootDirectory, "ApplicationUpdate.Client.dll"), FileMode.Open))
                assembly = PatchApplicationUpdate(stream);

#pragma warning disable CS0618
            // Though these methods are obsolete, they are the only option I've found for doing this.
            appDomain.AppendPrivatePath(AppDomain.CurrentDomain.BaseDirectory);
            appDomain.AssemblyResolve += new AssemblyResolver(rootDirectory, assembly).ResolveAssembly;
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
