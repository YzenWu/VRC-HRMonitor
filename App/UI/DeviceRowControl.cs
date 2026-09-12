namespace HeartRateMonitor.UI;

/// <summary> Device Line Control: Check box + Info + Connection/ Disconnect/Save (Deleted)/ Block. </summary>
public sealed class DeviceRowControl : UserControl
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Mac { get; }
    private string _name;
    private string _type;
    private int _rssi;

    private readonly CheckBox _check;
    private readonly Label _info;
    private readonly Button _connect;
    private readonly Button _disconnect;
    private readonly Button _save;
    private readonly Button _block;

    public event Action<DeviceRowControl>? OnConnect;
    public event Action<DeviceRowControl>? OnDisconnect;
    public event Action<DeviceRowControl>? OnToggleSave;
    public event Action<DeviceRowControl>? OnBlock;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool RowChecked
    {
        get => _check.Checked;
        set => _check.Checked = value;
    }

    public DeviceRowControl(string mac, string name, int rssi, string type, bool connected, bool saved)
    {
        Mac = mac;
        _name = name;
        _type = type;
        _rssi = rssi;
        Height = 50;
        Margin = new Padding(0, 0, 0, 6);
        BackColor = Color.FromArgb(30, 30, 36);
        Padding = new Padding(6);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Padding = new Padding(2) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 34));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));

        _check = new CheckBox { Dock = DockStyle.Fill, CheckAlign = ContentAlignment.MiddleCenter, Font = new Font("Microsoft YaHei UI", 12) };
        layout.Controls.Add(_check, 0, 0);

        _info = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        layout.Controls.Add(_info, 1, 0);

        _connect = MakeBtn("连接", Color.FromArgb(0, 120, 215));
        _connect.Click += (_, _) => OnConnect?.Invoke(this);
        layout.Controls.Add(_connect, 2, 0);
        _disconnect = MakeBtn("断开", Color.FromArgb(200, 90, 30));
        _disconnect.Click += (_, _) => OnDisconnect?.Invoke(this);
        layout.Controls.Add(_disconnect, 3, 0);
        _save = MakeBtn("保存", Color.FromArgb(60, 140, 60));
        _save.Click += (_, _) => OnToggleSave?.Invoke(this);
        layout.Controls.Add(_save, 4, 0);
        _block = MakeBtn("屏蔽", Color.FromArgb(140, 60, 60));
        _block.Click += (_, _) => OnBlock?.Invoke(this);
        layout.Controls.Add(_block, 5, 0);

        Controls.Add(layout);
        UpdateState(connected, 0, saved);
    }

    static Button MakeBtn(string text, Color bg)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Margin = new Padding(3, 6, 3, 6),
        };
    }

    public void SetInfo(string name, int rssi, string type)
    {
        _name = name;
        _type = type;
        _rssi = rssi;
        RefreshText();
    }

    public void UpdateState(bool connected, int bpm, bool saved)
    {
        _connect.Text = connected ? "已连接" : "连接";
        _connect.BackColor = connected ? Color.FromArgb(40, 90, 60) : Color.FromArgb(0, 120, 215);
        _disconnect.Enabled = connected;
        _save.Text = saved ? "删除" : "保存";
        _saved = saved;
        _connected = connected;
        _bpm = bpm;
        RefreshText();
    }

    bool _connected;
    bool _saved;
    int _bpm;

    void RefreshText()
    {
        var bpmText = _connected && _bpm > 0 ? $"   ● {_bpm} bpm" : (_connected ? "   已连接" : "");
        _info.Text = $"{_name}\n{Mac}  {_type}  RSSI {_rssi}{bpmText}";
    }
}
