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
        Native.DarkTitleBar(Handle);
        Native.RoundCorners(Handle);
        Native.SetBorderColor(Handle, Theme.BgBorderStrong);
    }

    protected static Label MakeLabel(string text, Point location) =>
        new() { Text = text, Location = location, AutoSize = true, Font = Theme.SmallBold, ForeColor = Theme.TextMuted };

    protected static ModernButton MakeButton(string text, Point location, bool primary, int width = 104) =>
        new(primary ? ModernButtonKind.Primary : ModernButtonKind.Secondary)
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
            BackColor = Theme.BgMain,
            WrapContents = false,
            Margin = Padding.Empty
        };

        // Output device + optional output volume
        _out.Location = new Point(18, 126); _out.Width = 340;
        Fill(_out, outputs, card.OutputId, card.OutputName, "(Don't change)");

        int initialOutVol = card.OutputVolume ?? (SelectedOutput?.CurrentVolume ?? 80);
        _outVolToggle = new ToggleSwitch { Location = new Point(18, 162), Checked = card.OutputVolume.HasValue };
        var outVolCap = new Label
        {
            Text = "Output volume",
            Location = new Point(68, 165),
            AutoSize = true,
            ForeColor = Theme.TextSoft
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
            Font = Theme.SmallBold,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextSoft
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
        _inVolToggle = new ToggleSwitch { Location = new Point(18, 314), Checked = card.InputVolume.HasValue };
        var inVolCap = new Label
        {
            Text = "Input volume",
            Location = new Point(68, 317),
            AutoSize = true,
            ForeColor = Theme.TextSoft
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
            Font = Theme.SmallBold,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextSoft
        };
        _inVolToggle.CheckedChanged += (_, _) =>
        {
            _inVolSlider.Enabled = _inVolToggle.Checked;
            UpdateVolumeLabels();
        };
        _inVolSlider.ValueChanged += (_, _) => UpdateVolumeLabels();

        // Auto-activate section pill
        var autoCard = new SectionCard
        {
            Location = new Point(18, 358),
            Size = new Size(340, 56),
            Padding = Padding.Empty
        };
        _auto = new ToggleSwitch { Location = new Point(14, 17), Checked = card.AutoActivate };
        var autoLbl = new Label
        {
            Text = "Auto-switch to this card when one of its devices connects",
            Location = new Point(66, 10),
            Size = new Size(262, 36),
            BackColor = Theme.BgSurface,
            ForeColor = Theme.TextSoft
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
        _outVolValueLbl.Text = _outVolToggle.Checked ? $"{_outVolSlider.Value}%" : "Keep";
        _outVolValueLbl.ForeColor = _outVolToggle.Checked ? Theme.AccentEnd : Theme.TextMuted;
        _inVolValueLbl.Text = _inVolToggle.Checked ? $"{_inVolSlider.Value}%" : "Keep";
        _inVolValueLbl.ForeColor = _inVolToggle.Checked ? Theme.AccentEnd : Theme.TextMuted;
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
            var measured = TextRenderer.MeasureText(s, Theme.Small);
            var chip = new ModernButton(ModernButtonKind.Chip)
            {
                Text = s,
                Size = new Size(Math.Clamp(measured.Width + 16, 48, 160), 24),
                Margin = new Padding(0, 0, 6, 0)
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
