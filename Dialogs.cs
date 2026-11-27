using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace AudioProfileSwitcher;

internal abstract class DarkDialog : Form
{
    protected DarkDialog(string title, Size clientSize)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = title;
        ClientSize = clientSize;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        BackColor = Theme.BgMain;
        ForeColor = Theme.TextPrimary;
        Font = Theme.Body;
        DoubleBuffered = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.EditorialDialogChrome(Handle);
    }

    protected static Label MakeLabel(string text, Point location) =>
        new() { Text = text.ToUpperInvariant(), Location = location, AutoSize = true, Font = Theme.MonoSmallBold, ForeColor = Theme.Graphite };

    protected static ModernButton MakeButton(string text, Point location, bool primary, int width = 104) =>
        new(primary ? ModernButtonKind.AccentPrimary : ModernButtonKind.Secondary)
        {
            Text = text,
            Size = new Size(width, 32),
            Location = location
        };
}

internal sealed class CardEditorDialog : DarkDialog
{
    private readonly DarkTextBox _name;
    private readonly FlowLayoutPanel _suggestionsPanel;
    private readonly DarkCombo _out = new();
    private readonly DarkCombo _commOut = new();
    private readonly DarkCombo _in = new();
    private readonly ToggleSwitch _outVolToggle;
    private readonly DarkSlider _outVolSlider;
    private readonly Label _outVolValueLbl;
    private readonly ToggleSwitch _inVolToggle;
    private readonly DarkSlider _inVolSlider;
    private readonly Label _inVolValueLbl;
    private readonly ToggleSwitch _auto;

    private bool _userEditedName;
    private bool _suppressNameChange;

    public CardEditorDialog(
        AudioCard card,
        bool isNew,
        IReadOnlyList<DeviceItem> outputs,
        IReadOnlyList<DeviceItem> inputs)
        : base(isNew ? "New Profile Card" : "Edit Profile Card", new Size(376, 514))
    {
        _userEditedName = !isNew && !string.Equals(card.Name, "New Card", StringComparison.OrdinalIgnoreCase);

        _name = new DarkTextBox
        {
            Text = card.Name,
            Location = new Point(18, 34),
            Size = new Size(340, 32)
        };
        _name.TextChanged += (_, _) =>
        {
            if (!_suppressNameChange) _userEditedName = true;
        };

        _suggestionsPanel = new FlowLayoutPanel
        {
            Location = new Point(18, 72),
            Size = new Size(340, 28),
            BackColor = Theme.Paper,
            WrapContents = false,
            Margin = Padding.Empty
        };

        // Output device + optional output volume
        _out.Location = new Point(18, 126); _out.Width = 340;
        Fill(_out, outputs, card.OutputId, card.OutputName, "(Don't change)");

        int initialOutVol = card.OutputVolume ?? (SelectedOutput?.CurrentVolume ?? 80);
        _outVolToggle = new ToggleSwitch { Location = new Point(18, 163), Checked = card.OutputVolume.HasValue };
        var outVolCap = new Label
        {
            Text = "Output volume",
            Location = new Point(64, 165),
            AutoSize = true,
            ForeColor = Theme.Ink
        };
        _outVolSlider = new DarkSlider
        {
            Location = new Point(164, 161),
            Size = new Size(148, 24),
            Value = Math.Clamp(initialOutVol, 0, 100),
            Enabled = card.OutputVolume.HasValue
        };
        _outVolValueLbl = new Label
        {
            Location = new Point(316, 163),
            Size = new Size(42, 20),
            Font = Theme.MonoBold,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Graphite
        };
        _outVolToggle.CheckedChanged += (_, _) =>
        {
            _outVolSlider.Enabled = _outVolToggle.Checked;
            UpdateVolumeLabels();
        };
        _outVolSlider.ValueChanged += (_, _) => UpdateVolumeLabels();

        // Separate voice / communications output
        _commOut.Location = new Point(18, 218); _commOut.Width = 340;
        Fill(_commOut, outputs, card.CommOutputId, card.CommOutputName, "(Same as output)");

        // Input device + optional input volume
        _in.Location = new Point(18, 278); _in.Width = 340;
        Fill(_in, inputs, card.InputId, card.InputName, "(Don't change)");

        int initialInVol = card.InputVolume ?? (SelectedInput?.CurrentVolume ?? 100);
        _inVolToggle = new ToggleSwitch { Location = new Point(18, 315), Checked = card.InputVolume.HasValue };
        var inVolCap = new Label
        {
            Text = "Input volume",
            Location = new Point(64, 317),
            AutoSize = true,
            ForeColor = Theme.Ink
        };
        _inVolSlider = new DarkSlider
        {
            Location = new Point(164, 313),
            Size = new Size(148, 24),
            Value = Math.Clamp(initialInVol, 0, 100),
            Enabled = card.InputVolume.HasValue
        };
        _inVolValueLbl = new Label
        {
            Location = new Point(316, 315),
            Size = new Size(42, 20),
            Font = Theme.MonoBold,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.Graphite
        };
        _inVolToggle.CheckedChanged += (_, _) =>
        {
            _inVolSlider.Enabled = _inVolToggle.Checked;
            UpdateVolumeLabels();
        };
        _inVolSlider.ValueChanged += (_, _) => UpdateVolumeLabels();

        // Auto-activate section sheet
        var autoCard = new SectionCard
        {
            Location = new Point(18, 358),
            Size = new Size(340, 56),
            Padding = Padding.Empty
        };
        _auto = new ToggleSwitch { Location = new Point(14, 18), Checked = card.AutoActivate };
        var autoLbl = new Label
        {
            Text = "Auto-switch to this card when one of its devices connects",
            Location = new Point(62, 10),
            Size = new Size(266, 36),
            BackColor = Theme.Sheet,
            ForeColor = Theme.Ink
        };
        autoCard.Controls.Add(_auto);
        autoCard.Controls.Add(autoLbl);

        _out.SelectedIndexChanged += (_, _) =>
        {
            if (!_outVolToggle.Checked && SelectedOutput?.CurrentVolume is int v) _outVolSlider.Value = v;
            RefreshNameSuggestions(autoUpdateName: !_userEditedName);
        };
        _commOut.SelectedIndexChanged += (_, _) => RefreshNameSuggestions(autoUpdateName: !_userEditedName);
        _in.SelectedIndexChanged += (_, _) =>
        {
            if (!_inVolToggle.Checked && SelectedInput?.CurrentVolume is int v) _inVolSlider.Value = v;
            RefreshNameSuggestions(autoUpdateName: !_userEditedName);
        };

        var save = MakeButton("Save Card", new Point(146, 462), primary: true, width: 106);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", new Point(260, 462), primary: false, width: 98);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[]
        {
            MakeLabel("CARD NAME", new Point(18, 16)), _name, _suggestionsPanel,
            MakeLabel("OUTPUT DEVICE (SPEAKERS / HEADSET)", new Point(18, 108)), _out,
            _outVolToggle, outVolCap, _outVolSlider, _outVolValueLbl,
            MakeLabel("VOICE / COMMUNICATIONS OUTPUT (DISCORD, ZOOM, TEAMS)", new Point(18, 200)), _commOut,
            MakeLabel("INPUT DEVICE (MICROPHONE)", new Point(18, 260)), _in,
            _inVolToggle, inVolCap, _inVolSlider, _inVolValueLbl,
            autoCard, save, cancel
        });

        UpdateVolumeLabels();
        RefreshNameSuggestions(autoUpdateName: isNew && !_userEditedName);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.FocusInput(selectAll: true);
    }

    private void UpdateVolumeLabels()
    {
        _outVolValueLbl.Text = _outVolToggle.Checked ? $"{_outVolSlider.Value}%" : "KEEP";
        _outVolValueLbl.ForeColor = _outVolToggle.Checked ? Theme.Ink : Theme.Graphite;
        _inVolValueLbl.Text = _inVolToggle.Checked ? $"{_inVolSlider.Value}%" : "KEEP";
        _inVolValueLbl.ForeColor = _inVolToggle.Checked ? Theme.Ink : Theme.Graphite;
    }

    private void RefreshNameSuggestions(bool autoUpdateName)
    {
        string? outName = SelectedOutput?.Id.HasValue == true ? SelectedOutput.Name : null;
        string? commName = SelectedCommOutput?.Id.HasValue == true ? SelectedCommOutput.Name : null;
        string? inName = SelectedInput?.Id.HasValue == true ? SelectedInput.Name : null;

        if (autoUpdateName)
        {
            _suppressNameChange = true;
            _name.Text = ProfileLogic.SuggestCardName(outName, inName, commName);
            _suppressNameChange = false;
        }

        _suggestionsPanel.SuspendLayout();
        foreach (var c in _suggestionsPanel.Controls.Cast<Control>().ToList())
        {
            _suggestionsPanel.Controls.Remove(c);
            c.Dispose();
        }

        foreach (var suggestion in ProfileLogic.SuggestCardNames(outName, inName, commName))
        {
            var s = suggestion;
            var measured = TextRenderer.MeasureText(s, Theme.MonoSmall);
            var chip = new ModernButton(ModernButtonKind.Chip)
            {
                Text = s,
                Size = new Size(Math.Clamp(measured.Width + 14, 48, 160), 22),
                Margin = new Padding(0, 2, 6, 0)
            };
            chip.Click += (_, _) =>
            {
                _userEditedName = true;
                _name.Text = s;
                _name.FocusInput();
                _name.SelectionStart = _name.Text.Length;
            };
            _suggestionsPanel.Controls.Add(chip);
        }
        _suggestionsPanel.ResumeLayout(true);
    }

    private static void Fill(
        DarkCombo combo,
        IReadOnlyList<DeviceItem> devices,
        Guid? selectedId,
        string savedName,
        string noneLabel)
    {
        combo.Items.Add(new DeviceItem(null, noneLabel));
        foreach (var d in devices) combo.Items.Add(d);

        int selected = 0;
        if (selectedId.HasValue)
        {
            var items = combo.Items.Cast<DeviceItem>().ToList();
            selected = items.FindIndex(i => i.Id == selectedId);
            if (selected < 0 && ProfileLogic.IsMeaningfulDeviceName(savedName))
            {
                selected = items.FindIndex(i => i.Id.HasValue && i.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase));
            }
            if (selected < 0)
            {
                combo.Items.Add(new DeviceItem(selectedId, savedName, false));
                selected = combo.Items.Count - 1;
            }
        }
        combo.SelectedIndex = selected;
    }

    public string CardName => string.IsNullOrWhiteSpace(_name.Text) ? "Untitled Card" : _name.Text.Trim();
    public DeviceItem? SelectedOutput => _out.SelectedItem as DeviceItem;
    public DeviceItem? SelectedCommOutput => _commOut.SelectedItem as DeviceItem;
    public DeviceItem? SelectedInput => _in.SelectedItem as DeviceItem;
    public int? OutputVolume => _outVolToggle.Checked ? _outVolSlider.Value : null;
    public int? InputVolume => _inVolToggle.Checked ? _inVolSlider.Value : null;
    public bool AutoActivate => _auto.Checked;
}

internal sealed class DeviceFilterDialog : DarkDialog
{
    private readonly DarkCheckedList _outList;
    private readonly DarkCheckedList _inList;

    public DeviceFilterDialog(
        IReadOnlyList<DeviceItem> outputs,
        IReadOnlyList<DeviceItem> inputs,
        IReadOnlyList<CycleDeviceEntry> selectedOut,
        IReadOnlyList<CycleDeviceEntry> selectedIn)
        : base("Cycle Device Filter", new Size(404, 440))
    {
        _outList = new DarkCheckedList { Location = new Point(18, 36), Size = new Size(368, 142) };
        PopulateList(_outList, outputs, selectedOut);

        _inList = new DarkCheckedList { Location = new Point(18, 212), Size = new Size(368, 142) };
        PopulateList(_inList, inputs, selectedIn);

        var clearBtn = MakeButton("Clear All", new Point(18, 378), primary: false, width: 84);
        clearBtn.Click += (_, _) =>
        {
            _outList.SetAllChecked(false);
            _inList.SetAllChecked(false);
        };

        var selectAllBtn = MakeButton("Select All", new Point(108, 378), primary: false, width: 84);
        selectAllBtn.Click += (_, _) =>
        {
            _outList.SetAllChecked(true);
            _inList.SetAllChecked(true);
        };

        var save = MakeButton("Save Filter", new Point(206, 378), primary: true, width: 96);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", new Point(308, 378), primary: false, width: 78);
        cancel.DialogResult = DialogResult.Cancel;

        Controls.AddRange(new Control[]
        {
            MakeLabel("INCLUDE IN CYCLE OUTPUT (NONE CHECKED = ALL)", new Point(18, 16)), _outList,
            MakeLabel("INCLUDE IN CYCLE INPUT (NONE CHECKED = ALL)", new Point(18, 192)), _inList,
            clearBtn, selectAllBtn, save, cancel
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static void PopulateList(
        DarkCheckedList list,
        IReadOnlyList<DeviceItem> connected,
        IReadOnlyList<CycleDeviceEntry> saved)
    {
        var matchedSavedIndices = new HashSet<int>();

        foreach (var d in connected)
        {
            if (!d.Id.HasValue) continue;
            int idx = -1;
            for (int i = 0; i < saved.Count; i++)
            {
                if (saved[i].Id == d.Id.Value ||
                    (ProfileLogic.IsMeaningfulDeviceName(saved[i].Name) &&
                     d.Name.Equals(saved[i].Name, StringComparison.OrdinalIgnoreCase)))
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0) matchedSavedIndices.Add(idx);
            list.AddItem(d, idx >= 0);
        }

        for (int i = 0; i < saved.Count; i++)
        {
            if (matchedSavedIndices.Contains(i)) continue;
            var entry = saved[i];
            string label = ProfileLogic.IsMeaningfulDeviceName(entry.Name)
                ? entry.Name
                : $"Saved device ({entry.Id.ToString()[..8]})";
            list.AddItem(new DeviceItem(entry.Id, label, Connected: false), isChecked: true);
        }
    }

    public List<CycleDeviceEntry> SelectedOutputs => Collect(_outList);
    public List<CycleDeviceEntry> SelectedInputs => Collect(_inList);

    private static List<CycleDeviceEntry> Collect(DarkCheckedList list) =>
        list.GetCheckedItems()
            .Where(d => d.Id.HasValue)
            .Select(d => new CycleDeviceEntry { Id = d.Id!.Value, Name = d.Name })
            .ToList();
}

internal sealed class UpdateDialog : DarkDialog
{
    private readonly ReleaseCheckResult _release;
    private readonly bool _relaunchMinimized;
    private readonly bool _canSelfUpdate;
    private readonly DarkProgressBar _progressBar;
    private readonly Label _statusLbl;
    private readonly ModernButton _primaryBtn;
    private readonly ModernButton _githubBtn;
    private readonly ModernButton _closeBtn;

    private CancellationTokenSource? _cts;
    private bool _downloading;

    public bool UpdateLaunched { get; private set; }

    public UpdateDialog(ReleaseCheckResult release, bool relaunchMinimized = false)
        : base("Software Update", new Size(420, 384))
    {
        _release = release;
        _relaunchMinimized = relaunchMinimized;

        bool envAllowsSelfUpdate = UpdateService.CanSelfUpdate(out string? envReason);
        bool releaseHasVerified = release.HasVerifiedDownload;
        _canSelfUpdate = envAllowsSelfUpdate && releaseHasVerified;

        var emblem = new Panel
        {
            Location = new Point(18, 16),
            Size = new Size(34, 34),
            BackColor = Theme.Paper
        };
        emblem.Paint += (_, e) =>
        {
            Theme.ConfigureGraphics(e.Graphics);
            Gfx.DrawAppEmblem(e.Graphics, new RectangleF(1, 1, 32, 32));
        };

        var titleLbl = new Label
        {
            Text = $"AudioProfileSwitcher {release.LatestVersion} is available",
            Location = new Point(62, 14),
            Size = new Size(340, 22),
            Font = Theme.Header,
            ForeColor = Theme.Ink,
            AutoEllipsis = true
        };

        string sizePart = release.AssetSizeBytes > 0
            ? $" · {ProfileLogic.FormatByteSize(release.AssetSizeBytes)}"
            : "";
        var subLbl = new Label
        {
            Text = $"INSTALLED v{AppInfo.Version} · {AppInfo.BuildFlavorLabel.ToUpperInvariant()}{sizePart}",
            Location = new Point(62, 37),
            Size = new Size(340, 16),
            Font = Theme.MonoSmall,
            ForeColor = Theme.Graphite,
            AutoEllipsis = true
        };

        var notesCard = new SectionCard
        {
            Location = new Point(18, 82),
            Size = new Size(384, 164),
            Padding = new Padding(12, 10, 10, 10)
        };
        var notesBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Theme.Sheet,
            ForeColor = Theme.Ink,
            Font = Theme.Body,
            TabStop = false,
            Text = string.IsNullOrWhiteSpace(release.ReleaseNotes)
                ? "Bug fixes and improvements."
                : release.ReleaseNotes
        };
        notesCard.Controls.Add(notesBox);

        string initialStatus;
        Color initialColor;
        if (_canSelfUpdate)
        {
            initialStatus = "Verified via SHA-256 checksum. Settings and profiles are preserved.";
            initialColor = Theme.Graphite;
        }
        else if (!envAllowsSelfUpdate)
        {
            initialStatus = envReason ?? "Automatic update isn't available for this installation.";
            initialColor = Theme.Vermilion;
        }
        else
        {
            initialStatus = "This release doesn't include a verified single-file asset for automatic install. Download manually on GitHub.";
            initialColor = Theme.Vermilion;
        }

        _statusLbl = new Label
        {
            Text = initialStatus,
            Location = new Point(18, 258),
            Size = new Size(384, 34),
            Font = Theme.Small,
            ForeColor = initialColor
        };

        _progressBar = new DarkProgressBar
        {
            Location = new Point(18, 298),
            Size = new Size(384, 8),
            Visible = false
        };

        _githubBtn = MakeButton("Release Page", new Point(18, 332), primary: false, width: 110);
        _githubBtn.Click += (_, _) => OpenReleasePage();

        _primaryBtn = MakeButton(
            _canSelfUpdate ? "Update & Restart" : "Download on GitHub",
            new Point(150, 332),
            primary: true,
            width: 150);
        _primaryBtn.Click += (_, _) =>
        {
            if (_canSelfUpdate) _ = StartDownloadAndInstallAsync();
            else OpenReleasePage();
        };

        _closeBtn = MakeButton("Later", new Point(308, 332), primary: false, width: 94);
        _closeBtn.Click += (_, _) =>
        {
            if (_downloading)
            {
                _cts?.Cancel();
            }
            else
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        Controls.AddRange(new Control[]
        {
            emblem, titleLbl, subLbl,
            MakeLabel("WHAT'S NEW", new Point(18, 64)),
            notesCard,
            _statusLbl,
            _progressBar,
            _githubBtn, _primaryBtn, _closeBtn
        });
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            if (_downloading) _cts?.Cancel();
            else
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_downloading)
        {
            _cts?.Cancel();
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
        base.Dispose(disposing);
    }

    private void OpenReleasePage()
    {
        try
        {
            string safeUrl = ProfileLogic.SanitizeReleaseUrl(_release.ReleaseUrl);
            Process.Start(new ProcessStartInfo { FileName = safeUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Error("Failed to open GitHub release page", ex);
            _statusLbl.Text = "Couldn't open browser: " + ex.Message;
            _statusLbl.ForeColor = Theme.Danger;
        }
    }

    private async Task StartDownloadAndInstallAsync()
    {
        if (_downloading) return;
        _downloading = true;
        _primaryBtn.Enabled = false;
        _primaryBtn.Text = "Updating…";
        _closeBtn.Text = "Cancel";
        _progressBar.Visible = true;
        _progressBar.Indeterminate = true;
        _progressBar.Value = 0;
        _statusLbl.ForeColor = Theme.TextSoft;
        _statusLbl.Text = "Preparing verified download…";

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            if (IsDisposed) return;
            _progressBar.Indeterminate = p.Indeterminate;
            if (!p.Indeterminate) _progressBar.Value = p.Percent;

            if (p.TotalBytes > 0 && !p.Indeterminate)
            {
                _statusLbl.Text =
                    $"{p.Stage}  {ProfileLogic.FormatByteSize(p.BytesReceived)} / {ProfileLogic.FormatByteSize(p.TotalBytes)} ({p.Percent}%)";
            }
            else
            {
                _statusLbl.Text = p.Stage;
            }
        });

        try
        {
            var staged = await UpdateService.DownloadAndVerifyUpdateAsync(_release, progress, token);
            if (IsDisposed) return;

            _progressBar.Indeterminate = false;
            _progressBar.Value = 100;
            _statusLbl.ForeColor = Theme.Success;
            _statusLbl.Text = "SHA-256 verified! Restarting AudioSwitcher…";
            _closeBtn.Enabled = false;

            await Task.Delay(250, CancellationToken.None);
            if (IsDisposed) return;

            UpdateService.LaunchStagedUpdater(staged, _relaunchMinimized);
            UpdateLaunched = true;
            _downloading = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            if (IsDisposed) return;
            _downloading = false;
            _progressBar.Indeterminate = false;
            _progressBar.Visible = false;
            _statusLbl.ForeColor = Theme.TextMuted;
            _statusLbl.Text = "Download cancelled.";
            _primaryBtn.Enabled = true;
            _primaryBtn.Text = "Update & Restart";
            _closeBtn.Text = "Later";
        }
        catch (Exception ex)
        {
            AppLog.Error("Self-update failed", ex);
            if (IsDisposed) return;
            _downloading = false;
            _progressBar.Indeterminate = false;
            _progressBar.Visible = false;
            _statusLbl.ForeColor = Theme.Danger;
            _statusLbl.Text = "Update failed: " + ex.Message;
            _primaryBtn.Enabled = true;
            _primaryBtn.Text = "Retry Update";
            _closeBtn.Text = "Later";
        }
    }
}
