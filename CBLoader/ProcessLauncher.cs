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
using System.Threading;

namespace CBLoader
{
    internal sealed class TargetDomainCallback : MarshalByRefObject
    {
        private string cbDirectory;
        private Dictionary<string, byte[]> patchedAssemblies = new Dictionary<string, byte[]>();

        private readonly Assembly myAssembly = Assembly.GetAssembly(typeof(TargetDomainCallback));

        internal void Init(string cbDirectory, LogRemoteReceiver logRemote)
        {
            this.cbDirectory = cbDirectory;
            Log.InitLoggingForChildDomain(logRemote);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        }
        internal void AddOverride(string name, byte[] data)
        {
            patchedAssemblies[name] = data;
        }

        private Assembly CheckExtension(string name, string extension)
        {
            var path = Path.Combine(cbDirectory, $"{name}.{extension}");
            if (File.Exists(path))
            {
                Log.Debug($" - Found assembly at {path}");
                return Assembly.LoadFrom(path);
            }
            return null;
        }
        private Assembly ResolveAssembly(Object sender, ResolveEventArgs ev)
        {
            Log.Debug($"Handling ResolveAssembly event for {ev.Name}");
            
            if (ev.Name == myAssembly.FullName)
            {
                Log.Debug(" - Using callback assembly");
                return myAssembly;
            }

            string name = ev.Name.Split(',')[0].Trim();

            if (patchedAssemblies.ContainsKey(name))
            {
                Log.Debug(" - Using patched assembly");
                return Assembly.Load(patchedAssemblies[name]);
            }

            var dll = CheckExtension(name, "dll");
            if (dll != null) return dll;

            var exe = CheckExtension(name, "exe");
            if (exe != null) return exe;

            return null;
        }
    }

    internal static class ProcessLauncher
    {
        private static NamedPermissionSet FULL_TRUST = new NamedPermissionSet("FullTrust");

        private static AssemblyDef LoadAssembly(string cbDirectory, string name)
        {
            var path = Path.Combine(cbDirectory, name);
            var expectedName = Path.GetFileNameWithoutExtension(name);
            var assembly = AssemblyDef.Load(path);
            Trace.Assert(assembly.Name == expectedName, $"{name} does not contain the correct assembly!");
            return assembly;
        }

        private static void AddOverride(TargetDomainCallback callback, AssemblyDef assembly, bool expectObfusication)
        {
            Log.Debug($" - Adding patched assembly for {assembly.Name}");

            var settings = new ModuleWriterOptions(assembly.ManifestModule);
            if (expectObfusication)
                settings.MetadataOptions.Flags |= MetadataFlags.KeepOldMaxStack;

            var patchedData = new MemoryStream();
            assembly.Write(patchedData, settings);
            callback.AddOverride(assembly.Name, patchedData.ToArray());
        }

        private static void DisableExceptionHandlers(AssemblyDef assembly)
        {
            var unhandledExceptionDef =
                assembly.ManifestModule.Find("SmartAssembly.SmartExceptionsCore.UnhandledException", false);
            
            foreach (var method in unhandledExceptionDef.Methods)
            {
                var exception_ty = typeof(Exception).FullName;
                if (method.Parameters.Count > 0 &&
                    method.Parameters[0].Type.FullName == exception_ty &&
                    method.ReturnType.FullName == exception_ty)
                {
                    Log.Trace($"     - Disabling {method.FullName}");
                    method.Body = new CilBody();
                    method.Body.MaxStack = 1;
                    method.Body.Instructions.Add(OpCodes.Ldarg_0.ToInstruction());
                    method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
                }
            }
        }
        private static void ReplaceEntryPointHandler(AssemblyDef assembly)
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
            method.Body.Variables.Add(tempField);
            method.Body.Instructions.InsertRange(handlerStart, new Instruction[] {
                OpCodes.Stloc.ToInstruction(tempField),
                OpCodes.Ldstr.ToInstruction("Character Builder encountered unexpected error."),
                OpCodes.Ldloc.ToInstruction(tempField),
                OpCodes.Call.ToInstruction(imp.Import(typeof(Log).GetMethod("Error"))),
                OpCodes.Ret.ToInstruction(),
            });
            exception.TryEnd = method.Body.Instructions[handlerStart];
            exception.HandlerStart = method.Body.Instructions[handlerStart];
        }

        private static void RemoveTitleCallHome(AssemblyDef assembly)
        {
            var imp = new Importer(assembly.ManifestModule);
            var type = assembly.ManifestModule.Find("Character_Builder.TitlePage", false);

            // Replace the URI the update page (I presume) is loaded from with an non-existing resource.
            // We can use this later to put our own update information, I guess.
            var property = type.FindProperty("CBInfoUrl");
            var method = property.GetMethod;

            method.Body = new CilBody();
            method.Body.MaxStack = 1;
            method.Body.Instructions.Add(OpCodes.Ldstr.ToInstruction("pack://application:,,,/NonExistantResource.html"));
            method.Body.Instructions.Add(OpCodes.Ret.ToInstruction());
        }

        private const string FIND_RULES_ELEMENT = 
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

        private static void PatchApplication(TargetDomainCallback callback, string cbDirectory)
        {
            var assembly = LoadAssembly(cbDirectory, "CharacterBuilder.exe");

            Log.Debug("   - Replacing SmartAssembly exception handler.");
            DisableExceptionHandlers(assembly);
            ReplaceEntryPointHandler(assembly);

            Log.Debug("   - Preventing initial attempt to load page on Wizards website.");
            RemoveTitleCallHome(assembly);

            Log.Debug("   - Removing D&D Compendium links.");
            RemoveCompendiumLinks(assembly);

            AddOverride(callback, assembly, true);
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
                OpCodes.Ldnull.ToInstruction(),
                OpCodes.Call.ToInstruction(imp.Import(typeof(Log).GetMethod("Debug"))),
            });
            method.Body.OptimizeMacros();
        }

        private static void PatchApplicationUpdate(TargetDomainCallback callback, string cbDirectory, string redirectPath)
        {
            var assembly = LoadAssembly(cbDirectory, "ApplicationUpdate.Client.dll");

            Log.Debug("   - Injecting combined rules location into GetDecryptedStream");
            RedirectPath(assembly, redirectPath);

            AddOverride(callback, assembly, false);
        }
        
        public static void StartProcess(LoaderOptions options, string[] args, string redirectPath)
        {
            Log.Info("Preparing to start CharacterBuilder.exe");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            redirectPath = Path.GetFullPath(redirectPath);

            Log.Debug(" - Creating application domain.");
            var setup = new AppDomainSetup();
            setup.ApplicationBase = AppDomain.CurrentDomain.BaseDirectory;
            setup.ApplicationName = "D&D 4E Character Builder";
            setup.DisallowCodeDownload = true;
            setup.DisallowPublisherPolicy = true;
            // For some reason, unless we set PrivateBinPathProbe, D20RulesEngine.dll bypasses ResolveAssembly
            setup.PrivateBinPath = setup.ApplicationBase;
            setup.PrivateBinPathProbe = "true";
            var appDomain = AppDomain.CreateDomain("D&D 4E Character Builder", null, setup, FULL_TRUST);

            Log.Debug(" - Creating remote callback.");
            var callbackObj = appDomain.CreateInstance("CBLoader", typeof(TargetDomainCallback).FullName);
            var callback = (TargetDomainCallback) callbackObj.Unwrap();

            // Seal the AppDomain by setting the private path to an invalid path.
            // For some reason, we must *also* do this to stop D20RulesEngine.dll from bypassing.
#pragma warning disable CS0618
            appDomain.ClearPrivatePath();
            appDomain.AppendPrivatePath("<|>");
#pragma warning restore CS0618

            callback.Init(options.CBPath, Log.RemoteReceiver);

            Log.Debug(" - Patching CharacterBuilder.exe");
            PatchApplication(callback, options.CBPath);

            Log.Debug(" - Patching ApplicationUpdate.Client.dll");
            PatchApplicationUpdate(callback, options.CBPath, redirectPath);

            Log.Debug(" - Setting up environment.");
            Environment.CurrentDirectory = options.CBPath;

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();

            Log.Info("Launching CharacterBuilder.exe");
            if (!Log.VerboseMode) ConsoleWindow.SetConsoleShown(false);
            var thread = new Thread(() => {
                appDomain.ExecuteAssemblyByName("CharacterBuilder", null, args);
                Log.Debug("CharacterBuilder.exe terminated.");
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
        }
    }
}
 