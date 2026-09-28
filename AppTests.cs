using System;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace StreamingBridge
{
    internal static class AppTests
    {
        private static int assertions;
        private static void Assert(bool condition, string description)
        {
            if (!condition) throw new Exception(description);
            assertions++;
        }
        private static void Reject(Action action, string description)
        {
            bool rejected = false;
            try { action(); } catch (ArgumentException) { rejected = true; }
            Assert(rejected, description);
        }
        private static int Main()
        {
            try
            {
                string secret = String.Concat("test", "-password-only-", Guid.NewGuid().ToString("N"));
                string[] names = { "Gaming", " Desktop two ", "", null, "Cam \"wide\"", "Présentation" };
                BridgeConfig config = ConfigStore.Make("192.0.2.10", 4455, secret, names);
                string json = new JavaScriptSerializer().Serialize(config);
                Assert(!json.Contains(secret), "Plaintext password must not occur in saved JSON.");
                BridgeConfig read = new JavaScriptSerializer().Deserialize<BridgeConfig>(json);
                Assert(ConfigStore.Password(read) == secret, "Encrypted password must round-trip for this Windows user.");
                Assert(read.Scenes[1] == " Desktop two ", "Scene names must preserve significant spaces.");
                Assert(read.Scenes[4] == "Cam \"wide\"" && read.Scenes[5] == "Présentation", "Scene names must preserve JSON punctuation and Unicode.");
                Assert(ConfigStore.Password(ConfigStore.Make("macbook.local", 4455, "", new string[6])) == "", "Unauthenticated OBS must allow an empty password.");
                Reject(() => ConfigStore.Make("ws://192.0.2.10", 4455, "", new string[6]), "A URL must not be accepted as a host.");
                Reject(() => ConfigStore.Make("192.0.2.10:4455", 4455, "", new string[6]), "A host field must not include its port.");
                Reject(() => ConfigStore.Make("user@obs.local", 4455, "", new string[6]), "A host field must not include user information.");
                Reject(() => ConfigStore.Make("obs.local?query", 4455, "", new string[6]), "A host field must not include a query.");
                Reject(() => ConfigStore.Make("", 4455, "", new string[6]), "Empty host must be rejected.");
                Reject(() => ConfigStore.Make("macbook.local", 0, "", new string[6]), "Port zero must be rejected.");
                Reject(() => ConfigStore.Make("macbook.local", 65536, "", new string[6]), "Port above 65535 must be rejected.");
                byte[] corrupt = Convert.FromBase64String(read.ProtectedPassword); corrupt[corrupt.Length - 1] ^= 0xff;
                read.ProtectedPassword = Convert.ToBase64String(corrupt);
                bool refused = false;
                try { ConfigStore.Password(read); } catch (CryptographicException) { refused = true; }
                Assert(refused, "Corrupted encrypted credentials must be rejected.");
                Console.WriteLine("PASS: " + assertions + " configuration and credential checks. No desktop or OBS changes.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
        }
    }
}
