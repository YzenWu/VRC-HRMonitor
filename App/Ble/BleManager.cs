using System.Collections.Concurrent;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using HeartRateMonitor.Core;

namespace HeartRateMonitor.Ble;

/// <summary>BLE scanning, connection, and heart rate subscription via the standard Windows.Devices.Bluetooth API.</summary>
public class BleManager
{
    public static readonly Guid HeartRateServiceUuid = new("0000180d-0000-1000-8000-00805f9b34fb");
    public static readonly Guid HeartRateCharUuid = new("00002a37-0000-1000-8000-00805f9b34fb");

    /// <summary>Connection result used by auto-detection to distinguish a device without a heart rate characteristic from a failed connection attempt.</summary>
    public enum ConnectResult
    {
        Ok,
        /// <summary>GATT is accessible but has no 0x2A37 characteristic, so this device can never report heart rate and may be skipped permanently.</summary>
        NoHeartRate,
        /// <summary>Retryable failure such as a timeout, pairing restriction, or a device that is no longer advertising.</summary>
        Failed,
    }

    public sealed class DeviceState
    {
        public required string Mac;
        public required string Name;
        public string Type = "BLE Device";
        public bool Connected;
        public int Bpm;
        public BluetoothLEDevice? Device;
        public GattCharacteristic? Characteristic;
    }

    private readonly ConcurrentDictionary<string, DeviceState> _devices = new();
    private BluetoothLEAdvertisementWatcher? _watcher;
    private readonly ConcurrentDictionary<ulong, string> _pendingName = new();
    /// <summary>Devices currently connecting, mapped from MAC to starting tick; the UI uses this to show a solid connecting indicator.</summary>
    private readonly ConcurrentDictionary<string, long> _connecting = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _scanLock = new();
    public volatile bool Scanning;

    public event Action? ScanStarted;
    public event Action? ScanStopped;
    public event Action<string, string, int, string>? DeviceFound;   // mac, name, rssi, type
    public event Action<string, string>? Connecting;                  // mac, name
    public event Action<string, string>? Connected;                   // mac, name
    public event Action<string, string, bool>? Disconnected;          // mac, name, manual
    public event Action<string, string, int>? HeartRate;              // mac, name, bpm
    public event Action<string>? Error;                               // message

    /// <summary>Whether a ConnectAsync operation has started but has not yet completed.</summary>
    public bool IsConnecting(string mac) => _connecting.ContainsKey(mac);

    /// <summary>Filter for valid names: empty, UNKNOWN, and values equal to the MAC all mean that no identifier was obtained.</summary>
    static string? Named(string? name, string mac)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var n = name.Trim();
        return n == "UNKNOWN" || n.Equals(mac, StringComparison.OrdinalIgnoreCase) ? null : n;
    }

    static string MacOf(ulong addr)
    {
        var b = new byte[6];
        for (var i = 0; i < 6; i++) b[5 - i] = (byte)(addr >> (8 * i));
        return string.Join(":", b.Select(x => x.ToString("X2")));
    }

    // ------------------------------------------------------------------ Scanning
    public void StartScan(int? durationSec = null)
    {
        if (App.SafeMode) return;
        lock (_scanLock)
        {
            if (_watcher != null) return;
            HrmTrace.Event("ble.start_scan", durationSec?.ToString() ?? "∞");
            _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Start();
            Scanning = true;
            ScanStarted?.Invoke();
            App.Log.Info(LogText.L("log.ble.scan_start"));
            if (durationSec is > 0)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(durationSec.Value * 1000);
                    StopScan();
                });
            }
        }
    }

    public void StopScan()
    {
        lock (_scanLock)
        {
            if (_watcher == null) return;
            HrmTrace.Event("ble.stop_scan");
            _watcher.Stop();
            _watcher.Received -= OnAdvertisementReceived;
            _watcher = null;
            Scanning = false;
            ScanStopped?.Invoke();
            App.Log.Info(LogText.L("log.ble.scan_stop"));
        }
    }

    private async void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        var mac = MacOf(args.BluetoothAddress);
        var rssi = (int)args.RawSignalStrengthInDBm;
        var adv = args.Advertisement;
        var name = adv.LocalName?.Trim() ?? "";
        if (name.Length == 0 && !_pendingName.ContainsKey(args.BluetoothAddress))
        {
            _pendingName[args.BluetoothAddress] = mac;
            name = await ResolveNameAsync(args.BluetoothAddress) ?? "";
        }
        var type = GuessType(adv);
        DeviceFound?.Invoke(mac, name.Length > 0 ? name : "UNKNOWN", rssi, type);
    }

    private async Task<string?> ResolveNameAsync(ulong addr)
    {
        try
        {
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
            if (device != null)
            {
                var name = device.Name;
                if (device.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
                    device.Dispose();
                return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            }
        }
        catch (Exception e)
        {
            App.Log.Debug(LogText.L("log.ble.resolve_name_fail", MacOf(addr), e.Message));
        }
        return null;
    }

    static string GuessType(BluetoothLEAdvertisement adv)
    {
        var uuids = adv.ServiceUuids.ToList();
        if (uuids.Contains(HeartRateServiceUuid)) return "Heart Rate Monitor";
        if (uuids.Count > 0)
        {
            var parts = uuids.Take(2).Select(u => u.ToString()[4..8].ToUpperInvariant());
            return "BLE (" + string.Join(", ", parts) + ")";
        }
        return "BLE Device";
    }

    // ------------------------------------------------------------------ Connection
    /// <summary>Connect and subscribe to heart rate; retained for compatibility with callers that use a Boolean connected/not-connected result.</summary>
    public async Task<bool> ConnectAsync(string mac) => await TryConnectAsync(mac) == ConnectResult.Ok;

    /// <summary>Connect and subscribe to heart rate, returning a detailed result so auto-detection can permanently skip devices without a heart rate characteristic.</summary>
    public async Task<ConnectResult> TryConnectAsync(string mac)
    {
        if (App.SafeMode) return ConnectResult.Failed;
        if (_devices.TryGetValue(mac, out var existing) && existing.Connected) return ConnectResult.Ok;
        var state = _devices.GetOrAdd(mac, _ => new DeviceState { Mac = mac, Name = mac });
        // Name precedence: existing state, cached advertised name from the registry, then MAC.
        // Falling back directly to the MAC previously caused the list name to change to the MAC after connection.
        state.Name = Named(existing?.Name, mac) ?? Named(App.Devices?.Get(mac)?.Name, mac) ?? mac;
        // Register the connecting state on entry and always remove it in finally.
        _connecting[mac] = Environment.TickCount64;
        Connecting?.Invoke(mac, state.Name);
        App.Log.Info(LogText.L("log.ble.connecting", state.Name, mac));
        try
        {
            var addr = ParseMac(mac);
            var device = await BluetoothLEDevice.FromBluetoothAddressAsync(addr);
            if (device == null) throw new Exception(LogText.L("log.ble.device_missing"));
            state.Device = device;

            GattDeviceServicesResult services;
            try
            {
                services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            }
            catch
            {
                services = await device.GetGattServicesAsync();
            }

            if (services.Status != GattCommunicationStatus.Success)
            {
                App.Log.Info(LogText.L("log.ble.gatt_limited", mac));
                await TryPairAsync(device);
                services = await device.GetGattServicesAsync(BluetoothCacheMode.Uncached);
            }
            if (services.Status != GattCommunicationStatus.Success)
                throw new Exception(LogText.L("log.ble.gatt_status", services.Status));

            GattCharacteristic? hrChar = null;
            foreach (var svc in services.Services)
            {
                if (svc.Uuid != HeartRateServiceUuid) continue;
                var chars = await svc.GetCharacteristicsAsync();
                foreach (var ch in chars.Characteristics)
                {
                    if (ch.Uuid == HeartRateCharUuid || ch.UserDescription.Contains("heart rate", StringComparison.OrdinalIgnoreCase) ||
                        ch.UserDescription.Contains("hr", StringComparison.OrdinalIgnoreCase))
                    {
                        hrChar = ch;
                        break;
                    }
                }
                if (hrChar != null) break;
            }
            if (hrChar == null)
            {
                // GATT is readable but has no 0x2A37 characteristic, so this device cannot report heart rate; let the caller skip it permanently.
                device.Dispose();
                _devices.TryRemove(mac, out _);
                Error?.Invoke(LogText.L("log.ble.no_hr_char_evt", mac));
                App.Log.Warn(LogText.L("log.ble.no_hr_char", state.Name, mac));
                return ConnectResult.NoHeartRate;
            }

            var result = await hrChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                GattClientCharacteristicConfigurationDescriptorValue.Notify);
            if (result != GattCommunicationStatus.Success)
            {
                device.Dispose();
                throw new Exception(LogText.L("log.ble.subscribe_fail", result));
            }

            state.Characteristic = hrChar;
            hrChar.ValueChanged += OnHeartRateValueChanged;
            state.Connected = true;
            state.Name = Named(device.Name, mac) ?? state.Name;
            // A successful connection proves the heart rate service exists. Write the system name obtained during the connection back to the registry.
            // The device stops advertising after connection; if the connection precedes a named advertisement, the registry name/type stays empty and the default Bluetooth icon remains.
            App.Devices?.OnGattVerified(mac, state.Name);
            device.ConnectionStatusChanged += OnConnectionStatusChanged;
            Connected?.Invoke(mac, state.Name);
            App.Log.Info(LogText.L("log.ble.connected", state.Name, mac));
            return ConnectResult.Ok;
        }
        catch (Exception e)
        {
            Error?.Invoke($"{mac}: {e.Message}");
            App.Log.Error(LogText.L("log.ble.connect_fail", mac, e.Message));
            return ConnectResult.Failed;
        }
        finally
        {
            _connecting.TryRemove(mac, out _);
        }
    }

    static async Task TryPairAsync(BluetoothLEDevice device)
    {
        try
        {
            var pairing = device.DeviceInformation.Pairing;
            if (pairing.CanPair && !pairing.IsPaired)
                await pairing.PairAsync(Windows.Devices.Enumeration.DevicePairingProtectionLevel.None);
        }
        catch { }
    }

    static ulong ParseMac(string mac)
    {
        var hex = mac.Replace(":", "").Replace("-", "");
        return Convert.ToUInt64(hex, 16);
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            var mac = MacOf(sender.BluetoothAddress);
            CleanupDevice(mac, manual: false);
        }
    }

    private void OnHeartRateValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        try
        {
            var mac = _devices.FirstOrDefault(kv => kv.Value.Characteristic == sender).Key;
            if (mac == null) return;
            var state = _devices[mac];
            var reader = DataReader.FromBuffer(args.CharacteristicValue);
            var bytes = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(bytes);
            if (bytes.Length < 2) return;
            var flags = bytes[0];
            var bpm = (flags & 0x01) != 0 ? BitConverter.ToUInt16(bytes, 1) : bytes[1];
            if (bpm == 0) return;
            state.Bpm = bpm;
            HeartRate?.Invoke(mac, state.Name, bpm);
        }
        catch (Exception e)
        {
            App.Log.Debug(LogText.L("log.ble.hr_parse_fail", e.Message));
        }
    }

    // ------------------------------------------------------------------ Disconnection
    public void Disconnect(string mac, bool manual = true)
    {
        var state = _devices.GetValueOrDefault(mac);
        if (state == null) return;
        CleanupDevice(mac, manual);
    }

    public void DisconnectAll()
    {
        // Last step before a normal exit from the main window or tray: snapshot the devices still connected in this session
        // to config.Devices.LastConnected so AutoConnectLast can reconnect all of them on the next launch.
        // Previously, only the first History entry was connected, losing devices in multi-device scenarios.
        try { App.Devices.SnapshotSessionDevices(); } catch { /* A snapshot failure must not prevent exit. */ }
        foreach (var mac in _devices.Keys.ToList()) CleanupDevice(mac, manual: true);
    }

    private void CleanupDevice(string mac, bool manual)
    {
        if (!_devices.TryRemove(mac, out var state)) return;
        try
        {
            if (state.Characteristic != null)
            {
                state.Characteristic.ValueChanged -= OnHeartRateValueChanged;
                _ = state.Characteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            if (state.Device != null)
            {
                state.Device.ConnectionStatusChanged -= OnConnectionStatusChanged;
                state.Device.Dispose();
            }
        }
        catch { }
        state.Connected = false;
        state.Bpm = 0;
        Disconnected?.Invoke(mac, state.Name, manual);
        App.Log.Info(LogText.L("log.ble.disconnected", state.Name, mac));
    }

    // ------------------------------------------------------------------ State snapshot
    public List<DeviceState> ConnectedDevices() => _devices.Values.Where(d => d.Connected).ToList();

    public Dictionary<string, DeviceState> AllDevices() => new(_devices);

    public DeviceState? Get(string mac) => _devices.GetValueOrDefault(mac);

    public int AverageBpm()
    {
        var values = _devices.Values.Where(d => d.Connected && d.Bpm > 0).Select(d => d.Bpm).ToList();
        return values.Count > 0 ? (int)Math.Round(values.Average()) : 0;
    }

    public bool AnyConnected() => _devices.Values.Any(d => d.Connected);
}
