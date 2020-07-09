using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace CBLoader
{
    internal sealed class TargetDomainCallback : PersistantRemoteObject
    {
        private string cbDirectory;
        private readonly Dictionary<string, byte[]> patchedAssemblies = new Dictionary<string, byte[]>();

        private readonly Assembly myAssembly = Assembly.GetAssembly(typeof(TargetDomainCallback));

        internal void Init(string cbDirectory, LogRemoteReceiver logRemote, string redirectPath, string callback)
        {
            this.cbDirectory = cbDirectory;
            Log.InitLoggingForChildDomain(logRemote);
            AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

            Callbacks.redirectDataPath = redirectPath;
            Callbacks.callbackPath = callback;
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

    public static class Callbacks
    {
        internal static String redirectDataPath;
        internal static String callbackPath;

        public static String DoRedirectPath(String streamPath)
        {
            if (streamPath != "combined.dnd40.encrypted") return streamPath;
            Log.Debug("Overriding combined.dnd40.encrypted location.");
            return redirectDataPath;
        }

        public static void OnException(Exception e)
        {
            Log.Error("Character Builder encountered unexpected error.", e);
        }

        public static string GetCallbackPath()
        {
            return $"file://{callbackPath}";
        }

        public static void FallbackFrame(Frame frame)
        {
            frame.Navigating += (sender, ev) => {
                if (ev.Uri == null) return;
                if (ev.Uri.Scheme != "file")
                {
                    ev.Cancel = true;
                    frame.Navigate(new Uri(GetCallbackPath()));
                }
            };
        }

        public static string LocalizedString(int tableId)
        {
            switch (tableId)
            {
                case 6225: // Contact Customer Support.
                    return "There was an error loading combined.dnd40.  This usually means there's a malformed part file.";
                default:
                    return $"<{tableId}>";
            }
        }
    }

    internal static class ProcessLauncher
    {
        private static readonly NamedPermissionSet FULL_TRUST = new NamedPermissionSet("FullTrust");

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
            File.WriteAllBytes(assembly.Name, patchedData.ToArray());
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
                    method.Body = new CilBody { MaxStack = 1 };
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
            method.Body.Instructions.InsertRange(handlerStart, new Instruction[] {
                OpCodes.Call.ToInstruction(imp.Import(typeof(Callbacks).GetMethod("OnException"))),
                OpCodes.Ret.ToInstruction(),
            });
            exception.TryEnd = method.Body.Instructions[handlerStart];
            exception.HandlerStart = method.Body.Instructions[handlerStart];
        }

        private static void ReplaceInitializationFailure(AssemblyDef assembly)
        {
            var imp = new Importer(assembly.ManifestModule);
            var type = assembly.ManifestModule.Find("Character_Builder.App", false);
            var method = type.FindDefaultConstructor();
            for (int i = 0; i < method.Body.Instructions.Count; i++)
            {
                if (method.Body.Instructions[i].OpCode != OpCodes.Ldc_I4)
                    continue;
                if (method.Body.Instructions[i].GetLdcI4Value() != 6225)
                    continue;
                var getStringCall = method.Body.Instructions[i + 1];
                method.Body.Instructions.RemoveAt(i + 1);
                method.Body.Instructions.Insert(i + 1, OpCodes.Call.ToInstruction(imp.Import(typeof(Callbacks).GetMethod("LocalizedString"))));
                return;
            }
        }

        private static readonly string ADD_NAVIGATIONFAILED =
            "System.Void System.Windows.Controls.Frame::add_NavigationFailed(System.Windows.Navigation.NavigationFailedEventHandler)";
        private static void InjectChangelogFallback(AssemblyDef assembly, string changelog)
        {
            var imp = new Importer(assembly.ManifestModule);
            var type = assembly.ManifestModule.Find("Character_Builder.MainWindow", false);
            
            var method = type.FindMethod("System.Windows.Markup.IComponentConnector.Connect");

            for (int i = 0; i < method.Body.Instructions.Count; i++)
            {
                if (method.Body.Instructions[i].OpCode != OpCodes.Callvirt) continue;
                var target = (IMethod)method.Body.Instructions[i].Operand;
                if (target.FullName != ADD_NAVIGATIONFAILED) continue;

                method.Body.Instructions.RemoveAt(i);
                method.Body.Instructions.InsertRange(i, new Instruction[] {
                    OpCodes.Pop.ToInstruction(),
                    OpCodes.Call.ToInstruction(imp.Import(typeof(Callbacks).GetMethod("FallbackFrame"))),
                });

                break;
            }
        }

        private static void InjectChangelog(AssemblyDef assembly, string changelog)
        {
            var imp = new Importer(assembly.ManifestModule);
            var type = assembly.ManifestModule.Find("Character_Builder.TitlePage", false);

            if (type == null)
            {
                Log.Debug("     - This appears to be an older version, trying fallback approach.");
                InjectChangelogFallback(assembly, changelog);
                return;
            } 
            
            var method = type.FindProperty("CBInfoUrl").GetMethod;

            method.Body = new CilBody { MaxStack = 1 };
            method.Body.Instructions.Add(OpCodes.Call.ToInstruction(imp.Import(typeof(Callbacks).GetMethod("GetCallbackPath"))));
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

            // Probably only want to do this for broken links
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

        private static void PatchApplication(TargetDomainCallback callback, string cbDirectory, string changelog)
        {
            var assembly = LoadAssembly(cbDirectory, "CharacterBuilder.exe");

            Log.Debug("   - Replacing SmartAssembly exception handler.");
            DisableExceptionHandlers(assembly);
            ReplaceEntryPointHandler(assembly);

            Log.Debug("   - Injecting CBLoader changelog.");
            InjectChangelog(assembly, changelog);

            Log.Debug("   - Removing D&D Compendium links.");
            RemoveCompendiumLinks(assembly);

            Log.Debug("   - Fixing D20Workspace error.");
            ReplaceInitializationFailure(assembly);

            AddOverride(callback, assembly, true);
        }

        private static void RedirectPath(AssemblyDef assembly)
        {
            var imp = new Importer(assembly.ManifestModule);

            var type = assembly.ManifestModule.Find("ApplicationUpdate.Client.CommonMethods", false);
            var method = type.FindMethod("GetDecryptedStream", 
                MethodSig.CreateStatic(imp.ImportAsTypeSig(typeof(Stream)),
                                       imp.ImportAsTypeSig(typeof(Guid)), imp.ImportAsTypeSig(typeof(string))));
            
            method.Body.Instructions.InsertRange(0, new Instruction[] {
                OpCodes.Ldarg.ToInstruction(method.Parameters[1]),
                OpCodes.Call.ToInstruction(imp.Import(typeof(Callbacks).GetMethod("DoRedirectPath"))),
                OpCodes.Starg.ToInstruction(method.Parameters[1]),
            });
            method.Body.OptimizeMacros();
        }

        private static void PatchApplicationUpdate(TargetDomainCallback callback, string cbDirectory)
        {
            var assembly = LoadAssembly(cbDirectory, "ApplicationUpdate.Client.dll");

            Log.Debug("   - Injecting combined rules location into GetDecryptedStream");
            RedirectPath(assembly);

            AddOverride(callback, assembly, false);
        }

        public static Thread StartProcess(LoaderOptions options, string[] args, string redirectPath, string changelog)
        {
            Log.Info("Preparing to start CharacterBuilder.exe");
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            redirectPath = Path.GetFullPath(redirectPath);

            Log.Debug(" - Creating application domain.");
            var setup = new AppDomainSetup
            {
                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                ApplicationName = "D&D 4E Character Builder",
                DisallowCodeDownload = true,
                DisallowPublisherPolicy = true
            };
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

            callback.Init(options.CBPath, Log.RemoteReceiver, Path.GetFullPath(redirectPath), Path.GetFullPath(changelog));

            Log.Debug(" - Patching CharacterBuilder.exe");
            PatchApplication(callback, options.CBPath, changelog);

            Log.Debug(" - Patching ApplicationUpdate.Client.dll");
            PatchApplicationUpdate(callback, options.CBPath);

            Log.Debug(" - Setting up environment.");
            Environment.CurrentDirectory = options.CBPath;

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();

            Log.Info("Launching CharacterBuilder.exe");
            var thread = new Thread(() => {
                var hideLog = !Log.VerboseMode;
                if (hideLog) ConsoleWindow.SetConsoleShown(false);
                appDomain.ExecuteAssemblyByName("CharacterBuilder", null, args);
                Log.Debug("CharacterBuilder.exe terminated.");
                if (hideLog && Log.ErrorLogged) ConsoleWindow.SetConsoleShown(true);
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return thread;
        }
    }
}
 
