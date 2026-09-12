using System.Text.Json;

namespace HeartRateMonitor.UI;

/// <summary>Webhook Edit Window. </summary>
public class WebhookWindow : Form
{
    private readonly TextBox _name = new();
    private readonly TextBox _url = new();
    private readonly CheckBox _enabled = new() { Text = "启用", Checked = true };
    private readonly TextBox _triggers = new();
    private readonly TextBox _headers = new() { Multiline = true, Height = 90, Font = new Font("Consolas", 9) };
    private readonly TextBox _body = new() { Multiline = true, Height = 110, Font = new Font("Consolas", 9) };
    private readonly System.Text.Json.Nodes.JsonObject? _original;

    public System.Text.Json.Nodes.JsonObject? Result;

    public WebhookWindow(System.Text.Json.Nodes.JsonObject? original = null)
    {
        _original = original;
        Text = original == null ? "新增 Webhook" : "编辑 Webhook";
        Width = 520;
        Height = 560;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowCount = 10;

        Add(grid, 0, "名称:", _name, "webhook-1");
        Add(grid, 1, "URL:", _url, "https://example.com/hook");
        grid.Controls.Add(_enabled, 1, 2);

        Add(grid, 3, "触发(逗号分隔):", _triggers, "heart_rate_updated");
        Add(grid, 4, "Headers(JSON):", _headers, "");
        Add(grid, 5, "Body(JSON):", _body, "{\"heart_rate\": {bpm}}");

        var btnRow = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var ok = new Button { Text = "确定", Width = 80 };
        var cancel = new Button { Text = "取消", Width = 80 };
        ok.Click += (_, _) => { Save(); DialogResult = DialogResult.OK; };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        btnRow.Controls.Add(cancel);
        btnRow.Controls.Add(ok);
        grid.Controls.Add(btnRow, 0, 7);
        grid.SetColumnSpan(btnRow, 2);

        Controls.Add(grid);
        if (_original != null)
        {
            _name.Text = _original["name"]?.GetValue<string>() ?? "";
            _url.Text = _original["url"]?.GetValue<string>() ?? "";
            _enabled.Checked = _original["enabled"]?.GetValue<bool>() ?? true;
            _triggers.Text = _original["triggers"] is var t && t != null
                ? string.Join(",", t.AsArray().Select(x => x?.GetValue<string>()))
                : "heart_rate_updated";
            _headers.Text = _original["headers"]?.GetValue<string>() ?? "";
            _body.Text = _original["body"]?.GetValue<string>() ?? "{}";
        }
    }

    static void Add(TableLayoutPanel grid, int row, string label, TextBox box, string placeholder)
    {
        grid.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        box.Dock = DockStyle.Fill;
        grid.Controls.Add(box, 1, row);
    }

    void Save()
    {
        var triggers = new System.Text.Json.Nodes.JsonArray();
        foreach (var t in _triggers.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            triggers.Add(t);
        var obj = new System.Text.Json.Nodes.JsonObject
        {
            ["name"] = _name.Text,
            ["url"] = _url.Text,
            ["enabled"] = _enabled.Checked,
            ["triggers"] = triggers,
            ["headers"] = _headers.Text,
            ["body"] = _body.Text,
        };
        Result = obj;
    }
}
