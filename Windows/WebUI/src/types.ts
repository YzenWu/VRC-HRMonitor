/** Backend returns the model (fields correspond to   PH 0 PH 1 1, and only those parts of the front end are stated). */

/** Name Sub-section: The   PH 0  clip hits the brand/model keyword, displayed in bold italics in the list. */
export interface NameSeg {
  text: string;
  hit: boolean;
}

export interface Device {
  mac: string;
  name: string;
  rawName: string;
  nameSegs: NameSeg[];
  alias: string;
  type: string;
  rssi: number;
  /** Is   PH 0. The Bluetooth Barn is no longer reported broadcast strength after the connection, at   PH 1, and the interface displays '-'. */
  hasRssi: boolean;
  connected: boolean;
  /** Connection initiated but not yet produced: indicator light is a solid circle (no connection is empty). */
  connecting: boolean;
  /** Signal level:   PH 0  ash /   PH 1  green /   PH 2  orange /   PH 3  red. */
  signal: 'unknown' | 'ok' | 'weak' | 'critical';
  /** Device type:   PH 0 /   PH 1 /   PH 2 /   PH 3, at the front end of this selection. */
  category: 'hr' | 'audio' | 'home' | 'generic';
  /** Iconary heart rate determination: Radio exposure heart rate service, or thorium thiram for thiram;   PH 0  for icon heart rate (over   PH 1). */
  hrMarked?: boolean;
  /** PH 0 has been detected, but heartlessness features (automated detection skips). */
  noHrChar: boolean;
  bpm: number;
  saved: boolean;
  /** Radio frequency ( PH 0): Rate of equipment to broadcast the package, scanned period available. */
  advHz: number;
  /** Frequency of reporting ( PH 0): Rate of notification of the thrust rate of the connected device, connection period available. */
  notifyHz: number;
  reports: number;
  reconnecting: boolean;
  score: number;
}

/** PH 0 state. Note that   PH 1 is a string in the backend   PH 2, and must be   PH 3. */
export interface OscState {
  connected: boolean;
  sent: number;
  fail: number;
  recv: number;
  ip: string;
  port: string;
  address: string;
  intervalMs: string;
  receivePort: string;
  autoStart: boolean;
  template: string;
}

export interface HrWindow {
  visible: boolean;
  locked: boolean;
  format: string;
  unlockedColor: string;
  lockedColor: string;
  imagePath?: string | null;
  geometry: string;
  source: string;
  refreshMs: number;
  /** Specify the data source by window identification (backend is   PH 0,   PH 1). */
  sources: Record<string, string>;
}

/** Health snapshot.   PH 0    is a backend fixed Chinese text, with multiple languages showing   PH 1 (i.e.   PH 3   PH 4). */
export interface HealthSnapshot {
  status: string;
  statusKey: string;
  restingBpm: number;
  restingSd: number;
  calibratedAt: string;
  calibrating: boolean;
  calibrateRemain: number;
  [k: string]: unknown;
}

export interface AppInfo {
  debug: boolean;
  version: string;
  startTime: string;
  baseDir: string;
  /** P2 component version check result: true = all four executables match the manifest, false = mismatch, undefined = unchecked. */
  componentsOk?: boolean;
  /** P3 startup update-check toggle (app.update_check). */
  updateCheck?: boolean;
}

/** Release manifest (#23, P0): backend-embedded single source for all release metadata shown on the About page. */
export interface ReleaseInfo {
  /** Product display name, such as HeartRateMonitor. */
  projectName: string;
  /** Repository home page (empty = unpublished). */
  repo: string;
  /** Repository name in owner/name form, such as yzenwu/VRC-HRMonitor. */
  repoName: string;
  /** Author display name. */
  author: string;
  /** Author profile URL. */
  authorUrl: string;
  /** Thumbnail relative to the exe directory, such as image/icon.svg; pages handle missing files. */
  thumbnail: string;
  /** Release display name (local value for update comparisons). */
  releaseName: string;
  /** Published version from the manifest. */
  releaseVersion: string;
  /** UTC build time in ISO-8601 ("auto" = not built by the release script). */
  buildTimeUtc: string;
  /** Build note edited in the manifest; shown by the CLI banner and About page. */
  buildNote: string;
  /** Build target/platform declared in the manifest (default x64). */
  buildTarget: string;
  /** Release asset name pattern, such as HeartRateMonitor-*-x64.zip. */
  assetPattern: string;
  /** License identifier, such as MIT. */
  license: string;
  /** License text location (repository link or local file). */
  licenseContext: string;
  /** Component versions declared in the manifest (P2 runtime integrity check). */
  components: { engine: string; webui: string; cli: string; dump: string };
}

export interface StatusPayload {
  bpm: number;
  avg: number;
  connectedCount: number;
  scanning: boolean;
  devices: Device[];
  osc: OscState;
  hr: { displaySource: string; window: HrWindow };
  webhookEnabled: boolean;
  app: AppInfo;
  /** Release List (#23)   PH 0  by /   PH 1. */
  release: ReleaseInfo;
  floatCount: number;
  floatMain: boolean;
  /** E1: ids of every open floating window (main + per-device MAC). */
  floatIds?: string[];
  safeMode: boolean;
  health: HealthSnapshot;
  recording: boolean;
}

export interface OscParamItem {
  addr: string;
  args?: unknown[];
  count: number;
}
