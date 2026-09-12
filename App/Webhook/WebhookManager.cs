using System.Net.Http;
using System.Text;
using System.Text.Json;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.Webhook;

/// <summary>XQ1QXZ sent (read/write back config_webhook.json). </summary>
public class WebhookManager
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    public List<JsonElement> Webhooks { get; private set; } = new();
    private string _path;

    public WebhookManager(string path)
    {
        _path = path;
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(_path));
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    Webhooks = doc.RootElement.Clone().EnumerateArray().ToList();
            }
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.webhook.load_fail", e.Message));
        }
    }

    public void Save()
    {
        try
        {
            var arr = new System.Text.Json.Nodes.JsonArray();
            foreach (var w in Webhooks) arr.Add(JsonNode(w));
            File.WriteAllText(_path, arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch (Exception e)
        {
            App.Log.Error(LogText.L("log.webhook.save_fail", e.Message));
        }
    }

    static System.Text.Json.Nodes.JsonNode? JsonNode(JsonElement el) => System.Text.Json.Nodes.JsonNode.Parse(el.GetRawText());

    /// <summary> incident trigger: connected / disconnected / heart_rate_updated. </summary>
    public void Trigger(string eventType, int heartRate)
    {
        if (App.SafeMode) return;
        var eventDesc = eventType switch
        {
            "connected" => "设备已连接",
            "disconnected" => "设备已断开",
            _ => $"心率刷新: {heartRate}bpm",
        };
        foreach (var w in Webhooks)
        {
            try
            {
                if (!(w.TryGetProperty("enabled", out var en) && en.ValueKind != JsonValueKind.False)) continue;
                var triggers = new List<string> { "heart_rate_updated" };
                if (w.TryGetProperty("triggers", out var tg) && tg.ValueKind == JsonValueKind.Array)
                    triggers = tg.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (!triggers.Contains(eventType)) continue;

                var body = w.TryGetProperty("body", out var bE) ? bE.GetString() ?? "{}" : "{}";
                body = body.Replace("{event}", eventDesc);
                _ = Task.Run(() => Send(w, heartRate, body));
            }
            catch { }
        }
    }

    /// <summary> Test Send: Return (ok, statusCode?, response). </summary>
    public (bool ok, int? status, string response, string? error) Test(JsonElement config)
    {
        if (App.SafeMode) return (false, null, "", "safe mode blocks external webhook requests");
        try
        {
            var (url, headers, body) = Build(config, 88, $"心率刷新: 88bpm");
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            using var resp = _http.Send(req);
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            return ((int)resp.StatusCode is >= 200 and < 400, (int)resp.StatusCode, text, null);
        }
        catch (Exception e)
        {
            return (false, null, "", e.Message);
        }
    }

    void Send(JsonElement config, int heartRate, string body)
    {
        try
        {
            var (url, headers, finalBody) = Build(config, heartRate, body);
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var (k, v) in headers) req.Headers.TryAddWithoutValidation(k, v);
            req.Content = new StringContent(finalBody, Encoding.UTF8, "application/json");
            using var resp = _http.Send(req);
            App.Log.Debug($"webhook '{NameOf(config)}' -> {(int)resp.StatusCode}");
        }
        catch (Exception e)
        {
            App.Log.Error($"webhook '{NameOf(config)}' fail: {e.Message}");
        }
    }

    static string NameOf(JsonElement c)
        => c.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

    static (string url, Dictionary<string, string> headers, string body) Build(JsonElement config, int heartRate, string body)
    {
        var bpmStr = heartRate > 0 ? heartRate.ToString() : "N/A";
        // {bpm} Replace and then give SysInfo to render the rest of {VAR} (same set of variables as OSC templates)
        var url = Vars((config.TryGetProperty("url", out var uE) ? uE.GetString() ?? "" : "").Replace("{bpm}", bpmStr));

        var headers = new Dictionary<string, string>();
        if (config.TryGetProperty("headers", out var hE) && hE.GetString() is { Length: > 0 } hs)
        {
            try
            {
                using var doc = JsonDocument.Parse(hs.Replace("{bpm}", bpmStr));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    headers[prop.Name] = Vars(prop.Value.ToString());
            }
            catch { }
        }
        if (!headers.ContainsKey("User-Agent")) headers["User-Agent"] = "HeartRateMonitor-Webhook";
        if (!headers.ContainsKey("Content-Type")) headers["Content-Type"] = "application/json";

        var finalBody = Vars(body.Replace("{bpm}", bpmStr));
        return (url, headers, finalBody);
    }

    /// <summary> Render system information placeholder; returns directly when none {to avoid unnecessary running through. </summary>
    static string Vars(string text)
        => text.Contains('{') ? App.SysInfo.FormatTemplate(text) : text;
}
