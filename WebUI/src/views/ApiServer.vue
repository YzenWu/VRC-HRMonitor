<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Copy } from 'lucide-vue-next';
import { api } from '../api';
import { useAutoSave } from '../composables/useAutoSave';
import { markSaved } from '../stores/notify';
import XSwitch from '../components/XSwitch.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();


const CARDS: GridCard[] = [
  { id: 'basic', titleKey: 'api.enabled', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'endpoint', titleKey: 'api.endpoint', span: 12, bodyStyle: 'padding: 0' },
  { id: 'vars', titleKey: 'api.vars', span: 12 },
];

interface ApiCfg {
  enabled: boolean;
  token: string;
  pushSysInfo: boolean;
  pushIntervalMs: number;
  sysInfoVars: string[];
  webhookThrottleMs: number;
  varNames: string[];
}

const cfg = ref<ApiCfg | null>(null);
const search = ref('');
const hint = ref('');
const port = ref(8228);

const endpoints = computed(() => [
  `http://127.0.0.1:${port.value}/heartbeat`,
  `http://127.0.0.1:${port.value}/api/sysinfo?template={HR}`,
  `ws://127.0.0.1:${port.value}/ws`,
]);

const filteredVars = computed(() => {
  const q = search.value.trim().toLowerCase();
  const all = cfg.value?.varNames ?? [];
  return q === '' ? all : all.filter((v) => v.toLowerCase().includes(q));
});

async function load(): Promise<void> {
  const c = (await api.apiConfig()) as unknown as ApiCfg;
  cfg.value = {
    enabled: !!c.enabled,
    token: c.token ?? '',
    pushSysInfo: !!c.pushSysInfo,
    pushIntervalMs: c.pushIntervalMs ?? 1000,
    sysInfoVars: c.sysInfoVars ?? [],
    webhookThrottleMs: c.webhookThrottleMs ?? 0,
    varNames: c.varNames ?? [],
  };
  port.value = Number(location.port || 8228);
}

async function save(): Promise<void> {
  if (!cfg.value) return;
  const { enabled, token, pushSysInfo, pushIntervalMs, sysInfoVars, webhookThrottleMs } = cfg.value;
  await api.apiConfig({ enabled, token, pushSysInfo, pushIntervalMs, sysInfoVars, webhookThrottleMs });
  markSaved(); // Align " saved " in the bottom bar (replace only copy/failer type hint)
}

function flash(msg: string): void {
  hint.value = msg;
  window.setTimeout(() => (hint.value = ''), 1800);
}

function toggleVar(name: string): void {
  if (!cfg.value) return;
  const set = new Set(cfg.value.sysInfoVars);
  if (set.has(name)) set.delete(name);
  else set.add(name);
  cfg.value.sysInfoVars = [...set];
}

function selectFiltered(on: boolean): void {
  if (!cfg.value) return;
  const set = new Set(cfg.value.sysInfoVars);
  for (const v of filteredVars.value) {
    if (on) set.add(v);
    else set.delete(v);
  }
  cfg.value.sysInfoVars = [...set];
}

async function copy(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
    flash(t('api.copied'));
  } catch {

  }
}


const auto = useAutoSave('apiserver', () => cfg.value, save);

onMounted(() => void load().then(() => auto.hydrated()));
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.apiserver') }}</h1>
        <p class="page-desc" style="color: var(--warn)">{{ t('api.warn') }}</p>
      </div>
      <EditLayoutBtn />
    </header>

    <div v-if="!cfg" class="page-body x-muted">{{ t('mon.loading') }}</div>

    <div v-else class="page-body">
      <CardGrid view="apiserver" :cards="CARDS">
        <!-- Basic -->
        <template #head-basic>
          {{ t('api.enabled') }}
          <span v-if="hint" class="x-muted" style="font-weight: 400; font-size: 12px">· {{ hint }}</span>
          <span class="x-grow"></span>
          <span class="x-muted" style="font-weight: 400; font-size: 11.5px">{{ t('common.autosave') }}</span>
        </template>
        <template #basic>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch v-model="cfg.enabled" :label="t('api.enabled')" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('api.token') }}</label>
            <input v-model="cfg.token" class="x-input x-mono" style="width: 240px" :placeholder="t('api.tokenhint')" />
            <span class="x-muted" style="font-size: 11.5px">{{ t('api.tokenhint') }}</span>
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch v-model="cfg.pushSysInfo" :label="t('api.push')" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('api.interval') }}</label>
            <XRange
              v-model="cfg.pushIntervalMs"
              :min="200"
              :max="60000"
              :step="100"
              :hard-min="200"
              :default="2000"
              suffix="ms"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('api.throttle') }}</label>
            <XRange
              v-model="cfg.webhookThrottleMs"
              :min="0"
              :max="10000"
              :step="100"
              :hard-min="0"
              :default="1000"
              suffix="ms"
            />
            <span class="x-muted" style="font-size: 11.5px">{{ t('api.throttlehint') }}</span>
          </div>
        </template>

        <!-- Peer -->
        <template #endpoint>
          <div v-for="e in endpoints" :key="e" class="x-row">
            <span class="x-mono x-grow" style="font-size: 12.5px">{{ e }}</span>
            <button class="x-btn sm ghost" @click="() => void copy(e)">
              <Copy class="x-btn-ico" />
              {{ t('api.copy') }}
            </button>
          </div>
          <div class="x-muted" style="padding: var(--gap-2) var(--gap-3); font-size: 11.5px">
            {{ t('api.wshint') }}
          </div>
        </template>

        <!-- White List of Variables -->
        <template #head-vars>
          {{ t('api.vars') }}
          <span class="x-muted" style="font-weight: 400; font-size: 12px">
            · {{ cfg.sysInfoVars.length }} / {{ cfg.varNames.length }}
          </span>
        </template>
        <template #vars>
          <div class="x-gap" style="flex-wrap: wrap">
            <input v-model="search" class="x-input" style="width: 180px" :placeholder="t('common.filter')" />
            <button class="x-btn sm" v-bubble="t('api.varsall')" @click="selectFiltered(true)">{{ t('api.selectall') }}</button>
            <button class="x-btn sm ghost" @click="selectFiltered(false)">{{ t('common.clear') }}</button>
          </div>
          <div style="display: flex; flex-wrap: wrap; gap: 4px 10px; max-height: 260px; overflow: auto">
            <label
              v-for="v in filteredVars"
              :key="v"
              class="x-gap x-mono"
              style="font-size: 12px; min-width: 240px"
            >
              <input type="checkbox" :checked="cfg.sysInfoVars.includes(v)" @change="toggleVar(v)" />
              {{ v }}
            </label>
          </div>
        </template>
      </CardGrid>
    </div>
  </div>
</template>
