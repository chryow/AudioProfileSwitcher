using System.Drawing;
using System.Text;
using Xunit;

namespace AudioProfileSwitcher.Tests;

public sealed class AudioProfileSwitcherTests
{
    private static AudioSnapshot MakeSnapshot(
        (Guid Id, string Name)[]? outputs = null,
        (Guid Id, string Name)[]? inputs = null,
        Guid? defaultOut = null,
        Guid? defaultIn = null,
        bool micMuted = false,
        Guid? defaultCommOut = null)
    {
        var outs = outputs ?? Array.Empty<(Guid, string)>();
        var ins = inputs ?? Array.Empty<(Guid, string)>();
        var outMap = outs.ToDictionary(x => x.Id, x => x.Name);
        var inMap = ins.ToDictionary(x => x.Id, x => x.Name);

        return new AudioSnapshot(
            outMap.Keys.ToHashSet(),
            inMap.Keys.ToHashSet(),
            outMap,
            inMap,
            defaultOut,
            defaultIn,
            defaultOut.HasValue && outMap.TryGetValue(defaultOut.Value, out var oName) ? oName : null,
            defaultIn.HasValue && inMap.TryGetValue(defaultIn.Value, out var iName) ? iName : null,
            micMuted,
            defaultCommOut,
            defaultCommOut.HasValue && outMap.TryGetValue(defaultCommOut.Value, out var coName) ? coName : null);
    }

    [Fact]
    public void ParseEndpointGuid_ExtractsWindowsCoreAudioEndpointGuid()
    {
        var expected = Guid.Parse("a1b2c3d4-e5f6-4789-abcd-0123456789ab");
        string winEndpointId = $"{{0.0.0.00000000}}.{{{expected}}}";

        Assert.Equal(expected, ProfileLogic.ParseEndpointGuid(winEndpointId));
        Assert.Equal(expected, ProfileLogic.ParseEndpointGuid(expected.ToString()));

        // Non-standard driver string hashes deterministically:
        var fallback1 = ProfileLogic.ParseEndpointGuid("custom-driver-endpoint-1");
        var fallback2 = ProfileLogic.ParseEndpointGuid("CUSTOM-DRIVER-ENDPOINT-1");
        Assert.NotEqual(Guid.Empty, fallback1);
        Assert.Equal(fallback1, fallback2);
    }

    [Fact]
    public void AudioSnapshot_EqualsState_DetectsMatchingAndChangedStates()
    {
        var outA = Guid.NewGuid();
        var outB = Guid.NewGuid();
        var inA = Guid.NewGuid();

        var snap1 = MakeSnapshot(
            new[] { (outA, "Speakers"), (outB, "Headphones") },
            new[] { (inA, "Yeti Mic") },
            defaultOut: outA,
            defaultIn: inA,
            micMuted: false,
            defaultCommOut: outB);

        var snap2 = MakeSnapshot(
            new[] { (outB, "Headphones"), (outA, "Speakers") },
            new[] { (inA, "Yeti Mic") },
            defaultOut: outA,
            defaultIn: inA,
            micMuted: false,
            defaultCommOut: outB);

        Assert.True(snap1.EqualsState(snap2));
        Assert.False(snap1.EqualsState(null));

        var mutedChanged = snap2 with { MicMuted = true };
        Assert.False(snap1.EqualsState(mutedChanged));

        var commChanged = snap2 with { DefaultCommOut = outA, CommOutName = "Speakers" };
        Assert.False(snap1.EqualsState(commChanged));

        var renamed = MakeSnapshot(
            new[] { (outA, "Speakers"), (outB, "Headphones Renamed") },
            new[] { (inA, "Yeti Mic") },
            defaultOut: outA,
            defaultIn: inA,
            micMuted: false,
            defaultCommOut: outB);
        Assert.False(snap1.EqualsState(renamed));
    }

    [Theory]
    [InlineData(ChimeCue.Switch)]
    [InlineData(ChimeCue.Muted)]
    [InlineData(ChimeCue.Unmuted)]
    public void ChimeSynth_BuildWav_ProducesValidHeaderAndContinuousWaveform(ChimeCue cue)
    {
        var wav = ChimeSynth.BuildWav(cue);
        Assert.True(wav.Length > 44);
        Assert.Equal("RIFF", Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal("WAVE", Encoding.ASCII.GetString(wav, 8, 4));
        Assert.Equal("fmt ", Encoding.ASCII.GetString(wav, 12, 4));
        Assert.Equal("data", Encoding.ASCII.GetString(wav, 36, 4));

        int dataSize = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataSize);

        var (firstHz, secondHz) = ChimeSynth.GetFrequencies(cue);
        if (cue == ChimeCue.Muted)
            Assert.True(firstHz > secondHz);
        else
            Assert.True(firstHz < secondHz);

        var samples = ChimeSynth.SynthesizeSamples(firstHz, secondHz);
        Assert.Equal(0, samples[0]);
        Assert.Equal(0, samples[^1]);

        int split = samples.Length / 2;
        int transitionDelta = Math.Abs(samples[split] - samples[split - 1]);
        Assert.True(transitionDelta < 1100, $"Expected smooth phase transition, got delta={transitionDelta}");
    }

    [Fact]
    public void MigrateConfig_UpgradesLegacyFallbackAndCycleIdLists()
    {
        var cardId = Guid.NewGuid();
        var outId = Guid.NewGuid();
        var inId = Guid.NewGuid();
        var orphanOutId = Guid.NewGuid();

        var legacyJson = $$"""
        {
          "Cards": [
            {
              "Id": "{{cardId}}",
              "Name": "Desk",
              "OutputId": "{{outId}}",
              "OutputName": "Speakers (DAC)",
              "InputId": "{{inId}}",
              "InputName": "Microphone (Boom)"
            }
          ],
          "FallbackCardId": "{{cardId}}",
          "CycleOutputIds": [ "{{outId}}", "{{orphanOutId}}" ],
          "CycleInputIds": [ "{{inId}}" ]
        }
        """;

        var tempDir = Path.Combine(Path.GetTempPath(), "aps-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "config.json"), legacyJson);

            var store = new ConfigStore(tempDir);
            var cfg = store.Load();

            Assert.Equal(FallbackMode.SpecificCard, cfg.FallbackMode);
            Assert.Equal(cardId, cfg.FallbackCardId);

            Assert.Null(cfg.CycleOutputIds);
            Assert.Null(cfg.CycleInputIds);
            Assert.Equal(2, cfg.CycleOutputs.Count);
            Assert.Equal("Speakers (DAC)", cfg.CycleOutputs[0].Name);
            Assert.Equal("", cfg.CycleOutputs[1].Name);
            Assert.Single(cfg.CycleInputs);
            Assert.Equal("Microphone (Boom)", cfg.CycleInputs[0].Name);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ConfigStore_CorruptJson_BacksUpAndSetsWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "aps-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "config.json"), "{ invalid json");

            var store = new ConfigStore(tempDir);
            var cfg = store.Load();

            Assert.NotNull(cfg);
            Assert.NotNull(store.LoadWarning);
            Assert.Contains(Directory.GetFiles(tempDir), f => f.Contains(".corrupt-", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RelinkByName_RelinksCardAndCycleFilterGuidsAndBackfillsNames()
    {
        var oldOut = Guid.NewGuid();
        var newOut = Guid.NewGuid();
        var oldComm = Guid.NewGuid();
        var newComm = Guid.NewGuid();
        var oldIn = Guid.NewGuid();
        var newIn = Guid.NewGuid();
        var existingCycleOut = Guid.NewGuid();

        var cfg = new AppConfig
        {
            Cards =
            {
                new AudioCard
                {
                    Name = "Wireless Headset",
                    OutputId = oldOut,
                    OutputName = "Headphones (Arctis)",
                    CommOutputId = oldComm,
                    CommOutputName = "Earphone (Arctis Chat)",
                    InputId = oldIn,
                    InputName = "Microphone (Arctis)"
                }
            },
            CycleOutputs =
            {
                new CycleDeviceEntry { Id = oldOut, Name = "Headphones (Arctis)" },
                new CycleDeviceEntry { Id = existingCycleOut, Name = "" }
            },
            CycleInputs =
            {
                new CycleDeviceEntry { Id = oldIn, Name = "Microphone (Arctis)" }
            }
        };

        var snap = MakeSnapshot(
            new[]
            {
                (newOut, "Headphones (Arctis)"),
                (newComm, "Earphone (Arctis Chat)"),
                (existingCycleOut, "Speakers (Desk)")
            },
            new[] { (newIn, "Microphone (Arctis)") },
            defaultOut: newOut,
            defaultIn: newIn,
            defaultCommOut: newComm);

        bool changed = ProfileLogic.RelinkByName(cfg, snap);
        Assert.True(changed);
        Assert.Equal(newOut, cfg.Cards[0].OutputId);
        Assert.Equal(newComm, cfg.Cards[0].CommOutputId);
        Assert.Equal(newIn, cfg.Cards[0].InputId);
        Assert.Equal(newOut, cfg.CycleOutputs[0].Id);
        Assert.Equal("Speakers (Desk)", cfg.CycleOutputs[1].Name);
        Assert.Equal(newIn, cfg.CycleInputs[0].Id);
        Assert.True(ProfileLogic.CardIsActive(cfg.Cards[0], snap));
    }

    [Fact]
    public void FallbackResolution_SupportsPreviousCardAndSpecificCardModes()
    {
        var deskOut = Guid.NewGuid();
        var headsetOut = Guid.NewGuid();
        var backupOut = Guid.NewGuid();
        var mic = Guid.NewGuid();

        var desk = new AudioCard { Name = "Desk", OutputId = deskOut, OutputName = "Speakers", InputId = mic, InputName = "Mic" };
        var headset = new AudioCard { Name = "Headset", OutputId = headsetOut, OutputName = "Headset", InputId = mic, InputName = "Mic" };
        var backup = new AudioCard { Name = "Backup", OutputId = backupOut, OutputName = "Monitor", InputId = mic, InputName = "Mic" };

        var cfg = new AppConfig
        {
            Cards = { desk, headset, backup },
            FallbackMode = FallbackMode.PreviousCard
        };

        var beforeSwitch = MakeSnapshot(
            new[] { (deskOut, "Speakers"), (headsetOut, "Headset"), (backupOut, "Monitor") },
            new[] { (mic, "Mic") },
            defaultOut: deskOut,
            defaultIn: mic);

        ProfileLogic.RecordCardSwitch(cfg, desk, beforeSwitch);
        ProfileLogic.RecordCardSwitch(cfg, headset, beforeSwitch);
        Assert.Equal(desk.Id, cfg.PreviousCardId);
        Assert.Equal(headset.Id, cfg.ActiveCardId);

        var afterUnplug = MakeSnapshot(
            new[] { (deskOut, "Speakers"), (backupOut, "Monitor") },
            new[] { (mic, "Mic") },
            defaultOut: backupOut,
            defaultIn: mic);

        var removed = new HashSet<Guid> { headsetOut };
        var resolvedPrev = ProfileLogic.ResolveFallbackCard(cfg, beforeSwitch, afterUnplug, removed);
        Assert.Same(desk, resolvedPrev);

        cfg.FallbackMode = FallbackMode.SpecificCard;
        cfg.FallbackCardId = backup.Id;
        Assert.Null(ProfileLogic.ResolveFallbackCard(cfg, beforeSwitch, afterUnplug, removed));

        var afterUnplugDeskDefault = afterUnplug with { DefaultOut = deskOut, OutName = "Speakers" };
        var resolvedSpec = ProfileLogic.ResolveFallbackCard(cfg, beforeSwitch, afterUnplugDeskDefault, removed);
        Assert.Same(backup, resolvedSpec);

        cfg.FallbackMode = FallbackMode.None;
        Assert.Null(ProfileLogic.ResolveFallbackCard(cfg, beforeSwitch, afterUnplugDeskDefault, removed));
    }

    [Fact]
    public void SelectNextCycleIndex_RespectsGuidAndNameFilter()
    {
        var d1 = (Id: Guid.NewGuid(), Name: "Monitor Audio");
        var d2 = (Id: Guid.NewGuid(), Name: "Speakers (DAC)");
        var d3 = (Id: Guid.NewGuid(), Name: "Headphones (USB)");
        var all = new[] { d1, d2, d3 };

        var filter = new[]
        {
            new CycleDeviceEntry { Id = d2.Id, Name = d2.Name },
            new CycleDeviceEntry { Id = Guid.NewGuid(), Name = "headphones (usb)" }
        };

        int nextFromD2 = ProfileLogic.SelectNextCycleIndex(all, d2.Id, filter);
        Assert.Equal(2, nextFromD2);

        int nextFromD3 = ProfileLogic.SelectNextCycleIndex(all, d3.Id, filter);
        Assert.Equal(1, nextFromD3);
    }

    [Fact]
    public void SuggestCardName_ExtractsConciseHardwareNames()
    {
        Assert.Equal("HyperX Cloud II", ProfileLogic.SuggestCardName(
            "Headset Earphone (HyperX Cloud II)",
            "Headset Microphone (HyperX Cloud II)"));

        Assert.Equal("Speakers + Blue Yeti", ProfileLogic.SuggestCardName(
            "Speakers (Realtek(R) Audio)",
            "Microphone (Blue Yeti)"));

        var suggestions = ProfileLogic.SuggestCardNames("Speakers (Realtek(R) Audio)", "Microphone (Blue Yeti)");
        Assert.NotEmpty(suggestions);
        Assert.Contains("Speakers + Blue Yeti", suggestions);
    }

    [Fact]
    public void LayoutHelpers_ComputeBannerPositionsCustomDragAndClampBounds()
    {
        var area = new Rectangle(0, 0, 1920, 1040);
        var banner = new Size(128, 30);

        Assert.Equal(new Point(896, 14), ProfileLogic.ComputeMuteBannerLocation(area, banner, MuteBannerPosition.TopCenter, 1f));
        Assert.Equal(new Point(1776, 14), ProfileLogic.ComputeMuteBannerLocation(area, banner, MuteBannerPosition.TopRight, 1f));
        Assert.Equal(new Point(1776, 996), ProfileLogic.ComputeMuteBannerLocation(area, banner, MuteBannerPosition.BottomRight, 1f));

        // Custom dragged coordinates clamp inside the screen working area:
        Assert.Equal(new Point(320, 48), ProfileLogic.ComputeMuteBannerLocation(area, banner, MuteBannerPosition.Custom, 1f, 320, 48));
        Assert.Equal(new Point(1920 - 128, 1040 - 30), ProfileLogic.ComputeMuteBannerLocation(area, banner, MuteBannerPosition.Custom, 1f, 5000, 3000));

        Assert.Equal(420, ProfileLogic.ClampWindowHeight(200));
        Assert.Equal(740, ProfileLogic.ClampWindowHeight(740));
        Assert.Equal(620, ProfileLogic.ClampWindowHeight(null));

        var clamped = ProfileLogic.ClampWindowLocation(new Point(2400, 120), new Size(380, 620), new[] { area });
        Assert.InRange(clamped.X, 0, 1920 - 380);
        Assert.Equal(120, clamped.Y);
    }

    [Fact]
    public async Task SingleFlightCoalescer_SerializesAndMergesQueuedFlags()
    {
        int active = 0;
        int maxConcurrent = 0;
        var runs = new List<(bool Auto, bool Force)>();
        var firstEntered = new TaskCompletionSource();
        var releaseFirst = new TaskCompletionSource();

        var coalescer = new SingleFlightCoalescer(async (auto, force) =>
        {
            int now = Interlocked.Increment(ref active);
            int prevMax;
            do
            {
                prevMax = maxConcurrent;
                if (now <= prevMax) break;
            } while (Interlocked.CompareExchange(ref maxConcurrent, now, prevMax) != prevMax);

            runs.Add((auto, force));
            if (runs.Count == 1)
            {
                firstEntered.TrySetResult();
                await releaseFirst.Task;
            }
            Interlocked.Decrement(ref active);
        });

        var t1 = coalescer.RunAsync(allowAutoEvents: false, force: false);
        await firstEntered.Task;

        var t2 = coalescer.RunAsync(allowAutoEvents: true, force: false);
        var t3 = coalescer.RunAsync(allowAutoEvents: false, force: true);
        var t4 = coalescer.RunAsync(allowAutoEvents: false, force: false);

        releaseFirst.SetResult();
        await Task.WhenAll(t1, t2, t3, t4);

        Assert.Equal(1, maxConcurrent);
        Assert.Equal(2, runs.Count);
        Assert.Equal((false, false), runs[0]);
        Assert.Equal((true, true), runs[1]);
    }

    [Fact]
    public void FindHotKeyConflict_SupportsToggleWindowAndDisabledBindings()
    {
        var cfg = new AppConfig { JumpHotKeysEnabled = true };

        Assert.Equal("Show / Hide Window", ProfileLogic.FindHotKeyConflict(
            cfg,
            HotKeyTarget.Mute,
            new HotKeyBinding { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.Space }));

        Assert.Equal("Jump to card", ProfileLogic.FindHotKeyConflict(
            cfg,
            HotKeyTarget.ToggleWindow,
            new HotKeyBinding { Modifiers = HotKeyModifiers.Control | HotKeyModifiers.Shift, Key = Keys.D3 }));

        var disabled = new HotKeyBinding { Modifiers = HotKeyModifiers.None, Key = Keys.None };
        Assert.False(disabled.IsEnabled);
        Assert.Equal("Disabled", disabled.Format());
        Assert.Null(ProfileLogic.FindHotKeyConflict(cfg, HotKeyTarget.ToggleWindow, disabled));
    }

    [Fact]
    public void ExportAndImportProfiles_RoundTripsAndRelinksAcrossMachines()
    {
        var oldOut = Guid.NewGuid();
        var oldIn = Guid.NewGuid();
        var newMachineOut = Guid.NewGuid();
        var newMachineIn = Guid.NewGuid();

        var source = new AppConfig
        {
            FallbackMode = FallbackMode.PreviousCard,
            Cards =
            {
                new AudioCard
                {
                    Name = "Desk Setup",
                    OutputId = oldOut,
                    OutputName = "Speakers (USB DAC)",
                    InputId = oldIn,
                    InputName = "Microphone (Wave:3)",
                    OutputVolume = 65,
                    InputVolume = 95
                }
            },
            CycleOutputs =
            {
                new CycleDeviceEntry { Id = oldOut, Name = "Speakers (USB DAC)" }
            }
        };

        string json = ProfileLogic.ExportProfilesJson(source);

        var dest = new AppConfig();
        var newMachineSnap = MakeSnapshot(
            new[] { (newMachineOut, "Speakers (USB DAC)") },
            new[] { (newMachineIn, "Microphone (Wave:3)") },
            defaultOut: newMachineOut,
            defaultIn: newMachineIn);

        var (addedCards, addedFilters) = ProfileLogic.ImportProfilesJson(json, dest, newMachineSnap);
        Assert.Equal(1, addedCards);
        Assert.Equal(1, addedFilters);
        Assert.Equal(FallbackMode.PreviousCard, dest.FallbackMode);
        Assert.Equal(newMachineOut, dest.Cards[0].OutputId);
        Assert.Equal(newMachineIn, dest.Cards[0].InputId);
        Assert.Equal(65, dest.Cards[0].OutputVolume);
        Assert.Equal(newMachineOut, dest.CycleOutputs[0].Id);
    }

    [Fact]
    public void ParseLatestRelease_ComparesSemanticVersionsCleanly()
    {
        const string newerJson = """
        {
          "tag_name": "v1.4.0",
          "html_url": "https://github.com/chryow/AudioProfileSwitcher/releases/tag/v1.4.0"
        }
        """;

        var newer = ProfileLogic.ParseLatestRelease(newerJson, "1.3.0");
        Assert.True(newer.UpdateAvailable);
        Assert.Equal("v1.4.0", newer.LatestVersion);
        Assert.Equal("https://github.com/chryow/AudioProfileSwitcher/releases/tag/v1.4.0", newer.ReleaseUrl);

        var same = ProfileLogic.ParseLatestRelease(newerJson, "1.4.0");
        Assert.False(same.UpdateAvailable);
    }

    [Theory]
    [InlineData("file:///C:/Windows/System32/notepad.exe")]
    [InlineData("http://github.com/chryow/AudioProfileSwitcher/releases/tag/v2.0.0")]
    [InlineData("https://evil.example/chryow/AudioProfileSwitcher/releases/tag/v2.0.0")]
    [InlineData("https://github.com/other/repo/releases/tag/v2.0.0")]
    [InlineData("https://user@github.com/chryow/AudioProfileSwitcher/releases/tag/v2.0.0")]
    [InlineData("https://github.com:8443/chryow/AudioProfileSwitcher/releases/tag/v2.0.0")]
    [InlineData("ms-msdt:/id")]
    public void SanitizeReleaseUrl_RejectsUnsafeSchemesHostsAndPaths(string unsafeUrl)
    {
        Assert.Equal(AppInfo.ReleasesPageUrl, ProfileLogic.SanitizeReleaseUrl(unsafeUrl));

        string json = $$"""{"tag_name":"v9.0.0","html_url":"{{unsafeUrl}}"}""";
        var parsed = ProfileLogic.ParseLatestRelease(json, "1.4.1");
        Assert.Equal(AppInfo.ReleasesPageUrl, parsed.ReleaseUrl);
    }

    [Fact]
    public void ImportProfilesJson_EnforcesLimitsAndSanitizesNames()
    {
        // 1. Oversized payload rejected before deserialization:
        string huge = new string('a', ProfileLogic.MaxProfileJsonBytes + 16);
        Assert.Throws<InvalidDataException>(() => ProfileLogic.ImportProfilesJson(huge, new AppConfig()));

        // 2. Excess card count rejected:
        var tooMany = new AppConfig();
        for (int i = 0; i < ProfileLogic.MaxImportedCards + 1; i++)
            tooMany.Cards.Add(new AudioCard { Name = $"Card {i}" });
        string tooManyJson = ProfileLogic.ExportProfilesJson(tooMany);
        Assert.Throws<InvalidDataException>(() => ProfileLogic.ImportProfilesJson(tooManyJson, new AppConfig()));

        // 3. Control characters stripped and long names truncated:
        var dirty = new AppConfig
        {
            Cards =
            {
                new AudioCard
                {
                    Name = "Desk\r\n\0" + new string('X', 200),
                    OutputName = "Speakers\t(DAC)\n"
                }
            }
        };
        var dest = new AppConfig();
        ProfileLogic.ImportProfilesJson(ProfileLogic.ExportProfilesJson(dirty), dest);
        Assert.Single(dest.Cards);
        Assert.Equal(ProfileLogic.MaxNameLength, dest.Cards[0].Name.Length);
        Assert.DoesNotContain("\r", dest.Cards[0].Name);
        Assert.DoesNotContain("\n", dest.Cards[0].Name);
        Assert.Equal("Speakers(DAC)", dest.Cards[0].OutputName);
    }
}
