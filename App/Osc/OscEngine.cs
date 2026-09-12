using System.Runtime.InteropServices;
using System.Text;

namespace HeartRateMonitor.Osc;

/// <summary>XQ1QXZ (C Engine) P/Invoke Packaging. </summary>
public static class OscEngine
{
    const string Dll = "osc_engine.dll";

    public static readonly bool Available = Load();

    static bool Load()
    {
        try { return osc_engine_version() > 0; }
        catch { return false; }
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_version();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_send_text([MarshalAs(UnmanagedType.LPUTF8Str)] string ip, int port,
                                           [MarshalAs(UnmanagedType.LPUTF8Str)] string address,
                                           [MarshalAs(UnmanagedType.LPUTF8Str)] string text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_send_chatbox([MarshalAs(UnmanagedType.LPUTF8Str)] string ip, int port,
                                              [MarshalAs(UnmanagedType.LPUTF8Str)] string address,
                                              [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
                                              int immediate, int sound);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_send([MarshalAs(UnmanagedType.LPUTF8Str)] string ip, int port,
                                      byte[] data, int len);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_send_bool([MarshalAs(UnmanagedType.LPUTF8Str)] string ip, int port,
                                           [MarshalAs(UnmanagedType.LPUTF8Str)] string address, int value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_send_float([MarshalAs(UnmanagedType.LPUTF8Str)] string ip, int port,
                                            [MarshalAs(UnmanagedType.LPUTF8Str)] string address, float value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_decode_to_json(byte[] data, int len, StringBuilder outBuf, int outCap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReceiveFn(IntPtr data, int len, IntPtr user);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern int osc_engine_start_receiver(int port, ReceiveFn cb, IntPtr user);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    static extern void osc_engine_stop_receiver();

    static ReceiveFn? _cbHolder;   // Prevent commissioning GC

    /// <summary> text Quick Path: Send a formatted template to the target. </summary>
    public static bool SendText(string ip, int port, string address, string text)
    {
        try { return osc_engine_send_text(ip, port, address, text) == 0; }
        catch { return false; }
    }

    /// <summary>
    /// /chatbox/input for special: text + send immediately + hint two booleans.
    /// immediate must be true, otherwise VRChat only fills in the input box and needs to be confirmed by the user by article.
    /// </summary>
    public static bool SendChatbox(string ip, int port, string address, string text, bool immediate = true, bool sound = false)
    {
        try { return osc_engine_send_chatbox(ip, port, address, text, immediate ? 1 : 0, sound ? 1 : 0) == 0; }
        catch { return false; }
    }

    public static bool SendRaw(string ip, int port, byte[] data)
    {
        try { return osc_engine_send(ip, port, data, data.Length) == 0; }
        catch { return false; }
    }

    /// <summary>Sends a typed boolean OSC argument for endpoints such as VRChat avatar parameters and `/chatbox/typing`.</summary>
    public static bool SendBool(string ip, int port, string address, bool value)
    {
        try { return osc_engine_send_bool(ip, port, address, value ? 1 : 0) == 0; }
        catch { return false; }
    }

    /// <summary> type parameters are written (#32): float →, f (/avatar/parameters numerical parameters). </summary>
    public static bool SendFloat(string ip, int port, string address, float value)
    {
        try { return osc_engine_send_float(ip, port, address, value) == 0; }
        catch { return false; }
    }

    /// <summary> decoder package is JSON text (C engine). </summary>
    public static string DecodeToJson(byte[] data)
    {
        try
        {
            var sb = new StringBuilder(65536);
            osc_decode_to_json(data, data.Length, sb, sb.Capacity);
            return sb.ToString();
        }
        catch { return "[]"; }
    }

    /// <summary> initiates the receiving thread; back-to-back is performed on C (self-interrupted). </summary>
    public static bool StartReceiver(int port, Action<byte[]> onPacket)
    {
        try
        {
            if (_cbHolder != null) return true;
            _cbHolder = (data, len, user) =>
            {
                if (len <= 0) return;
                var buf = new byte[len];
                Marshal.Copy(data, buf, 0, len);
                onPacket(buf);
            };
            return osc_engine_start_receiver(port, _cbHolder, IntPtr.Zero) == 0;
        }
        catch { return false; }
    }

    public static void StopReceiver()
    {
        try { osc_engine_stop_receiver(); } catch { }
        _cbHolder = null;
    }
}
