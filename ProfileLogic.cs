using System.Drawing;

namespace AudioProfileSwitcher;

/// <summary>
/// Platform-independent profile, migration, device-relinking, fallback, layout, and single-flight logic.
/// </summary>
public static class ProfileLogic
{
    public static bool IsMeaningfulDeviceName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !name.Equals("None", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("(Don't change)", StringComparison.OrdinalIgnoreCase) &&
        !name.Equals("(Same as output)", StringComparison.OrdinalIgnoreCase);

    /// <summary>Migrates legacy v1.1 config fields (FallbackCardId, CycleOutputIds, CycleInputIds) in place.</summary>
    public static bool MigrateConfig(AppConfig config)
    {
        bool changed = false;

        // Materialize implicit fallback mode so subsequent saves write it explicitly.
        var mode = config.FallbackMode;
        if (mode == FallbackMode.SpecificCard)
        {
            if (!config.FallbackCardId.HasValue ||
                (config.Cards.Count > 0 && config.Cards.All(c => c.Id != config.FallbackCardId.Value)))
            {
                mode = FallbackMode.None;
                config.FallbackCardId = null;
                changed = true;
            }
        }
        else if (config.FallbackCardId.HasValue)
        {
            config.FallbackCardId = null;
            changed = true;
        }
        config.FallbackMode = mode;

        if (config.CycleOutputIds is { Count: > 0 } legacyOuts)
        {
            foreach (var id in legacyOuts)
            {
                if (config.CycleOutputs.Any(x => x.Id == id)) continue;
                var knownName = config.Cards
                    .Select(c => c.OutputId == id ? c.OutputName : (c.CommOutputId == id ? c.CommOutputName : null))
                    .FirstOrDefault(IsMeaningfulDeviceName) ?? "";
                config.CycleOutputs.Add(new CycleDeviceEntry { Id = id, Name = knownName });
            }
            config.CycleOutputIds = null;
            changed = true;
        }
        else if (config.CycleOutputIds != null)
        {
            config.CycleOutputIds = null;
            changed = true;
        }

        if (config.CycleInputIds is { Count: > 0 } legacyIns)
        {
            foreach (var id in legacyIns)
            {
                if (config.CycleInputs.Any(x => x.Id == id)) continue;
                var knownName = config.Cards
                    .Select(c => c.InputId == id ? c.InputName : null)
                    .FirstOrDefault(IsMeaningfulDeviceName) ?? "";
                config.CycleInputs.Add(new CycleDeviceEntry { Id = id, Name = knownName });
            }
            config.CycleInputIds = null;
            changed = true;
        }
        else if (config.CycleInputIds != null)
        {
            config.CycleInputIds = null;
            changed = true;
        }

        if (config.OutputVolumeOutOfRange() || config.InputVolumeOutOfRange())
        {
            foreach (var c in config.Cards)
            {
                if (c.OutputVolume.HasValue) c.OutputVolume = Math.Clamp(c.OutputVolume.Value, 0, 100);
                if (c.InputVolume.HasValue) c.InputVolume = Math.Clamp(c.InputVolume.Value, 0, 100);
            }
            changed = true;
        }

        return changed;
    }

    private static bool OutputVolumeOutOfRange(this AppConfig config) =>
        config.Cards.Any(c => c.OutputVolume is < 0 or > 100);

    private static bool InputVolumeOutOfRange(this AppConfig config) =>
        config.Cards.Any(c => c.InputVolume is < 0 or > 100);

    public static bool CardIsActive(AudioCard c, AudioSnapshot s) =>
        (c.OutputId.HasValue || c.CommOutputId.HasValue || c.InputId.HasValue)
        && (!c.OutputId.HasValue || c.OutputId == s.DefaultOut)
        && (!c.CommOutputId.HasValue || c.CommOutputId == (s.DefaultCommOut ?? s.DefaultOut))
        && (!c.InputId.HasValue || c.InputId == s.DefaultIn);

    public static bool CardIsAvailable(AudioCard c, AudioSnapshot? s)
    {
        if (s == null) return true;
        return (!c.OutputId.HasValue || s.OutputIds.Contains(c.OutputId.Value))
            && (!c.CommOutputId.HasValue || s.OutputIds.Contains(c.CommOutputId.Value))
            && (!c.InputId.HasValue || s.InputIds.Contains(c.InputId.Value));
    }

    /// <summary>
    /// Relinks disconnected card &amp; cycle-filter GUIDs when a connected device matches by full name,
    /// and backfills empty names on migrated cycle entries.
    /// </summary>
    public static bool RelinkByName(AppConfig config, AudioSnapshot snap, Action<string>? log = null)
    {
        bool changed = false;

        foreach (var card in config.Cards)
        {
            if (card.OutputId.HasValue && !snap.OutputIds.Contains(card.OutputId.Value) &&
                IsMeaningfulDeviceName(card.OutputName))
            {
                var match = FindByName(snap.OutputNames, card.OutputName);
                if (match.HasValue)
                {
                    card.OutputId = match.Value;
                    changed = true;
                    log?.Invoke($"Auto-relinked card '{card.Name}' output by name to {match.Value}.");
                }
            }

            if (card.CommOutputId.HasValue && !snap.OutputIds.Contains(card.CommOutputId.Value) &&
                IsMeaningfulDeviceName(card.CommOutputName))
            {
                var match = FindByName(snap.OutputNames, card.CommOutputName);
                if (match.HasValue)
                {
                    card.CommOutputId = match.Value;
                    changed = true;
                    log?.Invoke($"Auto-relinked card '{card.Name}' comm output by name to {match.Value}.");
                }
            }

            if (card.InputId.HasValue && !snap.InputIds.Contains(card.InputId.Value) &&
                IsMeaningfulDeviceName(card.InputName))
            {
                var match = FindByName(snap.InputNames, card.InputName);
                if (match.HasValue)
                {
                    card.InputId = match.Value;
                    changed = true;
                    log?.Invoke($"Auto-relinked card '{card.Name}' input by name to {match.Value}.");
                }
            }
        }

        if (RelinkCycleEntries(config.CycleOutputs, snap.OutputIds, snap.OutputNames, "output", log))
            changed = true;

        if (RelinkCycleEntries(config.CycleInputs, snap.InputIds, snap.InputNames, "input", log))
            changed = true;

        return changed;
    }

    private static bool RelinkCycleEntries(
        List<CycleDeviceEntry> entries,
        HashSet<Guid> connectedIds,
        Dictionary<Guid, string> connectedNames,
        string kind,
        Action<string>? log)
    {
        bool changed = false;
        foreach (var entry in entries)
        {
            if (connectedIds.Contains(entry.Id))
            {
                if (connectedNames.TryGetValue(entry.Id, out var liveName) &&
                    !string.Equals(entry.Name, liveName, StringComparison.Ordinal))
                {
                    entry.Name = liveName;
                    changed = true;
                }
            }
            else if (IsMeaningfulDeviceName(entry.Name))
            {
                var match = FindByName(connectedNames, entry.Name);
                if (match.HasValue)
                {
                    entry.Id = match.Value;
                    changed = true;
                    log?.Invoke($"Auto-relinked cycle {kind} '{entry.Name}' by name to {match.Value}.");
                }
            }
        }
        return changed;
    }

    private static Guid? FindByName(Dictionary<Guid, string> names, string target)
    {
        foreach (var kv in names)
        {
            if (kv.Value.Equals(target, StringComparison.OrdinalIgnoreCase))
                return kv.Key;
        }
        return null;
    }

    public static void RecordCardSwitch(AppConfig config, AudioCard target, AudioSnapshot? currentSnap)
    {
        var currentActive = config.Cards.FirstOrDefault(c => c.Id == config.ActiveCardId)
            ?? (currentSnap != null ? config.Cards.FirstOrDefault(c => CardIsActive(c, currentSnap)) : null);

        if (currentActive != null && currentActive.Id != target.Id)
        {
            config.PreviousCardId = currentActive.Id;
        }

        config.ActiveCardId = target.Id;
    }

    public static void HandleCardDeleted(AppConfig config, Guid deletedCardId)
    {
        if (config.ActiveCardId == deletedCardId) config.ActiveCardId = null;
        if (config.PreviousCardId == deletedCardId) config.PreviousCardId = null;
        if (config.FallbackCardId == deletedCardId)
        {
            config.FallbackCardId = null;
            if (config.FallbackMode == FallbackMode.SpecificCard)
                config.FallbackMode = FallbackMode.None;
        }
    }

    public static AudioCard? ResolveFallbackCard(
        AppConfig config,
        AudioSnapshot previous,
        AudioSnapshot now,
        IReadOnlySet<Guid> removedIds)
    {
        if (removedIds.Count == 0 || config.FallbackMode == FallbackMode.None)
            return null;

        var activeCard = config.Cards.FirstOrDefault(c => c.Id == config.ActiveCardId)
            ?? config.Cards.FirstOrDefault(c => CardIsActive(c, previous));
        if (activeCard == null)
            return null;

        bool lostDevice =
            (activeCard.OutputId.HasValue && removedIds.Contains(activeCard.OutputId.Value)) ||
            (activeCard.CommOutputId.HasValue && removedIds.Contains(activeCard.CommOutputId.Value)) ||
            (activeCard.InputId.HasValue && removedIds.Contains(activeCard.InputId.Value));
        if (!lostDevice)
            return null;

        Guid? fallbackId = config.FallbackMode switch
        {
            FallbackMode.PreviousCard => config.PreviousCardId,
            FallbackMode.SpecificCard => config.FallbackCardId,
            _ => null
        };

        if (!fallbackId.HasValue || fallbackId.Value == activeCard.Id)
            return null;

        var fallback = config.Cards.FirstOrDefault(c => c.Id == fallbackId.Value);
        if (fallback == null || !CardIsAvailable(fallback, now) || CardIsActive(fallback, now))
            return null;

        return fallback;
    }

    public static AudioCard? FindNextAvailableCard(
        IReadOnlyList<AudioCard> cards,
        AudioSnapshot? snap,
        Guid? activeCardId)
    {
        if (cards.Count == 0) return null;

        int start = -1;
        if (snap != null)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (CardIsActive(cards[i], snap)) { start = i; break; }
            }
        }
        if (start < 0 && activeCardId.HasValue)
        {
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Id == activeCardId.Value) { start = i; break; }
            }
        }

        for (int step = 1; step <= cards.Count; step++)
        {
            var next = cards[(start + step) % cards.Count];
            if (CardIsAvailable(next, snap))
                return next;
        }

        return null;
    }

    public static bool MatchesCycleFilter(Guid deviceId, string deviceName, IReadOnlyCollection<CycleDeviceEntry> filter)
    {
        if (filter.Count == 0) return true;
        foreach (var f in filter)
        {
            if (f.Id == deviceId) return true;
            if (IsMeaningfulDeviceName(f.Name) && deviceName.Equals(f.Name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public static int SelectNextCycleIndex(
        IReadOnlyList<(Guid Id, string Name)> allDevices,
        Guid? currentDefaultId,
        IReadOnlyCollection<CycleDeviceEntry> filter)
    {
        var eligibleIndices = new List<int>(allDevices.Count);
        for (int i = 0; i < allDevices.Count; i++)
        {
            if (MatchesCycleFilter(allDevices[i].Id, allDevices[i].Name, filter))
                eligibleIndices.Add(i);
        }

        if (eligibleIndices.Count < 2) return -1;

        int pos = currentDefaultId.HasValue
            ? eligibleIndices.FindIndex(idx => allDevices[idx].Id == currentDefaultId.Value)
            : -1;

        return eligibleIndices[(pos + 1) % eligibleIndices.Count];
    }

    public static string? FindHotKeyConflict(AppConfig config, HotKeyTarget target, HotKeyBinding candidate)
    {
        static HotKeyBinding GetBinding(AppConfig cfg, HotKeyTarget t) => t switch
        {
            HotKeyTarget.Card => cfg.CardHotKey,
            HotKeyTarget.Output => cfg.OutputHotKey,
            HotKeyTarget.Input => cfg.InputHotKey,
            _ => cfg.MuteHotKey
        };

        static string TargetName(HotKeyTarget t) => t switch
        {
            HotKeyTarget.Card => "Cycle Cards",
            HotKeyTarget.Output => "Cycle Output",
            HotKeyTarget.Input => "Cycle Input",
            _ => "Toggle Mic Mute"
        };

        foreach (var other in Enum.GetValues<HotKeyTarget>())
        {
            if (other != target && GetBinding(config, other).SameAs(candidate))
                return TargetName(other);
        }

        if (config.JumpHotKeysEnabled)
        {
            static bool IsJump(HotKeyBinding b, HotKeyModifiers mods) =>
                b.Modifiers == mods && b.Key >= Keys.D1 && b.Key <= Keys.D9;

            if (target != HotKeyTarget.Card && IsJump(candidate, config.CardHotKey.Modifiers))
                return "Jump to card";

            if (target == HotKeyTarget.Card)
            {
                foreach (var other in Enum.GetValues<HotKeyTarget>())
                {
                    if (other != target && IsJump(GetBinding(config, other), candidate.Modifiers))
                        return TargetName(other);
                }
            }
        }

        return null;
    }

    /// <summary>Extracts a concise human label from Windows "Endpoint (Adapter)" device strings.</summary>
    public static string? ExtractShortDeviceName(string? fullName)
    {
        if (!IsMeaningfulDeviceName(fullName)) return null;
        var s = fullName!.Trim();

        int open = s.IndexOf('(');
        int close = s.LastIndexOf(')');
        if (open > 0 && close > open + 1)
        {
            string prefix = s[..open].Trim();
            string inner = s[(open + 1)..close].Trim();

            // Strip noisy driver tokens like "Realtek(R) Audio" -> keep prefix ("Speakers")
            bool genericAdapter =
                inner.Contains("Realtek", StringComparison.OrdinalIgnoreCase) ||
                inner.Contains("High Definition Audio", StringComparison.OrdinalIgnoreCase) ||
                inner.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
                inner.Equals("USB Audio Device", StringComparison.OrdinalIgnoreCase);

            if (!genericAdapter && inner.Length <= 28)
                return inner;

            if (prefix.Length > 0)
                return prefix;
        }

        return s.Length <= 28 ? s : s[..27].TrimEnd() + "…";
    }

    public static string SuggestCardName(string? outputName, string? inputName, string? commOutputName = null)
    {
        string? shortOut = ExtractShortDeviceName(outputName);
        string? shortIn = ExtractShortDeviceName(inputName);
        string? shortComm = ExtractShortDeviceName(commOutputName);

        if (shortOut != null && shortIn != null)
        {
            if (shortOut.Equals(shortIn, StringComparison.OrdinalIgnoreCase))
                return shortOut;

            string combined = $"{shortOut} + {shortIn}";
            return combined.Length <= 32 ? combined : shortOut;
        }

        return shortOut ?? shortIn ?? shortComm ?? "New Card";
    }

    public static IReadOnlyList<string> SuggestCardNames(string? outputName, string? inputName, string? commOutputName = null)
    {
        var list = new List<string>(5);
        void AddUnique(string? candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return;
            var trimmed = candidate.Trim();
            if (trimmed.Length == 0 || trimmed.Equals("New Card", StringComparison.OrdinalIgnoreCase)) return;
            if (!list.Any(x => x.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
                list.Add(trimmed);
        }

        AddUnique(SuggestCardName(outputName, inputName, commOutputName));
        AddUnique(ExtractShortDeviceName(outputName));
        AddUnique(ExtractShortDeviceName(inputName));

        foreach (var preset in new[] { "Headset", "Speakers", "Desk", "Calls", "Gaming" })
        {
            if (list.Count >= 4) break;
            AddUnique(preset);
        }

        return list;
    }

    public static Point ComputeMuteBannerLocation(
        Rectangle workingArea,
        Size bannerSize,
        MuteBannerPosition position,
        float dpiScale)
    {
        int padX = (int)Math.Round(16 * dpiScale);
        int padY = (int)Math.Round(14 * dpiScale);

        return position switch
        {
            MuteBannerPosition.TopRight => new Point(
                workingArea.Right - bannerSize.Width - padX,
                workingArea.Top + padY),
            MuteBannerPosition.BottomRight => new Point(
                workingArea.Right - bannerSize.Width - padX,
                workingArea.Bottom - bannerSize.Height - padY),
            // TopCenter & ActiveScreenTop both center horizontally near the top of the chosen screen.
            _ => new Point(
                workingArea.Left + (workingArea.Width - bannerSize.Width) / 2,
                workingArea.Top + padY)
        };
    }

    public static Point ClampWindowLocation(Point saved, Size windowSize, IReadOnlyList<Rectangle> workingAreas)
    {
        if (workingAreas.Count == 0) return saved;

        var winRect = new Rectangle(saved, windowSize);
        const int minVisibleW = 80;
        const int minVisibleH = 40;

        foreach (var area in workingAreas)
        {
            var inter = Rectangle.Intersect(winRect, area);
            if (inter.Width >= minVisibleW && inter.Height >= minVisibleH)
            {
                int clampedY = Math.Max(area.Top, saved.Y);
                return new Point(saved.X, clampedY);
            }
        }

        // Saved monitor is disconnected: clamp onto the primary/first screen's working area.
        var fallback = workingAreas[0];
        int x = Math.Clamp(saved.X, fallback.Left, Math.Max(fallback.Left, fallback.Right - windowSize.Width));
        int y = Math.Clamp(saved.Y, fallback.Top, Math.Max(fallback.Top, fallback.Bottom - windowSize.Height));
        return new Point(x, y);
    }
}

/// <summary>
/// Thread-safe single-flight coalescer: ensures at most one refresh pass executes at a time,
/// coalescing concurrent callers into at most one trailing pass with OR-merged flags.
/// </summary>
public sealed class SingleFlightCoalescer
{
    private readonly object _gate = new();
    private readonly Func<bool, bool, Task> _runner;
    private Task? _inFlight;
    private bool _queued;
    private bool _queuedAllowAutoEvents;
    private bool _queuedForce;

    public SingleFlightCoalescer(Func<bool, bool, Task> runner)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task RunAsync(bool allowAutoEvents, bool force = false)
    {
        lock (_gate)
        {
            if (_inFlight != null)
            {
                _queued = true;
                _queuedAllowAutoEvents |= allowAutoEvents;
                _queuedForce |= force;
                return _inFlight;
            }

            _inFlight = ExecuteLoopAsync(allowAutoEvents, force);
            return _inFlight;
        }
    }

    private async Task ExecuteLoopAsync(bool allowAutoEvents, bool force)
    {
        bool curAuto = allowAutoEvents;
        bool curForce = force;

        while (true)
        {
            try
            {
                await _runner(curAuto, curForce);
            }
            catch (Exception)
            {
                lock (_gate)
                {
                    if (!_queued)
                    {
                        _inFlight = null;
                        throw;
                    }
                }
            }

            lock (_gate)
            {
                if (!_queued)
                {
                    _inFlight = null;
                    return;
                }

                curAuto = _queuedAllowAutoEvents;
                curForce = _queuedForce;
                _queued = false;
                _queuedAllowAutoEvents = false;
                _queuedForce = false;
            }
        }
    }
}
