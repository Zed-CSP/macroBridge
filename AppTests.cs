using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
            try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidDataException) { rejected = true; }
            Assert(rejected, description);
        }
        private static bool SameBytes(string path, byte[] expected) { return File.ReadAllBytes(path).SequenceEqual(expected); }

        private static int Main()
        {
            string fixture = Path.Combine(ConfigStore.Root, "profiles-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixture);
            try
            {
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                string secret = String.Concat("test", "-password-only-", Guid.NewGuid().ToString("N"));
                string protectedValue = ConfigStore.ProtectPassword(secret);
                string[] names = { "Gaming", " Desktop two ", "", null, "Cam \"wide\"", "Pr\u00e9sentation" };
                string legacyJson = serializer.Serialize(new { Host = "192.0.2.10", Port = 4455, ProtectedPassword = protectedValue, Scenes = names });
                bool migrated;
                BridgeSettings settings = ConfigStore.Parse(legacyJson, out migrated);
                BridgeProfile original = settings.Active();
                Assert(migrated && settings.Version == 2 && original.Name == "Original setup", "An original configuration must become a named active profile.");
                Assert(original.Host == "192.0.2.10" && original.Port == 4455, "Migration must preserve the OBS connection.");
                Assert(original.ProtectedPassword == protectedValue && ConfigStore.Password(original) == secret, "Migration must preserve the original DPAPI credential without re-encrypting it.");
                Assert(original.Macros.Select(macro => macro.Scene).SequenceEqual(names.Select(name => name ?? "")), "Migration must preserve all six exact scene assignments.");
                Assert(original.Macros.Select(macro => macro.Number).SequenceEqual(Enumerable.Range(1, 6))
                    && original.Macros.All(macro => macro.FunctionKey == macro.Number && macro.Desktop == macro.Number), "Migration must preserve all original G keys, shortcuts, and desktop targets.");
                Assert(!serializer.Serialize(settings).Contains(secret), "Plaintext passwords must never occur in saved JSON.");
                Assert(original.Macros[1].Scene == " Desktop two " && original.Macros[4].Scene == "Cam \"wide\"" && original.Macros[5].Scene == "Pr\u00e9sentation", "Significant spaces, punctuation, and Unicode must survive migration.");

                string configPath = Path.Combine(fixture, "config.json");
                File.WriteAllText(configPath, legacyJson, new UTF8Encoding(false)); byte[] legacyBytes = File.ReadAllBytes(configPath);
                BridgeSettings loaded = ConfigStore.Load(configPath, out migrated);
                Assert(migrated && SameBytes(configPath + ".before-profiles.bak", legacyBytes), "The upgrade must preserve an exact local backup before replacing the legacy file.");
                loaded = ConfigStore.Load(configPath, out migrated);
                Assert(!migrated && loaded.Active().ProtectedPassword == protectedValue, "Loading an upgraded file must keep the selected profile and original credential.");
                Assert(Directory.GetFiles(fixture, "*.before-profiles*.bak").Length == 1, "Loading an upgraded file must not create more migration backups.");

                BridgeProfile copy = ConfigStore.Duplicate(settings, original); settings.Profiles.Add(copy);
                copy.Host = "macbook.local"; copy.Macros[0].Scene = "Studio";
                Assert(original.Macros[0].Scene == "Gaming" && original.Host == "192.0.2.10", "Duplicated profiles must have independent connection and macro settings.");
                Assert(copy.ProtectedPassword == protectedValue && copy.Id != original.Id && copy.Macros[0].Id != original.Macros[0].Id, "A duplicate must copy the encrypted credential while getting independent identifiers.");
                Assert(ConfigStore.Duplicate(settings, original).Name == "Original setup copy 2", "Repeated copies must get distinct profile names.");

                MacroBinding retained = copy.Macros[2].Copy();
                copy.Macros.RemoveAt(1);
                Assert(copy.Macros[1].Id == retained.Id && copy.Macros[1].Number == 3 && copy.Macros[1].FunctionKey == 3 && copy.Macros[1].Desktop == 3, "Removing a macro must not renumber or remap remaining macros.");
                MacroBinding added = ConfigStore.NewMacro(copy); copy.Macros.Add(added);
                Assert(added.Number == 2 && added.FunctionKey == 2 && added.Id != retained.Id, "Adding a macro must reuse an available slot without changing existing bindings.");
                BridgeProfile customized = original.Copy(); customized.Macros[0].FunctionKey = 8; customized.Macros.RemoveAt(1);
                Assert(ConfigStore.NewMacro(customized).FunctionKey == 2, "A new G2 should prefer F2 when that key is free, even if another macro moved away from F1.");
                added.FunctionKey = 24; added.Desktop = 64; added.Scene = " Caf\u00e9 / \"wide\" ";
                settings.ActiveProfileId = copy.Id; ConfigStore.Save(settings, configPath);
                BridgeSettings roundTrip = ConfigStore.Load(configPath, out migrated);
                Assert(roundTrip.ActiveProfileId == copy.Id && roundTrip.Profiles.Count == 2, "Saved profile selection and all profiles must survive a restart.");
                MacroBinding custom = roundTrip.Active().Macros.Single(macro => macro.Id == added.Id);
                Assert(custom.FunctionKey == 24 && custom.Desktop == 64 && custom.Scene == added.Scene, "Custom shortcuts, desktop targets, and exact scene names must persist.");
                Assert(roundTrip.Profiles.Single(profile => profile.Id == original.Id).ProtectedPassword == protectedValue, "Saving another profile must preserve the original profile's encrypted credential.");
                Assert(SameBytes(configPath + ".before-profiles.bak", legacyBytes), "Later profile saves must not change the original backup.");

                BridgeProfile empty = new BridgeProfile { Name = "Empty setup" }; settings.Profiles.Add(empty);
                settings.ActiveProfileId = empty.Id; ConfigStore.Save(settings, configPath);
                Assert(ConfigStore.Load(configPath, out migrated).Active().Macros.Count == 0, "A profile with zero macros must save and load.");
                Assert(ConfigStore.Password(empty) == "", "Unauthenticated OBS must allow an empty password.");

                foreach (string invalidHost in new[] { "ws://192.0.2.10", "192.0.2.10:4455", "user@obs.local", "obs.local?query", "" })
                {
                    BridgeProfile invalid = original.Copy(); invalid.Host = invalidHost;
                    Reject(() => ConfigStore.Validate(invalid), "Malformed host fields must be rejected.");
                }
                foreach (int invalidPort in new[] { 0, 65536 })
                {
                    BridgeProfile invalid = original.Copy(); invalid.Port = invalidPort;
                    Reject(() => ConfigStore.Validate(invalid), "Ports outside 1-65535 must be rejected.");
                }
                BridgeProfile duplicateKey = original.Copy(); duplicateKey.Macros[1].FunctionKey = duplicateKey.Macros[0].FunctionKey;
                Reject(() => ConfigStore.Validate(duplicateKey), "A profile must not accept two macros using the same shortcut.");
                BridgeProfile invalidDesktop = original.Copy(); invalidDesktop.Macros[0].Desktop = 0;
                Reject(() => ConfigStore.Validate(invalidDesktop), "Desktop targets must be positive.");
                invalidDesktop.Macros[0].Desktop = 65;
                Reject(() => ConfigStore.Validate(invalidDesktop), "Desktop targets must respect the supported limit.");
                BridgeProfile invalidKey = original.Copy(); invalidKey.Macros[0].FunctionKey = 25;
                Reject(() => ConfigStore.Validate(invalidKey), "Function keys outside F1-F24 must be rejected.");
                BridgeSettings duplicateName = settings.Copy(); duplicateName.Profiles[1].Name = duplicateName.Profiles[0].Name.ToUpperInvariant();
                Reject(() => ConfigStore.Validate(duplicateName), "Profile names must be unique regardless of case.");
                BridgeSettings noActive = settings.Copy(); noActive.ActiveProfileId = Guid.NewGuid().ToString("N");
                Reject(() => ConfigStore.Validate(noActive), "The active profile must exist.");
                BridgeSettings future = settings.Copy(); future.Version = 99;
                Reject(() => ConfigStore.Parse(serializer.Serialize(future), out migrated), "Unknown settings versions must not be rewritten.");
                BridgeSettings noProfiles = new BridgeSettings();
                Reject(() => ConfigStore.Validate(noProfiles), "Deleting the last profile must be rejected.");

                BridgeProfile full = new BridgeProfile();
                for (int i = 0; i < ConfigStore.MaximumMacros; i++) full.Macros.Add(ConfigStore.NewMacro(full));
                Assert(full.Macros.Select(macro => macro.FunctionKey).Distinct().Count() == 24, "A full profile must provide 24 distinct shortcuts.");
                Reject(() => ConfigStore.NewMacro(full), "Adding a 25th macro must be rejected.");

                byte[] beforeFailedSave = File.ReadAllBytes(configPath);
                Reject(() => ConfigStore.Save(duplicateName, configPath), "An invalid edit must fail before replacing saved settings.");
                Assert(SameBytes(configPath, beforeFailedSave), "A failed profile save must preserve existing settings.");
                Assert(Directory.GetFiles(fixture, "*.tmp").Length == 0, "Profile saves must not leave temporary settings behind.");
                string invalidPath = Path.Combine(fixture, "invalid.json");
                File.WriteAllText(invalidPath, serializer.Serialize(new { Host = "localhost", Port = 4455, Scenes = new string[5] }));
                byte[] invalidBytes = File.ReadAllBytes(invalidPath);
                Reject(() => ConfigStore.Load(invalidPath, out migrated), "Incomplete legacy settings must not be silently upgraded.");
                Assert(SameBytes(invalidPath, invalidBytes) && !File.Exists(invalidPath + ".before-profiles.bak"), "Rejected migrations must not alter the original file.");

                BridgeProfile corruptProfile = original.Copy();
                byte[] corrupt = Convert.FromBase64String(corruptProfile.ProtectedPassword); corrupt[corrupt.Length - 1] ^= 0xff;
                corruptProfile.ProtectedPassword = Convert.ToBase64String(corrupt);
                bool refused = false;
                try { ConfigStore.Password(corruptProfile); } catch (CryptographicException) { refused = true; }
                Assert(refused, "Corrupted encrypted credentials must be rejected.");

                Console.WriteLine("PASS: " + assertions + " migration, profile, macro, and credential checks. No desktop or OBS changes.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.Message); return 1; }
            finally
            {
                foreach (string file in Directory.GetFiles(fixture)) File.Delete(file);
                Directory.Delete(fixture);
            }
        }
    }
}
