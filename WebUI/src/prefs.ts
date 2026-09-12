import { ref, watch, type Ref } from 'vue';
import { api } from './api';

const refRegistry = new Map<string, (v: string) => void>();

export type CurveMode = 'area' | 'line' | 'bars' | 'dots' | 'step' | 'gradient';
export const CURVE_MODES: { value: CurveMode; key: string }[] = [
  { value: 'area', key: 'hb.modearea' },
  { value: 'line', key: 'hb.modeline' },
  { value: 'bars', key: 'hb.modebars' },
  { value: 'dots', key: 'hb.modedots' },
  { value: 'step', key: 'hb.modestep' },
  { value: 'gradient', key: 'hb.modegradient' },
];

export type HrSource = 'main' | 'avg' | (string & {});

function persistedStr<T extends string>(key: string, def: T): Ref<T> {
  const raw = localStorage.getItem(key);
  const r = ref((raw ?? def) as T) as Ref<T>;
  watch(r, (v) => {
    localStorage.setItem(key, String(v));
    queueSync(key, String(v));
  }, { flush: 'sync' });
  refRegistry.set(key, (v) => { r.value = v as T; });
  return r;
}

function persistedNum(key: string, def: number): Ref<number> {
  const raw = Number(localStorage.getItem(key));
  const r = ref(Number.isFinite(raw) && raw > 0 ? raw : def);
  watch(r, (v) => {
    if (!Number.isFinite(v)) return;
    localStorage.setItem(key, String(v));
    queueSync(key, String(v));
  }, { flush: 'sync' });
  refRegistry.set(key, (v) => {
    const n = Number(v);
    if (Number.isFinite(n)) r.value = n;
  });
  return r;
}

function persistedBool(key: string, def: boolean): Ref<boolean> {
  const raw = localStorage.getItem(key);
  const r = ref(raw === null ? def : raw === '1');
  watch(r, (v) => {
    localStorage.setItem(key, v ? '1' : '0');
    queueSync(key, v ? '1' : '0');
  }, { flush: 'sync' });
  refRegistry.set(key, (v) => { r.value = v === '1'; });
  return r;
}

export const hbSmooth = persistedNum('hb-smooth', 1);
export const hbPoints = persistedNum('hb-points', 180);
export const hbMode = persistedStr<CurveMode>('hb-mode', 'area');
export const hbSource = persistedStr<HrSource>('hb-source', 'main');
export const hbMainSource = persistedStr<HrSource>('hb-main-source', 'main');

export type BrandPillMode = 'off' | 'ping' | 'avg' | 'main' | 'devices' | 'connected';
export const brandPill = persistedStr<BrandPillMode>('hrm-pill', 'ping');
export const statusPop = persistedBool('hrm-status-pop', true);
export const chipNameW = persistedNum('hrm-chip-name-w', 108);
export const listCap = persistedNum('hrm-list-cap', 10);
export const barH = persistedNum('hrm-bar-h', 0);
export const navCollapsed = persistedBool('hrm-nav-collapsed', false);
export const navWidth = persistedNum('hrm-nav-width', 224);
export const titleH = persistedNum('hrm-title-h', 34);

export type RollMode = 'roll' | 'odometer' | 'fade';
export const rollMode = persistedStr<RollMode>('hrm-roll-mode', 'roll');
export const ROLL_MODES: { value: RollMode; key: string }[] = [
  { value: 'roll', key: 'settings.animroll' },
  { value: 'odometer', key: 'settings.animodo' },
  { value: 'fade', key: 'settings.animfade' },
];

export const autoRestart = persistedBool('hrm-auto-restart', true);
export const devWeightSort = persistedBool('hrm-dev-weight-sort', true);
export type DevSortKey = 'name' | 'mac' | 'signal' | 'advHz' | 'notifyHz' | 'bpm';

export function resetCurveView(): void {
  hbSmooth.value = 1;
  hbPoints.value = 180;
  hbMode.value = 'area';
  hbSource.value = 'main';
  hbMainSource.value = 'main';
}

const SYNC_PREFIXES = ['hrm-', 'hb-'];
let pending: Record<string, string> = {};
let pushTimer = 0;
let syncEnabled = false;
let started = false;

function isSyncKey(key: string): boolean {
  return SYNC_PREFIXES.some((prefix) => key.startsWith(prefix));
}

function queueSync(key: string, value: string): void {
  pending[key] = value;
  window.clearTimeout(pushTimer);
  pushTimer = window.setTimeout(flushSync, 800);
}

function flushSync(): void {
  if (!syncEnabled || Object.keys(pending).length === 0) return;
  const patch = pending;
  pending = {};
  void api.prefsSet(patch).catch(() => { pending = { ...patch, ...pending }; });
}

export function flushPrefsSync(): void { flushSync(); }

export function startPrefsSync(): void {
  if (started) return;
  started = true;
  void api.prefs().then((response) => {
    for (const [key, value] of Object.entries(response.prefs ?? {})) {
      if (!isSyncKey(key) || value === '' || localStorage.getItem(key) !== null) continue;
      localStorage.setItem(key, value);
      refRegistry.get(key)?.(value);
    }
  }).catch(() => {}).finally(() => { syncEnabled = true; });
  window.addEventListener('beforeunload', flushSync);
}
