using System.Text.Json;

namespace AudioProfileSwitcher;

[Flags]
public enum HotKeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
    NoRepeat = 0x4000 // registration-only flag, never stored in config
}

public enum HotKeyTarget { Card, Output, Input, Mute }

public class HotKeyBinding
{
    public HotKeyModifiers Modifiers { get; set; } = HotKeyModifiers.Control | HotKeyModifiers.Shift;
    public Keys Key { get; set; } = Keys.A;

    public string FormatModifiers()
    {
        var parts = new List<string>();
        if ((Modifiers & HotKeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotKeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotKeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotKeyModifiers.Win) != 0) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public string Format()
    {
        string key = Key >= Keys.D0 && Key <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString();
        var mods = FormatModifiers();
        return mods.Length == 0 ? key : mods + " + " + key;
    }

    public bool SameAs(HotKeyBinding other) => Modifiers == other.Modifiers && Key == other.Key;
}

public class AudioCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Card";
    public Guid? OutputId { get; set; }
    public string OutputName { get; set; } = "None";
    public Guid? InputId { get; set; }
    public string InputName { get; set; } = "None";
    public bool AutoActivate { get; set; }
}

public class AppConfig
{
    public List<AudioCard> Cards { get; set; } = new();
    public Guid? ActiveCardId { get; set; }
    public HotKeyBinding CardHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.A };
    public HotKeyBinding OutputHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.O };
    public HotKeyBinding InputHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.I };
    public HotKeyBinding MuteHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.M };
    public List<Guid> CycleOutputIds { get; set; } = new();
    public List<Guid> CycleInputIds { get; set; } = new();
    public bool ChimeEnabled { get; set; } = true;
    public bool JumpHotKeysEnabled { get; set; } = true;
    public bool TrayHintShown { get; set; }
}

/// <summary>Item shown in combo boxes / checked lists. Id == null means "don't change".</summary>
public sealed record DeviceItem(Guid? Id, string Name, bool Connected = true)
{
    public override string ToString() => Connected ? Name : Name + " (not connected)";
}

/// <summary>Loads/saves config in %AppData%, never silently discarding an unreadable file.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string FilePath { get; }
    public string? LoadWarning { get; private set; }

    public ConfigStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AudioProfileSwitcher");
        Directory.CreateDirectory(dir);
        FilePath = Path.Combine(dir, "config.json");

        // Migrate the old next-to-the-exe config on first run of this version.
        var legacy = Path.Combine(AppContext.BaseDirectory, "config.json");
        if (!File.Exists(FilePath) && File.Exists(legacy))
        {
            try { File.Copy(legacy, FilePath); } catch { }
        }
    }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath)) return new AppConfig();
        try
        {
            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOpts) ?? new AppConfig();
        }
        catch
        {
            try
            {
                var backup = FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(FilePath, backup, true);
                LoadWarning = "Settings file was unreadable; a backup was kept (" + Path.GetFileName(backup) + ")";
            }
            catch
            {
                LoadWarning = "Settings file was unreadable and could not be backed up";
            }
            return new AppConfig();
        }
    }

    /// <summary>Returns null on success, otherwise an error message.</summary>
    public string? Save(AppConfig config)
    {
        try
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(config, JsonOpts));
            File.Move(tmp, FilePath, true);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
