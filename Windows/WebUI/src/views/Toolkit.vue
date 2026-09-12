<script setup lang="ts">
import { computed, onActivated, onBeforeUnmount, onDeactivated, ref, watch, type Component } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useI18n } from 'vue-i18n';
import { Activity, Copy, Database, ExternalLink, FileJson, Gamepad2, Images, Play, RefreshCw, ScrollText, Trash2, Wrench } from 'lucide-vue-next';
import { api, type ToolkitCacheAnalyze, type ToolkitCleanResult, type ToolkitGameStats, type ToolkitLogList, type ToolkitPaths, type ToolkitPhoto, type ToolkitProcess, type ToolkitScanProgress, type ToolkitVrchatConfig } from '../api';
import { useAppStore } from '../stores/app';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import BarChart from '../components/BarChart.vue';
import BpmChart from '../components/BpmChart.vue';
import XDialog from '../components/XDialog.vue';
import XRange from '../components/XRange.vue';
import XSelect from '../components/XSelect.vue';
import XSwitch from '../components/XSwitch.vue';

/** P10 Toolkit: dock entry (/toolkit?tool=xxx), one CardGrid section per tool. */
const { t } = useI18n();
const route = useRoute();
const router = useRouter();
const app = useAppStore();

const TOOLS = ['config', 'logs', 'cache', 'photos', 'game', 'process'] as const;
type Tool = (typeof TOOLS)[number];
const TOOL_ICONS: Record<Tool, Component> = { config: FileJson, logs: ScrollText, cache: Database, photos: Images, game: Gamepad2, process: Activity };

const cur = ref<Tool>('config');
function pick(tool: Tool): void {
  if (tool === cur.value) return;
  void router.replace({ query: { ...route.query, tool } });
}
function syncFromQuery(): void {
  const v = route.query.tool;
  if (typeof v === 'string' && (TOOLS as readonly string[]).includes(v)) cur.value = v as Tool;
}
watch(
  () => route.query.tool,
  () => {
    syncFromQuery();
    syncTimers();
    void loadTool(cur.value);
  },
);

// ---- Shared helpers ----
function fmtBytes(n: number): string {
  if (n < 1024) return `${Math.round(n)} B`;
  if (n < 1048576) return `${(n / 1024).toFixed(1)} KB`;
  if (n < 1073741824) return `${(n / 1048576).toFixed(1)} MB`;
  return `${(n / 1073741824).toFixed(2)} GB`;
}
function dur(sec: number): string {
  if (sec < 60) return `${Math.round(sec)}s`;
  if (sec < 3600) return `${Math.round(sec / 60)}m`;
  const h = Math.floor(sec / 3600);
  const m = Math.round((sec % 3600) / 60);
  return `${h}h${m > 0 ? ` ${m}m` : ''}`;
}
function baseName(p: string): string {
  return p.split(/[\\/]/).pop() ?? p;
}

// ---- Tool: config editor (table fields + strict JSON + path hints) ----
const paths = ref<ToolkitPaths>();
const cfgInfo = ref<ToolkitVrchatConfig>();
const draft = ref<Record<string, unknown>>({});
const cfgHint = ref('');
const FIELDS = ['cache_directory', 'cache_size', 'cache_expiry_delay', 'disableRichPresence', 'dynamic_bone_max_affected_transform_count', 'dynamic_bone_max_collider_check_count', 'screenshot_res_width', 'screenshot_res_height', 'camera_res_width', 'camera_res_height', 'picture_output_folder', 'picture_output_split_by_date', 'fpv_steadycam_fov'];
const BOOL_FIELDS = new Set(['disableRichPresence', 'picture_output_split_by_date']);
const STR_FIELDS = new Set(['cache_directory', 'picture_output_folder']);
type FieldKind = 'bool' | 'num' | 'str';
const kindOf = (f: string): FieldKind => (BOOL_FIELDS.has(f) ? 'bool' : STR_FIELDS.has(f) ? 'str' : 'num');
const cfgRows = computed(() => FIELDS.map((f) => ({ f, kind: kindOf(f), value: draft.value[f] })));

function setDraft(f: string, v: unknown): void {
  draft.value = { ...draft.value, [f]: v };
}
function onNumInput(f: string, e: Event): void {
  const raw = (e.target as HTMLInputElement).value.trim();
  setDraft(f, raw === '' ? 0 : Number(raw));
}
function onStrInput(f: string, e: Event): void {
  setDraft(f, (e.target as HTMLInputElement).value);
}

async function loadConfig(): Promise<void> {
  try {
    paths.value = await api.toolkitPaths();
  } catch {
    /* keep old snapshot */
  }
  try {
    cfgInfo.value = await api.toolkitVrchatConfig();
    draft.value = { ...cfgInfo.value.values };
  } catch {
    /* keep old snapshot */
  }
}
/** Strict JSON save: numeric/boolean/string fields must carry their JSON type verbatim. */
function saveConfig(): void {
  for (const f of FIELDS) {
    const v = draft.value[f];
    const kind = kindOf(f);
    if (kind === 'num' && (typeof v !== 'number' || !Number.isFinite(v))) {
      cfgHint.value = t('toolkit.jsonerr');
      return;
    }
    if (kind === 'bool' && typeof v !== 'boolean') {
      cfgHint.value = t('toolkit.jsonerr');
      return;
    }
    if (kind === 'str' && typeof v !== 'string') {
      cfgHint.value = t('toolkit.jsonerr');
      return;
    }
  }
  void api
    .toolkitVrchatConfigSave(draft.value)
    .then((r) => {
      cfgHint.value = r.ok ? t('common.saved') : String(r.error ?? '');
      if (r.ok) void loadConfig();
    })
    .catch(() => {
      cfgHint.value = t('toolkit.jsonerr');
    });
}

const pathRows = computed(() => {
  const p = paths.value;
  if (!p) return [];
  const rows: { k: string; v: string; tag?: string }[] = [
    { k: 'base', v: p.base },
    { k: 'config', v: p.config },
    { k: 'logs', v: p.logs },
    { k: 'cache', v: p.cache, tag: p.cacheOverridden ? t('toolkit.override') : undefined },
    { k: 'photos', v: p.photos, tag: p.photoOverridden ? t('toolkit.override') : undefined },
    { k: 'picture_output_split_by_date', v: p.splitByDate ? t('common.on') : t('common.off') },
  ];
  return rows;
});

// ---- Tool: log browser (file list + Join/Leave/Exception highlight, monospace) ----
const logFiles = ref<ToolkitLogList['files']>([]);
const logName = ref('');
const logFilter = ref('');
const logLines = ref<string[]>([]);
const logHint = ref('');
const logOpts = computed(() => logFiles.value.map((f) => ({ value: f.name, label: `${f.name} · ${fmtBytes(f.size)}` })));
const filteredLines = computed(() => {
  const q = logFilter.value.trim().toLowerCase();
  if (!q) return logLines.value;
  return logLines.value.filter((l) => l.toLowerCase().includes(q));
});

async function loadLogs(): Promise<void> {
  try {
    const r = await api.toolkitLogs();
    logFiles.value = r.files;
    logHint.value = '';
    if (!logName.value && r.files.length > 0) {
      logName.value = r.files[0].name;
      void readLog();
    }
  } catch {
    logHint.value = t('toolkit.loglocal');
  }
}
async function readLog(): Promise<void> {
  if (!logName.value) return;
  try {
    logLines.value = (await api.toolkitLogRead(logName.value)).lines;
    logHint.value = '';
  } catch {
    logHint.value = t('toolkit.loglocal');
  }
}
function lineCls(l: string): string {
  if (/exception|error|fail/i.test(l)) return 'exc';
  if (/join/i.test(l)) return 'join';
  if (/leav|left|disconnect/i.test(l)) return 'leave';
  return '';
}

// ---- Tool: cache manager (analyze + VRChat-running guard + dry-run XDialog) ----
const cacheInfo = ref<ToolkitCacheAnalyze>();
const cacheHint = ref('');
const cleanDlg = ref(false);
const dry = ref<ToolkitCleanResult | null>(null);
const cleaning = ref(false);
const proc = ref<ToolkitProcess | null>(null);
const vrRunning = computed(() => proc.value?.running ?? cfgInfo.value?.vrchatRunning ?? false);

async function loadCache(): Promise<void> {
  try {
    cacheInfo.value = await api.toolkitCacheAnalyze();
  } catch {
    /* keep old snapshot */
  }
  try {
    proc.value = await api.toolkitProcess();
  } catch {
    /* keep old snapshot */
  }
}
function openClean(): void {
  cleanDlg.value = true;
  dry.value = null;
  void api
    .toolkitCacheClean(false)
    .then((r) => (dry.value = r))
    .catch(() => (dry.value = { ok: false, error: t('toolkit.cleanfail') }));
}
async function doClean(): Promise<void> {
  cleaning.value = true;
  try {
    const r = await api.toolkitCacheClean(true);
    if (r.ok) {
      cacheHint.value = t('toolkit.cleared');
      cleanDlg.value = false;
      await loadCache();
    } else {
      cacheHint.value = String(r.error ?? '');
    }
  } catch {
    cacheHint.value = t('toolkit.vrrunning');
  } finally {
    cleaning.value = false;
  }
}

// ---- Tool: photo gallery (manual index + concurrency + progress + token search) ----
const concurrency = ref(2);
const scan = ref<ToolkitScanProgress>({ ok: true, running: false, done: 0, total: 0, indexed: 0 });
const scanHint = ref('');
const query = ref('');
const photoRows = ref<ToolkitPhoto[]>([]);
const photoTotal = ref(0);
const selected = ref<ToolkitPhoto | null>(null);
const photoHint = ref('');
const scanPct = computed(() => (scan.value.total ? Math.round((scan.value.done * 100) / scan.value.total) : 0));

async function doSearch(): Promise<void> {
  try {
    const r = await api.toolkitPhotosSearch(query.value.trim());
    photoRows.value = r.rows;
    photoTotal.value = r.total;
  } catch {
    /* keep old rows */
  }
}
async function startScan(): Promise<void> {
  try {
    scan.value = await api.toolkitPhotosScan(Math.round(concurrency.value));
    scanHint.value = t('toolkit.scanning');
    syncTimers();
  } catch {
    /* backend refused (safe mode / already running) */
  }
}
async function pollScan(): Promise<void> {
  try {
    const wasRunning = scan.value.running;
    scan.value = await api.toolkitPhotosScanStatus();
    if (wasRunning && !scan.value.running) {
      scanHint.value = `${t('toolkit.scandone')}: ${scan.value.indexed}`;
      if (scan.value.done > 0) void doSearch();
    }
  } catch {
    // Backend without a GET scan endpoint: stop polling silently, POST response keeps the last snapshot.
    stopScanPoll();
  }
}
async function copyId(id: string): Promise<void> {
  if (!id) return;
  try {
    await navigator.clipboard.writeText(id);
    photoHint.value = t('toolkit.copied');
  } catch {
    photoHint.value = id;
  }
  window.setTimeout(() => (photoHint.value = ''), 2200);
}
function openVr(kind: 'user' | 'world', id: string): void {
  if (!id) return;
  window.open(`https://vrchat.com/home/${kind}/${id}`, '_blank', 'noopener');
}

// ---- Tool: game stats (days selector + BarChart hours + BpmChart heart rate) ----
const DAYS = [7, 30, 90];
const days = ref(30);
const DAY_KEYS: Record<number, string> = { 7: 'toolkit.d7', 30: 'toolkit.d30', 90: 'toolkit.d90' };
const daysOpts = computed(() => DAYS.map((d) => ({ value: d, label: t(DAY_KEYS[d]) })));
const gameStats = ref<ToolkitGameStats | null>(null);
const hoursValues = computed(() => gameStats.value?.rows.map((r) => r.hours) ?? []);
const hoursLabels = computed(() => gameStats.value?.rows.map((r) => r.date) ?? []);
const bpmValues = computed(() => gameStats.value?.rows.map((r) => r.avgBpm) ?? []);
const gameSummary = computed(() => {
  const g = gameStats.value;
  if (!g || g.rows.length === 0) return t('toolkit.nodata');
  const avg = g.rows.reduce((a, b) => a + b.avgBpm, 0) / g.rows.length;
  const max = g.rows.reduce((a, b) => Math.max(a, b.maxBpm), 0);
  return `${t('toolkit.total')}: ${Number(g.totalHours ?? 0).toFixed(1)} h · ${t('toolkit.avgbpm')}: ${Math.round(avg)} · ${t('toolkit.maxbpm')}: ${max}`;
});

async function loadGame(): Promise<void> {
  try {
    gameStats.value = await api.toolkitGameStats(days.value);
  } catch {
    gameStats.value = null;
  }
}

// ---- Tool: process (live CPU/RAM + frontend-kept history curves) ----
const cpuHist = ref<number[]>([]);
const ramHist = ref<number[]>([]);
async function pollProc(): Promise<void> {
  try {
    const p = await api.toolkitProcess();
    proc.value = p;
    if (p.running) {
      cpuHist.value = [...cpuHist.value.slice(-89), Number(p.cpu ?? 0)];
      ramHist.value = [...ramHist.value.slice(-89), Number(p.ramBytes ?? 0) / 1048576];
    } else {
      cpuHist.value = [];
      ramHist.value = [];
    }
  } catch {
    /* keep last snapshot */
  }
}

// ---- Section switching + poll lifecycle (view lives inside KeepAlive) ----
async function loadTool(tool: Tool): Promise<void> {
  if (tool === 'config') await loadConfig();
  else if (tool === 'logs') await loadLogs();
  else if (tool === 'cache') await loadCache();
  else if (tool === 'photos') {
    await doSearch();
    void pollScan();
  } else if (tool === 'game') await loadGame();
  else if (tool === 'process') await pollProc();
}

let scanTimer: number | undefined;
let procTimer: number | undefined;
function stopScanPoll(): void {
  if (scanTimer) {
    window.clearInterval(scanTimer);
    scanTimer = undefined;
  }
}
function stopTimers(): void {
  stopScanPoll();
  if (procTimer) {
    window.clearInterval(procTimer);
    procTimer = undefined;
  }
}
function syncTimers(): void {
  stopTimers();
  if (cur.value === 'photos') scanTimer = window.setInterval(() => void pollScan(), 1000);
  if (cur.value === 'process') procTimer = window.setInterval(() => void pollProc(), 2000);
}

onActivated(() => {
  syncFromQuery();
  syncTimers();
  void loadTool(cur.value);
});
onDeactivated(stopTimers);
onBeforeUnmount(stopTimers);

const CARDS: Record<Tool, GridCard[]> = {
  config: [
    { id: 'cfg-paths', titleKey: 'toolkit.paths', span: 12 },
    { id: 'cfg-fields', titleKey: 'toolkit.config', span: 12 },
  ],
  logs: [{ id: 'logs-view', titleKey: 'toolkit.logs', span: 12, bodyStyle: 'display:flex;flex-direction:column;gap:10px' }],
  cache: [{ id: 'cache-usage', titleKey: 'toolkit.cache', span: 12, bodyStyle: 'display:flex;flex-direction:column;gap:10px' }],
  photos: [
    { id: 'photo-scan', titleKey: 'toolkit.index', span: 4 },
    { id: 'photo-gallery', titleKey: 'toolkit.gallery', span: 8 },
  ],
  game: [
    { id: 'game-hours', titleKey: 'toolkit.hours', span: 6 },
    { id: 'game-bpm', titleKey: 'toolkit.avgbpm', span: 6 },
  ],
  process: [
    { id: 'proc-now', titleKey: 'toolkit.process', span: 12 },
    { id: 'proc-cpu', titleKey: 'toolkit.cpuhist', span: 6 },
    { id: 'proc-ram', titleKey: 'toolkit.ramhist', span: 6 },
  ],
};
const cards = computed(() => CARDS[cur.value]);
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.toolkit') }}</h1>
        <p class="page-desc">{{ t('toolkit.desc') }}</p>
      </div>
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <!-- Tool switcher: dock links land on /toolkit?tool=xxx -->
      <div class="x-gap tk-tabs">
        <button
          v-for="tool in TOOLS"
          :key="tool"
          class="x-btn"
          :class="{ primary: cur === tool }"
          v-bubble="t(`toolkit.${tool}desc`)"
          @click="pick(tool)"
        >
          <component :is="TOOL_ICONS[tool]" class="x-btn-ico" />
          {{ t(`toolkit.${tool}`) }}
        </button>
      </div>

      <CardGrid view="toolkit" :cards="cards">
        <!-- Config: path hints (live paths + splitByDate + config overrides) -->
        <template #cfg-paths>
          <div class="tk-paths">
            <div v-for="row in pathRows" :key="row.k">
              <span class="tk-k">{{ row.k }}</span>
              <code>{{ row.v }}</code>
              <span v-if="row.tag" class="tk-tag">{{ row.tag }}</span>
            </div>
          </div>
        </template>

        <!-- Config: table editor + strict JSON save -->
        <template #cfg-fields>
          <table class="tk-table">
            <thead>
              <tr>
                <th style="width: 34%">{{ t('toolkit.field') }}</th>
                <th style="width: 14%">{{ t('toolkit.type') }}</th>
                <th>{{ t('common.value') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="row in cfgRows" :key="row.f">
                <td class="x-mono">{{ row.f }}</td>
                <td class="x-muted">{{ row.kind }}</td>
                <td>
                  <XSwitch
                    v-if="row.kind === 'bool'"
                    :model-value="row.value === true"
                    @update:model-value="(v) => setDraft(row.f, v)"
                  />
                  <input
                    v-else-if="row.kind === 'num'"
                    class="x-input"
                    type="number"
                    :value="typeof row.value === 'number' ? row.value : 0"
                    @change="onNumInput(row.f, $event)"
                  />
                  <input
                    v-else
                    class="x-input x-mono"
                    type="text"
                    :value="typeof row.value === 'string' ? row.value : ''"
                    @input="onStrInput(row.f, $event)"
                  />
                </td>
              </tr>
            </tbody>
          </table>
          <div class="x-gap" style="margin-top: 10px">
            <button class="x-btn primary" :disabled="app.safeMode || vrRunning" @click="saveConfig">
              {{ t('common.save') }}
            </button>
            <span class="x-muted" :class="{ 'tk-bad': cfgHint === t('toolkit.jsonerr') }">{{ vrRunning ? t('toolkit.vrrunning') : cfgHint }}</span>
          </div>
          <p class="x-muted tk-note">{{ t('toolkit.jsonhint') }}</p>
        </template>

        <!-- Logs: file list + content + Join/Leave/Exception highlight -->
        <template #logs-view>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSelect
              :model-value="logName"
              :items="logOpts"
              :placeholder="t('toolkit.picklog')"
              style="width: 300px"
              @update:model-value="(v) => { logName = String(v); void readLog(); }"
            />
            <button class="x-btn ghost" @click="() => void loadLogs()">
              <RefreshCw class="x-btn-ico" />
              {{ t('common.refresh') }}
            </button>
            <input v-model="logFilter" class="x-input" style="width: 220px" :placeholder="t('toolkit.filterhint')" />
            <span class="x-muted">{{ filteredLines.length }} {{ t('toolkit.lines') }}</span>
            <span v-if="logHint" class="x-muted">{{ logHint }}</span>
            <span class="x-grow"></span>
            <span class="tk-legend">
              <i class="dot join" />{{ t('toolkit.join') }}
              <i class="dot leave" />{{ t('toolkit.leave') }}
              <i class="dot exc" />{{ t('toolkit.exception') }}
            </span>
          </div>
          <div class="tk-logbox x-mono">
            <div v-if="filteredLines.length === 0" class="x-muted">{{ t('common.none') }}</div>
            <div v-for="(l, i) in filteredLines" :key="i" class="tk-ln" :class="lineCls(l)">{{ l }}</div>
          </div>
        </template>

        <!-- Cache: usage analysis + guarded clean (dry-run XDialog) -->
        <template #cache-usage>
          <div class="tk-stat">
            <span class="tk-k">{{ t('toolkit.analyze') }}</span>
            <code>{{ cacheInfo?.path ?? '—' }}</code>
          </div>
          <div class="x-gap">
            <span>{{ cacheInfo?.files ?? 0 }} {{ t('toolkit.files') }}</span>
            <span>{{ fmtBytes(cacheInfo?.bytes ?? 0) }}</span>
          </div>
          <table v-if="cacheInfo?.breakdown?.length" class="tk-table">
            <thead>
              <tr>
                <th>{{ t('common.name') }}</th>
                <th>{{ t('toolkit.files') }}</th>
                <th>{{ t('toolkit.size') }}</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in cacheInfo.breakdown" :key="b.name">
                <td class="x-mono">{{ b.name }}</td>
                <td>{{ b.files }}</td>
                <td>{{ fmtBytes(b.bytes) }}</td>
              </tr>
            </tbody>
          </table>
          <div class="x-gap" style="flex-wrap: wrap">
            <button class="x-btn danger" :disabled="app.safeMode || vrRunning" v-bubble="t('toolkit.dryrun')" @click="openClean">
              <Trash2 class="x-btn-ico" />
              {{ t('toolkit.clear') }}
            </button>
            <span class="x-muted">{{ vrRunning ? t('toolkit.vrrunning') : cacheHint }}</span>
          </div>
        </template>

        <!-- Photos: manual index -->
        <template #photo-scan>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted">{{ t('toolkit.concurrency') }}</label>
            <XRange
              :model-value="concurrency"
              :min="2"
              :max="8"
              :step="1"
              :hard-min="1"
              :hard-max="16"
              :default="2"
              @update:model-value="(v) => (concurrency = v)"
            />
          </div>
          <div class="x-gap" style="margin-top: 10px">
            <button class="x-btn primary" :disabled="app.safeMode || scan.running" @click="() => void startScan()">
              <Play class="x-btn-ico" />
              {{ t('toolkit.start') }}
            </button>
            <span class="x-muted">{{ scanHint }}</span>
          </div>
          <div class="tk-scan">
            <div class="x-muted" style="font-size: 12px">
              {{ t('toolkit.progress') }}: {{ scan.done }} / {{ scan.total }} ({{ scanPct }}%) · {{ scan.indexed }}
            </div>
            <progress :value="scan.done" :max="scan.total || 1"></progress>
          </div>
        </template>

        <!-- Photos: search + result table + copy ID / open vrchat.com -->
        <template #photo-gallery>
          <div class="x-gap" style="flex-wrap: wrap">
            <input v-model="query" class="x-input grow" :placeholder="t('toolkit.searchhint')" @keydown.enter="() => void doSearch()" />
            <button class="x-btn" @click="() => void doSearch()">
              {{ t('common.search') }}
            </button>
            <span class="x-muted">{{ photoTotal }}</span>
            <span v-if="photoHint" class="x-muted">{{ photoHint }}</span>
          </div>
          <div class="tk-photo-layout">
            <table class="tk-table">
              <thead>
                <tr>
                  <th>{{ t('toolkit.file') }}</th>
                  <th>{{ t('toolkit.author') }}</th>
                  <th>{{ t('toolkit.world') }}</th>
                  <th>{{ t('toolkit.date') }}</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="p in photoRows"
                  :key="p.path"
                  :class="{ sel: selected?.path === p.path }"
                  @click="selected = p"
                >
                  <td class="x-mono">{{ baseName(p.path) }}</td>
                  <td>
                    <span class="tk-cell">
                      {{ p.author }}
                      <button class="x-btn sm ghost" v-bubble="`${t('toolkit.copyid')}: ${p.authorId}`" @click.stop="() => void copyId(p.authorId)">
                        <Copy class="x-btn-ico" />
                      </button>
                      <button class="x-btn sm ghost" v-bubble="t('toolkit.openvr')" :disabled="!p.authorId" @click.stop="openVr('user', p.authorId)">
                        <ExternalLink class="x-btn-ico" />
                      </button>
                    </span>
                  </td>
                  <td>
                    <span class="tk-cell">
                      {{ p.worldName }}
                      <button class="x-btn sm ghost" v-bubble="`${t('toolkit.copyid')}: ${p.worldId}`" @click.stop="() => void copyId(p.worldId)">
                        <Copy class="x-btn-ico" />
                      </button>
                      <button class="x-btn sm ghost" v-bubble="t('toolkit.openvr')" :disabled="!p.worldId" @click.stop="openVr('world', p.worldId)">
                        <ExternalLink class="x-btn-ico" />
                      </button>
                    </span>
                  </td>
                  <td class="x-muted">{{ p.capturedAt }}</td>
                </tr>
              </tbody>
            </table>
            <aside v-if="selected" class="tk-detail">
              <b class="x-mono">{{ baseName(selected.path) }}</b>
              <p class="x-muted" style="margin: 0">
                {{ selected.author }} · {{ selected.worldName }} · {{ fmtBytes(selected.size) }} · {{ selected.capturedAt }}
              </p>
              <pre class="x-mono">{{ selected.metadata }}</pre>
            </aside>
          </div>
        </template>

        <!-- Game: days + playtime BarChart -->
        <template #game-hours>
          <div class="x-gap" style="flex-wrap: wrap; margin-bottom: 10px">
            <label class="x-muted">{{ t('toolkit.days') }}</label>
            <XSelect
              :model-value="days"
              :items="daysOpts"
              style="width: 130px"
              @update:model-value="(v) => { days = Number(v); void loadGame(); }"
            />
            <span class="x-muted">{{ gameSummary }}</span>
          </div>
          <BarChart :values="hoursValues" :labels="hoursLabels" :height="140" highlight-last />
        </template>

        <!-- Game: average heart rate BpmChart -->
        <template #game-bpm>
          <div class="x-muted" style="font-size: 12.5px; margin-bottom: 10px">{{ t('toolkit.avgbpm') }}</div>
          <BpmChart :data="bpmValues" :height="140" mode="bars" />
        </template>

        <!-- Process: live CPU/RAM/PID/uptime -->
        <template #proc-now>
          <div v-if="proc?.running" class="tk-proc">
            <div class="tk-big">
              <span class="tk-k">{{ t('toolkit.cpu') }}</span>
              <b>{{ Number(proc.cpu ?? 0).toFixed(1) }}%</b>
            </div>
            <div class="tk-big">
              <span class="tk-k">{{ t('toolkit.ram') }}</span>
              <b>{{ fmtBytes(Number(proc.ramBytes ?? 0)) }}</b>
            </div>
            <div class="tk-big">
              <span class="tk-k">{{ t('toolkit.uptime') }}</span>
              <b>{{ dur(Number(proc.uptimeSec ?? 0)) }}</b>
            </div>
            <div class="tk-big">
              <span class="tk-k">{{ t('toolkit.pid') }}</span>
              <b>{{ proc.pid }}</b>
            </div>
          </div>
          <div v-else class="x-muted">{{ t('toolkit.vrnotrunning') }}</div>
        </template>

        <template #proc-cpu>
          <BpmChart :data="cpuHist" :height="110" />
        </template>
        <template #proc-ram>
          <BpmChart :data="ramHist" :height="110" />
        </template>
      </CardGrid>
    </div>

    <!-- Cache clean: dry-run preview first, confirm cleans -->
    <XDialog :open="cleanDlg" :title="t('toolkit.clear')" :desc="t('toolkit.cleanhint')" size="sm" @close="cleanDlg = false">
      <div v-if="dry">
        <p v-if="dry.ok" style="margin: 0">
          {{ t('toolkit.willclean') }}: {{ dry.files ?? 0 }} {{ t('toolkit.files') }} · {{ fmtBytes(dry.bytes ?? 0) }}
        </p>
        <p v-else style="margin: 0; color: var(--bad)">{{ dry.error }}</p>
      </div>
      <p v-else class="x-muted" style="margin: 0">{{ t('toolkit.dryrun') }}…</p>
      <template #actions>
        <button class="x-btn" @click="cleanDlg = false">{{ t('common.cancel') }}</button>
        <button class="x-btn danger" :disabled="!dry?.ok || cleaning" @click="() => void doClean()">
          {{ t('toolkit.cleanconfirm') }}
        </button>
      </template>
    </XDialog>
  </div>
</template>

<style scoped>
.tk-tabs {
  flex-wrap: wrap;
}
.tk-paths {
  display: grid;
  gap: 6px;
}
.tk-paths > div {
  display: flex;
  align-items: baseline;
  gap: 8px;
  flex-wrap: wrap;
}
.tk-k {
  min-width: 220px;
  font-size: 12px;
  color: var(--muted-foreground);
}
.tk-paths code,
.tk-stat code {
  overflow-wrap: anywhere;
}
.tk-tag {
  font-size: 11px;
  color: var(--primary);
}
.tk-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}
.tk-table th,
.tk-table td {
  text-align: left;
  padding: 7px 8px;
  border-bottom: var(--hairline) solid var(--border);
  vertical-align: middle;
}
.tk-table td input.x-input {
  width: 100%;
}
.tk-note {
  font-size: 11.5px;
  margin: 8px 0 0;
}
.tk-bad {
  color: var(--bad);
}
.tk-stat {
  display: flex;
  align-items: baseline;
  gap: 8px;
  flex-wrap: wrap;
}
.tk-legend {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  font-size: 11.5px;
  color: var(--muted-foreground);
}
.tk-legend .dot {
  width: 8px;
  height: 8px;
  border-radius: 2px;
  display: inline-block;
}
.tk-legend .dot.join {
  background: var(--good);
}
.tk-legend .dot.leave {
  background: var(--primary);
}
.tk-legend .dot.exc {
  background: var(--bad);
}
.tk-logbox {
  border: var(--hairline) solid var(--border);
  border-radius: var(--radius);
  padding: var(--gap-2);
  font-size: 11.5px;
  line-height: 1.55;
  max-height: 440px;
  overflow: auto;
}
.tk-ln {
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
.tk-ln.join {
  color: var(--good);
}
.tk-ln.leave {
  color: var(--primary);
}
.tk-ln.exc {
  color: var(--bad);
}
.tk-scan {
  margin-top: 10px;
  display: grid;
  gap: 6px;
}
.tk-scan progress {
  width: 100%;
}
.grow {
  flex: 1;
}
.tk-cell {
  display: inline-flex;
  align-items: center;
  gap: 2px;
}
.tk-photo-layout {
  display: grid;
  grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
  gap: 12px;
  margin-top: 10px;
}
.tk-table tbody tr {
  cursor: pointer;
}
.tk-table tbody tr:hover {
  background: var(--secondary);
}
.tk-table tbody tr.sel {
  background: var(--secondary);
}
.tk-detail {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 12px;
  min-width: 0;
}
.tk-detail pre {
  margin: 0;
  max-height: 220px;
  overflow: auto;
  font-size: 11px;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  border: var(--hairline) solid var(--border);
  border-radius: var(--radius);
  padding: var(--gap-2);
}
.tk-proc {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(150px, 1fr));
  gap: var(--gap-2);
}
.tk-big {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.tk-big b {
  font-size: 20px;
}
@media (max-width: 900px) {
  .tk-photo-layout {
    grid-template-columns: 1fr;
  }
  .tk-k {
    min-width: 130px;
  }
}
</style>
