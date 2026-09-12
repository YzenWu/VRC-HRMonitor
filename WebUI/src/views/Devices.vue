<script setup lang="ts">
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { ArrowDown, ArrowUp, ArrowUpDown, Play, Radar, RefreshCw, RotateCcw, Square } from 'lucide-vue-next';
import { useRoute, useRouter } from 'vue-router';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { useAutoSave } from '../composables/useAutoSave';
import { markSaved } from '../stores/notify';
import { devWeightSort, listCap, type DevSortKey } from '../prefs';
import type { Device } from '../types';
import DeviceDot from '../components/DeviceDot.vue';
import DeviceIcon from '../components/DeviceIcon.vue';
import DeviceName from '../components/DeviceName.vue';
import RollingNumber from '../components/RollingNumber.vue';
import { deviceRowClick, deviceRowDblClick } from '../deviceDialog';
import XSwitch from '../components/XSwitch.vue';
import XDialog from '../components/XDialog.vue';
import XRange from '../components/XRange.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();
const app = useAppStore();

/** #9 Layout Editor: This page card (top batch operating bar left outside the grid). */
const CARDS: GridCard[] = [
  { id: 'devices', titleKey: 'dev.history', span: 12, bodyStyle: 'padding: 0' },
  { id: 'policy', titleKey: 'dev.policy', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
];




function onRowClick(e: MouseEvent): void {
  const el = e.target as Element | null;
  if (!el || !el.closest) return;
  if (el.closest('button, input, a, .th-grip')) return;
  const cell = el.closest('[data-dev-mac]');
  if (!cell) return;
  const mac = cell.getAttribute('data-dev-mac');
  if (mac) deviceRowClick(mac);
}

function onRowDblClick(e: MouseEvent): void {
  const el = e.target as Element | null;
  if (!el || !el.closest) return;
  if (el.closest('button, input, a, .th-grip')) return;
  const cell = el.closest('[data-dev-mac]');
  if (!cell) return;
  const d = app.devices.find((x) => x.mac === cell.getAttribute('data-dev-mac'));
  if (d) deviceRowDblClick(d);
}

const checked = ref<Record<string, boolean>>({});
const busy = ref(false);

const checkedMacs = computed(() => Object.entries(checked.value).filter(([, v]) => v).map(([m]) => m));
const allChecked = computed(() => app.devices.length > 0 && checkedMacs.value.length === app.devices.length);

function toggleAll(): void {
  const next: Record<string, boolean> = {};
  for (const d of app.devices) next[d.mac] = !allChecked.value;
  checked.value = next;
}

/** Inverts the current visible device (tick for other positions). */
function invertAll(): void {
  const next = { ...checked.value };
  for (const d of app.devices) next[d.mac] = !next[d.mac];
  checked.value = next;
}

function clearAll(): void {
  checked.value = {};
}

// -  Foreign sorting / weighting switch (#15): Reshare weights → column values are the same for a combined pivot bottom; off → pure rows by point -
const weightSort = devWeightSort;
const sortKey = ref<DevSortKey | null>(null);
const sortDir = ref<'asc' | 'desc'>('asc');

const SORTABLE: DevSortKey[] = ['name', 'mac', 'signal', 'advHz', 'notifyHz', 'bpm'];


const SIG_RANK: Record<string, number> = { unknown: 0, critical: 1, weak: 2, ok: 3 };

function colVal(d: Device, k: DevSortKey): number | string {
  switch (k) {
    case 'name':
      return d.name.toLowerCase();
    case 'mac':
      return d.mac.toLowerCase();
    case 'signal':
      return SIG_RANK[d.signal] ?? 0;
    case 'advHz':
      return d.advHz;
    case 'notifyHz':
      return d.notifyHz;
    case 'bpm':
      // No connection or silent participation in the straight row (lowest)
      return d.connected && d.bpm > 0 ? d.bpm : -1;
  }
}

function toggleSort(k: DevSortKey): void {
  if (sortKey.value === k) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc';
  } else {
    sortKey.value = k;
    sortDir.value = 'asc';
  }
}

const isSortable = (k: string): boolean => (SORTABLE as string[]).includes(k);

const sortedDevices = computed(() => {
  const list = [...app.devices];
  const k = sortKey.value;
  if (!k) return list; // No list header: maintain backend integration order
  const dir = sortDir.value === 'asc' ? 1 : -1;
  list.sort((a, b) => {
    const va = colVal(a, k);
    const vb = colVal(b, k);
    if (va < vb) return -1 * dir;
    if (va > vb) return 1 * dir;

    return weightSort.value ? b.score - a.score : 0;
  });
  return list;
});

// Status Aggregation of Batch Binary Buttons: Any linked Zoom Show and execute " Disconnect " in the selection set (User Rule: Disconnect Mixed Selection Priority)
const anyConnectedChecked = computed(() =>
  checkedMacs.value.some((m) => app.devices.find((d) => d.mac === m)?.connected),
);
// Any unsaved poach display and execute "saved"; all saved poached poached
const anyUnsavedChecked = computed(() =>
  checkedMacs.value.some((m) => !(app.devices.find((d) => d.mac === m)?.saved ?? false)),
);

async function runBatch(
  action: 'connect' | 'disconnect' | 'save' | 'unsave' | 'block' | 'unblock',
): Promise<void> {
  const macs = checkedMacs.value;
  if (macs.length === 0) return;
  busy.value = true;
  try {
    await api.batch(action, macs);
    if (action === 'block' || action === 'unblock') checked.value = {};
    await app.sync();
    if (action === 'block' || action === 'unblock') await loadCfg(); // Refresh Block List
  } finally {
    busy.value = false;
  }
}


const renameFor = ref<{ mac: string; current: string } | null>(null);
const renameText = ref('');

function openRename(mac: string, current: string): void {
  renameFor.value = { mac, current };
  renameText.value = current;
}

async function submitRename(): Promise<void> {
  const target = renameFor.value;
  if (!target) return;
  const alias = renameText.value.trim();
  renameFor.value = null;
  if (alias === '' || alias === target.current) return;
  await api.rename(target.mac, alias);
  await app.sync();
}




const COLS = [
  { key: 'sel', labelKey: '', raw: '', def: 34, min: 30, align: 'center' },
  { key: 'state', labelKey: 'dev.state', raw: '', def: 52, min: 44, align: 'center' },
  { key: 'icon', labelKey: '', raw: '', def: 32, min: 28, align: 'center' },
  { key: 'name', labelKey: 'common.name', raw: '', def: 190, min: 80, align: 'start' },
  { key: 'mac', labelKey: 'dev.mac', raw: '', def: 148, min: 80, align: 'start' },
  { key: 'signal', labelKey: 'dev.rssi', raw: '', def: 88, min: 56, align: 'end' },
  { key: 'advHz', labelKey: 'dev.advrate', raw: '', def: 96, min: 56, align: 'end' },
  { key: 'notifyHz', labelKey: 'dev.notifyrate', raw: '', def: 96, min: 56, align: 'end' },
  { key: 'bpm', labelKey: '', raw: 'BPM', def: 74, min: 54, align: 'end' },
  { key: 'ops', labelKey: 'dev.ops', raw: '', def: 250, min: 120, align: 'start' },
] as const;

const LS_KEY = 'hrm-dev-cols';

function loadWidths(): number[] {
  try {
    const raw = JSON.parse(localStorage.getItem(LS_KEY) ?? 'null') as Record<string, number> | null;
    if (raw) return COLS.map((c) => Math.max(c.min, Number(raw[c.key]) || c.def));
  } catch {
    /* Bad data default */
  }
  return COLS.map((c) => c.def);
}

const widths = ref<number[]>(loadWidths());

function reloadRestoredWidths(event: Event): void {
  const keys = (event as CustomEvent<{ keys?: string[] }>).detail?.keys ?? [];
  if (keys.includes(LS_KEY)) widths.value = loadWidths();
}

watch(
  widths,
  (w) => {
    const obj: Record<string, number> = {};
    COLS.forEach((c, i) => (obj[c.key] = Math.round(w[i])));
    localStorage.setItem(LS_KEY, JSON.stringify(obj));
  },
  { deep: true },
);

function resetWidths(): void {
  widths.value = COLS.map((c) => c.def);
}

// - List shows ceiling (thirty-first round #13) -


const devScroll = ref<HTMLElement | null>(null);
const capHeight = ref<string | undefined>(undefined);

function remeasureCap(): void {
  const sc = devScroll.value;
  if (!sc) return;
  const head = sc.querySelector<HTMLElement>('.dev-head');
  const cells = Array.from(sc.querySelectorAll<HTMLElement>('.dev-td'));
  if (!head) return;
  const headH = head.getBoundingClientRect().height;
  let rowH = 30;
  if (cells.length > 0) {
    if (cells.length >= COLS.length * 2) {
      const a = cells[0].getBoundingClientRect().top;
      const b = cells[COLS.length].getBoundingClientRect().top;
      rowH = b - a;
    } else {
      rowH = cells[0].getBoundingClientRect().height;
    }
  }
  capHeight.value = `${Math.round(headH + listCap.value * rowH)}px`;
}

watch(() => app.devices.length, () => void nextTick(remeasureCap));
watch(listCap, () => void nextTick(remeasureCap));


const gridCols = computed(() => widths.value.map((w) => `${Math.round(w)}px`).join(' '));


let dragIdx = -1;
let dragX = 0;
let dragW = 0;

function onGripDown(e: PointerEvent, i: number): void {
  dragIdx = i;
  dragX = e.clientX;
  dragW = widths.value[i];
  (e.target as HTMLElement).setPointerCapture(e.pointerId);
  e.preventDefault();
}

function onGripMove(e: PointerEvent): void {
  if (dragIdx < 0) return;
  const next = [...widths.value];
  next[dragIdx] = Math.max(COLS[dragIdx].min, dragW + (e.clientX - dragX));
  widths.value = next;
}

function onGripUp(e: PointerEvent): void {
  if (dragIdx < 0) return;
  dragIdx = -1;
  try {
    (e.target as HTMLElement).releasePointerCapture(e.pointerId);
  } catch {
    /* Released */
  }
}


const detecting = ref(false);
const detectText = ref('');
let detectTimer: number | null = null;

async function pollDetect(): Promise<void> {
  const r = await api.autoDetect('status');
  detecting.value = !!r.running;
  detectText.value = r.running
    ? `${t('dev.detecting')} ${r.done ?? 0}/${r.total ?? 0} ${r.current ?? ''}`
    : '';
  // Query only during run-off, stop watch
  if (r.running && detectTimer === null) detectTimer = window.setInterval(() => void pollDetect(), 1000);
  if (!r.running && detectTimer !== null) {
    window.clearInterval(detectTimer);
    detectTimer = null;
    await app.sync();
  }
}

async function toggleDetect(): Promise<void> {
  const r = await api.autoDetect(detecting.value ? 'stop' : 'start');
  detecting.value = !!r.running;
  await pollDetect();
}


interface DevCfg {
  continuousScan: boolean;
  refreshThrottleMs: number;
  autoReconnect: boolean;
  reconnectIntervalSec: number;
  reconnectGiveUpMin: number;
  rssiWeakThreshold: number;
  rssiCriticalThreshold: number;
  autoConnect: boolean;
  autoDetect: boolean;
  autoDetectTimeoutSec: number;
}
const dcfg = ref<DevCfg>({
  continuousScan: true,
  refreshThrottleMs: 400,
  autoReconnect: true,
  reconnectIntervalSec: 15,
  reconnectGiveUpMin: 30,
  rssiWeakThreshold: 100,
  rssiCriticalThreshold: 110,
  autoConnect: false,
  autoDetect: false,
  autoDetectTimeoutSec: 12,
});

const blockedList = ref<string[]>([]);
const showBlocked = ref(false);

async function unblockOne(mac: string): Promise<void> {
  await api.unblock(mac);
  await app.sync();
  await loadCfg();
}


function blockedName(mac: string): string {
  const hit = app.devices.find((d) => d.mac === mac);
  if (hit) return hit.name;
  return mac;
}

async function loadCfg(): Promise<void> {
  try {
    const c = (await api.devicesConfig()) as Partial<DevCfg> & { autoDetecting?: boolean; blocked?: string[] };
    dcfg.value = { ...dcfg.value, ...c };
    blockedList.value = Array.isArray(c.blocked) ? c.blocked : [];
    if (c.autoDetecting) await pollDetect();
  } catch {
    /* Keep Default */
  }
}
async function saveCfg(): Promise<void> {
  await api.devicesConfig({ ...dcfg.value });
  markSaved(); // Align Saved logo to the right of the bottombar
}

/** Frequency display: 0 is deemed not available (radio frequency only has a scanned period and reported frequency only a bridging period). */
const hz = (v: number): string => (v > 0 ? `${v.toFixed(2)} Hz` : '—');


const auto = useAutoSave('devices', () => dcfg.value, saveCfg);


const route = useRoute();
const router = useRouter();
const HL_MS = 2600;
let hlTimer: number | undefined;
let hlGiveUp = 0;

let hlSeq = 0;

function clearHl(): void {
  hlSeq++;
  if (hlTimer !== undefined) {
    window.clearTimeout(hlTimer);
    hlTimer = undefined;
  }
  document.querySelectorAll('.dev-td.dev-hl').forEach((el) => el.classList.remove('dev-hl'));
}


function flashDeviceRow(mac: string): void {
  clearHl();
  const seq = hlSeq;
  const cells = Array.from(document.querySelectorAll<HTMLElement>(`.dev-td[data-row-mac="${mac}"]`));
  if (cells.length === 0) {
    if (hlGiveUp < 60) {
      hlGiveUp++;
      window.setTimeout(() => {
        if (seq === hlSeq) flashDeviceRow(mac);
      }, 150);
    } else {
      void router.replace({ query: {} });
    }
    return;
  }
  cells[0].scrollIntoView({ block: 'center' });
  cells.forEach((el) => el.classList.add('dev-hl'));
  hlTimer = window.setTimeout(() => {
    clearHl();
    void router.replace({ query: {} });
  }, HL_MS);
}

watch(
  () => route.query.hl,
  (v) => {
    clearHl();
    if (v) {
      hlGiveUp = 0;
      const seq = hlSeq;
      window.setTimeout(() => {
        if (seq === hlSeq) flashDeviceRow(String(v));
      }, 30);
    }
  },
);

onMounted(() => void loadCfg().then(() => auto.hydrated()));
onMounted(() => {
  window.addEventListener('hrm:prefs-restored', reloadRestoredWidths);
  void nextTick(remeasureCap);
  if (route.query.hl) {
    hlGiveUp = 0;
    const seq = hlSeq;
    window.setTimeout(() => {
      if (seq === hlSeq) flashDeviceRow(String(route.query.hl));
    }, 60);
  }
});
onUnmounted(() => {
  window.removeEventListener('hrm:prefs-restored', reloadRestoredWidths);
  if (detectTimer !== null) window.clearInterval(detectTimer);
  clearHl();
});
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1 v-bubble="t('dev.cfghint')">{{ t('tab.devices') }}</h1>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <div class="x-gap" style="flex-wrap: wrap">
        <button class="x-btn primary" @click="() => void api.scan(app.scanning ? 'stop' : 'start').then(() => app.sync())">
          <component :is="app.scanning ? Square : Play" class="x-btn-ico" />
          {{ app.scanning ? t('scan.stop') : t('scan.start') }}
        </button>
        <button class="x-btn" :class="{ primary: detecting }" @click="() => void toggleDetect()">
          <Radar class="x-btn-ico" :class="{ spinning: detecting }" />
          {{ detecting ? t('dev.autodetectstop') : t('dev.autodetect') }}
        </button>
        <span v-if="detectText" class="x-muted" style="font-size: 12px">{{ detectText }}</span>
        <button class="x-btn ghost" @click="() => void app.refreshDevices()">
          <RefreshCw class="x-btn-ico" />
          {{ t('common.refresh') }}
        </button>
        <span class="x-vsep" />
        <button class="x-btn sm" @click="toggleAll">{{ t('dev.selectAll') }}</button>
        <button class="x-btn sm" @click="invertAll">{{ t('dev.invert') }}</button>
        <button class="x-btn sm ghost" :disabled="checkedMacs.length === 0" @click="clearAll">{{ t('dev.clearSel') }}</button>
        <span class="x-muted" style="font-size: 12px; white-space: nowrap">{{ checkedMacs.length }}</span>
        <!-- Batch connection/disconnect merged into a single button: any connected → "disconnected" (mix selection priority disconnected); completely unconnected → Connection -->
        <button
          class="x-btn"
          :class="{ primary: !anyConnectedChecked && checkedMacs.length > 0 }"
          :disabled="busy || checkedMacs.length === 0"
          @click="() => void runBatch(anyConnectedChecked ? 'disconnect' : 'connect')"
        >
          {{ anyConnectedChecked ? t('dev.disconnect') : t('dev.connect') }}
        </button>
        <!-- Batch Save/Remove Save Same -->
        <button
          class="x-btn"
          :class="{ primary: anyUnsavedChecked && checkedMacs.length > 0 }"
          :disabled="busy || checkedMacs.length === 0"
          @click="() => void runBatch(anyUnsavedChecked ? 'save' : 'unsave')"
        >
          {{ anyUnsavedChecked ? t('dev.save') : t('dev.unsave') }}
        </button>
        <button class="x-btn danger" :disabled="busy || checkedMacs.length === 0" @click="() => void runBatch('block')">
          {{ t('dev.block') }}
        </button>
        <button
          class="x-btn ghost"
          :disabled="blockedList.length === 0"
          v-bubble="t('dev.blockedhint')"
          @click="showBlocked = true"
        >
          {{ t('dev.blocklist') }} ({{ blockedList.length }})
        </button>
        <span class="x-vsep" />
        <XSwitch v-model="weightSort" :label="t('dev.weightsort')" style="min-width: 128px" />
      </div>

      <CardGrid view="devices" :cards="CARDS">
        <!-- Device Table: Table Header + Column Alignment + Drag-Low Width (Consistence), Scroll Horizontally Beyond Width -->
        <template #head-devices>
          <span v-bubble="t('dev.colhint')">{{ t('dev.history') }}</span>
          <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ app.devices.length }}</span>
        </template>
        <template #devices>
          <div class="x-gap" style="padding: var(--gap-2) var(--gap-2) 0">
            <span class="x-grow"></span>
            <button class="x-btn sm ghost" v-bubble="t('dev.colreset')" @click="resetWidths">
              <RotateCcw class="x-btn-ico" />
              {{ t('dev.colreset') }}
            </button>
          </div>
          <div ref="devScroll" class="dev-scroll" :style="{ maxHeight: capHeight }">



            <div class="dev-table">
              <div class="dev-head" :style="{ gridTemplateColumns: gridCols }">

                <div v-for="(c, i) in COLS" :key="c.key" class="dev-th" :class="`a-${c.align}`">
                  <input
                    v-if="c.key === 'sel'"
                    type="checkbox"
                    :checked="allChecked"
                    v-bubble="t('dev.selectAll')"
                    @change="toggleAll"
                  />
                  <button
                    v-else-if="isSortable(c.key)"
                    class="th-sort"
                    :class="{ on: sortKey === c.key }"
                    @click="toggleSort(c.key as DevSortKey)"
                  >
                    <span class="th-text">{{ c.labelKey ? t(c.labelKey) : c.raw }}</span>
                    <ArrowUp v-if="sortKey === c.key && sortDir === 'asc'" class="th-arr" />
                    <ArrowDown v-else-if="sortKey === c.key && sortDir === 'desc'" class="th-arr" />
                    <ArrowUpDown v-else class="th-arr dim" />
                  </button>
                  <span v-else class="th-text">{{ c.labelKey ? t(c.labelKey) : c.raw }}</span>
                  <span
                    class="th-grip"
                    :class="{ dragging: dragIdx === i }"
                    @pointerdown="onGripDown($event, i)"
                    @pointermove="onGripMove"
                    @pointerup="onGripUp"
                    @pointercancel="onGripUp"
                  />
                </div>
              </div>

              <div class="dev-body" :style="{ gridTemplateColumns: gridCols }" @click="onRowClick" @dblclick="onRowDblClick">

                <template v-for="d in sortedDevices" :key="d.mac">
                  <div class="dev-td a-center" :data-row-mac="d.mac">
                    <input
                      type="checkbox"
                      :checked="checked[d.mac] ?? false"
                      @change="checked = { ...checked, [d.mac]: ($event.target as HTMLInputElement).checked }"
                    />
                  </div>
                  <div class="dev-td a-center state" :data-row-mac="d.mac">
                    <DeviceDot :device="d" />
                    <span v-if="d.reconnecting" class="retry" v-bubble="t('dev.autoreconnect')">↻</span>
                  </div>
                  <div class="dev-td a-center" :data-row-mac="d.mac"><DeviceIcon :device="d" /></div>
                  <div class="dev-td a-start" :data-row-mac="d.mac" v-bubble="d.name" :data-dev-mac="d.mac" :data-dev-name="d.name" :data-dev-connected="d.connected ? '1' : '0'" :data-dev-connecting="d.connecting ? '1' : '0'">
                    <DeviceName :device="d" />
                  </div>
                  <div class="dev-td a-start x-mono x-muted num" :data-row-mac="d.mac" v-bubble="d.mac">{{ d.mac }}</div>
                  <div class="dev-td a-end x-muted num" :data-row-mac="d.mac" v-bubble="d.hasRssi ? d.type : t('dev.rssihint')">
                    {{ d.hasRssi ? `${d.rssi} dBm` : '—' }}
                  </div>
                  <div class="dev-td a-end x-muted num" :data-row-mac="d.mac" v-bubble="t('dev.advratehint')">{{ hz(d.advHz) }}</div>
                  <div class="dev-td a-end x-muted num" :data-row-mac="d.mac" v-bubble="t('dev.notifyratehint')">
                    {{ hz(d.notifyHz) }}
                  </div>
                  <div class="dev-td a-end num bpm" :data-row-mac="d.mac" :class="{ live: d.connected && d.bpm > 0 }">

                    <RollingNumber :value="d.connected && d.bpm > 0 ? d.bpm : null" placeholder="—" />
                  </div>
                  <div class="dev-td a-start ops" :data-row-mac="d.mac" :data-dev-mac="d.mac" :data-dev-name="d.name" :data-dev-connected="d.connected ? '1' : '0'" :data-dev-connecting="d.connecting ? '1' : '0'">
                    <button
                      class="x-btn sm"
                      :disabled="d.connecting"
                      @click="() => void (d.connected ? api.disconnect(d.mac) : api.connect(d.mac)).then(() => app.sync())"
                    >
                      {{ d.connecting ? t('dev.connecting') : d.connected ? t('dev.disconnect') : t('dev.connect') }}
                    </button>
                    <button class="x-btn sm ghost" @click="openRename(d.mac, d.name)">{{ t('dev.rename') }}</button>
                    <button class="x-btn sm ghost" @click="() => void api.save(d.mac, !d.saved).then(() => app.sync())">
                      {{ d.saved ? t('dev.unsave') : t('dev.save') }}
                    </button>
                    <span v-if="d.noHrChar" class="x-muted" style="font-size: 11px">{{ t('dev.nohrchar') }}</span>
                  </div>
                </template>
              </div>
            </div>
            <div v-if="app.devices.length === 0" class="x-muted" style="padding: var(--gap-2)">{{ t('dev.empty') }}</div>
          </div>
        </template>

        <!-- Device Policy -->
        <template #head-policy>
          {{ t('dev.policy') }}
          <span class="x-grow"></span>
          <span class="x-muted" style="font-weight: 400; font-size: 11.5px">{{ t('common.autosave') }}</span>
        </template>
        <template #policy>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch v-model="dcfg.continuousScan" :label="t('dev.continuous')" style="min-width: 150px" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('dev.scaninterval') }}</label>
            <XRange
              :model-value="dcfg.refreshThrottleMs"
              :min="0"
              :max="2000"
              :step="50"
              :hard-min="0"
              :default="400"
              suffix="ms"
              @update:model-value="(v) => (dcfg.refreshThrottleMs = v)"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('dev.weak') }}</label>
            <XRange
              :model-value="dcfg.rssiWeakThreshold"
              :min="1"
              :max="127"
              :step="1"
              :hard-min="0"
              :default="100"
              @update:model-value="(v) => (dcfg.rssiWeakThreshold = v)"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('dev.critical') }}</label>
            <XRange
              :model-value="dcfg.rssiCriticalThreshold"
              :min="1"
              :max="127"
              :step="1"
              :hard-min="0"
              :default="110"
              @update:model-value="(v) => (dcfg.rssiCriticalThreshold = v)"
            />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch v-model="dcfg.autoReconnect" :label="t('dev.autoreconnect')" style="min-width: 150px" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('dev.retry') }}</label>
            <XRange
              :model-value="dcfg.reconnectIntervalSec"
              :min="1"
              :max="120"
              :step="1"
              :hard-min="1"
              :default="15"
              suffix="s"
              @update:model-value="(v) => (dcfg.reconnectIntervalSec = v)"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('dev.giveup') }}</label>
            <XRange
              :model-value="dcfg.reconnectGiveUpMin"
              :min="1"
              :max="720"
              :step="1"
              :hard-min="1"
              :default="30"
              suffix="min"
              @update:model-value="(v) => (dcfg.reconnectGiveUpMin = v)"
            />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch v-model="dcfg.autoConnect" :label="t('dev.autoconnect')" style="min-width: 240px" />
            <XSwitch v-model="dcfg.autoDetect" :label="t('dev.autodetect')" style="min-width: 170px" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('dev.detecttimeout') }}</label>
            <XRange
              :model-value="dcfg.autoDetectTimeoutSec"
              :min="3"
              :max="60"
              :step="1"
              :hard-min="3"
              :default="12"
              suffix="s"
              @update:model-value="(v) => (dcfg.autoDetectTimeoutSec = v)"
            />
          </div>
        </template>
      </CardGrid>
    </div>


    <XDialog
      :open="renameFor !== null"
      :title="t('dev.rename')"
      :desc="renameFor?.mac"
      size="sm"
      @close="renameFor = null"
    >
      <input
        v-model="renameText"
        class="x-input"
        style="width: 100%"
        :placeholder="t('dev.alias')"
        @keyup.enter="() => void submitRename()"
      />
      <template #actions>
        <button class="x-btn sm primary" @click="() => void submitRename()">{{ t('common.save') }}</button>
        <button class="x-btn sm ghost" @click="renameFor = null">{{ t('common.cancel') }}</button>
      </template>
    </XDialog>


    <XDialog
      :open="showBlocked"
      :title="t('dev.blocklist')"
      size="sm"
      @close="showBlocked = false"
    >
      <div style="display: flex; flex-direction: column; gap: var(--gap-2)">
        <div class="x-muted" style="font-size: 12px">{{ t('dev.blockedhint') }}</div>
        <div v-if="blockedList.length === 0" class="x-muted" style="font-size: 12.5px">{{ t('common.none') }}</div>
        <div v-for="mac in blockedList" :key="mac" class="x-row">
          <span class="x-mono x-grow" style="overflow: hidden; text-overflow: ellipsis">{{ blockedName(mac) }} · {{ mac }}</span>
          <button class="x-btn sm" @click="() => void unblockOne(mac)">{{ t('dev.unblock') }}</button>
        </div>
      </div>
      <template #actions>
        <button class="x-btn sm ghost" @click="showBlocked = false">{{ t('common.close') }}</button>
      </template>
    </XDialog>
  </div>
</template>

<style scoped>


.dev-scroll {
  overflow-x: auto;
  overflow-y: auto;
}

.dev-td.dev-hl {
  background: var(--accent);
  box-shadow: inset 0 0 0 2px color-mix(in oklab, var(--primary) 55%, transparent);
  animation: dev-hl-fade 1.1s linear 1;
}
@keyframes dev-hl-fade {
  to {
    background: transparent;
    box-shadow: none;
  }
}
.dev-table {
  display: flex;
  flex-direction: column;

  width: max-content;
  min-width: 100%;
}




.dev-head {
  display: grid;
  align-items: stretch;
  background: var(--secondary);
  border-bottom: var(--hairline) solid var(--border);

  position: sticky;
  top: 0;
  z-index: 3;
}
.dev-body {
  display: grid;
  align-items: stretch;

}
.dev-th {
  position: relative;
  display: flex;
  align-items: center;
  padding: 6px var(--gap-2);
  min-height: calc(26px * var(--sp));
  font-size: 11.5px;
  font-weight: 600;
  color: var(--muted-foreground);
  overflow: hidden;
  white-space: nowrap;
  user-select: none;
}
.dev-th .th-text {
  overflow: hidden;
  text-overflow: ellipsis;
}
.dev-th .th-sort .th-text {
  /* The text segments in the button are constricted, long enough to leave the ellipses rather than squeeze the arrows out of the column width */
  min-width: 0;
  flex: 1 1 auto;
}
/* Sortable column: whole size point (trawled handle independently on the right side and with a pointer, without misdirection) */
.dev-th .th-sort {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  width: 100%;
  min-width: 0;
  padding: 0;
  border: 0;
  background: none;
  font: inherit;
  font-weight: 600;
  color: inherit;
  cursor: pointer;
}
.dev-th .th-sort:hover {
  color: var(--foreground);
}
.dev-th .th-sort.on {
  color: var(--foreground);
}
.a-start .th-sort .th-text {
  text-align: start;
}
.a-center .th-sort .th-text {
  text-align: center;
}
.a-end .th-sort .th-text {
  text-align: end;
}
.dev-th .th-sort .th-arr {
  width: 12px;
  height: 12px;
  flex-shrink: 0;
  color: var(--primary);
}
.dev-th .th-sort .th-arr.dim {
  color: var(--muted-foreground);
  opacity: 0.5;
}

.dev-th .th-grip {
  position: absolute;
  top: 0;
  right: 0;
  width: 7px;
  height: 100%;
  cursor: col-resize;
  touch-action: none;
}
.dev-th .th-grip::after {
  content: '';
  position: absolute;
  top: 20%;
  right: 3px;
  width: 1px;
  height: 60%;
  background: var(--border);
}
.dev-th .th-grip:hover::after,
.dev-th .th-grip.dragging::after {
  background: var(--primary);
  width: 2px;
}
.dev-td {
  display: flex;
  align-items: center;
  gap: 5px;
  /* Lines are high-fixed: state points, icons, buttons are mixed without error depending on the height of the content Okay. */
  min-height: calc(30px * var(--sp));
  padding: 4px var(--gap-2);
  border-bottom: var(--hairline) solid var(--border);
  font-size: 12.5px;
  /* Cell content should not spill column width */
  overflow: hidden;
  white-space: nowrap;
  text-overflow: ellipsis;
  min-width: 0;
}
/* Three horizontal alignments (table headers shared with data columns to ensure visual coaxis) */
.a-start {
  justify-content: flex-start;
}
.a-center {
  justify-content: center;

  padding-left: 2px;
  padding-right: 2px;
}
.a-end {
  justify-content: flex-end;
}
/* Wide fonts, such as numerical columns: Numbers do not beat right or left when refreshed at high speed */
.num {
  font-variant-numeric: tabular-nums;
  font-size: 12px;
}
.dev-td.state {
  gap: 3px;
}
.dev-td.state .retry {
  font-size: 11px;
  color: var(--warn);
}
.dev-td.bpm {
  font-weight: 600;
  font-size: 13px;
  color: var(--muted-foreground);
}
.dev-td.bpm.live {
  color: var(--foreground);
}
.dev-td.ops {
  gap: 4px;
}
.spinning {
  animation: spin 1.4s linear infinite;
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .spinning {
    animation: none;
  }
}
</style>
