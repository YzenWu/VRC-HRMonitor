using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeartRateMonitor.Ble;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.UI;

/// <summary>Main form containing the OSC, heart rate, hardware, webhook, log, and settings tabs.
/// The heart rate tab provides per-device selection and connection controls plus batch actions.</summary>
public class MainForm : Form
{
    // ---- Form basis -
    public MainForm()
    {
        Text = "OSC Pusher V1";
        Font = new Font("Microsoft YaHei UI", 10);
        Width = 1240;
        Height = 820;
        MinimumSize = new Size(1060, 700);
        var tabs = new TabControl { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 10.5f) };
        Controls.Add(tabs);
        tabs.TabPages.Add(BuildOscTab());
        tabs.TabPages.Add(BuildHeartTab());
        tabs.TabPages.Add(BuildHwTab());
        tabs.TabPages.Add(BuildWebhookTab());
        tabs.TabPages.Add(BuildLogTab());
        tabs.TabPages.Add(BuildSettingsTab());

        _floatFormat = App.Config.HeartRate.Window.Format;
        _floatUnlocked = App.Config.HeartRate.Window.UnlockedColor;
        _floatLocked = App.Config.HeartRate.Window.LockedColor;
        _floatImage = App.Config.HeartRate.Window.ImagePath;

        WireEvents();
        LoadSettingsIntoUi();
        App.Log.Info(LogText.L("log.main.loaded"));
    }

    // ================================================================ OSC Page

    private readonly TextBox _oscIp = new() { Text = "127.0.0.1" };
    private readonly TextBox _oscPort = new() { Text = "9000", Width = 90 };
    private readonly TextBox _oscAddress = new() { Text = "/chatbox/input" };
    private readonly TextBox _oscInterval = new() { Text = "1000", Width = 90 };
    private readonly TextBox _oscRecvPort = new() { Text = "9001", Width = 90 };
    private readonly TextBox _templateText = new()
    {
        Multiline = true,
        AcceptsReturn = true,
        AcceptsTab = true,
        WordWrap = true,
        Font = new Font("Consolas", 11),
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
    };
    private readonly TextBox _previewText = new() { Multiline = true, ReadOnly = true, Font = new Font("Consolas", 10), Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
    private readonly Label _oscStatus = new() { Text = "状态: 未连接", ForeColor = Color.Gray };
    private readonly Label _oscCounts = new() { Text = "发送 0 / 失败 0 / 接收 0", ForeColor = Color.Gray };
    private readonly Button _oscConnectBtn = new() { Text = "连接 OSC", Width = 120, Height = 34 };

    TabPage BuildOscTab()
    {
        var tab = new TabPage("OSC") { Padding = new Padding(16) };
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(outer);

        var conn = new GroupBox { Text = "OSC 连接", Dock = DockStyle.Fill, Padding = new Padding(16) };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 2, Padding = new Padding(6) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 15));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        AddLabeledRow(grid, 0, "IP:", _oscIp, "端口:", _oscPort, "地址:", _oscAddress);

        var row1 = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        row1.Controls.Add(Label("推送间隔(ms):", 0));
        row1.Controls.Add(_oscInterval);
        row1.Controls.Add(Label("接收端口:", 18));
        row1.Controls.Add(_oscRecvPort);
        row1.Controls.Add(_oscConnectBtn);
        var saveBtn = Button("保存设置", 120, Color.FromArgb(60, 60, 80));
        saveBtn.Click += (_, _) => SaveSettings();
        row1.Controls.Add(saveBtn);
        row1.Controls.Add(Label("  ", 8));
        row1.Controls.Add(_oscStatus);
        grid.Controls.Add(row1, 0, 1);
        grid.SetColumnSpan(row1, 6);
        conn.Controls.Add(grid);
        outer.Controls.Add(conn, 0, 0);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 580, SplitterWidth = 10, Padding = new Padding(0) };
        outer.Controls.Add(split, 0, 1);

        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 10, 10, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.Controls.Add(new Label { Text = "OSC 推送内容模板（支持多行 / 换行，变量格式 {变量名}）", AutoSize = true, Margin = new Padding(0, 0, 0, 8) });
        left.Controls.Add(_templateText);
        split.Panel1.Controls.Add(left);

        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10, 10, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var previewBox = new GroupBox { Text = "预览（每周期推送内容）", Dock = DockStyle.Fill, Padding = new Padding(10) };
        previewBox.Controls.Add(_previewText);
        right.Controls.Add(previewBox);
        var statBox = new GroupBox { Text = "统计", Dock = DockStyle.Fill, Padding = new Padding(16) };
        statBox.Controls.Add(_oscCounts);
        right.Controls.Add(statBox);
        var testRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        var testAddr = new TextBox { Text = "/test/echo", Width = 120, Height = 34 };
        var testText = new TextBox { Text = "hello", Width = 160, Height = 34 };
        var testBtn = Button("发送测试", 100, Color.FromArgb(0, 120, 215));
        testBtn.Click += (_, _) =>
        {
            var ok = App.Osc.SendTest(_oscIp.Text, _oscPort.Text, testAddr.Text, testText.Text);
            App.Log.Info(LogText.L(ok ? "log.main.osc_test_ok" : "log.main.osc_test_fail", testAddr.Text, testText.Text));
        };
        testRow.Controls.Add(Label("地址:", 0));
        testRow.Controls.Add(testAddr);
        testRow.Controls.Add(Label("文本:", 14));
        testRow.Controls.Add(testText);
        testRow.Controls.Add(testBtn);
        right.Controls.Add(testRow);
        split.Panel2.Controls.Add(right);
        return tab;
    }

    // ================================================================ Heartrate Page

    private readonly ComboBox _hrSource = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly Label _hrValue = new() { Text = "心率: --", Font = new Font("Arial", 36, FontStyle.Bold), ForeColor = Color.Red };
    private readonly Label _hrStatus = new() { Text = "状态: 未连接", Font = new Font("Arial", 12), ForeColor = Color.Gray };
    private readonly Label _deviceInfo = new() { Text = "无", AutoSize = true };
    private readonly Button _scanBtn = Button("开始扫描", 120, Color.FromArgb(0, 120, 215));
    private readonly Button _clearListBtn = Button("清空列表", 100, Color.FromArgb(60, 60, 80));
    private readonly CheckBox _apiEnabled = new() { Text = "启用API服务器" };
    private readonly TextBox _apiPort = new() { Text = "8000", Width = 90, Height = 34 };
    private readonly Label _apiStatus = new() { Text = "状态: 已禁用", ForeColor = Color.Gray };
    private readonly CheckBox _webhookEnabled = new() { Text = "启用 Webhook 推送" };

    // Device List (line)
    private readonly FlowLayoutPanel _deviceScroll = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    private readonly Dictionary<string, (string name, int rssi, string type)> _devices = new();
    private readonly Dictionary<string, DeviceRowControl> _rows = new();
    private bool _scanning;

    TabPage BuildHeartTab()
    {
        var tab = new TabPage("心率") { Padding = new Padding(16) };
        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 540, SplitterWidth = 10 };
        tab.Controls.Add(split);

        // ---- Left: Device List -
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(0, 0, 10, 0) };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Panel1.Controls.Add(left);

        var ctrlBox = new GroupBox { Text = "连接控制", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var ctrl = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        ctrl.Controls.Add(_scanBtn);
        ctrl.Controls.Add(_clearListBtn);
        ctrlBox.Controls.Add(ctrl);
        left.Controls.Add(ctrlBox, 0, 0);

        var batchBox = new GroupBox { Text = "批量操作（作用于勾选的设备）", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var batch = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        _connectSelBtn = Button("连接所选", 110, Color.FromArgb(0, 120, 215));
        _connectSelBtn.Click += (_, _) => BatchConnect();
        batch.Controls.Add(_connectSelBtn);
        _disconnectSelBtn = Button("断开所选", 110, Color.FromArgb(200, 90, 30));
        _disconnectSelBtn.Click += (_, _) => BatchDisconnect();
        batch.Controls.Add(_disconnectSelBtn);
        _invertBtn = Button("反选", 80, Color.FromArgb(60, 60, 80));
        _invertBtn.Click += (_, _) => InvertSelection();
        batch.Controls.Add(_invertBtn);
        _saveSelBtn = Button("保存所选", 110, Color.FromArgb(60, 140, 60));
        _saveSelBtn.Click += (_, _) => BatchSave();
        batch.Controls.Add(_saveSelBtn);
        _blockSelBtn = Button("屏蔽所选", 110, Color.FromArgb(140, 60, 60));
        _blockSelBtn.Click += (_, _) => BatchBlock();
        batch.Controls.Add(_blockSelBtn);
        batchBox.Controls.Add(batch);
        left.Controls.Add(batchBox, 0, 1);

        var listBox = new GroupBox { Text = "扫描到的设备（点击行内按钮操作）", Dock = DockStyle.Fill, Padding = new Padding(10) };
        _deviceScroll.Resize += (_, _) => ResizeRows();
        listBox.Controls.Add(_deviceScroll);
        left.Controls.Add(listBox, 0, 2);

        // ---- Right: Heartrate / Device Information / API / Suspend Window -
        var right = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10, 0, 0, 0) };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Panel2.Controls.Add(right);

        var hrBox = new GroupBox { Text = "心率监控", Dock = DockStyle.Fill, Padding = new Padding(14) };
        var hrFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        var srcRow = new FlowLayoutPanel { AutoSize = true };
        srcRow.Controls.Add(Label("显示来源:", 0));
        srcRow.Controls.Add(_hrSource);
        hrFlow.Controls.Add(srcRow);
        hrFlow.Controls.Add(_hrValue);
        hrFlow.Controls.Add(_hrStatus);
        hrBox.Controls.Add(hrFlow);
        right.Controls.Add(hrBox, 0, 0);

        var devBox = new GroupBox { Text = "已连接设备", Dock = DockStyle.Fill, Padding = new Padding(14) };
        devBox.Controls.Add(_deviceInfo);
        right.Controls.Add(devBox, 0, 1);

        var svcRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        svcRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        svcRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var api = new GroupBox { Text = "心率API服务器", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var apiFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        apiFlow.Controls.Add(_apiEnabled);
        apiFlow.Controls.Add(Label("端口:", 10));
        apiFlow.Controls.Add(_apiPort);
        apiFlow.Controls.Add(_apiStatus);
        api.Controls.Add(apiFlow);
        svcRow.Controls.Add(api, 0, 0);
        var webhookBox = new GroupBox { Text = "Webhook 数据推送", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var whRow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        whRow.Controls.Add(_webhookEnabled);
        var whBtn = Button("打开设置...", 130, Color.FromArgb(60, 60, 80));
        whBtn.Click += (_, _) => tabs.SelectedIndex = 3;
        whRow.Controls.Add(whBtn);
        webhookBox.Controls.Add(whRow);
        svcRow.Controls.Add(webhookBox, 1, 0);
        right.Controls.Add(svcRow, 0, 2);

        var floatBox = new GroupBox { Text = "悬浮窗", Dock = DockStyle.Fill, Padding = new Padding(14) };
        var fFlow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        var fRow = new FlowLayoutPanel { AutoSize = true };
        var openMain = Button("打开主悬浮窗", 130, Color.FromArgb(60, 60, 80));
        openMain.Click += (_, _) => OpenFloating("__main__", "心率");
        fRow.Controls.Add(openMain);
        var openAll = Button("打开全部", 100, Color.FromArgb(60, 60, 80));
        openAll.Click += (_, _) => OpenAllFloating();
        fRow.Controls.Add(openAll);
        var closeAll = Button("关闭全部", 100, Color.FromArgb(60, 60, 80));
        closeAll.Click += (_, _) => CloseAllFloating();
        fRow.Controls.Add(closeAll);
        var lockAll = Button("锁定全部", 100, Color.FromArgb(60, 60, 80));
        lockAll.Click += (_, _) => ToggleLockAll();
        fRow.Controls.Add(lockAll);
        fFlow.Controls.Add(fRow);

        var fmtRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        fmtRow.Controls.Add(Label("格式:", 0));
        var fmtBoxText = new TextBox { Text = _floatFormat, Width = 240, Height = 34 };
        fmtRow.Controls.Add(fmtBoxText);
        var applyFmt = Button("应用格式", 100, Color.FromArgb(60, 60, 80));
        applyFmt.Click += (_, _) => { _floatFormat = fmtBoxText.Text; ApplyFloatingStyle(); };
        fmtRow.Controls.Add(applyFmt);
        fFlow.Controls.Add(fmtRow);
        fFlow.Controls.Add(new Label { Text = "{bpm}: 心率, {img}: 图片", ForeColor = Color.Gray, AutoSize = true, Margin = new Padding(0, 2, 0, 0) });

        var colorRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        colorRow.Controls.Add(Label("解锁颜色:", 0));
        var pickUnlocked = Button("选择...", 90, Color.FromArgb(60, 60, 80));
        pickUnlocked.Click += (_, _) => PickColor(isUnlocked: true);
        colorRow.Controls.Add(pickUnlocked);
        colorRow.Controls.Add(Label("锁定颜色:", 18));
        var pickLocked = Button("选择...", 90, Color.FromArgb(60, 60, 80));
        pickLocked.Click += (_, _) => PickColor(isUnlocked: false);
        colorRow.Controls.Add(pickLocked);
        fFlow.Controls.Add(colorRow);

        var imgRow = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 10, 0, 0) };
        imgRow.Controls.Add(Label("图片:", 0));
        var pickImg = Button("选择图片...", 110, Color.FromArgb(60, 60, 80));
        pickImg.Click += (_, _) => PickImage();
        imgRow.Controls.Add(pickImg);
        var clearImg = Button("清除图片", 100, Color.FromArgb(60, 60, 80));
        clearImg.Click += (_, _) => { _floatImage = null; ApplyFloatingStyle(); };
        imgRow.Controls.Add(clearImg);
        fFlow.Controls.Add(imgRow);
        floatBox.Controls.Add(fFlow);
        right.Controls.Add(floatBox, 0, 3);

        return tab;
    }

    private Button _connectSelBtn = null!;
    private Button _disconnectSelBtn = null!;
    private Button _invertBtn = null!;
    private Button _saveSelBtn = null!;
    private Button _blockSelBtn = null!;

    // ================================================================ Hardware Page

    private readonly ComboBox _hwFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ListView _hwList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    private readonly Label _hwStatus = new() { Text = "采集: 注册表 + wmic(仅Win10-) + Get-ComputerInfo + systeminfo", ForeColor = Color.Gray };

    TabPage BuildHwTab()
    {
        var tab = new TabPage("硬件") { Padding = new Padding(16) };
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(outer);

        var tool = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        tool.Controls.Add(Label("筛选:", 0));
        _hwFilter.Items.AddRange(new object[] { "全部", "CPU", "GPU", "内存/RAM", "磁盘", "系统/OS", "其他" });
        _hwFilter.SelectedIndex = 0;
        tool.Controls.Add(_hwFilter);
        var refresh = Button("刷新", 90, Color.FromArgb(60, 60, 80));
        refresh.Click += (_, _) => { App.SysInfo.CollectFull(); App.Log.Info(LogText.L("log.main.hw_refresh")); };
        tool.Controls.Add(refresh);
        tool.Controls.Add(_hwStatus);
        outer.Controls.Add(tool, 0, 0);

        _hwList.Columns.Add("变量", 240);
        _hwList.Columns.Add("值", 440);
        outer.Controls.Add(_hwList, 0, 1);
        _hwFilter.SelectedIndexChanged += (_, _) => RefreshHwList();
        return tab;
    }

    // ================================================================ Webhook Page

    private readonly ListView _webhookList = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };

    TabPage BuildWebhookTab()
    {
        var tab = new TabPage("Webhook") { Padding = new Padding(16) };
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(outer);

        var tool = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        var addBtn = Button("新增", 90, Color.FromArgb(60, 60, 80));
        addBtn.Click += (_, _) => EditWebhook(null);
        var editBtn = Button("编辑", 90, Color.FromArgb(60, 60, 80));
        editBtn.Click += (_, _) => EditWebhook(SelectedWebhook());
        var delBtn = Button("删除", 90, Color.FromArgb(140, 60, 60));
        delBtn.Click += (_, _) => DeleteWebhook();
        var testBtn = Button("测试", 90, Color.FromArgb(60, 140, 60));
        testBtn.Click += (_, _) => TestWebhook();
        tool.Controls.Add(addBtn);
        tool.Controls.Add(editBtn);
        tool.Controls.Add(delBtn);
        tool.Controls.Add(testBtn);
        outer.Controls.Add(tool, 0, 0);

        _webhookList.Columns.Add("名称", 140);
        _webhookList.Columns.Add("URL", 320);
        _webhookList.Columns.Add("启用", 60);
        _webhookList.Columns.Add("触发", 180);
        outer.Controls.Add(_webhookList, 0, 1);
        RefreshWebhookList();
        return tab;
    }

    // ================================================================ Log Page

    private readonly TextBox _logText = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _logFilter = new() { Width = 240, Height = 34 };
    private readonly CheckBox _autoDump = new() { Text = "自动转储", Checked = true };
    private readonly TextBox _dumpInterval = new() { Text = "30", Width = 70, Height = 34 };

    TabPage BuildLogTab()
    {
        var tab = new TabPage("日志") { Padding = new Padding(16) };
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        tab.Controls.Add(outer);

        var tool = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        tool.Controls.Add(Label("过滤:", 0));
        tool.Controls.Add(_logFilter);
        var clear = Button("清除日志", 100, Color.FromArgb(60, 60, 80));
        clear.Click += (_, _) => Ui(() => _logText.Clear());
        tool.Controls.Add(clear);
        var dump = Button("转储日志", 100, Color.FromArgb(60, 60, 80));
        dump.Click += (_, _) => { var f = App.Log.Dump(App.LogBuffer.ToList()); App.Log.Info(LogText.L("log.hub.log_dump", f)); };
        tool.Controls.Add(dump);
        tool.Controls.Add(_autoDump);
        tool.Controls.Add(Label("间隔(分):", 12));
        tool.Controls.Add(_dumpInterval);
        outer.Controls.Add(tool, 0, 0);
        outer.Controls.Add(_logText, 0, 1);
        return tab;
    }

    // ================================================================ Set Page

    TabPage BuildSettingsTab()
    {
        var tab = new TabPage("设置") { Padding = new Padding(16) };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        var about = new GroupBox { Text = "关于", AutoSize = true, Padding = new Padding(20) };
        var txt = new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10),
            Text = $"版本: {GetType().Assembly.GetName().Version}\n" +
                   $"引擎: osc_engine.dll {(Osc.OscEngine.Available ? "已加载" : "未加载")}\n" +
                   $"模式: {(App.DebugMode ? "DEBUG（详细日志+控制台）" : "RELEASE")}\n" +
                   $"程序目录: {App.BaseDir}\n" +
                   $"日志文件: {Path.Combine(App.BaseDir, "logs", "app.log")}\n" +
                   $"启动时间: {App.StartTime:yyyy-MM-dd HH:mm:ss}"
        };
        about.Controls.Add(txt);
        flow.Controls.Add(about);

        var openLogBtn = Button("打开日志目录", 130, Color.FromArgb(60, 60, 80));
        openLogBtn.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", Path.Combine(App.BaseDir, "logs")) { UseShellExecute = true }); }
            catch { }
        };
        flow.Controls.Add(openLogBtn);

        var tips = new GroupBox { Text = "使用说明", AutoSize = true, Padding = new Padding(20) };
        tips.Controls.Add(new Label
        {
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            ForeColor = Color.Gray,
            Text = "硬件信息采集方法: 注册表(主) / wmic(仅Win10-) / Get-ComputerInfo / systeminfo；实时指标用 PDH 计数器\n" +
                   "设备列表: 每行可勾选，行内按钮连接/断开/保存(删除)/屏蔽；顶部批量操作作用于勾选设备\n" +
                   "屏蔽: 将 MAC 写入 config.json 的 heart_rate.blocked，之后不再显示\n" +
                   "CLI 模式: HeartRateMonitor.exe --cli （控制台拉起时自动进入）\n" +
                   "构建分支: build.bat [--standalone | --releases | --debug]\n" +
                   "模板变量: 在「OSC」标签页多行编辑，支持 {变量名} 占位"
        });
        flow.Controls.Add(tips);

        tab.Controls.Add(flow);
        return tab;
    }

    // ================================================================ Events

    void WireEvents()
    {
        _oscConnectBtn.Click += (_, _) =>
        {
            var connect = !App.Osc.Connected;
            App.Osc.SetConnected(connect);
            _oscConnectBtn.Text = connect ? "断开 OSC" : "连接 OSC";
        };
        _scanBtn.Click += (_, _) => ToggleScan();
        _clearListBtn.Click += (_, _) => Ui(() =>
        {
            _devices.Clear();
            foreach (var r in _rows.Values.ToList()) r.Dispose();
            _rows.Clear();
        });
        _hrSource.SelectedIndexChanged += (_, _) => RefreshDisplaySource();
        _apiEnabled.CheckedChanged += (_, _) => ToggleApiServer();
        _webhookEnabled.CheckedChanged += (_, _) =>
        {
            App.Config.Webhook.Enabled = _webhookEnabled.Checked;
            App.Log.Info(LogText.L(_webhookEnabled.Checked ? "log.main.webhook_enabled" : "log.main.webhook_disabled"));
        };

        App.Log.OnLog += line =>
        {
            App.LogBuffer.Enqueue(line);
            while (App.LogBuffer.Count > 5000) App.LogBuffer.TryDequeue(out _);
            Ui(() =>
            {
                var f = _logFilter.Text.Trim();
                if (f.Length > 0 && !line.Contains(f)) return;
                _logText.AppendText(line + Environment.NewLine);
                _logText.SelectionStart = _logText.TextLength;
                _logText.ScrollToCaret();
            });
        };

        // Device detection (block list filter)
        App.Ble.DeviceFound += (mac, name, rssi, type) => Ui(() =>
        {
            if (App.Config.HeartRate.Blocked.Contains(mac, StringComparer.OrdinalIgnoreCase)) return;
            _devices[mac] = (name, rssi, type);
            AddOrUpdateRow(mac);
        });
        App.Ble.HeartRate += (mac, name, bpm) =>
        {
            App.SysInfo.UpdateHeartRateVars();
            Ui(() =>
            {
                if (_rows.TryGetValue(mac, out var row))
                {
                    var dev = App.Ble.Get(mac);
                    row.UpdateState(dev?.Connected == true, dev?.Bpm ?? 0, IsSaved(mac));
                }
                RefreshDisplaySource();
                foreach (var w in _floatWindows.Values) w.UpdateHeartRate();
                if (DateTime.Now - _lastWebhookHr > TimeSpan.FromSeconds(1))
                {
                    _lastWebhookHr = DateTime.Now;
                    App.Webhooks.Trigger("heart_rate_updated", App.CurrentBpm);
                }
            });
        };
        App.Ble.Connected += (mac, name) =>
        {
            App.Webhooks.Trigger("connected", App.CurrentBpm);
            Ui(() => { RefreshDisplaySource(); RefreshRows(); });
        };
        App.Ble.Disconnected += (mac, name, manual) => Ui(() => { RefreshDisplaySource(); RefreshRows(); });

        App.SysInfo.Updated += () => Ui(() =>
        {
            _previewText.Text = App.SysInfo.FormatTemplate(_templateText.Text);
            _oscCounts.Text = $"发送 {App.Osc.SentCount} / 失败 {App.Osc.FailCount} / 接收 {App.Osc.RecvCount}";
            if (tabs.SelectedIndex == 2) RefreshHwList();
        });

        App.Osc.ConnectionChanged += connected => Ui(() =>
        {
            _oscStatus.Text = connected ? "状态: 已连接" : "状态: 未连接";
            _oscStatus.ForeColor = connected ? Color.Green : Color.Gray;
        });

        var dumpTimer = new System.Threading.Timer(_ =>
        {
            if (_autoDump.Checked)
            {
                var min = int.TryParse(_dumpInterval.Text, out var m) ? Math.Max(1, m) : 30;
                if (DateTime.Now - _lastDump > TimeSpan.FromMinutes(min))
                {
                    _lastDump = DateTime.Now;
                    var f = App.Log.Dump(App.LogBuffer.ToList());
                    App.Log.Debug(LogText.L("log.main.autodump", f));
                }
            }
        }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        _dumpTimer = dumpTimer;
    }

    // ================================================================ Equipment line management

    bool IsSaved(string mac) => App.Config.HeartRate.Devices.Contains(mac, StringComparer.OrdinalIgnoreCase);

    void AddOrUpdateRow(string mac)
    {
        if (!_devices.TryGetValue(mac, out var info)) return;
        if (_rows.TryGetValue(mac, out var existing))
        {
            existing.SetInfo(info.name, info.rssi, info.type);
            return;
        }
        var dev = App.Ble.Get(mac);
        var row = new DeviceRowControl(mac, info.name, info.rssi, info.type,
            dev?.Connected == true, IsSaved(mac))
        {
            Width = Math.Max(_deviceScroll.ClientSize.Width - 24, 380),
        };
        row.OnConnect += r => _ = App.Ble.ConnectAsync(r.Mac);
        row.OnDisconnect += r => App.Ble.Disconnect(r.Mac);
        row.OnToggleSave += r => ToggleSave(r.Mac);
        row.OnBlock += r => BlockDevice(r.Mac);
        _rows[mac] = row;
        _deviceScroll.Controls.Add(row);
        row.UpdateState(dev?.Connected == true, dev?.Bpm ?? 0, IsSaved(mac));
        _deviceScroll.Controls.SetChildIndex(row, 0); // New device tops
    }

    void RefreshRows()
    {
        foreach (var (mac, row) in _rows.ToList())
        {
            var dev = App.Ble.Get(mac);
            row.UpdateState(dev?.Connected == true, dev?.Bpm ?? 0, IsSaved(mac));
        }
    }

    void ResizeRows()
    {
        foreach (var row in _rows.Values)
            row.Width = Math.Max(_deviceScroll.ClientSize.Width - 24, 380);
    }

    void ToggleSave(string mac)
    {
        var list = App.Config.HeartRate.Devices;
        if (list.Contains(mac, StringComparer.OrdinalIgnoreCase))
            list.RemoveAll(m => m.Equals(mac, StringComparison.OrdinalIgnoreCase));
        else
            list.Add(mac);
        App.Config.Save();
        if (_rows.TryGetValue(mac, out var row)) row.UpdateState(App.Ble.Get(mac)?.Connected == true, App.Ble.Get(mac)?.Bpm ?? 0, IsSaved(mac));
        App.Log.Info(LogText.L("log.main.save_state", mac));
    }

    void BlockDevice(string mac)
    {
        if (!App.Config.HeartRate.Blocked.Contains(mac, StringComparer.OrdinalIgnoreCase))
            App.Config.HeartRate.Blocked.Add(mac);
        App.Config.Save();
        _devices.Remove(mac);
        if (_rows.TryGetValue(mac, out var row))
        {
            _rows.Remove(mac);
            row.Dispose();
        }
        try { App.Ble.Disconnect(mac); } catch { }
        App.Log.Info(LogText.L("log.main.device_blocked", mac));
    }

    // ---- Batch Operations -

    List<string> CheckedMacs()
    {
        return _rows.Values.Where(r => r.RowChecked).Select(r => r.Mac).ToList();
    }

    void BatchConnect()
    {
        foreach (var mac in CheckedMacs())
            _ = App.Ble.ConnectAsync(mac);
        if (CheckedMacs().Count > 0) App.Log.Info(LogText.L("log.main.batch_connect", CheckedMacs().Count));
    }

    void BatchDisconnect()
    {
        foreach (var mac in CheckedMacs())
            App.Ble.Disconnect(mac);
    }

    void InvertSelection()
    {
        foreach (var r in _rows.Values) r.RowChecked = !r.RowChecked;
    }

    void BatchSave()
    {
        var list = App.Config.HeartRate.Devices;
        foreach (var mac in CheckedMacs())
            if (!list.Contains(mac, StringComparer.OrdinalIgnoreCase))
                list.Add(mac);
        App.Config.Save();
        RefreshRows();
        App.Log.Info(LogText.L("log.main.batch_save", CheckedMacs().Count));
    }

    void BatchBlock()
    {
        foreach (var mac in CheckedMacs().ToList())
            BlockDevice(mac);
    }

    // ================================================================ Heart rate

    void LoadSettingsIntoUi()
    {
        _oscIp.Text = App.Config.Osc.Ip;
        _oscPort.Text = App.Config.Osc.Port;
        _oscAddress.Text = App.Config.Osc.Address;
        _oscInterval.Text = App.Config.Osc.IntervalMs;
        _oscRecvPort.Text = App.Config.Osc.ReceivePort;
        _templateText.Text = App.Config.Osc.Template;
        _apiPort.Text = "8000";
        _webhookEnabled.Checked = App.Config.Webhook.Enabled;
        _hrSource.Items.Add("平均");
        RefreshSourceDevices();
        _hrSource.SelectedIndex = 0;
        if (App.Config.HeartRate.DisplaySource is { Length: > 0 } ds && ds != "平均" && _hrSource.Items.Contains(ds))
            _hrSource.SelectedItem = ds;
    }

    void RefreshSourceDevices()
    {
        var sel = _hrSource.SelectedItem?.ToString();
        _suppressSource = true;
        _hrSource.Items.Clear();
        _hrSource.Items.Add("平均");
        foreach (var d in App.Ble.ConnectedDevices())
            _hrSource.Items.Add(d.Name);
        if (sel != null && _hrSource.Items.Contains(sel)) _hrSource.SelectedItem = sel;
        else _hrSource.SelectedIndex = 0;
        _suppressSource = false;
    }

    void RefreshDisplaySource()
    {
        if (_suppressSource) return;
        RefreshSourceDevices();
        var sel = _hrSource.SelectedItem?.ToString();
        if (sel == "平均")
        {
            App.CurrentBpm = App.Ble.AverageBpm();
        }
        else
        {
            var dev = App.Ble.ConnectedDevices().FirstOrDefault(x => x.Name == sel);
            App.CurrentBpm = dev?.Bpm ?? 0;
        }
        _hrValue.Text = App.CurrentBpm > 0 ? $"心率: {App.CurrentBpm}" : "心率: --";
        var names = string.Join(", ", App.Ble.ConnectedDevices().Select(x => x.Name));
        _deviceInfo.Text = names.Length > 0 ? names : "无";
        _hrStatus.Text = App.Ble.AnyConnected() ? "状态: 已连接" : "状态: 未连接";
        _hrStatus.ForeColor = App.Ble.AnyConnected() ? Color.Green : Color.Gray;
        App.SysInfo.UpdateHeartRateVars();
    }

    void ToggleScan()
    {
        if (_scanning) { App.Ble.StopScan(); _scanning = false; _scanBtn.Text = "开始扫描"; return; }
        _scanning = true;
        _scanBtn.Text = "停止扫描";
        App.Ble.StartScan(30);
    }

    // ================================================================ Floating Window

    private readonly Dictionary<string, FloatingWindow> _floatWindows = new();
    private bool _allLocked;
    private string _floatFormat;
    private string _floatUnlocked;
    private string _floatLocked;
    private string? _floatImage;

    Func<int> BpmProvider(string mac) => () =>
    {
        if (mac == "__main__") return App.CurrentBpm;
        var dev = App.Ble.Get(mac);
        return dev?.Bpm ?? 0;
    };

    void OpenFloating(string id, string title)
    {
        if (!_floatWindows.TryGetValue(id, out var w))
        {
            w = new FloatingWindow(id, title, BpmProvider(id), _ => { });
            _floatWindows[id] = w;
            w.FormClosed += (_, _) => _floatWindows.Remove(id);
        }
        w.SetStyle(_floatFormat, _floatUnlocked, _floatLocked, _floatImage);
        w.Show();
        // Lock-state dependent handle (click through) must be applied after Show
        w.ToggleLock(_allLocked);
        w.Activate();
    }

    void OpenAllFloating()
    {
        OpenFloating("__main__", "心率");
        foreach (var d in App.Ble.ConnectedDevices())
            OpenFloating(d.Mac, $"心率-{d.Name}");
    }

    void CloseAllFloating()
    {
        foreach (var w in _floatWindows.Values.ToList()) w.Close();
        _floatWindows.Clear();
    }

    void ToggleLockAll()
    {
        _allLocked = !_allLocked;
        foreach (var w in _floatWindows.Values) w.ToggleLock(_allLocked);
        App.Config.HeartRate.Window.Locked = _allLocked;
    }

    void ApplyFloatingStyle()
    {
        foreach (var w in _floatWindows.Values) w.SetStyle(_floatFormat, _floatUnlocked, _floatLocked, _floatImage);
    }

    void PickColor(bool isUnlocked)
    {
        using var dlg = new ColorDialog();
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            var hex = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            if (isUnlocked) _floatUnlocked = hex; else _floatLocked = hex;
            ApplyFloatingStyle();
        }
    }

    void PickImage()
    {
        using var dlg = new OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.gif;*.bmp" };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _floatImage = dlg.FileName;
            ApplyFloatingStyle();
        }
    }

    // ================================================================ Hardware List

    void RefreshHwList()
    {
        var filter = _hwFilter.SelectedItem?.ToString() ?? "全部";
        var vars = App.SysInfo.Vars.OrderBy(k => k.Key, StringComparer.Ordinal).ToList();
        _hwList.BeginUpdate();
        _hwList.Items.Clear();
        foreach (var kv in vars)
        {
            if (!MatchFilter(kv.Key, filter)) continue;
            var item = new ListViewItem(kv.Key);
            item.SubItems.Add(kv.Value);
            _hwList.Items.Add(item);
        }
        _hwList.EndUpdate();
    }

    static bool MatchFilter(string key, string filter)
    {
        if (filter == "全部") return true;
        return filter switch
        {
            "CPU" => key.StartsWith("CPU"),
            "GPU" => key.StartsWith("GPU") || key.StartsWith("VRAM"),
            "内存/RAM" => key.StartsWith("RAM") || key.StartsWith("DIMM"),
            "磁盘" => key.StartsWith("DISK") || key.StartsWith("DRIVE"),
            "系统/OS" => key.StartsWith("OS_") || key.StartsWith("BIOS") || key.StartsWith("MB_") || key.StartsWith("SYSINFO"),
            _ => key.StartsWith("BPM") || key.StartsWith("UPTIME") || key.StartsWith("PROCESS") ||
                 key.StartsWith("THREAD") || key.StartsWith("HANDLE") || key.StartsWith("FOCUS") || key.StartsWith("NIC"),
        };
    }

    // ================================================================ Webhook

    JsonObject? SelectedWebhook()
    {
        if (_webhookList.SelectedItems.Count == 0) return null;
        var idx = _webhookList.SelectedItems[0].Index;
        if (idx < 0 || idx >= App.Webhooks.Webhooks.Count) return null;
        var el = App.Webhooks.Webhooks[idx];
        return JsonNode.Parse(el.GetRawText()) as JsonObject;
    }

    void EditWebhook(JsonObject? original)
    {
        using var dlg = new WebhookWindow(original);
        if (dlg.ShowDialog(this) == DialogResult.OK && dlg.Result != null)
        {
            var el = JsonSerializer.Deserialize<JsonElement>(dlg.Result.ToJsonString());
            if (original != null && SelectedWebhook() != null)
            {
                var idx = _webhookList.SelectedItems[0].Index;
                if (idx >= 0 && idx < App.Webhooks.Webhooks.Count)
                    App.Webhooks.Webhooks[idx] = el;
            }
            else
            {
                App.Webhooks.Webhooks.Add(el);
            }
            App.Webhooks.Save();
            RefreshWebhookList();
        }
    }

    void DeleteWebhook()
    {
        var idx = _webhookList.SelectedItems.Count > 0 ? _webhookList.SelectedItems[0].Index : -1;
        if (idx >= 0 && idx < App.Webhooks.Webhooks.Count)
        {
            App.Webhooks.Webhooks.RemoveAt(idx);
            App.Webhooks.Save();
            RefreshWebhookList();
        }
    }

    void TestWebhook()
    {
        var obj = SelectedWebhook();
        if (obj == null) return;
        var el = JsonSerializer.Deserialize<JsonElement>(obj.ToJsonString());
        var (ok, status, resp, err) = App.Webhooks.Test(el);
        App.Log.Info(ok ? LogText.L("log.main.wh_test_ok", status ?? 0, resp) : LogText.L("log.main.wh_test_fail", err ?? resp));
    }

    void RefreshWebhookList()
    {
        _webhookList.BeginUpdate();
        _webhookList.Items.Clear();
        foreach (var w in App.Webhooks.Webhooks)
        {
            var name = w.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var url = w.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            var enabled = w.TryGetProperty("enabled", out var e) && e.ValueKind != JsonValueKind.False;
            var triggers = w.TryGetProperty("triggers", out var t) && t.ValueKind == JsonValueKind.Array
                ? string.Join(",", t.EnumerateArray().Select(x => x.GetString()))
                : "";
            var item = new ListViewItem(name);
            item.SubItems.Add(url);
            item.SubItems.Add(enabled ? "是" : "否");
            item.SubItems.Add(triggers);
            _webhookList.Items.Add(item);
        }
        _webhookList.EndUpdate();
    }

    // ================================================================ API Server

    private HttpListener? _api;
    private readonly object _apiLock = new();

    void ToggleApiServer()
    {
        lock (_apiLock)
        {
            if (_apiEnabled.Checked)
            {
                try
                {
                    _api?.Stop();
                    _api = new HttpListener();
                    var port = _apiPort.Text.Trim();
                    _api.Prefixes.Add($"http://127.0.0.1:{port}/");
                    _api.Start();
                    _apiStatus.Text = $"状态: 运行中 (:{port})";
                    App.Log.Info(LogText.L("log.main.api_started", port));
                    _ = Task.Run(ApiLoop);
                }
                catch (Exception e)
                {
                    _apiStatus.Text = "状态: 启动失败";
                    App.Log.Error(LogText.L("log.main.api_start_fail", e.Message));
                }
            }
            else
            {
                _api?.Stop();
                _api = null;
                _apiStatus.Text = "状态: 已禁用";
                App.Log.Info(LogText.L("log.main.api_stopped"));
            }
        }
    }

    async Task ApiLoop()
    {
        while (true)
        {
            try
            {
                if (_api == null || !_api.IsListening) break;
                var ctx = await _api.GetContextAsync();
                var json = $"{{\"bpm\":{App.CurrentBpm},\"connected\":{App.Ble.AnyConnected().ToString().ToLower()}," +
                           $"\"average\":{App.Ble.AverageBpm()},\"ts\":\"{DateTime.Now:O}\"}}";
                var buf = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                ctx.Response.ContentLength64 = buf.Length;
                await ctx.Response.OutputStream.WriteAsync(buf);
                ctx.Response.Close();
            }
            catch { break; }
        }
    }

    // ================================================================ Save Settings / Exit

    void SaveSettings()
    {
        App.Config.Osc.Ip = _oscIp.Text.Trim();
        App.Config.Osc.Port = _oscPort.Text.Trim();
        App.Config.Osc.Address = _oscAddress.Text.Trim();
        App.Config.Osc.IntervalMs = _oscInterval.Text.Trim();
        App.Config.Osc.ReceivePort = _oscRecvPort.Text.Trim();
        App.Config.Osc.Template = _templateText.Text;
        App.Config.HeartRate.DisplaySource = _hrSource.SelectedItem?.ToString() ?? "平均";
        App.Config.HeartRate.Window.Format = _floatFormat;
        App.Config.HeartRate.Window.UnlockedColor = _floatUnlocked;
        App.Config.HeartRate.Window.LockedColor = _floatLocked;
        App.Config.HeartRate.Window.ImagePath = _floatImage;
        App.Config.Logs.AutoDumpEnabled = _autoDump.Checked;
        App.Config.Logs.AutoDumpIntervalMin = _dumpInterval.Text.Trim();
        App.Config.Webhook.Enabled = _webhookEnabled.Checked;
        App.Config.Save();
        App.Osc.UpdateInterval();
        App.Log.Info(LogText.L("log.main.settings_saved"));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { App.Osc.Stop(); } catch { }
        try { App.Ble.DisconnectAll(); } catch { }
        try { lock (_apiLock) { _api?.Stop(); _api = null; } } catch { }
        try { _dumpTimer?.Dispose(); } catch { }
        base.OnFormClosed(e);
    }

    // ================================================================ Control Plant

    static Label Label(string text, int leftMargin)
    {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(leftMargin, 8, 0, 0) };
    }

    static Button Button(string text, int width, Color bg)
    {
        return new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = bg,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9.5f),
            Margin = new Padding(4, 4, 4, 4),
        };
    }

    static void AddLabeledRow(TableLayoutPanel grid, int row, string l1, TextBox b1, string l2, TextBox b2, string l3, TextBox b3)
    {
        grid.Controls.Add(Label(l1, 0), 0, row);
        b1.Dock = DockStyle.Fill;
        b1.Height = 34;
        grid.Controls.Add(b1, 1, row);
        grid.Controls.Add(Label(l2, 14), 2, row);
        b2.Dock = DockStyle.Fill;
        b2.Height = 34;
        grid.Controls.Add(b2, 3, row);
        grid.Controls.Add(Label(l3, 14), 4, row);
        b3.Dock = DockStyle.Fill;
        b3.Height = 34;
        grid.Controls.Add(b3, 5, row);
    }

    private DateTime _lastDump = DateTime.Now;
    private System.Threading.Timer? _dumpTimer;
    private DateTime _lastWebhookHr = DateTime.MinValue;
    private bool _suppressSource;
    private TabControl tabs => (TabControl)Controls[0];

    void Ui(Action a)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(a); } catch { }
    }
}
