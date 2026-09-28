using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace StreamingBridge
{
    internal sealed class MacroBinding
    {
        public string Id { get; set; }
        public int Number { get; set; }
        public int FunctionKey { get; set; }
        public int Desktop { get; set; }
        public string Scene { get; set; }

        internal MacroBinding Copy()
        {
            return new MacroBinding { Id = Id, Number = Number, FunctionKey = FunctionKey, Desktop = Desktop, Scene = Scene };
        }
    }

    internal sealed class BridgeProfile
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string ProtectedPassword { get; set; }
        public List<MacroBinding> Macros { get; set; }

        public BridgeProfile()
        {
            Id = Guid.NewGuid().ToString("N"); Name = "My bridge";
            Host = "localhost"; Port = 4455; ProtectedPassword = ""; Macros = new List<MacroBinding>();
        }
        public override string ToString() { return Name; }
        internal BridgeProfile Copy()
        {
            return new BridgeProfile { Id = Id, Name = Name, Host = Host, Port = Port,
                ProtectedPassword = ProtectedPassword, Macros = Macros.Select(macro => macro.Copy()).ToList() };
        }
    }

    internal sealed class BridgeSettings
    {
        public int Version { get; set; }
        public string ActiveProfileId { get; set; }
        public List<BridgeProfile> Profiles { get; set; }

        public BridgeSettings() { Version = 2; Profiles = new List<BridgeProfile>(); }
        internal BridgeProfile Active() { return Profiles.Single(profile => profile.Id == ActiveProfileId); }
        internal BridgeSettings Copy()
        {
            return new BridgeSettings { Version = Version, ActiveProfileId = ActiveProfileId,
                Profiles = Profiles.Select(profile => profile.Copy()).ToList() };
        }
    }

    internal static class ConfigStore
    {
        internal const int MaximumMacros = 24, MaximumDesktop = 64;
        internal static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
        internal static readonly string PathName = Path.Combine(Root, "config.json");

        internal static BridgeSettings Defaults()
        {
            BridgeProfile profile = new BridgeProfile();
            for (int i = 0; i < 6; i++) profile.Macros.Add(NewMacro(profile));
            return new BridgeSettings { ActiveProfileId = profile.Id, Profiles = new List<BridgeProfile> { profile } };
        }

        internal static MacroBinding NewMacro(BridgeProfile profile)
        {
            if (profile.Macros.Count >= MaximumMacros) throw new ArgumentException("A profile supports up to 24 macros.");
            int number = Enumerable.Range(1, MaximumMacros).First(value => !profile.Macros.Any(macro => macro.Number == value));
            int key = !profile.Macros.Any(macro => macro.FunctionKey == number) ? number
                : Enumerable.Range(1, MaximumMacros).First(value => !profile.Macros.Any(macro => macro.FunctionKey == value));
            return new MacroBinding { Id = Guid.NewGuid().ToString("N"), Number = number,
                FunctionKey = key, Desktop = number, Scene = "" };
        }

        internal static string UniqueName(BridgeSettings settings, string suggested)
        {
            // Leave room for a numeric suffix even when duplicating a long name.
            string basis = suggested.Length > 40 ? suggested.Substring(0, 40) : suggested;
            string candidate = basis;
            for (int i = 2; settings.Profiles.Any(profile => String.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)); i++)
                candidate = basis + " " + i;
            return candidate;
        }

        internal static BridgeProfile Duplicate(BridgeSettings settings, BridgeProfile source)
        {
            BridgeProfile copy = source.Copy(); copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = UniqueName(settings, source.Name + " copy");
            foreach (MacroBinding macro in copy.Macros) macro.Id = Guid.NewGuid().ToString("N");
            return copy;
        }

        internal static BridgeSettings Load(out bool migrated) { return Load(PathName, out migrated); }
        internal static BridgeSettings Load(string path, out bool migrated)
        {
            migrated = false;
            if (!File.Exists(path)) return Defaults();
            string json = File.ReadAllText(path);
            BridgeSettings settings = Parse(json, out migrated);
            if (migrated)
            {
                // Preserve the original bytes, including the existing DPAPI blob, before upgrading.
                string backup = path + ".before-profiles.bak";
                if (File.Exists(backup)) backup = path + ".before-profiles." + Guid.NewGuid().ToString("N") + ".bak";
                File.Copy(path, backup, false);
                Save(settings, path);
            }
            return settings;
        }

        internal static BridgeSettings Parse(string json, out bool migrated)
        {
            migrated = false;
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            Dictionary<string, object> fields = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (fields == null) throw new InvalidDataException("The saved settings are not a configuration object.");
            BridgeSettings settings;
            if (fields.ContainsKey("Profiles")) settings = serializer.Deserialize<BridgeSettings>(json);
            else
            {
                if (fields.ContainsKey("Version") || !fields.ContainsKey("Scenes"))
                    throw new InvalidDataException("The saved settings format is not recognized.");
                LegacyConfig legacy = serializer.Deserialize<LegacyConfig>(json);
                if (legacy.Scenes == null || legacy.Scenes.Length != 6)
                    throw new InvalidDataException("The original settings must contain six scene assignments.");
                BridgeProfile original = new BridgeProfile { Name = "Original setup", Host = legacy.Host, Port = legacy.Port,
                    ProtectedPassword = legacy.ProtectedPassword ?? "" };
                for (int i = 0; i < 6; i++)
                {
                    MacroBinding macro = NewMacro(original); macro.Scene = legacy.Scenes[i] ?? ""; original.Macros.Add(macro);
                }
                settings = new BridgeSettings { ActiveProfileId = original.Id, Profiles = new List<BridgeProfile> { original } };
                migrated = true;
            }
            Validate(settings);
            return settings;
        }

        internal static string Password(BridgeProfile profile)
        {
            return String.IsNullOrEmpty(profile.ProtectedPassword) ? "" : Encoding.UTF8.GetString(
                ProtectedData.Unprotect(Convert.FromBase64String(profile.ProtectedPassword), null, DataProtectionScope.CurrentUser));
        }

        internal static string ProtectPassword(string password)
        {
            return String.IsNullOrEmpty(password) ? "" : Convert.ToBase64String(
                ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser));
        }

        internal static void Validate(BridgeProfile profile)
        {
            if (profile == null || String.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 128)
                throw new ArgumentException("Every profile needs a valid identifier.");
            if (String.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 48 || profile.Name.IndexOfAny(new char[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("Enter a profile name between 1 and 48 characters.");
            if (String.IsNullOrWhiteSpace(profile.Host) || profile.Host.IndexOfAny(new char[] { '/', '\\', ':', '@', '?', '#', ' ', '\t', '\r', '\n' }) >= 0)
                throw new ArgumentException("Enter the OBS computer's IP address or hostname, without ws:// or a port.");
            if (profile.Port < 1 || profile.Port > 65535) throw new ArgumentException("Enter a port between 1 and 65535.");
            if (profile.Macros == null || profile.Macros.Count > MaximumMacros)
                throw new ArgumentException("A profile supports between zero and 24 macros.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            HashSet<int> numbers = new HashSet<int>(), keys = new HashSet<int>();
            foreach (MacroBinding macro in profile.Macros)
            {
                if (macro == null || String.IsNullOrWhiteSpace(macro.Id) || macro.Id.Length > 128 || !ids.Add(macro.Id))
                    throw new ArgumentException("Each macro needs its own identifier.");
                if (macro.Number < 1 || macro.Number > MaximumMacros || !numbers.Add(macro.Number))
                    throw new ArgumentException("Each macro needs a unique G number between 1 and 24.");
                if (macro.FunctionKey < 1 || macro.FunctionKey > MaximumMacros || !keys.Add(macro.FunctionKey))
                    throw new ArgumentException("Choose a different F1-F24 shortcut for each macro in this profile.");
                if (macro.Desktop < 1 || macro.Desktop > MaximumDesktop)
                    throw new ArgumentException("Choose a desktop number between 1 and 64.");
                if (macro.Scene == null) macro.Scene = "";
            }
            if (profile.ProtectedPassword == null) profile.ProtectedPassword = "";
        }

        internal static void Validate(BridgeSettings settings)
        {
            if (settings == null || settings.Version != 2) throw new ArgumentException("This settings version is not supported.");
            if (settings.Profiles == null || settings.Profiles.Count == 0) throw new ArgumentException("Keep at least one bridge profile.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal), names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BridgeProfile profile in settings.Profiles)
            {
                Validate(profile);
                if (!ids.Add(profile.Id)) throw new ArgumentException("Each profile needs its own identifier.");
                if (!names.Add(profile.Name.Trim())) throw new ArgumentException("Choose a different name for each profile.");
            }
            if (!ids.Contains(settings.ActiveProfileId ?? "")) throw new ArgumentException("Choose an existing profile to activate.");
        }

        internal static void Save(BridgeSettings settings) { Save(settings, PathName); }
        internal static void Save(BridgeSettings settings, string path)
        {
            Validate(settings);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private sealed class LegacyConfig
        {
            public string Host { get; set; }
            public int Port { get; set; }
            public string ProtectedPassword { get; set; }
            public string[] Scenes { get; set; }
        }
    }
}
