<script setup lang="ts">
import { computed, nextTick, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Copy, Pencil, RefreshCw, Search, Trash2 } from 'lucide-vue-next';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { useAutoSave } from '../composables/useAutoSave';
import { markSaved } from '../stores/notify';
import { closeCtx, openCtx, type CtxItem } from '../contextmenu';
import XSwitch from '../components/XSwitch.vue';
import XSelect from '../components/XSelect.vue';
import XRange from '../components/XRange.vue';
import XDialog from '../components/XDialog.vue';
import RollingNumber from '../components/RollingNumber.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();
const app = useAppStore();

/** #9 Layout Editor: This page of cards (formerly vertical stacking of row cards, default full width; filter/search toolbar left outside the grid). */
const CARDS: GridCard[] = [
  { id: 'collect', titleKey: 'settings.advanced', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'manage', titleKey: 'hw.manage', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'custom', titleKey: 'hw.custom', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2); min-height: 0' },
  { id: 'vars', titleKey: 'hw.vars', span: 12, bodyStyle: 'padding: 0' },
];

const vars = ref<Record<string, string>>({});
const search = ref('');
const selectedTypes = ref<string[]>([]);
const selectedSources = ref<string[]>([]);
const selectedStatuses = ref<string[]>([]);
const hint = ref('');


interface HwCfg {
  intervalMs: number;
  useFloat: boolean;
  decimals: number;
  round: boolean;
  ntpServer: string;
  ntpPresets: string[];
  units: Record<string, string>;
  overrides: Record<string, string>;
  renames: Record<string, string>;
  custom: CustomVar[];
}
interface CustomVar {
  name: string;
  kind: string;
  expr: string;
  pattern?: string;
  unit?: string;
  enabled?: boolean;
}
const cfg = ref<HwCfg>({
  intervalMs: 2000,
  useFloat: false,
  decimals: 0,
  round: true,
  ntpServer: '',
  ntpPresets: [],
  units: {},
  overrides: {},
  renames: {},
  custom: [],
});

type FilterOption = { id: string; label: string; test: (name: string) => boolean };
type VarRow = {
  name: string;
  displayName: string;
  value: string;
  context: string;
  type: string;
  source: string;
  status: string;
};

const TYPE_FILTERS: FilterOption[] = [
  { id: 'cpu', label: 'CPU', test: (k) => k.startsWith('CPU') },
  { id: 'gpu', label: 'GPU', test: (k) => k.startsWith('GPU') || k.startsWith('VRAM') },
  { id: 'ram', label: 'RAM', test: (k) => k.startsWith('RAM') || k.startsWith('DIMM') || k.startsWith('OS_PHYS') },
  { id: 'disk', label: 'DISK', test: (k) => k.startsWith('DISK') || k.startsWith('DRIVE') },
  { id: 'os', label: 'OS', test: (k) => k.startsWith('OS_') || k.startsWith('BIOS') || k.startsWith('MB_') },
  { id: 'hr', label: 'HR', test: (k) => k.startsWith('BPM') || k.startsWith('HR') || k.startsWith('HEALTH') },
  { id: 'other', label: 'OTHER', test: (k) => !TYPE_FILTERS.slice(0, -1).some((f) => f.test(k)) },
];
const SOURCE_FILTERS: FilterOption[] = [
  { id: 'custom', label: 'CUSTOM', test: (k) => cfg.value.custom.some((item) => item.name === k) },
  { id: 'derived', label: 'DERIVED', test: (k) => Object.values(cfg.value.renames).includes(k) },
  {
    id: 'system',
    label: 'SYSTEM',
    test: (k) => !cfg.value.custom.some((item) => item.name === k) && !Object.values(cfg.value.renames).includes(k),
  },
];
const STATUS_FILTERS: FilterOption[] = [
  { id: 'modified', label: 'MODIFIED', test: (k) => tweaked.value.has(k) },
  { id: 'default', label: 'DEFAULT', test: (k) => !tweaked.value.has(k) },
];

function toggleFilter(selected: string[], id: string): void {
  const at = selected.indexOf(id);
  if (at >= 0) selected.splice(at, 1);
  else selected.push(id);
}

function matchFilter(selected: string[], options: FilterOption[], name: string): boolean {
  return selected.length === 0 || selected.some((id) => options.find((option) => option.id === id)?.test(name));
}

function queryTerms(input: string): { field: 'all' | 'name' | 'context'; value: string }[] {
  return input
    .trim()
    .split(/\s+/)
    .filter(Boolean)
    .map((token) => {
      const match = /^(name|context)=(.*)$/i.exec(token);
      return match
        ? { field: match[1].toLowerCase() as 'name' | 'context', value: match[2].toLowerCase() }
        : { field: 'all', value: token.toLowerCase() };
    });
}

function rowFor(name: string, value: string): VarRow {
  const renamedFrom = Object.entries(cfg.value.renames).find(([, display]) => display === name)?.[0];
  const displayName = cfg.value.renames[name] || (renamedFrom ? name : name);
  const custom = cfg.value.custom.find((item) => item.name === name);
  const type = TYPE_FILTERS.find((option) => option.id !== 'other' && option.test(name))?.id ?? 'other';
  const source = custom ? 'custom' : renamedFrom ? 'derived' : 'system';
  const status = tweaked.value.has(name) ? 'modified' : 'default';
  const context = custom
    ? [type, source, status, custom.kind, custom.expr, custom.pattern, custom.unit].filter(Boolean).join(' ')
    : [type, source, status, renamedFrom, cfg.value.overrides[name], cfg.value.units[name]].filter(Boolean).join(' ');
  return { name, displayName, value: String(wsVars.value[name] ?? value), context, type, source, status };
}

const rows = computed(() => {
  const terms = queryTerms(search.value);
  return Object.entries(vars.value)
    .map(([name, value]) => rowFor(name, value))
    .filter((row) => matchFilter(selectedTypes.value, TYPE_FILTERS, row.name))
    .filter((row) => matchFilter(selectedSources.value, SOURCE_FILTERS, row.name))
    .filter((row) => matchFilter(selectedStatuses.value, STATUS_FILTERS, row.name))
    .filter((row) =>
      terms.every((term) => {
        const fields = term.field === 'name'
          ? [row.name, row.displayName]
          : term.field === 'context'
            ? [row.context]
            : [row.name, row.displayName, row.value, row.context];
        return fields.some((field) => field.toLowerCase().includes(term.value));
      }),
    )
    .sort((a, b) => a.name.localeCompare(b.name));
});

async function load(): Promise<void> {
  try {
    const r = (await api.hw()) as Record<string, unknown>;
    const src = (r.vars ?? r) as Record<string, unknown>;
    const out: Record<string, string> = {};
    for (const [k, v] of Object.entries(src)) {
      if (v !== null && typeof v === 'object') continue;
      out[k] = String(v);
    }
    vars.value = out;
  } catch {
    /* Keep old table */
  }
}

async function loadCfg(): Promise<void> {
  try {
    const c = (await api.hwConfig()) as Partial<HwCfg>;
    cfg.value = {
      intervalMs: typeof c.intervalMs === 'number' ? c.intervalMs : 2000,
      useFloat: !!c.useFloat,
      decimals: typeof c.decimals === 'number' ? c.decimals : 0,
      round: c.round !== false,
      ntpServer: c.ntpServer ?? '',
      ntpPresets: c.ntpPresets ?? [],
      units: c.units ?? {},
      overrides: c.overrides ?? {},
      renames: c.renames ?? {},
      custom: (c.custom ?? []).filter((item) => !!item && typeof item.name === 'string' && item.name.trim() !== ''),
    };
  } catch {
    /* Default for Backend Not Available */
  }
}

async function saveCfg(): Promise<void> {
  const { intervalMs, useFloat, decimals, round, ntpServer } = cfg.value;
  await api.hwConfig({ intervalMs, useFloat, decimals, round, ntpServer });
  markSaved(); // Basebar Unified Saved
  await loadCfg();
  void load();
}

function flash(msg: string): void {
  hint.value = msg;
  window.setTimeout(() => (hint.value = ''), 1800);
}


const vName = ref('');
const vOverride = ref('');
const vUnit = ref('');
const vRename = ref('');
const varDialogOpen = ref(false);
const overrideInput = ref<HTMLInputElement | null>(null);


async function applyVar(action: 'override' | 'unit' | 'rename', value: string): Promise<void> {
  const name = vName.value.trim();
  if (!name) return;
  const body: Record<string, unknown> = { action, name };
  if (value.trim() !== '') body.value = value.trim();
  const r = (await api.hwVar(body)) as { vars?: Record<string, string> };
  if (r.vars) vars.value = r.vars;
  await loadCfg();
  flash(t('hw.ovsaved'));
}

/** , loads the variable to the management form. */
function pickVar(name: string): void {
  vName.value = name;
  vOverride.value = cfg.value.overrides[name] ?? '';
  vUnit.value = cfg.value.units[name] ?? '';
  vRename.value = cfg.value.renames[name] ?? '';
}

function openVarDialog(name: string): void {
  pickVar(name);
  varDialogOpen.value = true;
  void nextTick(() => overrideInput.value?.focus());
}

async function copyVar(name: string): Promise<void> {
  await navigator.clipboard.writeText(name);
  flash(t('api.copied'));
}

function openVarMenu(event: MouseEvent, name: string): void {
  event.preventDefault();
  event.stopPropagation();
  const edit = (field: 'override' | 'unit' | 'rename') => {
    closeCtx();
    openVarDialog(name);
    if (field !== 'override') void nextTick(() => document.getElementById(`hw-var-${field}`)?.focus());
  };
  const items: CtxItem[] = [
    { label: name, disabled: true },
    { sep: true },
    { label: t('hw.override'), icon: 'pencil', onClick: () => edit('override') },
    { label: t('hw.unitset'), icon: 'pencil', onClick: () => edit('unit') },
    { label: t('hw.rename'), icon: 'pencil', onClick: () => edit('rename') },
    { sep: true },
    { label: t('api.copy'), icon: 'copy', onClick: () => { closeCtx(); void copyVar(name); } },
  ];
  openCtx(event, items);
}

function onVarKey(event: KeyboardEvent, name: string): void {
  if (event.key !== 'Enter' && event.key !== ' ') return;
  event.preventDefault();
  openVarDialog(name);
}

const tweaked = computed(() => {
  // A collection of variables that have been overwritten/renamed/added to mark in tables
  const s = new Set<string>();
  for (const k of Object.keys(cfg.value.overrides)) s.add(k);
  for (const k of Object.keys(cfg.value.units)) s.add(k);
  for (const k of Object.keys(cfg.value.renames)) s.add(k);
  return s;
});


const KINDS = [
  { id: 'expr', key: 'hw.kind.expr' },
  { id: 'concat', key: 'hw.kind.concat' },
  { id: 'regex', key: 'hw.kind.regex' },
  { id: 'cmd', key: 'hw.kind.cmd' },
];
const cv = ref<CustomVar>({ name: '', kind: 'expr', expr: '', pattern: '', unit: '', enabled: true });

const kindOpts = computed(() => KINDS.map((k) => ({ value: k.id, label: t(k.key) })));
const ntpOpts = computed(() => (cfg.value.ntpPresets ?? []).map((p) => ({ value: p, label: p })));

async function saveCustom(): Promise<void> {
  const name = cv.value.name.trim();
  if (!name) return;
  const r = (await api.hwVar({ action: 'custom', name, item: { ...cv.value, name } })) as {
    vars?: Record<string, string>;
  };
  if (r.vars) vars.value = r.vars;
  await loadCfg();
  flash(t('hw.cvsaved'));
  cv.value = { name: '', kind: 'expr', expr: '', pattern: '', unit: '', enabled: true };
}

async function removeCustom(name: string): Promise<void> {
  const r = (await api.hwVar({ action: 'remove', name })) as { vars?: Record<string, string> };
  if (r.vars) vars.value = r.vars;
  await loadCfg();
}

function editCustom(item: CustomVar): void {
  cv.value = { ...item, pattern: item.pattern ?? '', unit: item.unit ?? '', enabled: item.enabled !== false };
}

// Collect settings ready to be saved (variant overwrite was submitted on time)
const auto = useAutoSave(
  'hardware',
  () => {
    const { intervalMs, useFloat, decimals, round, ntpServer } = cfg.value;
    return { intervalMs, useFloat, decimals, round, ntpServer };
  },
  saveCfg,
);

onMounted(() => {
  void load();
  void loadCfg().then(() => auto.hydrated());
});


const wsVars = computed(() => app.sysVars);


function numVal(v: unknown): number | null {
  const n = typeof v === 'number' ? v : Number(String(v ?? '').trim());
  return Number.isFinite(n) ? n : null;
}
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.hwinfo') }}</h1>
        <p class="page-desc">
          {{ Object.keys(vars).length }} {{ t('hw.vars') }} · {{ t('hw.interval') }} {{ cfg.intervalMs }} ·
          {{ t('hw.custom') }} {{ cfg.custom.length }}
        </p>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <div class="hw-toolbar">
        <div class="x-gap hw-filter-group">
          <span class="x-muted hw-filter-label">{{ t('hw.kind') }}</span>
          <button
            v-for="option in TYPE_FILTERS"
            :key="option.id"
            type="button"
            class="x-btn sm"
            :class="{ primary: selectedTypes.includes(option.id) }"
            :aria-pressed="selectedTypes.includes(option.id)"
            @click="toggleFilter(selectedTypes, option.id)"
          >
            {{ option.label }}
          </button>
        </div>
        <div class="x-gap hw-filter-group">
          <span class="x-muted hw-filter-label">{{ t('common.source') }}</span>
          <button
            v-for="option in SOURCE_FILTERS"
            :key="option.id"
            type="button"
            class="x-btn sm"
            :class="{ primary: selectedSources.includes(option.id) }"
            :aria-pressed="selectedSources.includes(option.id)"
            @click="toggleFilter(selectedSources, option.id)"
          >
            {{ option.label }}
          </button>
        </div>
        <div class="x-gap hw-filter-group">
          <span class="x-muted hw-filter-label">{{ t('common.status') }}</span>
          <button
            v-for="option in STATUS_FILTERS"
            :key="option.id"
            type="button"
            class="x-btn sm"
            :class="{ primary: selectedStatuses.includes(option.id) }"
            :aria-pressed="selectedStatuses.includes(option.id)"
            @click="toggleFilter(selectedStatuses, option.id)"
          >
            {{ option.label }}
          </button>
        </div>
        <div class="x-gap hw-search-group">
          <Search class="x-btn-ico x-muted" />
          <input
            v-model="search"
            class="x-input"
            style="width: 280px"
            :placeholder="`${t('common.filter')} · name=x context=x`"
          />
          <button class="x-btn ghost" @click="() => void api.hwRefresh().then(() => load())">
            <RefreshCw class="x-btn-ico" />
            {{ t('common.refresh') }}
          </button>
          <span class="x-muted" style="font-size: 12px">{{ rows.length }}</span>
          <span v-if="hint" style="font-size: 12px; color: var(--good)">{{ hint }}</span>
        </div>
      </div>

      <CardGrid view="hwinfo" :cards="CARDS">
        <!-- Collection Settings -->
        <template #head-collect>
          {{ t('settings.advanced') }}
          <span class="x-grow"></span>
          <span class="x-muted" style="font-weight: 400; font-size: 11.5px">{{ t('common.autosave') }}</span>
        </template>
        <template #collect>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">{{ t('hw.interval') }}</label>
            <XRange
              :model-value="cfg.intervalMs"
              :min="200"
              :max="10000"
              :step="100"
              :hard-min="200"
              :default="1000"
              suffix="ms"
              @update:model-value="(v) => (cfg.intervalMs = v)"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('hw.decimals') }}</label>
            <XRange
              :model-value="cfg.decimals"
              :min="0"
              :max="6"
              :step="1"
              :hard-min="0"
              :hard-max="15"
              :default="1"
              @update:model-value="(v) => (cfg.decimals = v)"
            />
            <XSwitch v-model="cfg.useFloat" :label="t('hw.usefloat')" />
            <XSwitch v-model="cfg.round" :label="t('hw.round')" />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">{{ t('hw.ntp') }}</label>
            <input v-model="cfg.ntpServer" class="x-input x-mono" style="width: 220px" data-default="pool.ntp.org" />
            <XSelect
              style="width: 200px"
              :model-value="''"
              :items="ntpOpts"
              :placeholder="t('hw.ntppreset')"
              @update:model-value="(v) => (cfg.ntpServer = String(v))"
            />
          </div>
        </template>

        <!-- Variable Management: Overwrite / Unit / Change Name -->
        <template #head-manage>
          {{ t('hw.manage') }}
          <span class="x-muted" style="font-weight: 400; font-size: 11.5px">· {{ t('hw.clearhint') }}</span>
        </template>
        <template #manage>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('common.name') }}</label>
            <input v-model="vName" class="x-input x-mono" style="width: 260px" placeholder="CPU_FREQ_MHz" />
            <span class="x-muted" style="font-size: 11.5px">{{ t('hw.ovhint') }}</span>
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hw.override') }}</label>
            <input v-model="vOverride" class="x-input x-mono x-grow" style="min-width: 200px" />
            <button class="x-btn sm" :disabled="!vName.trim()" @click="() => void applyVar('override', vOverride)">
              {{ t('common.apply') }}
            </button>
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hw.unitset') }}</label>
            <input v-model="vUnit" class="x-input x-mono" style="width: 120px" placeholder="GHz" />
            <button class="x-btn sm" :disabled="!vName.trim()" @click="() => void applyVar('unit', vUnit)">
              {{ t('common.apply') }}
            </button>
            <span class="x-vsep" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('hw.rename') }}</label>
            <input v-model="vRename" class="x-input x-mono" style="width: 200px" />
            <button class="x-btn sm" :disabled="!vName.trim()" @click="() => void applyVar('rename', vRename)">
              {{ t('common.apply') }}
            </button>
          </div>
        </template>

        <!-- Custom Variables -->
        <template #head-custom>
          {{ t('hw.custom') }}
          <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ cfg.custom.length }}</span>
        </template>
        <template #custom>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('common.name') }}</label>
            <input v-model="cv.name" class="x-input x-mono" style="width: 200px" placeholder="CPU_GHZ" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('hw.kind') }}</label>
            <XSelect v-model="cv.kind" style="width: 120px" :items="kindOpts" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('common.unit') }}</label>
            <input v-model="cv.unit" class="x-input x-mono" style="width: 96px" placeholder="GHz" />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hw.expr') }}</label>
            <input v-model="cv.expr" class="x-input x-mono x-grow" style="min-width: 240px" placeholder="{CPU_FREQ_MHz}/1000" />
          </div>
          <div v-if="cv.kind === 'regex'" class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">{{ t('hw.pattern') }}</label>
            <input v-model="cv.pattern" class="x-input x-mono" style="width: 240px" placeholder="(\d+)" />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch
              :model-value="cv.enabled !== false"
              :label="t('common.enabled')"
              @update:model-value="(v) => (cv.enabled = v)"
            />
            <button class="x-btn sm primary" :disabled="!cv.name.trim()" @click="() => void saveCustom()">
              {{ t('common.save') }}
            </button>
          </div>

          <div v-if="cfg.custom.length > 0" style="display: flex; flex-direction: column; gap: 2px">
            <div
              v-for="item in cfg.custom"
              :key="item.name"
              class="x-row"
              style="padding-left: 0; padding-right: 0"
            >
              <span class="x-mono" style="min-width: 180px; color: var(--primary)">{{ item.name }}</span>
              <span class="x-muted" style="min-width: 64px">{{ item.kind }}</span>
              <span class="x-mono x-grow" style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                {{ item.expr }}
              </span>
              <span v-if="item.unit" class="x-muted x-mono">{{ item.unit }}</span>
              <button class="x-btn sm ghost" @click="editCustom(item)">
                <Pencil class="x-btn-ico" />
              </button>
              <button class="x-btn sm danger" @click="() => void removeCustom(item.name)">
                <Trash2 class="x-btn-ico" />
              </button>
            </div>
          </div>
        </template>

        <!-- Variable Table -->
        <template #vars>
          <div v-if="rows.length === 0" class="x-muted" style="padding: var(--gap-3)">{{ t('common.none') }}</div>
          <div
            v-for="row in rows"
            :key="row.name"
            class="hw-row x-row"
            role="button"
            tabindex="0"
            :aria-label="`${t('hw.manage')}: ${row.name}`"
            @click="openVarDialog(row.name)"
            @keydown="onVarKey($event, row.name)"
            @contextmenu="openVarMenu($event, row.name)"
          >
            <span class="x-mono hw-var-name">
              {{ row.name }}
              <small v-if="row.displayName !== row.name" class="x-muted">{{ row.displayName }}</small>
            </span>
            <span class="x-mono x-grow hw-var-value">
              <template v-if="numVal(row.value) !== null">
                <RollingNumber :value="numVal(row.value)" />
              </template>
              <template v-else>{{ row.value }}</template>
            </span>
            <span class="x-muted hw-var-meta">{{ row.type }} · {{ row.source }} · {{ row.status }}</span>
            <span v-if="tweaked.has(row.name)" class="x-muted" style="font-size: 11px">●</span>
            <Pencil class="x-btn-ico x-muted" aria-hidden="true" />
          </div>
        </template>
      </CardGrid>

      <XDialog :open="varDialogOpen" :title="t('hw.manage')" :desc="vName" size="lg" @close="varDialogOpen = false">
        <div class="hw-dialog-field">
          <label for="hw-var-override" class="x-muted">{{ t('hw.override') }}</label>
          <input id="hw-var-override" ref="overrideInput" v-model="vOverride" class="x-input x-mono x-grow" />
          <button class="x-btn sm" @click="() => void applyVar('override', vOverride)">{{ t('common.apply') }}</button>
        </div>
        <div class="hw-dialog-field">
          <label for="hw-var-unit" class="x-muted">{{ t('hw.unitset') }}</label>
          <input id="hw-var-unit" v-model="vUnit" class="x-input x-mono x-grow" />
          <button class="x-btn sm" @click="() => void applyVar('unit', vUnit)">{{ t('common.apply') }}</button>
        </div>
        <div class="hw-dialog-field">
          <label for="hw-var-rename" class="x-muted">{{ t('hw.rename') }}</label>
          <input id="hw-var-rename" v-model="vRename" class="x-input x-mono x-grow" />
          <button class="x-btn sm" @click="() => void applyVar('rename', vRename)">{{ t('common.apply') }}</button>
        </div>
        <span class="x-muted" style="font-size: 11.5px">{{ t('hw.clearhint') }}</span>
        <template #actions>
          <button class="x-btn" @click="() => void copyVar(vName)">
            <Copy class="x-btn-ico" />
            {{ t('api.copy') }}
          </button>
          <button class="x-btn primary" @click="varDialogOpen = false">{{ t('common.close') }}</button>
        </template>
      </XDialog>
    </div>
  </div>
</template>

<style scoped>
.hw-toolbar {
  display: flex;
  flex-direction: column;
  gap: var(--gap-1);
}
.hw-filter-group,
.hw-search-group {
  flex-wrap: wrap;
}
.hw-filter-label {
  min-width: 56px;
  font-size: 12px;
}
.hw-row {
  cursor: pointer;
}
.hw-row:focus-visible {
  outline: 2px solid var(--ring);
  outline-offset: -2px;
}
.hw-var-name {
  display: flex;
  min-width: 260px;
  flex-direction: column;
  color: var(--primary);
}
.hw-var-value {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.hw-var-meta {
  min-width: 180px;
  font-size: 11px;
  text-transform: uppercase;
}
.hw-dialog-field {
  display: grid;
  grid-template-columns: minmax(84px, auto) minmax(0, 1fr) auto;
  align-items: center;
  gap: var(--gap-2);
}
@media (max-width: 720px) {
  .hw-dialog-field {
    grid-template-columns: 1fr auto;
  }
  .hw-dialog-field label {
    grid-column: 1 / -1;
  }
  .hw-var-name {
    min-width: 160px;
  }
  .hw-var-meta {
    display: none;
  }
}
</style>
