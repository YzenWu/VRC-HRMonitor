<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { Bluetooth, Download, MonitorPlay, Play, Square } from 'lucide-vue-next';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { useAutoSave } from '../composables/useAutoSave';
import { markSaved } from '../stores/notify';
import BpmChart from '../components/BpmChart.vue';
import RollingNumber from '../components/RollingNumber.vue';
import DeviceDot from '../components/DeviceDot.vue';
import DeviceIcon from '../components/DeviceIcon.vue';
import DeviceName from '../components/DeviceName.vue';
import FloatToggle from '../components/FloatToggle.vue';
import XSwitch from '../components/XSwitch.vue';
import XSelect from '../components/XSelect.vue';
import XRange from '../components/XRange.vue';
import { CURVE_MODES, flushPrefsSync, hbMainSource, hbMode, hbPoints, hbSmooth, hbSource, resetCurveView } from '../prefs';
import { deviceRowClick, deviceRowDblClick } from '../deviceDialog';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();
const app = useAppStore();

/** #9 Layout Editor: This page of cards (original master card without header, grided and recapitulated; operational behavior toolbar, left outside the grid). */
const CARDS: GridCard[] = [
  { id: 'main', titleKey: 'hb.current', span: 12, bodyStyle: 'display: flex; gap: var(--gap-4); align-items: stretch' },
  { id: 'curve', titleKey: 'hb.curve', span: 3, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'health', titleKey: 'hb.health', span: 3, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'overlay', titleKey: 'hb.overlay', span: 3, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'export', titleKey: 'hb.dataexport', span: 3, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'devices', titleKey: 'tab.devices', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-1)' },
];


const healthKey = computed(() => {
  const k = String(app.health?.statusKey ?? '');
  return k.startsWith('hb.status.') ? k : 'hb.status.unknown';
});

const toggleScan = () => void api.scan(app.scanning ? 'stop' : 'start').then(() => app.sync());
const toggleRecord = () => void api.record(app.recording ? 'stop' : 'start').then(() => app.sync());

const smooth = hbSmooth;
const points = hbPoints;
const mode = hbMode;
const source = hbSource;
const mainSource = hbMainSource;
const resetView = resetCurveView;

const modesOpts = computed(() => CURVE_MODES.map((m) => ({ value: m.value, label: t(m.key) })));

/** Data source drop down: Main display / Average / Every connected or stored device. */
const sourceOptions = computed(() => {
  const list = [
    { value: 'main', label: t('hb.srcmain') },
    { value: 'avg', label: t('hb.average') },
  ];
  for (const d of app.devices) {
    if (d.connected || d.saved) list.push({ value: d.mac, label: `${d.name} (${d.mac})` });
  }
  return list;
});

const curveData = computed(() => app.curveOf(source.value));
const mainBpm = computed(() => app.bpmOf(mainSource.value));

const mainStats = computed(() => app.statsOf(mainSource.value));


const hcfg = ref({ sleepFactor: 0.88, activeFactor: 1.15, excitedFactor: 1.4, spikeDelta: 25, record: true });
const hHint = ref('');
async function loadHcfg(): Promise<void> {
  try {
    const c = (await api.healthConfig()) as {
      sleepFactor?: number;
      activeFactor?: number;
      excitedFactor?: number;
      spikeDelta?: number;
      record?: boolean;
    };
    if (typeof c.sleepFactor === 'number') {
      hcfg.value = {
        sleepFactor: c.sleepFactor,
        activeFactor: c.activeFactor ?? 1.15,
        excitedFactor: c.excitedFactor ?? 1.4,
        spikeDelta: c.spikeDelta ?? 25,
        record: c.record !== false,
      };
    }
  } catch {
    /* Keep Default */
  }
}
const calibrating = computed(() => !!app.health?.calibrating);
const calibrateRemain = computed(() => app.health?.calibrateRemain ?? 0);
async function saveHealth(): Promise<void> {
  await api.healthConfig({ ...hcfg.value });
  markSaved(); // Basebar Unified Saved
}
// Health Parameters Retain
const autoHealth = useAutoSave('heartbeat-health', () => hcfg.value, saveHealth);
function calibrate(): void {
  void api
    .healthAction(calibrating.value ? 'cancel' : 'calibrate')
    .then(() => app.sync());
}

// - Suspended window (data source / refresh interval / style customization / locking)
const fsrc = ref('');
const frefresh = ref(200);

const ffmt = ref('❤️{bpm}');
const fucolor = ref('#00FF00');
const flcolor = ref('#FF6600');
const fimg = ref('');
const fgeo = ref('');
const hrLoaded = ref(false);
watch(
  () => app.hrWindow,
  (w) => {
    if (!w) return;
    if (!hrLoaded.value) {
      hrLoaded.value = true;
      fsrc.value = w.source && w.source !== '平均' ? w.source : '';
      frefresh.value = w.refreshMs;
      if (w.format) ffmt.value = w.format;
      if (w.unlockedColor) fucolor.value = w.unlockedColor;
      if (w.lockedColor) flcolor.value = w.lockedColor;
      fimg.value = w.imagePath ?? '';
      if (w.geometry) fgeo.value = w.geometry;
    }
  },
  { immediate: true },
);
const overlaySources = computed(() => {

  const list = [{ value: '', label: t('hb.average') }];
  for (const d of app.devices) {
    if (d.connected || d.saved) list.push({ value: d.mac, label: `${d.name} (${d.mac})` });
  }
  return list;
});
const fLocked = computed(() => !!app.hrWindow?.locked);
async function saveOverlay(): Promise<void> {
  await api.floatConfig({
    source: fsrc.value || '平均',
    refreshMs: Number(frefresh.value) || 0,
    format: ffmt.value,
    unlockedColor: fucolor.value,
    lockedColor: flcolor.value,
    imagePath: fimg.value.trim(),
    geometry: fgeo.value.trim(),
  });
  await app.sync();
  hrLoaded.value = false;
  markSaved(); // Basebar Unified Saved
}

const autoOverlay = useAutoSave(
  'heartbeat-overlay',
  () => ({ src: fsrc.value, refresh: frefresh.value, fmt: ffmt.value, uc: fucolor.value, lc: flcolor.value, img: fimg.value, geo: fgeo.value }),
  saveOverlay,
);
watch(hrLoaded, (v) => {
  if (v) autoOverlay.hydrated();
}, { immediate: true });
function toggleLock(): void {
  void api.floatLock(!fLocked.value).then(() => app.sync());
}

function openAllDeviceFloats(): void {
  void api.floatOpenAll().then(() => app.sync());
}
function closeAllFloats(): void {
  void api.floatCloseAll().then(() => app.sync());
}


const TABLES = ['hr_records', 'health_records', 'osc_records', 'variables'];
const FORMATS = ['json', 'yaml', 'csv', 'sqlite'];
const tablesOpts = computed(() => TABLES.map((v) => ({ value: v, label: v })));
const formatsOpts = computed(() => FORMATS.map((v) => ({ value: v, label: v })));
const exTable = ref('hr_records');
const exFormat = ref('json');
const exLimit = ref(5000);
const exHint = ref('');
async function doExport(): Promise<void> {
  try {
    const r = await api.export({ table: exTable.value, format: exFormat.value, limit: exLimit.value });
    exHint.value = r.ok ? `${r.file ?? ''}` : (r.error ?? 'fail');
  } catch {
    exHint.value = 'export failed';
  }
  window.setTimeout(() => (exHint.value = ''), 4000);
}

onMounted(() => {
  void loadHcfg().then(() => autoHealth.hydrated());
});

</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1 v-bubble="t('hb.curve') + '\n' + t('hb.health') + '\n' + t('hb.overlay')">{{ t('tab.heartbeat') }}</h1>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <!-- Operation line (toolbar, no grid) -->
      <div class="x-gap" style="flex-wrap: wrap">
        <button class="x-btn primary" @click="toggleScan">
          <component :is="app.scanning ? Square : Play" class="x-btn-ico" />
          {{ app.scanning ? t('scan.stop') : t('scan.start') }}
        </button>
        <button class="x-btn" @click="toggleRecord">
          {{ app.recording ? t('record.stop') : t('record.start') }}
        </button>
        <span class="x-muted" style="font-size: 12px">
          {{ t('dev.connected') }}: {{ app.connectedCount }} · {{ t('hb.overlay') }}: {{ app.floatCount }}
        </span>
      </div>

      <CardGrid view="heartbeat" :cards="CARDS">

        <template #main>
          <div style="min-width: 168px; display: flex; flex-direction: column; justify-content: center">
            <div class="x-muted" style="font-size: 12px">{{ t('hb.current') }}</div>
            <div style="font-size: 54px; font-weight: 700; line-height: 1.05; color: var(--primary)">
              <RollingNumber :value="mainBpm || null" />
            </div>
            <div class="x-muted" style="font-size: 12px; display: flex; gap: 6px; flex-wrap: wrap" v-bubble="t('hb.winstats')">
              <span>{{ t('hb.min') }} <b class="x-mono"><RollingNumber :value="mainStats?.min ?? null" /></b></span>
              <span>·</span>
              <span>{{ t('hb.average') }} <b class="x-mono"><RollingNumber :value="mainStats?.avg ?? null" /></b></span>
              <span>·</span>
              <span>{{ t('hb.max') }} <b class="x-mono"><RollingNumber :value="mainStats?.max ?? null" /></b></span>
            </div>
            <div style="margin-top: var(--gap-2); display: flex; gap: 6px; flex-wrap: wrap">
              <span class="x-muted" style="font-size: 12px">{{ t('hb.health') }}:</span>
              <span style="font-size: 12px; font-weight: 600">{{ t(healthKey) }}</span>
            </div>
          </div>
          <div class="x-grow">
            <BpmChart :data="curveData" :height="170" :smooth="smooth" :points="points" :mode="mode" />
          </div>
        </template>

      <!-- Curve display -->
      <template #head-curve>
        {{ t('hb.curve') }}
        <span class="x-grow"></span>
        <button class="x-btn sm ghost" @click="resetView">{{ t('common.reset') }}</button>
      </template>
      <template #curve>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hb.srccurve') }}</label>
          <XSelect v-model="source" style="width: 210px" :items="sourceOptions" />
        </div>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hb.srcmainhr') }}</label>
          <XSelect v-model="mainSource" style="width: 210px" :items="sourceOptions" />
        </div>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hb.mode') }}</label>
          <XSelect v-model="mode" style="width: 150px" :items="modesOpts" />
        </div>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hb.smooth') }}</label>
          <XRange
            v-bubble="'≥1'"
            :model-value="smooth"
            :min="1"
            :max="30"
            :step="1"
            :hard-min="1"
            :default="1"
            @update:model-value="(v) => (smooth = v)"
            @change="flushPrefsSync"
          />
        </div>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hb.window') }}</label>
          <XRange
            v-bubble="'≥1'"
            :model-value="points"
            :min="30"
            :max="600"
            :step="1"
            :hard-min="1"
            :default="180"
            @update:model-value="(v) => (points = v)"
            @change="flushPrefsSync"
          />
        </div>
      </template>

      <!-- Health status and determination parameters -->
      <template #head-health>
        <span v-bubble="t('common.autosave')">{{ t('hb.health') }}</span>
      </template>
      <template #health>
        <div class="x-gap" style="flex-wrap: wrap">
          <button v-if="!calibrating" class="x-btn sm" @click="calibrate">{{ t('hb.calibrate') }}</button>
          <template v-else>
            <button class="x-btn sm primary" disabled>{{ t('hb.calibrating') }} {{ calibrateRemain }}s</button>
            <button class="x-btn sm ghost" @click="calibrate">{{ t('hb.cancelcal') }}</button>
          </template>
          <span class="x-muted" style="font-size: 12px">
            {{ t('hb.baseline') }}:
            <span class="x-mono">{{ app.health?.restingBpm?.toFixed?.(1) ?? app.health?.restingBpm ?? '—' }}</span>
          </span>
        </div>
        <div class="x-gap" style="flex-wrap: wrap">
          <label class="x-muted" style="font-size: 12.5px">{{ t('hb.sleepfactor') }}</label>
          <XRange
            :model-value="hcfg.sleepFactor"
            :min="0.5"
            :max="1.5"
            :step="0.01"
            :hard-min="0"
            :default="0.88"
            @update:model-value="(v) => (hcfg.sleepFactor = v)"
          />
          <label class="x-muted" style="font-size: 12.5px">{{ t('hb.activefactor') }}</label>
          <XRange
            :model-value="hcfg.activeFactor"
            :min="1"
            :max="2"
            :step="0.01"
            :hard-min="1"
            :default="1.15"
            @update:model-value="(v) => (hcfg.activeFactor = v)"
          />
          <label class="x-muted" style="font-size: 12.5px">{{ t('hb.excitedfactor') }}</label>
          <XRange
            :model-value="hcfg.excitedFactor"
            :min="1"
            :max="2.5"
            :step="0.01"
            :hard-min="1"
            :default="1.4"
            @update:model-value="(v) => (hcfg.excitedFactor = v)"
          />
        </div>
        <div class="x-gap" style="flex-wrap: wrap">
          <label class="x-muted" style="font-size: 12.5px">{{ t('hb.spike') }}</label>
          <XRange
              :model-value="hcfg.spikeDelta"
              :min="0"
              :max="60"
              :step="1"
              :hard-min="0"
            :default="25"
            @update:model-value="(v) => (hcfg.spikeDelta = v)"
          />
          <XSwitch v-model="hcfg.record" :label="t('hb.record')" />
        </div>
      </template>

      <!-- Floating Window -->
      <template #head-overlay>
        <span v-bubble="t('common.autosave')">{{ t('hb.overlay') }}</span>
        <span v-if="app.floatCount > 0" class="x-muted" style="font-size: 12px">· {{ app.floatCount }}</span>
      </template>
      <template #overlay>
        <div class="x-gap" style="flex-wrap: wrap">
          <FloatToggle />
        </div>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.source') }}</label>
          <XSelect v-model="fsrc" class="x-grow" :items="overlaySources" />
        </div>
        <div class="x-gap">
          <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.refresh') }}</label>
          <XRange
            :model-value="frefresh"
            :min="0"
            :max="2000"
            :step="50"
            :hard-min="0"
            :default="200"
            suffix="ms"
            @update:model-value="(v) => (frefresh = v)"
          />
        </div>
        <div class="x-gap" style="flex-wrap: wrap">
          <button class="x-btn sm" @click="toggleLock">
            {{ fLocked ? t('float.unlock') : t('float.lock') }}
          </button>

          <button class="x-btn sm" :disabled="app.connectedCount === 0" @click="openAllDeviceFloats">
            <MonitorPlay class="x-btn-ico" />
            {{ t('float.openall') }}
          </button>
          <button class="x-btn sm ghost" :disabled="app.floatCount === 0" @click="closeAllFloats">
            {{ t('float.closeall') }}
          </button>
        </div>

        <div style="display: flex; flex-direction: column; gap: var(--gap-2); border-top: var(--hairline) solid var(--border); padding-top: var(--gap-2)">
          <div class="x-gap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.format') }}</label>
            <input v-model="ffmt" class="x-input x-mono x-grow" spellcheck="false" v-bubble="t('float.fmthint')" />
            <button class="x-btn sm ghost" v-bubble="t('float.default')" @click="ffmt = '\u2764\ufe0f{bpm}'">⟲</button>
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.unlockcolor') }}</label>
            <input v-model="fucolor" class="x-input" type="color" style="width: 56px; padding: 2px" v-bubble="t('float.unlockcolor')" />
            <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.lockcolor') }}</label>
            <input v-model="flcolor" class="x-input" type="color" style="width: 56px; padding: 2px" v-bubble="t('float.lockcolor')" />
          </div>
          <div class="x-gap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.image') }}</label>
            <input v-model="fimg" class="x-input x-mono x-grow" spellcheck="false" placeholder="C:\path\bg.png" />
            <button class="x-btn sm ghost" @click="fimg = ''">{{ t('common.clear') }}</button>
          </div>
          <div class="x-gap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 72px">{{ t('float.geometry') }}</label>
            <input v-model="fgeo" class="x-input x-mono" style="width: 170px" spellcheck="false" placeholder="200x80+100+100" />
          </div>
        </div>
      </template>

      <!-- Data Export (L666: result hint moved into the body) -->
      <template #head-export>
        {{ t('hb.dataexport') }}
      </template>
      <template #export>
        <div class="x-gap" style="flex-wrap: wrap">
          <XSelect v-model="exTable" class="x-mono" style="flex: 1 1 0; min-width: 130px" :items="tablesOpts" />
          <XSelect v-model="exFormat" style="width: 92px" :items="formatsOpts" />
          <XRange
            :model-value="exLimit"
            :min="100"
            :max="50000"
            :step="100"
            :hard-min="1"
            :default="5000"
            @update:model-value="(v) => (exLimit = v)"
          />
        </div>
        <div class="x-gap">
          <button class="x-btn primary" v-bubble="t('logs.exporthint')" @click="() => void doExport()">
            <Download class="x-btn-ico" />
            {{ t('common.export') }}
          </button>
          <span v-if="exHint" class="x-muted x-mono" style="font-weight: 400; font-size: 11px; align-self: center">{{ exHint }}</span>
        </div>
      </template>

      <!-- Device List -->
      <template #head-devices>
        <Bluetooth class="x-btn-ico" />
        {{ t('tab.devices') }}
      </template>
      <template #devices>
        <div v-if="app.devices.length === 0" class="x-muted">{{ t('dev.empty') }}</div>
        <div v-else class="device-list">
          <div
            v-for="d in app.devices"
            :key="d.mac"
            :data-dev-mac="d.mac"
            :data-dev-name="d.name"
            :data-dev-connected="d.connected ? '1' : '0'"
            :data-dev-connecting="d.connecting ? '1' : '0'"
            class="dev-row"
            v-bubble="t('dev.clickhint')"
            @click="deviceRowClick(d.mac)"
            @dblclick="deviceRowDblClick(d)"
          >
            <DeviceDot :device="d" />
            <DeviceIcon :device="d" />
            <DeviceName :device="d" class="device-name" />
            <span class="x-muted x-mono device-mac">{{ d.mac }}</span>
            <span class="x-muted device-reading">{{ d.hasRssi ? `${d.rssi} dBm` : '—' }}</span>
            <span class="device-bpm">{{ d.connected && d.bpm > 0 ? `${d.bpm} BPM` : '—' }}</span>
            <span class="x-grow"></span>
            <button
              class="x-btn sm"
              :disabled="d.connecting"
              @click.stop="() => void (d.connected ? api.disconnect(d.mac) : api.connect(d.mac)).then(() => app.sync())"
            >
              {{ d.connecting ? t('dev.connecting') : d.connected ? t('dev.disconnect') : t('dev.connect') }}
            </button>
            <button
              class="x-btn sm ghost"
              :disabled="!d.connected"
              @click.stop="() => void (app.floatIds.includes(d.mac) ? api.floatClose(d.mac) : api.floatOpen(d.mac)).then(() => app.sync())"
            >
              {{ app.floatIds.includes(d.mac) ? t('float.closedev') : t('float.opendev') }}
            </button>
          </div>
        </div>
      </template>
      </CardGrid>
    </div>
  </div>
</template>

<style scoped>
.device-pager-actions {
  display: flex;
  gap: 2px;
}
.device-pager-actions .x-btn {
  width: var(--ctl-h);
  padding: 0;
}
.device-track {
  display: flex;
  gap: var(--gap-2);
  width: 100%;
  min-width: 0;
  overflow-x: auto;
  overscroll-behavior-x: contain;
  scroll-behavior: smooth;
  scroll-snap-type: x mandatory;
  scrollbar-width: thin;
}
.dev-row {
  display: flex;
  flex: 0 0 680px;
  align-items: center;
  gap: var(--gap-2);
  min-height: calc(var(--ctl-h) + var(--gap-2));
  padding: var(--gap-2);
  border: var(--hairline) solid var(--border);
  border-radius: calc(var(--radius) - 3px);
  background: var(--background);
  cursor: pointer;
  transition: background-color 0.14s linear;
}
.device-name {
  flex: 0 0 150px;
}
.device-mac,
.device-reading,
.device-bpm {
  flex: 0 0 auto;
  font-size: 12px;
  white-space: nowrap;
}
.device-bpm {
  min-width: 58px;
  font-weight: 600;
}
.dev-row:hover {
  background: var(--secondary);
}
@media (prefers-reduced-motion: reduce) {
  .device-track {
    scroll-behavior: auto;
  }
}
</style>
