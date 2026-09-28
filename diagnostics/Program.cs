using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OBSWebsocketDotNet;

// This program uses the bridge's saved DPAPI credential, then makes only a
// GetSceneList request. It never accepts or prints a password on the command line.
string configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config.json"));
if (!File.Exists(configPath))
{
    Console.Error.WriteLine("No saved bridge config.json found. Save the bridge settings first.");
    return 2;
}

string host;
int port;
string password;
string configStep = "read";
try
{
    using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(configPath));
    JsonElement root = doc.RootElement;
    configStep = "fields";
    if (root.TryGetProperty("Profiles", out JsonElement profiles))
    {
        if (root.GetProperty("Version").GetInt32() != 2) throw new InvalidDataException();
        string activeId = root.GetProperty("ActiveProfileId").GetString() ?? "";
        root = profiles.EnumerateArray().Single(profile => profile.GetProperty("Id").GetString() == activeId);
    }
    host = root.GetProperty("Host").GetString() ?? "";
    port = root.GetProperty("Port").GetInt32();
    string protectedPassword = root.GetProperty("ProtectedPassword").GetString() ?? "";
    configStep = "DPAPI decryption";
    password = protectedPassword.Length == 0 ? "" : Encoding.UTF8.GetString(
        ProtectedData.Unprotect(Convert.FromBase64String(protectedPassword), null, DataProtectionScope.CurrentUser));
    configStep = "validation";
    if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535)
        throw new InvalidDataException();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Cannot load saved bridge settings at {configStep}: {ex.GetType().Name}.");
    return 2;
}

Console.WriteLine($"Testing OBS at {host}:{port} with the saved bridge credential.");
var obs = new OBSWebsocket();
var outcome = new TaskCompletionSource<(bool Connected, string Detail)>(TaskCreationOptions.RunContinuationsAsynchronously);
obs.Connected += (_, _) => outcome.TrySetResult((true, "Identified"));
obs.Disconnected += (_, info) => outcome.TrySetResult((false, info.ObsCloseCode.ToString()));

try
{
    obs.ConnectAsync($"ws://{host}:{port}", password);
    Task completed = await Task.WhenAny(outcome.Task, Task.Delay(TimeSpan.FromSeconds(12)));
    if (completed != outcome.Task)
    {
        Console.Error.WriteLine("No authentication result within 12 seconds.");
        return 1;
    }
    var result = await outcome.Task;
    if (!result.Connected)
    {
        Console.Error.WriteLine($"Connection rejected or closed: {result.Detail}.");
        return 1;
    }

    // The sole OBS request in this diagnostic is read-only.
    var scenes = obs.GetSceneList().Scenes;
    Console.WriteLine($"Authenticated. Scene list read successfully ({scenes.Count} scenes).");
    foreach (var scene in scenes)
        Console.WriteLine($"- {scene.Name}");
    return 0;
}
catch (Exception ex)
{
    // Exception messages from transports can contain connection details. Print
    // the type only so neither the password nor its derivatives reach logs.
    Console.Error.WriteLine($"Diagnostic failed: {ex.GetType().Name}.");
    return 1;
}
finally
{
    obs.Disconnect();
}
