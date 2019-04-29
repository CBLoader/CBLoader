using dnlib.DotNet;
using dnlib.DotNet.Emit;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace CBLoader
{
    /// <summary>
    /// A class that handles calculating the hashes the character builder uses to validate its files.
    /// 
    /// This supports calculating such checksums, and performing preimage attacks to fix the hash of our
    /// generated files. (Since the hash is hardcoded into D20RulesEngine.dll)
    /// </summary>
    internal sealed class CBDataHasher
    {
        public uint State { get; set; }

        public CBDataHasher()
        {
            // Initial state of the hashes Character Builder actually uses.
            this.State = 5381;
        }
        public CBDataHasher(uint state)
        {
            this.State = state;
        }
        public CBDataHasher(string str) : this()
        {
            Update(str);
        }

        public void Update(string str)
        {
            foreach (var ch in str)
                this.State = (this.State * 33) + ch;
        }

        private void UpdateReverse(char ch)
        {
            this.State -= ch;
            this.State *= 1041204193; // 33^-1 mod 2^32
        }
        private void UpdateReverse(string str)
        {
            foreach (var ch in str.Reverse()) UpdateReverse(ch);
        }
        
        /// <summary>
        /// Finds a string that when appended to another string with the current hash, will have a final
        /// hash equal to targetHash.
        /// 
        /// Hence, to bring the hash of a string to any value, do:
        /// 
        /// str += new CBDataHasher(str).CalculatePreimage(targetHash, " whatever text ");
        /// </summary>
        /// <param name="targetHash">The target hash value.</param>
        /// <param name="tail">An optional suffix that the output string must end with.</param>
        /// <returns>The string to append.</returns>
        public string CalculatePreimage(uint targetHash, string tail = "")
        {
            // The effect of appending n characters to a string with this hash algorithm is effectively:
            //
            // hash(str + chars) = hash(str) * 33^n.Length + hash(chars) mod 2^32
            //
            // We calculate the hash value `chars` would need to have to set the hash to `targetHash`,
            // assuming it is 5 characters.
            //
            // In this way, we can use an algorithm that only needs to bring 0 to a certain hash value,
            // and not any value to any value.
            CBDataHasher target = new CBDataHasher(targetHash);
            target.UpdateReverse(tail); // account for the tail
            target.State -= this.State * 39135393; // 33^5

            // We calculate the string in reverse, effectively trying to bring the value of target down
            // to zero. The calculation in reverse is:
            //
            // hash(c + str) = (hash(str) - c) * 33^1 mod 2^32
            //
            // As long as 33 evenly divides (hash(str) - c), the modular division will behave as ordinary
            // division. As this value will always decrease, it eventually reaches zero, at which point
            // we can just output null characters until we have 5 characters.
            var outStr = "";
            for (int i = 0; i < 5; i++)
            {
                var tint = target.State;
                // Avoid generating surrogate pairs or BOMs (The CLR does weird things to it)
                var next = (tint >= 0xD800 && tint <= 0xDFFF) || tint == 0xFEFF ? 0x752D + (tint % 33) :
                           tint <= 0xFFFF ? tint : 0xFFC0 + (tint % 33);
                var next_ch = (char)next;

                outStr = next_ch + outStr;
                target.UpdateReverse(next_ch);
            }
            Trace.Assert(target.State == 0, "CalculatePreimage failed.");
            return outStr + tail;
        }
    }

    /// <summary>
    /// A class that extracts the expected hash for combined.dnd40.encrypted from D20RulesEngine.dll
    /// </summary>
    internal sealed class ParsedD20RulesEngine
    {
        public readonly uint expectedDemoHash, expectedNormalHash;

        public ParsedD20RulesEngine(string assemblyPath)
        {
            Log.Debug(" - Parsing D20RulesEngine.dll");

            var assembly = AssemblyDef.Load(assemblyPath);
            var moduleBase = assembly.ManifestModule.Find("<Module>", false);
            var method = moduleBase.FindMethod("D20RulesEngine.LoadRulesFile");

            method.Body.SimplifyMacros(method.Parameters);

            // Find call to ComputeHash
            int computeHashStart = -1;
            for (int i = 0; i < method.Body.Instructions.Count; i++)
            {
                if (method.Body.Instructions[i].OpCode == OpCodes.Call &&
                    ((IMethod) method.Body.Instructions[i].Operand).Name == "GenerateHash")
                {
                    computeHashStart = i;
                    break;
                }
            }
            if (computeHashStart == -1) throw new Exception("Cannot find invocation of ComputeHash in LoadRulesFile.");

            // Find the local the hash is stored in.
            if (method.Body.Instructions[computeHashStart + 1].OpCode != OpCodes.Stloc)
                throw new Exception("stloc does not follow call to ComputeHash in LoadRulesFile.");
            var hashVar = (IVariable) method.Body.Instructions[computeHashStart + 1].Operand;

            // Find the three comparisons that should follow.
            var hashes = new uint[3];
            int foundComparisons = 0;
            for (int i = computeHashStart; i < Math.Min(method.Body.Instructions.Count - 2, computeHashStart + 30); i++)
            {
                if (method.Body.Instructions[i + 0].OpCode == OpCodes.Ldloc &&
                    ((IVariable) method.Body.Instructions[i + 0].Operand).Index == hashVar.Index &&
                    method.Body.Instructions[i + 1].OpCode == OpCodes.Ldc_I4 &&
                    method.Body.Instructions[i + 2].OpCode.OperandType == OperandType.InlineBrTarget)
                {
                    var currentHash = (uint) (int) method.Body.Instructions[i + 1].Operand;
                    hashes[foundComparisons++] = currentHash;
                    if (foundComparisons == 3) break;
                }
            }
            if (foundComparisons != 3) throw new Exception("Not enough comparisons found in LoadRulesFile.");
            if (hashes[0] != hashes[2]) throw new Exception("hashes[0] != hashes[2] in LoadRulesFile.");

            // Output hash information.
            Log.Trace($"   - demoHash = {hashes[0]}, normalHash = {hashes[1]}");
            this.expectedDemoHash = hashes[0];
            this.expectedNormalHash = hashes[1];
        }
    }

    internal static class CryptoUtils
    {
        private const string XML_MARKER = "<!-- This file has been edited by CBLoader -->";

        public static bool IsXmlPatched(string str) =>
            str.Contains(XML_MARKER);
        public static uint HashString(string str) =>
            new CBDataHasher(str).State;
        public static string FixXmlHash(string str, uint target_hash)
        {
            str = Utils.StripBOM(str);
            str = Utils.NormalizeLineEndings(str);
            str += $"\n{XML_MARKER}\n<!-- Fix hash: ";
            str += new CBDataHasher(str).CalculatePreimage(target_hash, " -->\n");
            return str;
        }

        private static SymmetricAlgorithm CreateAlgorithm(Guid applicationId, byte[] keyData)
        {
            var algorithm = new AesCryptoServiceProvider();
            algorithm.IV = applicationId.ToByteArray().Take(algorithm.BlockSize / 8).ToArray();
            algorithm.Key = keyData;
            return algorithm;
        }
        public static Stream GetDecryptingStream(Stream inStream, Guid applicationId, Func<Guid, byte[]> getKeyData)
        {
            var guidData = new byte[16];
            Trace.Assert(inStream.Read(guidData, 0, 16) == 16, "Not enough bytes read from GUID.");
            var updateId = new Guid(guidData);
            var keyData = getKeyData.Invoke(updateId);

            var algorithm = CreateAlgorithm(applicationId, keyData);
            var cryptoStream = new CryptoStream(inStream, algorithm.CreateDecryptor(), CryptoStreamMode.Read);
            return new GZipStream(cryptoStream, CompressionMode.Decompress);
        }
        public static Stream GetEncryptingStream(Stream outStream, Guid applicationId, Guid updateId, byte[] keyData)
        {
            outStream.Write(updateId.ToByteArray(), 0, 16);

            var algorithm = CreateAlgorithm(applicationId, keyData);
            var cryptoStream = new CryptoStream(outStream, algorithm.CreateEncryptor(), CryptoStreamMode.Write);
            return new GZipStream(cryptoStream, CompressionMode.Compress);
        }
    }

    [XmlType(Namespace = "http://cbloader.github.io/CBLoader/ns/KeyStore/v1")]
    [XmlRoot(Namespace = "http://cbloader.github.io/CBLoader/ns/KeyStore/v1")]
    public sealed class UpdateKeyInfo
    {
        [XmlAttribute] public Guid Id;
        [XmlText] public byte[] KeyData;

        public UpdateKeyInfo() { }
        internal UpdateKeyInfo(Guid updateId, byte[] keyData)
        {
            this.Id = updateId;
            this.KeyData = keyData;
        }

        public override bool Equals(object obj)
        {
            var info = obj as UpdateKeyInfo;
            return info != null && Id == info.Id && KeyData.SequenceEqual(info.KeyData);
        }
        public override int GetHashCode() => KeyData.GetHashCode() ^ Id.GetHashCode();
    }

    [XmlType(Namespace = "http://cbloader.github.io/CBLoader/ns/KeyStore/v1")]
    [XmlRoot("CBLoaderKeyStore", Namespace = "http://cbloader.github.io/CBLoader/ns/KeyStore/v1")]
    public sealed class KeyStore
    {
        [XmlArray(IsNullable = false)]
        [XmlArrayItem(IsNullable = false)]
        public List<UpdateKeyInfo> UpdateKeys = new List<UpdateKeyInfo>();

        public Guid WriteGuid;
        public byte[] FallbackKey;

        public void AddKey(Guid updateId, byte[] keyData)
        {
            var existing = UpdateKeys.FirstOrDefault(x => x.Id == updateId);
            if (existing != null)
            {
                Log.Debug($"   - Found duplicate key for {updateId}");
                if (existing.KeyData.SequenceEqual(keyData)) return;
                throw new Exception($"Duplicate update info for {updateId}.");
            } else
            {
                Log.Debug($"   - Adding key for {updateId}");
                UpdateKeys.Add(new UpdateKeyInfo(updateId, keyData));
                UpdateKeys.Sort((a, b) => a.Id.CompareTo(b.Id));
            }
        }

        public byte[] Get(Guid updateId)
        {
            var existing = UpdateKeys.FirstOrDefault(x => x.Id == updateId);
            if (existing == null) return FallbackKey;
            return existing.KeyData;
        }

        public override bool Equals(object obj)
        {
            var store = obj as KeyStore;
            return store != null &&
                   UpdateKeys.SequenceEqual(store.UpdateKeys) &&
                   WriteGuid == store.WriteGuid &&
                   ((FallbackKey == null && store.FallbackKey == null) ||
                    (FallbackKey != null && FallbackKey.SequenceEqual(store.FallbackKey)));
        }
        public static bool operator ==(KeyStore a, KeyStore b) => (a is null && b is null) || (!(a is null) && a.Equals(b));
        public static bool operator !=(KeyStore a, KeyStore b) => !(a == b);
        public override int GetHashCode() => 
            UpdateKeys.GetHashCode() ^ WriteGuid.GetHashCode() ^ (FallbackKey ?? new byte[0]).GetHashCode();
    }

    internal sealed class CryptoInfo
    {
        private static XmlSerializer SERIALIZER = new XmlSerializer(typeof(KeyStore));

        private static Guid CB_APP_ID = new Guid("2a1ddbc4-4503-4392-9548-d0010d1ba9b1");
        private static Regex GUID_REGEX = new Regex("^########-####-####-####-############$".Replace("#", "[a-zA-Z0-9]"));
        private static Regex UPDATE_REGEX = new Regex("^Update(########-####-####-####-############)$".Replace("#", "[a-zA-Z0-9]"));
        private static byte[] REGISTRY_CRYPT_ENTROPY = new byte[] { 0x19, 0x25, 0x49, 0x62, 12, 0x41, 0x55, 0x1c, 0x15, 0x2f };

        private readonly LoaderOptions options;
        public readonly uint expectedDemoHash, expectedNormalHash;
        public readonly KeyStore keyStore = new KeyStore();

        private void processUpdateFile(Guid appGuid, string filename, bool isHeroicUpdate = false)
        {
            if (!File.Exists(filename)) {
                if (isHeroicUpdate)
                    throw new CBLoaderException($"Expected file {filename} not found.");
                return;
            }

            Log.Debug($" - Parsing {Path.GetFileName(filename)}");
            var document = XDocument.Load(filename);

            switch (document.Root.Name.LocalName.ToLower())
            {
                case "applications":
                    var applicationTag = document.Root.Elements()
                        .First(x => x.Name.LocalName == "Application" &&
                                    x.Attribute("ID").Value == appGuid.ToString());
                    foreach (var element in applicationTag.Elements())
                    {
                        var match = UPDATE_REGEX.Match(element.Name.LocalName);
                        var id = new Guid(match.Groups[1].Value);
                        var key = Convert.FromBase64String(element.Value);
                        keyStore.AddKey(id, key);
                    }

                    if (isHeroicUpdate)
                        keyStore.WriteGuid = new Guid(applicationTag.Attribute("CurrentUpdate").Value);

                    break;
                case "cbloaderkeystore":
                    var otherStore = (KeyStore) SERIALIZER.Deserialize(new StringReader(document.ToString()));
                    foreach (var key in otherStore.UpdateKeys)
                        keyStore.AddKey(key.Id, key.KeyData);
                    if (otherStore.WriteGuid != null) keyStore.WriteGuid = otherStore.WriteGuid;
                    if (otherStore.FallbackKey != null) keyStore.FallbackKey = otherStore.FallbackKey;
                    break;
                default:
                    throw new Exception("Unknown key store type.");
            }

            if (isHeroicUpdate && (keyStore.WriteGuid == null || keyStore.Get(keyStore.WriteGuid) == null))
                throw new CBLoaderException($"Key file {filename} is not valid.");
        }
        private void loadRegistryKeys(Guid appGuid)
        {
            if (!Utils.IS_WINDOWS) return;

            // Since we are a 32-bit application, we are omitting the WOW6432Node component.
            var reg = Registry.LocalMachine
                .OpenSubKey($@"SOFTWARE\Wizards of the Coast\{appGuid.ToString()}");
            if (reg == null) return;

            Log.Debug(" - Reading keys from registry...");

            foreach (var keyName in reg.GetSubKeyNames())
                if (GUID_REGEX.IsMatch(keyName))
                {
                    var keyGuid = new Guid(keyName);
                    byte[] encryptedKey = Convert.FromBase64String(reg.OpenSubKey(keyName).GetValue(null).ToString());
                    byte[] keyData = ProtectedData.Unprotect(encryptedKey, REGISTRY_CRYPT_ENTROPY, DataProtectionScope.LocalMachine);
                    keyStore.AddKey(keyGuid, keyData);
                }
        }
        public CryptoInfo(LoaderOptions options)
        {
            this.options = options;

            Log.Info("Loading encryption keys.");
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            var parsedD20Engine = new ParsedD20RulesEngine(Path.Combine(options.CBPath, "D20RulesEngine.dll"));
            this.expectedDemoHash = parsedD20Engine.expectedDemoHash;
            this.expectedNormalHash = parsedD20Engine.expectedNormalHash;

            // This is intentionally loaded first, so there's a chance to override it from other sources.
            if (options.KeyFile != null)
                processUpdateFile(CB_APP_ID, options.KeyFile);

            processUpdateFile(CB_APP_ID, Path.Combine(options.CBPath, "HeroicDemo.update"), true);
            loadRegistryKeys(CB_APP_ID);

            var regPatcherPath = Path.Combine(options.CBPath, "RegPatcher.dat");
            if (File.Exists(regPatcherPath))
            {
                Log.Debug($" - Loading default key from {regPatcherPath}");
                keyStore.FallbackKey = Convert.FromBase64String(File.ReadAllText(regPatcherPath));
            }

            if (options.WriteKeyFile) saveKeyFile();

            stopwatch.Stop();
            Log.Debug($"Finished in {stopwatch.ElapsedMilliseconds} ms");
            Log.Debug();
        }

        private string FixXmlHash(string str) =>
            CryptoUtils.FixXmlHash(str, this.expectedDemoHash);

        private Stream GetXmlEncryptingStream(Stream s, Guid? updateId = null)
        {
            var id = updateId ?? keyStore.WriteGuid;
            return CryptoUtils.GetEncryptingStream(s, CB_APP_ID, id, this.keyStore.Get(id));
        }
        
        private void saveKeyFile()
        {
            Log.Debug(" - Writing key file.");
            using (var sw = new StreamWriter(File.Open(options.KeyFile, FileMode.Create), Encoding.UTF8))
                SERIALIZER.Serialize(sw, keyStore);
        }

        public void SaveRulesFile(XDocument document, string filename)
        {
            using (var crypt = GetXmlEncryptingStream(File.Open(filename, FileMode.Create)))
            {
                var sw = new StreamWriter(crypt, Encoding.UTF8);
                sw.Write(FixXmlHash(document.ToString(SaveOptions.DisableFormatting)));
                sw.Flush();
            }
        }

        private byte[] KeyForGuid(Guid updateId)
        {
            var key = keyStore.Get(updateId);
            if (key == null)
                throw new CBLoaderException(
                    "Could not retrieve key data from RegPatcher.dat.\n"+
                    "Please reinstall Character Builder or apply the April 2009 update or later.");
            return key;
        }

        public Stream OpenEncryptedFile(string filename) =>
            CryptoUtils.GetDecryptingStream(File.Open(filename, FileMode.Open, FileAccess.Read), CB_APP_ID, KeyForGuid);
    }
}
