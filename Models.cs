using System.Text.Json;
using System.Text.Json.Serialization;

namespace AudioProfileSwitcher;

public static class AppInfo
{
    public const string Version = "1.4.0";
    public const string ReleasesPageUrl = "https://github.com/chryow/AudioProfileSwitcher/releases";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/chryow/AudioProfileSwitcher/releases/latest";
}

#if !WINDOWS
/// <summary>Minimal Win32 virtual-key subset so shared models compile cleanly on non-Windows test targets.</summary>
[Flags]
public enum Keys
{
    KeyCode = 0x0000FFFF,
    Modifiers = unchecked((int)0xFFFF0000),
    None = 0,
    Back = 8,
    Tab = 9,
    Enter = 13,
    ShiftKey = 16,
    ControlKey = 17,
    Menu = 18,
    Escape = 27,
    Space = 32,
    PageUp = 33,
    PageDown = 34,
    End = 35,
    Home = 36,
    Left = 37,
    Up = 38,
    Right = 39,
    Down = 40,
    Insert = 45,
    Delete = 46,
    D0 = 48, D1 = 49, D2 = 50, D3 = 51, D4 = 52,
    D5 = 53, D6 = 54, D7 = 55, D8 = 56, D9 = 57,
    A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71,
    H = 72, I = 73, J = 74, K = 75, L = 76, M = 77, N = 78,
    O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85,
    V = 86, W = 87, X = 88, Y = 89, Z = 90,
    LWin = 91,
    RWin = 92,
    NumPad0 = 96, NumPad1 = 97, NumPad2 = 98, NumPad3 = 99, NumPad4 = 100,
    NumPad5 = 101, NumPad6 = 102, NumPad7 = 103, NumPad8 = 104, NumPad9 = 105,
    F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117,
    F7 = 118, F8 = 119, F9 = 120, F10 = 121, F11 = 122, F12 = 123,
    Shift = 0x00010000,
    Control = 0x00020000,
    Alt = 0x00040000
}
#endif

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

public enum HotKeyTarget { Card, Output, Input, Mute, ToggleWindow }

public enum FallbackMode
{
    None = 0,
    PreviousCard = 1,
    SpecificCard = 2
}

public enum MuteBannerPosition
{
    TopCenter = 0,
    TopRight = 1,
    BottomRight = 2,
    ActiveScreenTop = 3,
    Custom = 4
}

public class HotKeyBinding
{
    public HotKeyModifiers Modifiers { get; set; } = HotKeyModifiers.Control | HotKeyModifiers.Shift;
    public Keys Key { get; set; } = Keys.A;

    [JsonIgnore]
    public bool IsEnabled => Key != Keys.None && Modifiers != HotKeyModifiers.None;

    public string FormatModifiers()
    {
        var parts = new List<string>(4);
        if ((Modifiers & HotKeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & HotKeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & HotKeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & HotKeyModifiers.Win) != 0) parts.Add("Win");
        return string.Join(" + ", parts);
    }

    public string Format()
    {
        if (!IsEnabled) return "Disabled";
        string key = Key >= Keys.D0 && Key <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString();
        var mods = FormatModifiers();
        return mods.Length == 0 ? key : mods + " + " + key;
    }

    public bool SameAs(HotKeyBinding other) =>
        IsEnabled && other.IsEnabled && Modifiers == other.Modifiers && Key == other.Key;
}

public class CycleDeviceEntry
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
}

public class AudioCard
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Card";
    public Guid? OutputId { get; set; }
    public string OutputName { get; set; } = "None";
    public Guid? CommOutputId { get; set; }
    public string CommOutputName { get; set; } = "None";
    public Guid? InputId { get; set; }
    public string InputName { get; set; } = "None";
    public int? OutputVolume { get; set; }
    public int? InputVolume { get; set; }
    public bool AutoActivate { get; set; }

    public AudioCard CloneAsDuplicate(string? newName = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = newName ?? $"{Name} (Copy)",
        OutputId = OutputId,
        OutputName = OutputName,
        CommOutputId = CommOutputId,
        CommOutputName = CommOutputName,
        InputId = InputId,
        InputName = InputName,
        OutputVolume = OutputVolume,
        InputVolume = InputVolume,
        AutoActivate = false
    };
}

public class AppConfig
{
    private FallbackMode? _fallbackMode;

    public List<AudioCard> Cards { get; set; } = new();
    public Guid? ActiveCardId { get; set; }
    public Guid? PreviousCardId { get; set; }

    public FallbackMode FallbackMode
    {
        get => _fallbackMode ?? (FallbackCardId.HasValue ? FallbackMode.SpecificCard : FallbackMode.None);
        set => _fallbackMode = value;
    }

    public Guid? FallbackCardId { get; set; }

    public HotKeyBinding CardHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.A };
    public HotKeyBinding OutputHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.O };
    public HotKeyBinding InputHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.I };
    public HotKeyBinding MuteHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.M };
    public HotKeyBinding ToggleWindowHotKey { get; set; } = new() { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.Space };

    public List<CycleDeviceEntry> CycleOutputs { get; set; } = new();
    public List<CycleDeviceEntry> CycleInputs { get; set; } = new();

    // Legacy v1.1 fields migrated on load into CycleOutputs / CycleInputs.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Guid>? CycleOutputIds { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Guid>? CycleInputIds { get; set; }

    public bool ChimeEnabled { get; set; } = true;
    public bool MuteChimeEnabled { get; set; } = true;
    public bool JumpHotKeysEnabled { get; set; } = true;
    public bool MuteBannerEnabled { get; set; } = true;
    public bool MuteBannerInteractive { get; set; }
    public MuteBannerPosition MuteBannerPosition { get; set; } = MuteBannerPosition.TopCenter;
    public int? MuteBannerX { get; set; }
    public int? MuteBannerY { get; set; }
    public int? WindowX { get; set; }
    public int? WindowY { get; set; }
    public int? WindowHeight { get; set; }
    public bool TrayHintShown { get; set; }
}

/// <summary>Portable JSON bundle for exporting/importing cards and cycle filters across PCs.</summary>
public sealed class ProfileExportBundle
{
    public string Version { get; set; } = AppInfo.Version;
    public DateTime ExportedAtUtc { get; set; } = DateTime.UtcNow;
    public List<AudioCard> Cards { get; set; } = new();
    public FallbackMode FallbackMode { get; set; }
    public Guid? FallbackCardId { get; set; }
    public List<CycleDeviceEntry> CycleOutputs { get; set; } = new();
    public List<CycleDeviceEntry> CycleInputs { get; set; } = new();
}

/// <summary>Item shown in combo boxes / checked lists. Id == null means "don't change".</summary>
public sealed record DeviceItem(Guid? Id, string Name, bool Connected = true, int? CurrentVolume = null)
{
    public override string ToString() => Connected ? Name : Name + " (not connected)";
}

public sealed record AudioSnapshot(
    HashSet<Guid> OutputIds,
    HashSet<Guid> InputIds,
    Dictionary<Guid, string> OutputNames,
    Dictionary<Guid, string> InputNames,
    Guid? DefaultOut,
    Guid? DefaultIn,
    string? OutName,
    string? InName,
    bool MicMuted,
    Guid? DefaultCommOut = null,
    string? CommOutName = null)
{
    /// <summary>Zero-allocation structural state comparison for UI/device-change detection.</summary>
    public bool EqualsState(AudioSnapshot? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;

        if (MicMuted != other.MicMuted ||
            DefaultOut != other.DefaultOut ||
            DefaultCommOut != other.DefaultCommOut ||
            DefaultIn != other.DefaultIn)
        {
            return false;
        }

        if (!string.Equals(OutName, other.OutName, StringComparison.Ordinal) ||
            !string.Equals(CommOutName, other.CommOutName, StringComparison.Ordinal) ||
            !string.Equals(InName, other.InName, StringComparison.Ordinal))
        {
            return false;
        }

        if (OutputIds.Count != other.OutputIds.Count ||
            InputIds.Count != other.InputIds.Count ||
            OutputNames.Count != other.OutputNames.Count ||
            InputNames.Count != other.InputNames.Count)
        {
            return false;
        }

        foreach (var id in OutputIds)
        {
            if (!other.OutputIds.Contains(id)) return false;
        }

        foreach (var id in InputIds)
        {
            if (!other.InputIds.Contains(id)) return false;
        }

        foreach (var kv in OutputNames)
        {
            if (!other.OutputNames.TryGetValue(kv.Key, out var otherName) ||
                !string.Equals(kv.Value, otherName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var kv in InputNames)
        {
            if (!other.InputNames.TryGetValue(kv.Key, out var otherName) ||
                !string.Equals(kv.Value, otherName, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Size-capped rolling log in %AppData%\AudioProfileSwitcher\app.log.</summary>
public static class AppLog
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Sync = new();

    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioProfileSwitcher");

    public static string FilePath { get; } = Path.Combine(DataDir, "app.log");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".1", true);
                }

                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
                if (ex != null) line += Environment.NewLine + ex;
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch { /* never throw from logger */ }
        }
    }
}

/// <summary>Loads/saves config in %AppData%, never silently discarding an unreadable file.</summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public string FilePath { get; }
    public string? LoadWarning { get; private set; }

    public ConfigStore() : this(AppLog.DataDir, migrateLegacyBaseDir: true) { }

    public ConfigStore(string dataDir, bool migrateLegacyBaseDir = false)
    {
        Directory.CreateDirectory(dataDir);
        FilePath = Path.Combine(dataDir, "config.json");

        if (migrateLegacyBaseDir)
        {
            var legacy = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (!File.Exists(FilePath) && File.Exists(legacy))
            {
                try
                {
                    File.Copy(legacy, FilePath);
                    AppLog.Info("Migrated legacy config.json to %AppData%.");
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to migrate legacy config.json.", ex);
                }
            }
        }
    }

    public AppConfig Load()
    {
        if (!File.Exists(FilePath)) return new AppConfig();
        try
        {
            var cfg = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), JsonOpts) ?? new AppConfig();
            if (ProfileLogic.MigrateConfig(cfg))
            {
                Save(cfg);
            }
            return cfg;
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to read config.json.", ex);
            try
            {
                var backup = FilePath + $".corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
                File.Copy(FilePath, backup, true);
                LoadWarning = "Settings file was unreadable; a backup was kept (" + Path.GetFileName(backup) + ")";
            }
            catch (Exception backupEx)
            {
                AppLog.Error("Failed to back up corrupt config.json.", backupEx);
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
            AppLog.Error("Failed to save config.json.", ex);
            return ex.Message;
        }
    }
}