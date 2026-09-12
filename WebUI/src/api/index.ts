/** The backend   PH 0 the contract (consistent with   PH 1   PH 2; the same-source relative path,   PH 3  was forwarded by   PH 4   PH 5  to 9460). */
import { notifyDown, notifyUp } from './watchdog';

/** The session is invalidated by a backend (401, data interface only; /   PH 0  is not triggered by the login process itself). */
let unauthorizedHandler: (() => void) | null = null;
export function setUnauthorizedHandler(h: (() => void) | null): void {
  unauthorizedHandler = h;
}

/** Part of a remote session fingerprint: Time zone/language to the back end of the requested hair (established at login and must be consistent thereafter). */
function extraHeaders(): Record<string, string> {
  const h: Record<string, string> = { 'X-Hrm-Tz': String(-new Date().getTimezoneOffset()) };
  try {
    h['X-Hrm-Lang'] = navigator.language || '';
  } catch {
    h['X-Hrm-Lang'] = '';
  }
  return h;
}

async function req<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
  let res: Response;
  const headers = { ...extraHeaders() } as Record<string, string>;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  try {
    res = await fetch(path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
    });
  } catch (e) {
    // Network layer failed (back-end process is off / port off) trigger offline mask and reconnect
    notifyDown(e instanceof Error ? `${method} ${path}: ${e.message}` : `${method} ${path}`);
    throw e;
  }
  if (!res.ok) {
    // 5 PH 0  Considers backend anomaly, 4 PH 1 is business error not offline State
    if (res.status >= 500) notifyDown(`${method} ${path} → ${res.status}`);
    if (res.status === 401 && !path.startsWith('/api/remote/')) unauthorizedHandler?.();
    throw new Error(`${method} ${path} → ${res.status}`);
  }
  notifyUp();
  const text = await res.text();
  return (text ? JSON.parse(text) : {}) as T;
}

const get = <T>(p: string) => req<T>('GET', p);
const post = <T>(p: string, b?: unknown) => req<T>('POST', p, b);

export interface Ok {
  ok?: boolean;
  [k: string]: unknown;
}

export interface RecordingConfig {
  ok: boolean;
  recording: boolean;
  directory: string;
  backends: string[];
  enabled: {
    avatar: boolean;
    vrchat: boolean;
    devices: boolean;
    heartRate: boolean;
    hardware: boolean;
  };
  retentionDays: {
    avatar: number;
    vrchat: number;
    devices: number;
    heartRate: number;
    hardware: number;
  };
}

export type RecordingConfigPatch = Pick<RecordingConfig, 'backends' | 'enabled' | 'retentionDays'>;

/** Login autostart response (P1): configured intent plus the actual system registrations. */
export interface AutoStartInfo {
  ok?: boolean;
  error?: string;
  methods?: string[];
  silent?: boolean;
  status?: {
    exe?: string;
    silent?: boolean;
    methods?: { key: string; registered: boolean; current: boolean; command: string }[];
  };
}

/** GitHub project snapshot and update state (P3). */
export interface GitHubInfo {
  ok?: boolean;
  snapshot?: {
    repo: string;
    stars: number;
    forks: number;
    latestCommitSha: string;
    latestCommitTime: string;
    issuesTotal: number;
    issuesOpen: number;
    fetchedAt: string;
    error: string;
    stale: boolean;
    latestRelease?: { tag: string; name: string; body: string; url: string; publishedAt: string; assets: string[] };
  };
  update?: {
    checkEnabled: boolean;
    hasLatest: boolean;
    current: boolean;
    available: boolean;
    skipped: boolean;
    localTag: string;
    localName: string;
    latestTag: string;
    latestName: string;
    assetPattern: string;
    buildTarget: string;
  };
}

/** P10 Toolkit: GET paths contract (splitByDate + overrides come from the live config). */
export interface ToolkitPaths {
  ok?: boolean;
  base: string;
  config: string;
  logs: string;
  cache: string;
  photos: string;
  splitByDate: boolean;
  cacheOverridden?: boolean;
  photoOverridden?: boolean;
}
/** P10: GET vrchat-config snapshot; POST body is the strict-JSON config object itself. */
export interface ToolkitVrchatConfig { ok: boolean; path: string; values: Record<string, unknown>; vrchatRunning: boolean }
/** P10: photo scan progress (POST scan starts it; GET scan polls it). */
export interface ToolkitScanProgress { ok: boolean; running: boolean; done: number; total: number; indexed: number; error?: string }
export interface ToolkitPhoto { path: string; size: number; modifiedAt: string; capturedAt: string; author: string; authorId: string; worldName: string; worldId: string; metadata: string }
export interface ToolkitPhotoSearch { ok: boolean; total: number; rows: ToolkitPhoto[] }
export interface ToolkitLogList { ok: boolean; files: { name: string; size: number; modifiedAt: string }[] }
export interface ToolkitLogContent { ok: boolean; lines: string[] }
/** P10: cache/analyze usage snapshot; optional per-subdirectory breakdown. */
export interface ToolkitCacheAnalyze { ok: boolean; path: string; bytes: number; files: number; breakdown?: { name: string; bytes: number; files: number }[] }
/** P10: cache/clean dry-run (confirm=false) preview and final result (confirm=true). */
export interface ToolkitCleanResult { ok: boolean; bytes?: number; files?: number; error?: string }
export interface ToolkitGameDay { date: string; hours: number; avgBpm: number; maxBpm: number }
export interface ToolkitGameStats { ok: boolean; days: number; totalHours: number; rows: ToolkitGameDay[] }
export interface ToolkitProcess { ok: boolean; running: boolean; pid: number; cpu: number; ramBytes: number; uptimeSec: number }

export const api = {
  // Status and Configuration
  status: () => get<Record<string, unknown>>('/api/status'),
  config: () => get<Record<string, unknown>>('/api/config'),
  settings: (patch: Record<string, unknown>) => post<Ok>('/api/settings', patch),
  /** Login autostart (P1): GET = actual system registrations; POST applies the selected methods. */
  autostart: () => get<AutoStartInfo>('/api/autostart'),
  autostartSet: (methods: string[], silent: boolean) => post<AutoStartInfo>('/api/autostart', { methods, silent }),
  /** GitHub project snapshot (P3); refresh re-queries api.github.com in the foreground. */
  github: () => get<GitHubInfo>('/api/github'),
  githubRefresh: () => post<GitHubInfo>('/api/github/refresh', {}),
  /** PH 0 2 View preference (  PH 1  PH 2):   PH 3 all /   PH 4  batch merge. */
  prefs: () => get<{ prefs?: Record<string, string> }>('/api/prefs'),
  prefsSet: (prefs: Record<string, string>) => post<Ok>('/api/prefs', { prefs }),

  // Equipment
  devices: () => get<Record<string, unknown>>('/api/devices'),
  scan: (action: 'start' | 'stop') => post<Ok>('/api/scan', { action }),
  connect: (mac: string) => post<Ok>('/api/device/connect', { mac }),
  disconnect: (mac: string) => post<Ok>('/api/device/disconnect', { mac }),
  save: (mac: string, saved: boolean) => post<Ok>('/api/device/save', { mac, saved }),
  block: (mac: string) => post<Ok>('/api/device/block', { mac }),
  unblock: (mac: string) => post<Ok>('/api/device/unblock', { mac }),
  batch: (action: string, macs: string[]) => post<Ok>('/api/devices/batch', { action, macs }),
  rename: (mac: string, alias: string) => post<Ok>('/api/device/rename', { mac, alias }),
  devicesConfig: (patch?: Record<string, unknown>) =>
    patch ? post<Ok>('/api/devices/config', patch) : get<Record<string, unknown>>('/api/devices/config'),
  /** Auto-detection: a combination of tests and sequences, with a life-centre feature that maintains the connection */
  autoDetect: (action: 'start' | 'stop' | 'status') =>
    post<{ ok?: boolean; running?: boolean; done?: number; total?: number; current?: string; candidates?: number }>(
      '/api/devices/autodetect',
      { action },
    ),

  // PH 0   (backend  PH 1  read string:  PH 2   both  PH 3)
  oscConnect: (connected: boolean) => post<Ok>('/api/osc/connect', { connected }),
  oscConfig: (patch: Record<string, unknown>) => post<Ok>('/api/osc/config', patch),
  oscTest: (ip: string, port: string, address: string, text: string) =>
    post<Ok>('/api/osc/test', { ip, port, address, text }),
  /** Custom send: text supports {} variable template;  PH 0  temporarily suspends the driver's protection.
   *  #32 typologies:   PH 0  with   PH 1  decipher values,   PH 2  with   PH 3    PH 4,
   *  Apply /   PH 0 Equivalent address; omit   PH 1 = text mode. */
  oscCustom: (p: {
    ip?: string;
    port?: string;
    address?: string;
    text?: string;
    kind?: 'text' | 'float' | 'bool';
    flag?: boolean;
    pauseMs?: number;
  }) => post<{ ok?: boolean }>('/api/osc/custom', p),
  oscParams: () => get<Record<string, unknown>>('/api/osc/params'),
  oscParamsClear: () => post<Ok>('/api/osc/params/clear'),

  // Second frontend (remote):   PH 0 401 = unlogged in (front end login page)
  remoteMe: () =>
    get<{
      ok?: boolean;
      remote?: boolean;
      authed?: boolean;
      username?: string;
      role?: string;
      admin?: boolean;
      tabs?: string[] | null;
      /** P6: loopback / private / public source zone of this session. */
      sourceZone?: string;
      /** P6: the admin account still uses the default password and must be migrated. */
      mustChangePassword?: boolean;
    }>('/api/remote/me'),
  remoteLogin: (username: string, password: string) =>
    post<{
      ok?: boolean;
      remote?: boolean;
      username?: string;
      role?: string;
      admin?: boolean;
      tabs?: string[] | null;
      sourceZone?: string;
      mustChangePassword?: boolean;
      /** P6: the user store is empty and the login doubles as the admin-initialization flow. */
      init?: boolean;
    }>(
      '/api/remote/login',
      { username, password },
    ),
  remoteLogout: () => post<Ok>('/api/remote/logout'),
  remoteSessions: () =>
    get<{
      ok?: boolean;
      sessions?: {
        id?: string;
        tokenHint?: string;
        username?: string;
        role?: string;
        remoteIp?: string;
        created?: string;
        lastSeen?: string;
        self?: boolean;
      }[];
    }>('/api/remote/sessions'),
  remoteKick: (token: string) => post<Ok>('/api/remote/sessions/kick', { token }),
  remoteUsers: () =>
    get<{ ok?: boolean; users?: { username?: string; role?: string; tabs?: string[] }[] }>('/api/remote/users'),
  remoteUser: (p: Record<string, unknown>) => post<{ ok?: boolean; users?: unknown[] }>('/api/remote/users', p),
  remoteConfig: (patch?: Record<string, unknown>) =>
    patch
      ? post<{ ok?: boolean; config?: Record<string, unknown> }>('/api/remote/config', patch)
      : get<{ ok?: boolean; config?: Record<string, unknown> }>('/api/remote/config'),
  webSecurityConfig: (patch?: Record<string, unknown>) =>
    patch
      ? post<{ ok?: boolean; config?: Record<string, unknown> }>('/api/remote/config', patch)
      : get<{ ok?: boolean; config?: Record<string, unknown> }>('/api/remote/config'),

  // Heart rate/health/record/export
  heartbeat: () => get<Record<string, unknown>>('/heartbeat'),
  health: () => get<Record<string, unknown>>('/api/health'),
  /** action: get / calibrate / cancel */
  healthAction: (action: 'get' | 'calibrate' | 'cancel') =>
    post<Record<string, unknown>>('/api/health', { action }),
  healthConfig: (patch?: Record<string, unknown>) =>
    patch ? post<Ok>('/api/health/config', patch) : get<Record<string, unknown>>('/api/health/config'),
  record: (action: 'start' | 'stop' | 'get') => post<Record<string, unknown>>('/api/record', { action }),
  recordConfig: (patch?: RecordingConfigPatch) =>
    patch ? post<RecordingConfig>('/api/record/config', patch) : get<RecordingConfig>('/api/record/config'),
  export: (p: { table: string; format: string; limit: number }) =>
    post<{ ok?: boolean; file?: string; error?: string }>('/api/export', p),

  // Hardware
  hw: () => get<Record<string, unknown>>('/api/hw'),
  hwRefresh: () => post<Ok>('/api/hw/refresh'),
  /** PH 0 Procedure straight check (  PH 1 head status card; normally updated by   PH 2 PH 3 per   PH 4). */
  vrchat: () => get<Record<string, unknown>>('/api/vrchat'),
  /** E1: launch VRChat (local install first, Steam URL fallback); refuses while the process is running. */
  vrchatLaunch: () => post<{ ok?: boolean; running?: boolean; launched?: boolean; error?: string }>('/api/vrchat/launch', {}),
  hwConfig: (patch?: Record<string, unknown>) =>
    patch ? post<Ok>('/api/hw/config', patch) : get<Record<string, unknown>>('/api/hw/config'),
  hwVar: (patch: Record<string, unknown>) => post<Ok>('/api/hw/var', patch),
  sysinfo: () => get<Record<string, unknown>>('/api/sysinfo'),
  /** Templates for real-time rendering (#4:   PH 0 for preview of the sending page;   PH 1 /   PH 2). */
  sysinfoQuery: (template: string) =>
    get<{ ok?: boolean; text?: string }>(`/api/sysinfo?template=${encodeURIComponent(template)}`),

  // Suspended Window ( PH 0 fault=Main Window PH 1)
  floatOpen: (id?: string) => post<Ok>('/api/float/open', { id: id ?? '__main__' }),
  floatOpenAll: () => post<Ok>('/api/float/open_all'),
  floatCloseAll: () => post<Ok>('/api/float/close_all'),
  floatClose: (id?: string) => post<Ok>('/api/float/close', { id: id ?? '__main__' }),
  floatLock: (locked: boolean) => post<Ok>('/api/float/lock', { locked }),
  floatConfig: (patch: Record<string, unknown>) => post<Ok>('/api/float/config', patch),

  // PH 0 /   PH 1 service
  webhooks: () => get<Record<string, unknown>>('/api/webhooks'),
  webhook: (p: Record<string, unknown>) => post<Ok>('/api/webhooks', p),
  apiConfig: (patch?: Record<string, unknown>) =>
    patch ? post<Ok>('/api/apiserver/config', patch) : get<Record<string, unknown>>('/api/apiserver/config'),

  // Log /   PH 0 /   PH 1
  logs: (filter = '', limit = 500, regex = false, levels: string[] = []) =>
    get<Record<string, unknown>>(
      `/api/logs?filter=${encodeURIComponent(filter)}&limit=${limit}&regex=${regex ? 1 : 0}&levels=${encodeURIComponent(levels.join(','))}`,
    ),
  logsClear: () => post<Ok>('/api/logs/clear'),
  logsDump: () => post<{ ok?: boolean; file?: string }>('/api/logs/dump'),
  logsExport: (p: Record<string, unknown>) => post<Ok>('/api/logs/export', p),

  // VRChat Toolkit (P10, dock entry: local/admin only; log reads stay loopback-only at the backend)
  toolkitPaths: () => get<ToolkitPaths>('/api/toolkit/paths'),
  toolkitVrchatConfig: () => get<ToolkitVrchatConfig>('/api/toolkit/config'),
  toolkitVrchatConfigSave: (config: Record<string, unknown>) => post<Ok>('/api/toolkit/config', config),
  toolkitLogs: () => get<ToolkitLogList>('/api/toolkit/logs'),
  toolkitLogRead: (file: string) => get<ToolkitLogContent>(`/api/toolkit/logs/read?name=${encodeURIComponent(file)}`),
  /** Cache usage snapshot; the backend `subdirs` breakdown is mapped onto `breakdown`. */
  toolkitCacheAnalyze: async (): Promise<ToolkitCacheAnalyze> => {
    const r = await get<ToolkitCacheAnalyze & { subdirs?: { name: string; bytes: number; files: number }[] }>('/api/toolkit/cache');
    return { ...r, breakdown: r.subdirs ?? r.breakdown };
  },
  /** `false` = dry-run preview; `true` executes with the backend confirmation phrase. */
  toolkitCacheClean: async (confirm: boolean): Promise<ToolkitCleanResult> => {
    const r = await post<{ ok: boolean; dryRun?: boolean; cleared?: boolean; bytes?: number; files?: number; failed?: number; error?: string }>(
      '/api/toolkit/cache/clear',
      confirm ? { confirm: true, phrase: 'CLEAR VRCHAT CACHE' } : { dryRun: true },
    );
    return { ok: r.ok, bytes: r.bytes, files: r.files, error: r.ok ? undefined : String(r.error ?? r.failed ?? 'failed') };
  },
  toolkitPhotosScan: (concurrency: number) => post<ToolkitScanProgress>('/api/toolkit/photos/index', { concurrency }),
  toolkitPhotosScanStatus: () => get<ToolkitScanProgress>('/api/toolkit/photos/progress'),
  toolkitPhotosSearch: (q: string) => get<ToolkitPhotoSearch>(`/api/toolkit/photos?q=${encodeURIComponent(q)}`),
  /** Backend game-stats (`seconds`/`hrAvg`/`hrMax`/`totalSeconds`) mapped to hours/BPM fields. */
  toolkitGameStats: async (days: number): Promise<ToolkitGameStats> => {
    const r = await get<{ ok: boolean; days: number; totalSeconds: number; rows: { date: string; seconds: number; hrAvg: number; hrMax: number }[] }>(
      `/api/toolkit/game-stats?days=${days}`,
    );
    return {
      ok: r.ok,
      days: r.days,
      totalHours: (r.totalSeconds ?? 0) / 3600,
      rows: (r.rows ?? []).map((row) => ({
        date: row.date,
        hours: (row.seconds ?? 0) / 3600,
        avgBpm: row.hrAvg ?? 0,
        maxBpm: row.hrMax ?? 0,
      })),
    };
  },
  /** Backend process snapshot (`cpuPct`/`memoryMb`/`startTime`) mapped to cpu/ramBytes/uptime. */
  toolkitProcess: async (): Promise<ToolkitProcess> => {
    const r = await get<{ ok: boolean; running: boolean; pid: number; cpuPct: number; memoryMb: number; startTime?: string }>('/api/toolkit/process');
    const start = r.startTime ? new Date(r.startTime).getTime() : NaN;
    return {
      ok: r.ok,
      running: r.running,
      pid: r.pid,
      cpu: r.cpuPct ?? 0,
      ramBytes: (r.memoryMb ?? 0) * 1048576,
      uptimeSec: Number.isFinite(start) ? Math.max(0, (Date.now() - start) / 1000) : 0,
    };
  },
  cli: (line: string) => post<{ ok: boolean; lines: string[] }>('/api/cli', { line }),
  monitor: (hours: number, buckets: number) =>
    get<Record<string, unknown>>(`/api/monitor?hours=${hours}&buckets=${buckets}`),
  monitorExport: (p: Record<string, unknown>) => post<Ok>('/api/monitor/export', p),

  // PH 0 serving itself
  webStart: () => post<Ok>('/api/web/start'),
  webStop: () => post<Ok>('/api/web/stop'),
  /** Ends the whole program (the backend closes   PH 0 Floating Window/ Tray after exit). */
  shutdown: () => post<Ok>('/api/shutdown'),
};
