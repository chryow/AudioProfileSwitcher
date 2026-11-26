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
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.DarkTitleBar(Handle);
    }

    protected static Label MakeLabel(string text, Point location) =>
        new() { Text = text, Location = location, AutoSize = true, ForeColor = Theme.TextMuted };

    protected static Button MakeButton(string text, Point location, bool primary, int width = 100)
    {
        var b = new Button
        {
            Text = text,
            Font = Theme.BodyBold,
            ForeColor = Theme.TextPrimary,
            BackColor = primary ? Theme.Accent : Theme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(width, 30),
            Location = location,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderColor = Theme.BgBorder;
        b.FlatAppearance.BorderSize = primary ? 0 : 1;
        return b;
    }
}

internal sealed class CardEditorDialog : DarkDialog
{
    private readonly TextBox _name;
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
        : base(isNew ? "New Card" : "Edit Card", new Size(360, 492))
    {
        _userEditedName = !isNew && !string.Equals(card.Name, "New Card", StringComparison.OrdinalIgnoreCase);

        _name = new TextBox
        {
            Text = card.Name,
            Location = new Point(16, 32),
            Width = 328,
            BackColor = Theme.BgCard,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        _name.TextChanged += (_, _) =>
        {
            if (!_suppressNameChange) _userEditedName = true;
        };

        _suggestionsPanel = new FlowLayoutPanel
        {
            Location = new Point(16, 58),
            Size = new Size(328, 24),
            BackColor = Theme.BgMain,
            WrapContents = false,
            Margin = Padding.Empty
        };

        // Output device + optional output volume
        _out.Location = new Point(16, 108); _out.Width = 328;
        Fill(_out, outputs, card.OutputId, card.OutputName, "(Don't change)");

        int initialOutVol = card.OutputVolume ?? (SelectedOutput?.CurrentVolume ?? 80);
        _outVolToggle = new ToggleSwitch { Location = new Point(16, 142), Checked = card.OutputVolume.HasValue };
        var outVolCap = new Label
        {
            Text = "Output volume",
            Location = new Point(62, 144),
            AutoSize = true,
            ForeColor = Theme.TextMuted
        };
        _outVolSlider = new DarkSlider
        {
            Location = new Point(156, 141),
            Size = new Size(144, 22),
            Value = Math.Clamp(initialOutVol, 0, 100),
            Enabled = card.OutputVolume.HasValue
        };
        _outVolValueLbl = new Label
        {
            Location = new Point(304, 143),
            Size = new Size(40, 20),
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
        _commOut.Location = new Point(16, 194); _commOut.Width = 328;
        Fill(_commOut, outputs, card.CommOutputId, card.CommOutputName, "(Same as output)");

        // Input device + optional input volume
        _in.Location = new Point(16, 252); _in.Width = 328;
        Fill(_in, inputs, card.InputId, card.InputName, "(Don't change)");

        int initialInVol = card.InputVolume ?? (SelectedInput?.CurrentVolume ?? 100);
        _inVolToggle = new ToggleSwitch { Location = new Point(16, 286), Checked = card.InputVolume.HasValue };
        var inVolCap = new Label
        {
            Text = "Input volume",
            Location = new Point(62, 288),
            AutoSize = true,
            ForeColor = Theme.TextMuted
        };
        _inVolSlider = new DarkSlider
        {
            Location = new Point(156, 285),
            Size = new Size(144, 22),
            Value = Math.Clamp(initialInVol, 0, 100),
            Enabled = card.InputVolume.HasValue
        };
        _inVolValueLbl = new Label
        {
            Location = new Point(304, 287),
            Size = new Size(40, 20),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Theme.TextSoft
        };
        _inVolToggle.CheckedChanged += (_, _) =>
        {
            _inVolSlider.Enabled = _inVolToggle.Checked;
            UpdateVolumeLabels();
        };
        _inVolSlider.ValueChanged += (_, _) => UpdateVolumeLabels();

        // Auto-activate on connect
        _auto = new ToggleSwitch { Location = new Point(16, 332), Checked = card.AutoActivate };
        var autoLbl = new Label
        {
            Text = "Switch to this card automatically when one of its devices connects",
            Location = new Point(64, 326),
            Size = new Size(280, 36),
            ForeColor = Theme.TextMuted
        };

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

        var save = MakeButton("Save Card", new Point(136, 444), true);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", new Point(244, 444), false);
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            MakeLabel("Card name", new Point(16, 12)), _name, _suggestionsPanel,
            MakeLabel("Output device (speakers / headset)", new Point(16, 88)), _out,
            _outVolToggle, outVolCap, _outVolSlider, _outVolValueLbl,
            MakeLabel("Voice / communications output (Discord, Zoom, Teams)", new Point(16, 174)), _commOut,
            MakeLabel("Input device (microphone)", new Point(16, 232)), _in,
            _inVolToggle, inVolCap, _inVolSlider, _inVolValueLbl,
            _auto, autoLbl, save, cancel
        });

        UpdateVolumeLabels();
        RefreshNameSuggestions(autoUpdateName: isNew && !_userEditedName);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _name.Focus();
        _name.SelectAll();
    }

    private void UpdateVolumeLabels()
    {
        _outVolValueLbl.Text = _outVolToggle.Checked ? $"{_outVolSlider.Value}%" : "Keep";
        _outVolValueLbl.ForeColor = _outVolToggle.Checked ? Theme.TextPrimary : Theme.TextMuted;
        _inVolValueLbl.Text = _inVolToggle.Checked ? $"{_inVolSlider.Value}%" : "Keep";
        _inVolValueLbl.ForeColor = _inVolToggle.Checked ? Theme.TextPrimary : Theme.TextMuted;
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
            var chip = new Button
            {
                Text = s,
                Font = Theme.Small,
                ForeColor = Theme.TextSoft,
                BackColor = Theme.BgCard,
                FlatStyle = FlatStyle.Flat,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, 0, 6, 0),
                Padding = new Padding(4, 0, 4, 0),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            chip.FlatAppearance.BorderColor = Theme.BgBorder;
            chip.FlatAppearance.BorderSize = 1;
            chip.Click += (_, _) =>
            {
                _userEditedName = true;
                _name.Text = s;
                _name.Focus();
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
                // Keep a card's saved device even while it's unplugged, so editing never loses it.
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
    private readonly CheckedListBox _outList;
    private readonly CheckedListBox _inList;

    public DeviceFilterDialog(
        IReadOnlyList<DeviceItem> outputs,
        IReadOnlyList<DeviceItem> inputs,
        IReadOnlyList<CycleDeviceEntry> selectedOut,
        IReadOnlyList<CycleDeviceEntry> selectedIn)
        : base("Cycle Device Filter", new Size(376, 412))
    {
        _outList = MakeList(new Point(16, 34), 132);
        PopulateList(_outList, outputs, selectedOut);

        _inList = MakeList(new Point(16, 198), 132);
        PopulateList(_inList, inputs, selectedIn);

        var clearBtn = MakeButton("Clear All", new Point(16, 352), false, width: 92);
        clearBtn.Click += (_, _) =>
        {
            UncheckAll(_outList);
            UncheckAll(_inList);
        };

        var save = MakeButton("Save Filter", new Point(152, 352), true);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", new Point(260, 352), false);
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            MakeLabel("Include in Cycle Output (none checked = all):", new Point(16, 12)), _outList,
            MakeLabel("Include in Cycle Input (none checked = all):", new Point(16, 176)), _inList,
            clearBtn, save, cancel
        });
    }

    private static void PopulateList(
        CheckedListBox list,
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
            list.Items.Add(d, idx >= 0);
        }

        // Show saved offline devices directly in the list so the user can see or uncheck them.
        for (int i = 0; i < saved.Count; i++)
        {
            if (matchedSavedIndices.Contains(i)) continue;
            var entry = saved[i];
            string label = ProfileLogic.IsMeaningfulDeviceName(entry.Name)
                ? entry.Name
                : $"Saved device ({entry.Id.ToString()[..8]})";
            list.Items.Add(new DeviceItem(entry.Id, label, Connected: false), isChecked: true);
        }
    }

    private static void UncheckAll(CheckedListBox list)
    {
        for (int i = 0; i < list.Items.Count; i++)
            list.SetItemChecked(i, false);
    }

    private static CheckedListBox MakeList(Point location, int height) => new()
    {
        Location = location,
        Size = new Size(344, height),
        BackColor = Theme.BgCard,
        ForeColor = Theme.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        CheckOnClick = true
    };

    public List<CycleDeviceEntry> SelectedOutputs => Collect(_outList);
    public List<CycleDeviceEntry> SelectedInputs => Collect(_inList);

    private static List<CycleDeviceEntry> Collect(CheckedListBox list) =>
        list.CheckedItems.Cast<DeviceItem>()
            .Where(d => d.Id.HasValue)
            .Select(d => new CycleDeviceEntry { Id = d.Id!.Value, Name = d.Name })
            .ToList();
}
