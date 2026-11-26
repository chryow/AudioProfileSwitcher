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

    protected static Button MakeButton(string text, Point location, bool primary)
    {
        var b = new Button
        {
            Text = text,
            Font = Theme.BodyBold,
            ForeColor = Theme.TextPrimary,
            BackColor = primary ? Theme.Accent : Theme.BgCard,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(100, 30),
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
    private readonly DarkCombo _out = new();
    private readonly DarkCombo _in = new();
    private readonly ToggleSwitch _auto;

    public CardEditorDialog(AudioCard card, bool isNew,
        IReadOnlyList<DeviceItem> outputs, IReadOnlyList<DeviceItem> inputs)
        : base(isNew ? "New Card" : "Edit Card", new Size(340, 296))
    {
        _name = new TextBox
        {
            Text = card.Name,
            Location = new Point(16, 34),
            Width = 308,
            BackColor = Theme.BgCard,
            ForeColor = Theme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        _out.Location = new Point(16, 94); _out.Width = 308;
        _in.Location = new Point(16, 154); _in.Width = 308;
        Fill(_out, outputs, card.OutputId, card.OutputName);
        Fill(_in, inputs, card.InputId, card.InputName);

        _auto = new ToggleSwitch { Location = new Point(16, 200), Checked = card.AutoActivate };
        var autoLbl = new Label
        {
            Text = "Switch to this card automatically when one of its devices connects",
            Location = new Point(64, 192),
            Size = new Size(260, 36),
            ForeColor = Theme.TextMuted
        };

        var save = MakeButton("Save Card", new Point(116, 248), true);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", new Point(224, 248), false);
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            MakeLabel("Card name", new Point(16, 14)), _name,
            MakeLabel("Output device (speakers / headset)", new Point(16, 74)), _out,
            MakeLabel("Input device (microphone)", new Point(16, 134)), _in,
            _auto, autoLbl, save, cancel
        });
    }

    private static void Fill(DarkCombo combo, IReadOnlyList<DeviceItem> devices, Guid? selectedId, string savedName)
    {
        combo.Items.Add(new DeviceItem(null, "(Don't change)"));
        foreach (var d in devices) combo.Items.Add(d);

        int selected = 0;
        if (selectedId.HasValue)
        {
            selected = combo.Items.Cast<DeviceItem>().ToList().FindIndex(i => i.Id == selectedId);
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
    public DeviceItem? SelectedInput => _in.SelectedItem as DeviceItem;
    public bool AutoActivate => _auto.Checked;
}

internal sealed class DeviceFilterDialog : DarkDialog
{
    private readonly CheckedListBox _outList;
    private readonly CheckedListBox _inList;
    private readonly List<Guid> _offlineOut;
    private readonly List<Guid> _offlineIn;

    public DeviceFilterDialog(IReadOnlyList<DeviceItem> outputs, IReadOnlyList<DeviceItem> inputs,
        List<Guid> selectedOut, List<Guid> selectedIn)
        : base("Cycle Device Filter", new Size(360, 400))
    {
        // Remember filter entries for devices that are unplugged right now so saving doesn't drop them.
        _offlineOut = selectedOut.Where(id => !outputs.Any(d => d.Id == id)).ToList();
        _offlineIn = selectedIn.Where(id => !inputs.Any(d => d.Id == id)).ToList();

        _outList = MakeList(new Point(16, 34), 130);
        foreach (var d in outputs) _outList.Items.Add(d, d.Id.HasValue && selectedOut.Contains(d.Id.Value));

        _inList = MakeList(new Point(16, 196), 120);
        foreach (var d in inputs) _inList.Items.Add(d, d.Id.HasValue && selectedIn.Contains(d.Id.Value));

        var save = MakeButton("Save Filter", new Point(136, 336), true);
        save.DialogResult = DialogResult.OK;
        var cancel = MakeButton("Cancel", new Point(244, 336), false);
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange(new Control[]
        {
            MakeLabel("Include in Cycle Output (none checked = all):", new Point(16, 12)), _outList,
            MakeLabel("Include in Cycle Input (none checked = all):", new Point(16, 174)), _inList,
            save, cancel
        });
    }

    private static CheckedListBox MakeList(Point location, int height) => new()
    {
        Location = location,
        Size = new Size(328, height),
        BackColor = Theme.BgCard,
        ForeColor = Theme.TextPrimary,
        BorderStyle = BorderStyle.FixedSingle,
        CheckOnClick = true
    };

    public List<Guid> OutputIds => Collect(_outList, _offlineOut);
    public List<Guid> InputIds => Collect(_inList, _offlineIn);

    private static List<Guid> Collect(CheckedListBox list, List<Guid> offline) =>
        list.CheckedItems.Cast<DeviceItem>()
            .Where(d => d.Id.HasValue).Select(d => d.Id!.Value)
            .Concat(offline).ToList();
}
