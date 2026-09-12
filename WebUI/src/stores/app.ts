import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { api, setUnauthorizedHandler } from '../api';
import { connectWs, onWs, stopWs, wsConnected } from '../api/ws';
import { setRecoverHandler } from '../api/watchdog';
import { hbPoints } from '../prefs';
import type { AppInfo, Device, HealthSnapshot, HrWindow, OscState, ReleaseInfo, StatusPayload } from '../types';

/** Second front-end session information (local = automatic   PH 0; remote login   PH 1  pop-in page). */
export interface RemoteInfo {
  loaded: boolean;
  remote: boolean;
  authed: boolean;
  admin: boolean;
  username: string;
  role: string;
  /** White list of plates ( PH 0 =all; local always PH 1). */
  tabs: string[] | null;
  /** P6 source zone of this session: loopback / private / public. */
  sourceZone: string;
  /** P6: the admin account still verifies against the default password and must be migrated. */
  mustChangePassword: boolean;
}

const REMOTE_INIT: RemoteInfo = { loaded: false, remote: false, authed: false, admin: false, username: '', role: 'user', tabs: null, sourceZone: 'loopback', mustChangePassword: false };

/** Global application status: state snapshot +   PH 0  real-time events (relative to the core of   PH 1 single   PH 2). */
export const useAppStore = defineStore('app', () => {
  const bpm = ref(0);
  const avg = ref(0);
  const scanning = ref(false);
  const recording = ref(false);
  const devices = ref<Device[]>([]);
  const osc = ref<OscState | null>(null);
  const hrWindow = ref<HrWindow | null>(null);
  /** Release list (  PH 0, #23):   PH 1  page  PH 2   PH 3 card. */
  const release = ref<ReleaseInfo | null>(null);
  const health = ref<HealthSnapshot | null>(null);
  const appInfo = ref<AppInfo | null>(null);
  const floatCount = ref(0);
  /** Whether or not the main suspended window (polymers) is currently open: for the main window switch button. */
  const floatMain = ref(false);
  /** Engine in safe mode (----PH 0: not automatically connected/detected/reconnected, awaiting manual operation). */
  const safeMode = ref(false);
  const engineOnline = ref(false);
  const floatIds = ref<string[]>([]);
  const curve = ref<number[]>([]);
  /** Curves keyed by source: backend main display and connected-device average. */
  const sourceCurves = ref<Record<'main' | 'avg', number[]>>({ main: [], avg: [] });
  /** Per-device curves keyed by MAC. */
  const deviceCurves = ref<Record<string, number[]>>({});
  const sysVars = ref<Record<string, string>>({});
  const logs = ref<string[]>([]);
  /** Delays in back-end return (  PH 0,   PH 1): /   PH 2   PH 3    for brand title {  PH 4). */
  const ping = ref(0);
  /** Second front-end session:   PH 0  (the layout displays the login page and disables   PH 1) when remote login is not available. */
  const remote = ref<RemoteInfo>({ ...REMOTE_INIT });

  const connectedCount = computed(() => devices.value.filter((d) => d.connected).length);
  const wsOnline = computed(() => wsConnected.value);
  const loginNeeded = computed(() => remote.value.loaded && remote.value.remote && !remote.value.authed);
  /** The current plate is accessible: permanent (non-tele) access; remote view of the White List. */
  const tabAllowed = (tab: string): boolean => {
    if (!remote.value.remote) return true; // = Manager, no restrictions
    if (!remote.value.authed) return false;
    return !remote.value.tabs || remote.value.admin || remote.value.tabs.includes(tab);
  };

  /** Pull once /   PH 0: Returning to the ring constant administrator; remote unlogged-in tag   PH 1. */
  async function checkRemote(): Promise<void> {
    try {
      const m = (await api.remoteMe()) as {
        ok?: boolean;
        remote?: boolean;
        authed?: boolean;
        username?: string;
        role?: string;
        admin?: boolean;
        tabs?: string[] | null;
        sourceZone?: string;
        mustChangePassword?: boolean;
      };
      remote.value = {
        loaded: true,
        remote: !!m.remote,
        authed: m.authed !== false,
        admin: !!m.admin,
        username: m.username ?? '',
        role: m.role ?? 'user',
        tabs: m.tabs ?? null,
        sourceZone: m.sourceZone ?? 'loopback',
        mustChangePassword: !!m.mustChangePassword,
      };
    } catch {
      // Backend unreachable/old version under host name: Backring host must be the manager (local front end is never locked)
      // Remote processing of the remainder by unlogged-in (bomb login)
      const loop = /^(127\.0\.0\.1|localhost|::1)$/.test(location.hostname);
      remote.value = loop
        ? { loaded: true, remote: false, authed: true, admin: true, username: 'local', role: 'admin', tabs: null, sourceZone: 'loopback', mustChangePassword: false }
        : { ...REMOTE_INIT, loaded: true, remote: true, tabs: [] };
    }
  }

  async function logout(): Promise<void> {
    try {
      await api.remoteLogout();
    } catch {
      /* Session may be invalid */
    }
    remote.value = { ...remote.value, authed: false, tabs: [] };
    stopWs();
  }

  /** The guard is memory protection, not a UI/configuration limit. */
  const CURVE_RESOURCE_GUARD = 1_000_000;
  const curveCap = () => Math.min(Math.max(Math.floor(hbPoints.value) || 1, 1), CURVE_RESOURCE_GUARD);

  function appendCurve(arr: number[], v: number): void {
    if (!(v > 0)) return;
    arr.push(v);
    const cap = curveCap();
    if (arr.length > cap) arr.splice(0, arr.length - cap);
  }

  /** Add one point to the legacy main curve used by Overview. */
  function pushCurve(v: number): void {
    appendCurve(curve.value, v);
  }

  function pushSourceCurve(source: 'main' | 'avg', v: number): void {
    appendCurve(sourceCurves.value[source], v);
  }

  /** Add a point to one device curve. */
  function pushDeviceCurve(mac: string, v: number): void {
    if (!mac) return;
    const map = deviceCurves.value;
    appendCurve(map[mac] ?? (map[mac] = []), v);
  }

  /**
   * Current values by data source:
   *   PH 0   = the heart rate of the backend (following the main float window data source),   PH 1 = the average of the installed device, and the rest press   PH 2  to take the device.
   */
  function bpmOf(source: string): number {
    if (source === 'avg') return avg.value;
    if (source === 'main' || source === '') return bpm.value;
    return devices.value.find((d) => d.mac === source)?.bpm ?? 0;
  }

  /** Return the actual history for the selected source. */
  function curveOf(source: string): number[] {
    if (source === 'avg') return sourceCurves.value.avg;
    if (source === 'main' || source === '') return sourceCurves.value.main;
    return deviceCurves.value[source] ?? [];
  }

  /** Curve window statistics (Twenty-seventh round #6):   PH 0, only >0; no data returns   PH 1.
   * Main card /   PH 0  / Device details bullet windows are shared with a calibre consistent with the current visible curve. */
  function statsOf(source: string): { min: number; avg: number; max: number; count: number } | null {
    const vals = curveOf(source).filter((v) => v > 0);
    if (vals.length === 0) return null;
    let mn = Infinity;
    let mx = -Infinity;
    let sum = 0;
    for (const v of vals) {
      if (v < mn) mn = v;
      if (v > mx) mx = v;
      sum += v;
    }
    return { min: mn, avg: Math.round(sum / vals.length), max: mx, count: vals.length };
  }

  async function sync(): Promise<void> {
    const t0 = (globalThis.performance?.now?.() ?? Date.now());
    try {
      const s = (await api.status()) as unknown as StatusPayload;
      const dt = (globalThis.performance?.now?.() ?? Date.now()) - t0;
      // Pre-value weights are higher to avoid   PH 0 sjumping in the heading of single shaking
      ping.value = ping.value > 0 ? Math.round((ping.value * 3 + dt) / 4) : Math.round(dt);
      bpm.value = s.bpm ?? 0;
      avg.value = s.avg ?? 0;
      scanning.value = !!s.scanning;
      recording.value = !!s.recording;
      devices.value = s.devices ?? [];
      osc.value = s.osc ?? null;
      hrWindow.value = s.hr?.window ?? null;
      release.value = s.release ?? null;
      health.value = s.health ?? null;
      appInfo.value = s.app ?? null;
      floatCount.value = s.floatCount ?? 0;
      floatMain.value = !!s.floatMain;
      floatIds.value = Array.isArray(s.floatIds) ? (s.floatIds as string[]) : [];
      safeMode.value = !!s.safeMode;
      engineOnline.value = true;
      // PH 0 Purpose points with a round query to ensure that the curve is not empty ( PH 1  driven by   PH 2  event on-line)
      if (!wsConnected.value) {
        pushCurve(bpm.value);
        pushSourceCurve('main', bpm.value);
        pushSourceCurve('avg', avg.value);
        for (const d of devices.value) if (d.connected) pushDeviceCurve(d.mac, d.bpm);
      }
    } catch {
      engineOnline.value = false;
    }
  }

  async function refreshDevices(): Promise<void> {
    try {
      devices.value = (await api.devices()) as unknown as Device[];
    } catch {
      /* Keep old list */
    }
  }

  /** Reread   PH 0 (version /   PH 1 state, etc.) as soon as   PH 2 PH 3 is finished. */
  async function pullInfo(): Promise<void> {
    try {
      const cfg = (await api.config()) as { app?: AppInfo };
      if (cfg.app) appInfo.value = cfg.app;
    } catch {
      /* Keep old values when engines are not available */
    }
  }

  /** Scan period   PH 0  multibars per second to be merged into   PH 1  to refresh the list once. */
  let devTimer: number | null = null;
  function refreshDevicesSoon(): void {
    if (devTimer !== null) return;
    devTimer = window.setTimeout(() => {
      devTimer = null;
      void refreshDevices();
    }, 300);
  }

  /** Hardware variable refreshed ( PH 0 incident driven, PH 1 consolidated). */
  let hwTimer: number | null = null;
  function refreshHwSoon(): void {
    if (hwTimer !== null) return;
    hwTimer = window.setTimeout(() => {
      hwTimer = null;
      void api
        .hw()
        .then((r) => {
          const v = (r as { vars?: Record<string, string> }).vars;
          if (v) sysVars.value = v;
        })
        .catch(() => {
          /* Keep old values when engines are not available */
        });
    }, 1000);
  }

  let started = false;
  /** Consumer of   PH 0 incident (registered by   PH 1  PH 2, avoiding   PH 3  recycling dependence). */
  let sysThemeHandler: ((t: { dark?: boolean; accent?: string }) => void) | null = null;
  function onSysTheme(h: (t: { dark?: boolean; accent?: string }) => void): void {
    sysThemeHandler = h;
  }

  /** Initial mount call: Discover remote session   PH 0 +subscription event + pull a snapshot.
      远程未登录时只登记事件订阅，不连 WS（登录页接管，避免对 401 反复重连）。 */
  async function start(): Promise<void> {
    if (started) return;
    started = true;

    // Session invalid (tele) return to login and disconnect  PH 0
    setUnauthorizedHandler(() => {
      if (!remote.value.remote) return;
      remote.value = { ...remote.value, authed: false, tabs: [] };
      stopWs();
    });
    // Reconnect the backend with   PH 0... and fill the snapshot immediately.
    setRecoverHandler(() => {
      if (!loginNeeded.value) {
        connectWs();
        void sync();
      }
    });

    // Event load is the   PH 1 inner layer of   PH 2 unopened envelope of   PH 0
    onWs('heart_rate', (d) => {
      const m = d as { mac?: string; bpm?: number; mainBpm?: number; avg?: number; notifyHz?: number; reports?: number } | null;
      const v = Number(m?.bpm ?? 0);
      const nextMain = Number(m?.mainBpm ?? v);
      const nextAvg = Number(m?.avg ?? 0);
      if (v > 0) {
        bpm.value = nextMain > 0 ? nextMain : v;
        if (nextAvg > 0) avg.value = nextAvg;
        pushCurve(bpm.value);
        pushSourceCurve('main', bpm.value);
        pushSourceCurve('avg', avg.value);
      }
      const mac = String(m?.mac ?? '');
      if (mac) {
        const dev = devices.value.find((x) => x.mac === mac);
        if (dev) {
          dev.bpm = v;
          if (typeof m?.notifyHz === 'number') dev.notifyHz = m.notifyHz;
          if (typeof m?.reports === 'number') dev.reports = m.reports;
        }
        pushDeviceCurve(mac, v);
      }
    });
    onWs('devices', (d) => {
      // PH 0 incident   PH 1  is in itself the number of devices Group
      if (Array.isArray(d)) devices.value = d as Device[];
      else refreshDevicesSoon();
    });
    onWs('device_found', () => refreshDevicesSoon());
    // PH 0 Queries Change
    onWs('connecting', () => refreshDevicesSoon());
    onWs('connected', () => void sync());
    onWs('disconnected', () => void sync());
    onWs('scan', (d) => {
      scanning.value = !!(d as { scanning?: boolean } | null)?.scanning;
    });
    onWs('osc_status', (d) => {
      const m = d as Partial<OscState> | null;
      if (osc.value && m) osc.value = { ...osc.value, ...m };
    });
    onWs('health_status', (d) => {
      const m = d as Partial<HealthSnapshot> | null;
      if (health.value && m) health.value = { ...health.value, ...m };
      else if (m) void sync();
    });
    onWs('sysinfo', () => refreshHwSoon());
    // PH 0 4: Surviving window additions and subtractions (including tray/right/   PH 1  local entrances)
    onWs('float', () => void sync());
    onWs('sysinfo_vars', (d) => {
      const v = (d as { vars?: Record<string, string> } | null)?.vars;
      if (v) sysVars.value = { ...sysVars.value, ...v };
    });
    // PH 0  Deep/Sweet Change (post-referral registration form broadcast): redraw the theme of the following system accordingly
    onWs('sys_theme', (d) => {
      const m = d as { dark?: boolean; accent?: string } | null;
      if (m) sysThemeHandler?.(m);
    });
    onWs('log', (d) => {
      const line = String((d as { line?: string } | null)?.line ?? '');
      if (line) logs.value = [...logs.value.slice(-999), line];
    });

    // Whether a rough second front-end by host name: a direct chain of return (locally available), remote etc.   PH 0  until confirmed
    const hostRemote = !/^(127\.0\.0\.1|localhost|::1)$/.test(location.hostname);
    if (!hostRemote)
    {
      connectWs();
      void sync();
      window.setInterval(() => void sync(), 5000);
      void checkRemote();
      return;
    }
    await checkRemote();
    if (loginNeeded.value) return; // Remote not login: not even  PH 0, no question-and-answer, after successful login page  PH 1
    connectWs();
    void sync();
    window.setInterval(() => void sync(), 5000);
  }

  /** Login access (PH 0 Part Call): */
  async function afterRemoteLogin(): Promise<void> {
    await checkRemote();
    if (loginNeeded.value) return;
    connectWs();
    void sync();
  }

  return {
    bpm,
    avg,
    scanning,
    recording,
    devices,
    osc,
    hrWindow,
    release,
    health,
    appInfo,
    floatCount,
    floatMain,
    floatIds,
    safeMode,
    engineOnline,
    curve,
    sourceCurves,
    deviceCurves,
    sysVars,
    logs,
    ping,
    remote,
    loginNeeded,
    tabAllowed,
    connectedCount,
    wsOnline,
    bpmOf,
    curveOf,
    statsOf,
    sync,
    refreshDevices,
    pullInfo,
    onSysTheme,
    checkRemote,
    logout,
    afterRemoteLogin,
    start,
  };
});
