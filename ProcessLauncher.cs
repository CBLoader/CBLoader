using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security;

namespace CharacterBuilderLoader
{
    [Serializable]
    internal sealed class TargetDomainCallback
    {
        private string rootDirectory;
        private LogRemoteReceiver remoteReceiver;
        private Dictionary<string, byte[]> patchedAssemblies = new Dictionary<string, byte[]>();

        public TargetDomainCallback(string rootDirectory)
        {
            this.rootDirectory = rootDirectory;
            this.remoteReceiver = Log.RemoteReceiver;
        }

        public void InitLogging()
        {
            Log.InitLoggingForChildDomain(remoteReceiver);
        }

        private static string assemblyName(string name) =>
            name.Split(',')[0].Trim();

        public void AddOverride(AssemblyDef assembly, bool expectObfusication)
        {
            var name = assemblyName(assembly.FullName);
            Log.Debug(" - Adding patched assembly for "+name);

            var settings = new ModuleWriterOptions(assembly.ManifestModule);
            if (expectObfusication)
                settings.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack;

            var patchedData = new MemoryStream();
            assembly.Write(patchedData, settings);
            patchedAssemblies[name] = patchedData.ToArray();
        }

        public Assembly ResolveAssembly(Object sender, ResolveEventArgs ev)
        {
            Log.Debug("Handling ResolveAssembly event for " + ev.Name);

            string name = assemblyName(ev.Name);

            if (patchedAssemblies.ContainsKey(name))
            {
                Log.Debug(" - Using patched assembly");
                return Assembly.Load(patchedAssemblies[name]);
            }

            var dllPath = Path.Combine(rootDirectory, name + ".dll");
            if (File.Exists(dllPath))
            {
                Log.Debug(" - Found assembly at " + dllPath);
                return Assembly.LoadFrom(dllPath);
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

    public static class ProcessLauncher
    {
        private static NamedPermissionSet FULL_TRUST = new NamedPermissionSet("FullTrust");

        private static AssemblyDef LoadAssembly(string rootDirectory, string name)
        {
            var path = Path.Combine(rootDirectory, name);
            var expectedName = Path.GetFileNameWithoutExtension(name);
            var assembly = AssemblyDef.Load(path);
            Trace.Assert(assembly.Name == expectedName, name + " does not contain the correct assembly!");
            return assembly;
        }
        
        private static void StripExceptionHandlers(MethodDef method, HashSet<string> handlers)
        {
            if (method == null || method.Body == null) return;
            if (method.Body.Instructions[method.Body.Instructions.Count - 1].OpCode != OpCodes.Ret) return;

            // Check if the exception handlers in the method match the pattern.
            foreach (var exception in method.Body.ExceptionHandlers.Where(exception =>
                exception.HandlerType == ExceptionHandlerType.Catch &&
                exception.CatchType.FullName == typeof(Exception).FullName
            ))
            {
                if (exception.HandlerEnd == null) continue;
                var handlerEnd = method.Body.Instructions.IndexOf(exception.HandlerEnd);
                if (handlerEnd < 2) continue;

                // Check if the opcodes in the method match the pattern.
                if (method.Body.Instructions[handlerEnd - 2].OpCode != OpCodes.Call ||
                    method.Body.Instructions[handlerEnd - 1].OpCode != OpCodes.Throw) return;
                var excCall = (IMethod) method.Body.Instructions[handlerEnd - 2].Operand;
                if (!handlers.Contains(excCall.FullName)) return;

                // If so, remove the instruction handler.
                var handlerStart = method.Body.Instructions.IndexOf(exception.HandlerStart);
                var handlerCount = handlerEnd - handlerStart;
                for (int i = 0; i < handlerCount; i++)
                    method.Body.Instructions.RemoveAt(handlerStart);
                method.Body.ExceptionHandlers.Remove(exception);

                return;
            }
        }
        private static void StripExceptionHandlers(AssemblyDef assembly, HashSet<string> handlers)
        {
            foreach (var module in assembly.Modules)
                foreach (var type in module.Types)
                {
                    foreach (var method in type.Methods) StripExceptionHandlers(method, handlers);
                    foreach (var property in type.Properties)
                    {
                        foreach (var method in property.SetMethods) StripExceptionHandlers(method, handlers);
                        foreach (var method in property.GetMethods) StripExceptionHandlers(method, handlers);
                    }
                    foreach (var ev in type.Events)
                    {
                        StripExceptionHandlers(ev.AddMethod, handlers);
                        StripExceptionHandlers(ev.InvokeMethod, handlers);
                        StripExceptionHandlers(ev.RemoveMethod, handlers);
                        foreach (var method in ev.OtherMethods) StripExceptionHandlers(method, handlers);
                    }
                }
        }
        private static HashSet<string> FindExceptionHandlers(AssemblyDef assembly)
        {
            var unhandledExceptionDef =
                assembly.ManifestModule.Find("SmartAssembly.SmartExceptionsCore.UnhandledException", false);

            var set = new HashSet<string>();
            foreach (var method in unhandledExceptionDef.Methods)
                if (method.Parameters.Count > 0 && method.Parameters[0].Type.FullName == typeof(Exception).FullName)
                {
                    set.Add(method.FullName);
                }
            return set;
        }

        private static void RemoveTitleCallHome(AssemblyDef assembly)
        {
            var imp = new Importer(assembly.ManifestModule);
            var type = assembly.ManifestModule.Find("Character_Builder.TitlePage", false);

            // Replace the URI the update page (I presume) is loaded from with an non-existing resource.
            // We can use this later to put our own update information, I guess.
            var property = type.FindProperty("CBInfoUrl");
            var method = property.GetMethod;

            method.Body.Instructions.Clear();
            method.Body.Instructions.InsertRange(0, new Instruction[] {
                OpCodes.Ldstr.ToInstruction("pack://application:,,,/NonExistantResource.html"),
                OpCodes.Ret.ToInstruction(),
            });
        }

        private static void PatchEntryPointHandler(AssemblyDef assembly)
        {
            var imp = new Importer(assembly.ManifestModule);
            var method = assembly.ManifestModule.EntryPoint;
            Trace.Assert(method.Body.ExceptionHandlers.Count == 1);
            var exception = method.Body.ExceptionHandlers[0];
            Trace.Assert(exception.TryEnd == exception.HandlerStart);

            // Remove the existing exception handler.
            var handlerStart = method.Body.Instructions.IndexOf(exception.HandlerStart);
            var handlerEnd = method.Body.Instructions.IndexOf(exception.HandlerEnd);
            var handlerCount = handlerEnd - handlerStart;
            for (int i = 0; i < handlerCount; i++)
                method.Body.Instructions.RemoveAt(handlerStart);

            // Add a new exception handler.
            var tempField = new Local(imp.ImportAsTypeSig(typeof(Exception)));
            var logError = imp.Import(typeof(Log).GetMethod("Error", new Type[] { typeof(string), typeof(Exception) }));
            method.Body.Variables.Add(tempField);
            method.Body.Instructions.InsertRange(handlerStart, new Instruction[] {
                OpCodes.Stloc.ToInstruction(tempField),
                OpCodes.Ldstr.ToInstruction("Character Builder encountered unexpected error."),
                OpCodes.Ldloc.ToInstruction(tempField),
                OpCodes.Call.ToInstruction(logError),
                OpCodes.Ret.ToInstruction(),
            });
            exception.TryEnd = method.Body.Instructions[handlerStart];
            exception.HandlerStart = method.Body.Instructions[handlerStart];
        }

        private static string FIND_RULES_ELEMENT = 
            "D20RulesEngine.RulesElement* D20RulesEngine.D20Workspace::FindRulesElement(System.String,D20RulesEngine.D20Type*)";
        private static void RemoveCompendiumLinks(AssemblyDef assembly)
        {
            // (Because they are all dead anyways.)
            var imp = new Importer(assembly.ManifestModule);
            var type = assembly.ManifestModule.Find("Character_Builder.App", false);

            {
                // Remove Compendium link URI
                var method = type.FindMethod("AddUri");
                method.Body = new CilBody();
                method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
            }

            {
                // Remove source link URI
                var method = type.FindMethod("AddSource");

                for (int i=0; i < method.Body.Instructions.Count; i++)
                {
                    if (method.Body.Instructions[i].OpCode != OpCodes.Callvirt) continue;
                    var target = (IMethod) method.Body.Instructions[i].Operand;
                    if (target.FullName != FIND_RULES_ELEMENT) continue;

                    method.Body.Instructions.RemoveAt(i);
                    method.Body.Instructions.InsertRange(i, new Instruction[] {
                        OpCodes.Pop.ToInstruction(),
                        OpCodes.Pop.ToInstruction(),
                        OpCodes.Pop.ToInstruction(),
                        OpCodes.Ldnull.ToInstruction(),
                    });

                    break;
                }
            }
        }

        private static void PatchApplication(TargetDomainCallback callback, string rootDirectory)
        {
            var assembly = LoadAssembly(rootDirectory, "CharacterBuilder.exe");

            Log.Debug("   - Finding SmartAssembly exception handler methods.");
            var handlers = FindExceptionHandlers(assembly);

            Log.Debug("   - Stripping SmartAssembly exception handler methods.");
            StripExceptionHandlers(assembly, handlers);

            Log.Debug("   - Preventing initial attempt to load page on Wizards website.");
            RemoveTitleCallHome(assembly);

            Log.Debug("   - Replacing SmartAssembly root exception handler.");
            PatchEntryPointHandler(assembly);

            Log.Debug("   - Removing D&D Compendium links.");
            RemoveCompendiumLinks(assembly);

            callback.AddOverride(assembly, true);
        }

        private static void RedirectPath(AssemblyDef assembly, string redirectPath)
        {
            var imp = new Importer(assembly.ManifestModule);

            var type = assembly.ManifestModule.Find("ApplicationUpdate.Client.CommonMethods", false);
            var method = type.FindMethod("GetDecryptedStream", 
                MethodSig.CreateStatic(imp.ImportAsTypeSig(typeof(Stream)),
                                       imp.ImportAsTypeSig(typeof(Guid)), imp.ImportAsTypeSig(typeof(string))));

            var head = method.Body.Instructions[0];

            method.Body.Instructions.InsertRange(0, new Instruction[] {
                OpCodes.Ldarg.ToInstruction(method.Parameters[1]),
                OpCodes.Ldstr.ToInstruction("combined.dnd40.encrypted"),
                OpCodes.Call.ToInstruction(imp.Import(typeof(String).GetMethod("op_Equality"))),
                OpCodes.Brfalse.ToInstruction(head),
                OpCodes.Ldstr.ToInstruction(redirectPath),
                OpCodes.Starg.ToInstruction(method.Parameters[1]),
                OpCodes.Ldstr.ToInstruction("Overriding combined.dnd40.encrypted location."),
                OpCodes.Call.ToInstruction(imp.Import(typeof(Log).GetMethod("Debug", new Type[] { typeof(String) }))),
            });
            method.Body.OptimizeMacros();
        }

        private static void PatchApplicationUpdate(TargetDomainCallback callback, string rootDirectory, string redirectPath)
        {
            var assembly = LoadAssembly(rootDirectory, "ApplicationUpdate.Client.dll");

            Log.Debug("   - Injecting combined rules location into GetDecryptedStream");
            RedirectPath(assembly, redirectPath);

            callback.AddOverride(assembly, false);
        }
        
        public static void StartProcess(string rootDirectory, string[] args, string redirectPath)
        {
            Log.Info("Preparing to start CharacterBuilder.exe");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            redirectPath = Path.GetFullPath(redirectPath);
            var callback = new TargetDomainCallback(rootDirectory);

            Log.Debug(" - Patching CharacterBuilder.exe");
            PatchApplication(callback, rootDirectory);

            Log.Debug(" - Patching ApplicationUpdate.Client.dll");
            PatchApplicationUpdate(callback, rootDirectory, redirectPath);

            Log.Debug(" - Creating application domain.");
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

            foreach (var assembly in appDomain.GetAssemblies())
                Log.Debug("   - Preloaded module: " + assembly);

            Log.Debug(" - Setting up environment.");
            Environment.CurrentDirectory = rootDirectory;

            stopwatch.Stop();
            Log.Debug("Finished in " + stopwatch.ElapsedMilliseconds + " ms");
            Log.Debug("");

            Log.Info("Launching CharacterBuilder.exe");
            appDomain.DoCallBack(callback.InitLogging);
            appDomain.AssemblyResolve += callback.ResolveAssembly;
            if (!Log.VerboseMode) ConsoleWindow.SetConsoleShown(false);
            appDomain.ExecuteAssemblyByName("CharacterBuilder", null, args);
        }
    }
}
 