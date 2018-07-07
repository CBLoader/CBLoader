using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace CharacterBuilderLoader
{
    /// <summary>
    /// A class that handles calculating the hashes the character builder uses
    /// to validate its files.
    /// 
    /// This supports calculating such checksums, and performing preimage attacks
    /// to fix the hash of our generated files. (Since the hash is hardcoded into
    /// D20RulesEngine.dll)
    /// </summary>
    internal sealed class CBDataHasher
    {
        public uint State { get; set; }

        public CBDataHasher()
        {
            this.State = 5381;
        }
        public CBDataHasher(uint state)
        {
            this.State = state;
        }

        public void Update(char ch)
        {
            this.State *= 33;
            this.State += ch;
        }
        public void Update(string str)
        {
            foreach (var ch in str) Update(ch);
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
        
        public string CalculatePreimage(uint targetHash, string tail)
        {
            CBDataHasher target = new CBDataHasher(targetHash);
            target.UpdateReverse(tail);
            target.State -= this.State * 39135393; // 33^5

            var outStr = "";
            for (int i = 0; i < 5; i++)
            {
                var tint = target.State;
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

    public sealed class CryptoInfo
    {
        public CryptoInfo(string baseDirectory)
        {

        }
    }

    public sealed class CryptoUtils
    {
        public static Guid CB_APP_ID = new Guid("2a1ddbc4-4503-4392-9548-d0010d1ba9b1");
        public static Guid HEROIC_DEMO_UPDATE_ID = new Guid("19806aaa-6d71-425d-9dcf-54e6bb6b1e57");

        public static Guid INJECT_UPDATE_ID = new Guid("f8ae4afc-dd59-46df-b467-0071d90e953d");
        public static string INJECT_UPDATE_KEY = "1Asic5ZWYplb3pdSZ7KcMP+8kuxQvCW02bNdlUtMT44=";

        private static string XML_MARKER = "<!-- This file has been edited by CBLoader -->";

        public static bool IsXmlPatched(string str)
        {
            return str.Contains(XML_MARKER);
        }
        public static uint HashString(string str)
        {
            var hasher = new CBDataHasher();
            hasher.Update(str);
            return hasher.State;
        }
        public static string FixXmlHash(string str, uint target_hash)
        {
            if (str.StartsWith("\uFEFF"))
            {
                // This is apparently stripped by ReadToEnd.
                str = str.Substring(1, str.Length - 1);
            }

            str += "\n" + XML_MARKER + "\n<!-- Fix hash: ";
            var hasher = new CBDataHasher();
            hasher.Update(str);
            str += hasher.CalculatePreimage(target_hash, " -->\n");
            Trace.Assert(HashString(str) == target_hash, "FixXmlHash failed!");
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
}
