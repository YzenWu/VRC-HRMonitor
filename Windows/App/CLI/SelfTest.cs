using System.Diagnostics;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.Cli;

/// <summary>Self-test from a real user's perspective: configuration, engine, OSC, BLE scanning/connection/heart rate, sending, and hardware.
/// Constraint: the longest individual blocking operation must be at most 250 ms; asynchronous waits do not count toward the blocking budget.</summary>
public static class SelfTest
{
    public const string TargetMac = "D4:DA:C6:CE:5F:D6";
    public const long MaxBlockingMs = 250;

    sealed class Step
    {
        public required string Name;
        public required string Kind;      // blocking / wait
        public long WallMs;
        public long BlockingMs;
        public bool Ok;
        public string Detail = "";
    }

    public static async Task Run()
    {
        try { await RunCore(); }
        finally
        {
            try { App.Ble.StopScan(); } catch { }
            try { Osc.OscEngine.StopReceiver(); } catch { }
            // The test temporarily replaces the application receiver; restore normal CLI reception.
            if (!App.SafeMode) try { App.Osc.Start(); } catch { }
        }
    }

    static async Task RunCore()
    {
        var steps = new List<Step>();
        Console.WriteLine();
        Console.WriteLine(Txt.T("selftest.title"));
        Console.WriteLine(Txt.T("selftest.constraint", MaxBlockingMs));

        // 1. Load configuration (blocking)
        steps.Add(Measure("1. config 加载", "blocking", () =>
        {
            var cfg = AppConfig.Load(Path.Combine(App.BaseDir, "config.json"));
            return cfg != null ? "" : "加载失败";
        }));

        // 2. Load engine (blocking)
        steps.Add(Measure("2. osc_engine 加载", "blocking", () =>
            Osc.OscEngine.Available ? "" : "引擎不可用"));

        // 3. Start OSC receiver (blocking)
        steps.Add(Measure("3. OSC 接收启动(9001)", "blocking", () =>
        {
            Osc.OscEngine.StopReceiver();
            var ok = Osc.OscEngine.StartReceiver(App.Osc.ReceivePort, _ => { });
            return ok ? "" : "绑定失败";
        }));

        // 4. Start BLE scan (blocking)
        var tcsFound = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFound(string mac, string name, int rssi, string type)
        {
            if (mac.Equals(TargetMac, StringComparison.OrdinalIgnoreCase) && !tcsFound.Task.IsCompleted)
                tcsFound.TrySetResult(name.Length > 0 ? name : mac);
        }
        App.Ble.DeviceFound += OnFound;
        steps.Add(Measure("4. BLE 扫描启动", "blocking", () =>
        {
            App.Ble.StartScan();
            return App.Ble.Scanning ? "" : "启动失败";
        }));

        // 5. Wait to discover D4:DA:C6:CE:5F:D6 (asynchronous wait with a 25 s timeout)
        var sw = Stopwatch.StartNew();
        var foundName = "";
        try
        {
            try { foundName = await tcsFound.Task.WaitAsync(TimeSpan.FromSeconds(25)); }
            catch (TimeoutException) { }
        }
        finally { App.Ble.DeviceFound -= OnFound; }
        sw.Stop();
        steps.Add(new Step
        {
            Name = "5. 等待发现目标设备",
            Kind = "wait",
            WallMs = sw.ElapsedMilliseconds,
            BlockingMs = 0,
            Ok = foundName.Length > 0,
            Detail = $"D4:DA:C6:CE:5F:D6 ({foundName}) 发现于 {sw.Elapsed.TotalSeconds:F1}s",
        });

        if (!steps[^1].Ok)
        {
            steps.Add(new Step { Name = "6. 连接设备", Kind = "wait", WallMs = 0, BlockingMs = 0, Ok = false, Detail = "跳过（设备未发现）" });
            steps.Add(new Step { Name = "7. 等待首条心率", Kind = "wait", WallMs = 0, BlockingMs = 0, Ok = false, Detail = "跳过" });
        }
        else
        {
            // 6. Connect (asynchronous wait; record blocking and wall-clock time)
            sw.Restart();
            var tcsHr = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnHr(string mac, string name, int bpm)
            {
                if (mac.Equals(TargetMac, StringComparison.OrdinalIgnoreCase) && !tcsHr.Task.IsCompleted)
                    tcsHr.TrySetResult(bpm);
            }
            App.Ble.HeartRate += OnHr;
            bool connectOk;
            var hr = 0;
            try
            {
                connectOk = await App.Ble.ConnectAsync(TargetMac);
                var connectWall = sw.ElapsedMilliseconds;
                steps.Add(new Step
                {
                    Name = "6. 连接设备",
                    Kind = "wait",
                    WallMs = connectWall,
                    BlockingMs = 0,
                    Ok = connectOk,
                    Detail = $"连接 {(connectOk ? "成功" : "失败")}（总耗时 {connectWall}ms，含异步 GATT）",
                });

                // 7. Wait for the first heart rate reading (asynchronous wait with a 20 s timeout)
                sw.Restart();
                try { hr = await tcsHr.Task.WaitAsync(TimeSpan.FromSeconds(20)); }
                catch (TimeoutException) { }
                sw.Stop();
            }
            finally { App.Ble.HeartRate -= OnHr; }
            steps.Add(new Step
            {
                Name = "7. 等待首条心率",
                Kind = "wait",
                WallMs = sw.ElapsedMilliseconds,
                BlockingMs = 0,
                Ok = hr > 0,
                Detail = hr > 0 ? $"{hr} bpm（{sw.Elapsed.TotalSeconds:F1}s）" : "超时/无数据",
            });
        }

        // 8. Send one OSC message (blocking)
        steps.Add(Measure("8. OSC 推送一次", "blocking", () =>
        {
            var text = $"SelfTest {App.CurrentBpm} BPM";
            var ok = Osc.OscService.SendOne(App.Config.Osc.Ip,
                int.TryParse(App.Config.Osc.Port, out var p) ? p : 9000, App.Config.Osc.Address, text);
            return ok ? "" : "发送失败";
        }));

        // 9. Fast hardware collection (blocking)
        steps.Add(Measure("9. 硬件 fast 采集", "blocking", () =>
        {
            App.SysInfo.UpdateHeartRateVars();
            return App.SysInfo.Vars.ContainsKey("CPU_USAGE") ? "" : "无数据";
        }));

        // 10. Hardware registry collection (blocking)
        steps.Add(Measure("10. 硬件注册表采集", "blocking", () =>
        {
            SysInfo.Collectors.RegistryInfo.Collect(App.SysInfo.Vars);
            return App.SysInfo.Vars.ContainsKey("CPU_PRODUCT_NAME") ? "" : "无数据";
        }));

        // ---- Summary ----
        var maxBlocking = steps.Where(s => s.Kind == "blocking").Max(s => s.BlockingMs);
        var passed = steps.All(s => s.Ok) && maxBlocking <= MaxBlockingMs;
        Console.WriteLine();
        Console.WriteLine(Txt.T("selftest.detail_head"));
        foreach (var s in steps)
        {
            var mark = s.Ok ? "PASS" : "FAIL";
            var extra = s.Kind == "blocking" ? $"  阻塞={s.BlockingMs}ms" : $"  等待={s.WallMs}ms";
            Console.WriteLine($"  [{mark}] {s.Name,-22} 总耗时={s.WallMs,6}ms{extra}  {s.Detail}");
        }
        Console.WriteLine();
        Console.WriteLine(Txt.T("selftest.max_blocking", maxBlocking, MaxBlockingMs,
            maxBlocking <= MaxBlockingMs ? "OK" : Txt.T("selftest.over_limit")));
        Console.WriteLine(Txt.T("selftest.result", passed ? "PASS" : "FAIL"));
        Console.WriteLine();
    }

    static Step Measure(string name, string kind, Func<string> action)
    {
        var sw = Stopwatch.StartNew();
        var detail = "";
        var ok = true;
        try { detail = action(); ok = detail.Length == 0; }
        catch (Exception e) { ok = false; detail = e.Message; }
        sw.Stop();
        return new Step
        {
            Name = name,
            Kind = kind,
            WallMs = sw.ElapsedMilliseconds,
            BlockingMs = kind == "blocking" ? sw.ElapsedMilliseconds : 0,
            Ok = ok,
            Detail = detail,
        };
    }
}
