<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Download, RefreshCw, Trash2 } from 'lucide-vue-next';
import { api } from '../api';
import XSwitch from '../components/XSwitch.vue';
import XSelect from '../components/XSelect.vue';
import XRange from '../components/XRange.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();


const CARDS: GridCard[] = [{ id: 'logbox', bare: true, span: 12 }];

const LEVELS = ['ERROR', 'WARN', 'INFO', 'DEBUG', 'TRACE'];
const FORMATS = ['txt', 'json', 'yaml', 'csv'];
const formatsOpts = computed(() => FORMATS.map((v) => ({ value: v, label: v.toUpperCase() })));

const filter = ref('');
const regex = ref(false);
const levels = ref<string[]>([]);
const follow = ref(true);

const logLimit = ref(Math.min(5000, Math.max(1, Number(localStorage.getItem('hrm-logs-limit')) || 800)));
function setLogLimit(v: number): void {
  logLimit.value = v;
  localStorage.setItem('hrm-logs-limit', String(v));
}
const format = ref('txt');
const lines = ref<string[]>([]);
const matched = ref(0);
const total = ref(0);
const err = ref('');
const hint = ref('');
const box = ref<HTMLDivElement | null>(null);

const traceEnabled = ref(false);
const traceActive = ref(false);

let timer: number | null = null;

async function loadTrace(): Promise<void> {
  try {
    const cfg = (await api.config()) as { logs?: { traceEnabled?: boolean; traceActive?: boolean } };
    traceEnabled.value = cfg.logs?.traceEnabled ?? false;
    traceActive.value = cfg.logs?.traceActive ?? false;
  } catch {
    /* Keep default when engine is not available */
  }
}

async function toggleTrace(v: boolean): Promise<void> {
  traceEnabled.value = v;
  await api.settings({ logs: { traceEnabled: v } });
  hint.value = t('logs.tracerestart');
  window.setTimeout(() => (hint.value = ''), 3200);
}

async function query(): Promise<void> {
  // And we'll check it locally, so we don't have to call the back end every time.
  if (regex.value && filter.value) {
    try {
      new RegExp(filter.value);
      err.value = '';
    } catch {
      err.value = t('logs.badregex');
      return;
    }
  } else {
    err.value = '';
  }
  try {
    const r = (await api.logs(filter.value, logLimit.value, regex.value, levels.value)) as {
      lines?: string[];
      matched?: number;
      total?: number;
      error?: string;
    };
    lines.value = r.lines ?? [];
    matched.value = r.matched ?? lines.value.length;
    total.value = r.total ?? 0;
    if (r.error) err.value = r.error;
    if (follow.value) {
      requestAnimationFrame(() => {
        if (box.value) box.value.scrollTop = box.value.scrollHeight;
      });
    }
  } catch {
    /* Keep old content when engine cannot reach */
  }
}

function toggleLevel(l: string): void {
  levels.value = levels.value.includes(l) ? levels.value.filter((x) => x !== l) : [...levels.value, l];
  void query();
}

async function doExport(): Promise<void> {
  const r = (await api.logsExport({ filter: filter.value, format: format.value, regex: regex.value, levels: levels.value })) as {
    ok?: boolean;
    file?: string;
    count?: number;
  };
  hint.value = r.ok ? `${t('logs.exported')}: ${r.file ?? ''} (${r.count ?? 0})` : t('logs.exportfail');
  window.setTimeout(() => (hint.value = ''), 3200);
}

async function doDump(): Promise<void> {
  const r = await api.logsDump();
  hint.value = r.ok ? `${t('logs.dumped')}: ${r.file ?? ''}` : t('logs.dumpfail');
  window.setTimeout(() => (hint.value = ''), 3200);
}

onMounted(() => {
  void query();
  void loadTrace();
  timer = window.setInterval(() => {
    if (follow.value) void query();
  }, 4000);
});
onUnmounted(() => {
  if (timer !== null) window.clearInterval(timer);
});

function lineColor(l: string): string {
  if (l.includes('[ERROR]')) return 'var(--bad)';
  if (l.includes('[WARN]')) return 'var(--warn)';
  if (l.includes('[DEBUG]') || l.includes('[TRACE]')) return 'var(--muted-foreground)';
  return 'inherit';
}

const countText = computed(() => `${t('logs.matched')} ${matched.value} / ${total.value}`);
</script>

<template>
  <div class="page-host" style="height: 100%">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.logs') }}</h1>
        <p class="page-desc">
          {{ countText }}
          <span v-if="err" style="color: var(--bad)"> · {{ err }}</span>
          <span v-if="hint" style="color: var(--good)"> · {{ hint }}</span>
        </p>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2); flex: 1; min-height: 0">
      <div class="x-gap" style="flex-wrap: wrap">
        <input
          v-model="filter"
          class="x-input"
          style="width: 240px"
          :placeholder="t('logs.filterhint')"
          @keydown.enter="query"
        />
        <XSwitch :model-value="regex" :label="t('logs.regex')" @update:model-value="(v) => { regex = v; void query(); }" />
        <XSwitch
          :model-value="traceEnabled"
          :label="t('logs.trace')"
          v-bubble="`${traceActive ? t('logs.traceactive') : t('logs.traceinactive')} · ${t('logs.tracehint')}`"
          @update:model-value="(v) => void toggleTrace(v)"
        />
        <span class="x-vsep" />
        <button
          v-for="l in LEVELS"
          :key="l"
          class="x-btn sm"
          :class="{ primary: levels.includes(l) }"
          @click="toggleLevel(l)"
        >
          {{ l }}
        </button>
        <span class="x-vsep" />
        <XSwitch v-model="follow" :label="t('logs.follow')" />
        <XRange
          :model-value="logLimit"
          :min="100"
          :max="5000"
          :step="100"
          :hard-min="1"
          :hard-max="5000"
          :default="800"
          suffix="#"
          @update:model-value="setLogLimit"
          @change="() => void query()"
        />
        <button class="x-btn ghost" @click="() => void query()">
          <RefreshCw class="x-btn-ico" />
          {{ t('common.refresh') }}
        </button>
        <button class="x-btn ghost" @click="() => void api.logsClear().then(() => query())">
          <Trash2 class="x-btn-ico" />
          {{ t('common.clear') }}
        </button>
        <span class="x-grow"></span>
        <XSelect v-model="format" style="width: 92px" :items="formatsOpts" />
        <button class="x-btn" @click="() => void doDump()">
          {{ t('logs.dump') }}
        </button>
        <button v-bubble="t('logs.exporthint')" class="x-btn primary" @click="() => void doExport()">
          <Download class="x-btn-ico" />
          {{ t('logs.exportfiltered') }}
        </button>
      </div>


      <CardGrid view="logs" :cards="CARDS" fill>
        <template #logbox>
          <div
            ref="box"
            class="x-card x-mono"
            style="height: 100%; min-height: 240px; overflow: auto; padding: var(--gap-2); font-size: 12px; line-height: 1.5"
          >
            <div v-if="lines.length === 0" class="x-muted">{{ t('common.none') }}</div>
            <div v-for="(l, i) in lines" :key="i" :style="{ color: lineColor(l), whiteSpace: 'pre-wrap' }">{{ l }}</div>
          </div>
        </template>
      </CardGrid>
    </div>
  </div>
</template>
