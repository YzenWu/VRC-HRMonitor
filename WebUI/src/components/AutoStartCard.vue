<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { RefreshCw } from 'lucide-vue-next';
import { api } from '../api';
import XSwitch from './XSwitch.vue';
import XMultiSelect from './XMultiSelect.vue';

/** Login autostart card (P1): shared by both settings pages. Method selection applies immediately;
 * the status line always reflects the actual system registrations (not just the saved config). */
const { t } = useI18n();

const METHODS = ['task', 'run', 'startup'];
const methods = ref<string[]>([]);
const silent = ref(true);
const loading = ref(true);
const error = ref('');
/** A registration exists but points elsewhere or carries different arguments: offer a repair. */
const stale = ref(false);
const registeredKeys = ref<string[]>([]);

const opts = computed(() => METHODS.map((k) => ({ value: k, label: t(`autostart.${k}`) })));

function readStatus(r: Awaited<ReturnType<typeof api.autostart>>): void {
  methods.value = r.methods ?? [];
  silent.value = r.silent ?? true;
  const st = r.status?.methods ?? [];
  registeredKeys.value = st.filter((m) => m.registered).map((m) => m.key);
  stale.value = st.some((m) => m.registered && !m.current);
}

async function load(): Promise<void> {
  loading.value = true;
  error.value = '';
  try {
    readStatus(await api.autostart());
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
  } finally {
    loading.value = false;
  }
}

async function apply(): Promise<void> {
  if (loading.value) return;
  loading.value = true;
  error.value = '';
  try {
    const r = await api.autostartSet(methods.value, silent.value);
    if (r.ok === false) {
      error.value = r.error ?? '';
      readStatus(await api.autostart());
    } else {
      readStatus(r);
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e);
    try {
      readStatus(await api.autostart());
    } catch {
      // Keep the submission error when the status refresh also fails.
    }
  } finally {
    loading.value = false;
  }
}

function onMethods(v: string[]): void {
  if (loading.value) return;
  methods.value = v;
  void apply();
}

function onSilent(v: boolean): void {
  if (loading.value) return;
  silent.value = v;
  void apply();
}

const registeredText = computed(() =>
  registeredKeys.value.length > 0
    ? registeredKeys.value.map((k) => t(`autostart.${k}`)).join(' · ')
    : t('autostart.none'),
);

onMounted(load);
</script>

<template>
  <div class="x-gap" style="flex-wrap: wrap">
    <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.autostart') }}</label>
    <XMultiSelect
      v-bubble="t('settings.autostarthint')"
      style="width: 240px"
      :model-value="methods"
      :items="opts"
      :placeholder="t('autostart.disabled')"
      :disabled="loading"
      @update:model-value="onMethods"
    />
    <button class="x-btn sm ghost" :disabled="loading" @click="load">
      <RefreshCw class="x-btn-ico" :class="{ spin: loading }" />
    </button>
  </div>

  <div class="x-gap" style="flex-wrap: wrap">
    <XSwitch
      v-bubble="t('autostart.silenthint')"
      :model-value="silent"
      :label="t('autostart.silent')"
      :disabled="loading"
      style="min-width: 150px"
      @update:model-value="onSilent"
    />
  </div>

  <div class="x-muted" style="font-size: 12px; display: flex; flex-direction: column; gap: 3px">
    <span>{{ t('autostart.registered') }}: {{ registeredText }}</span>
    <span v-if="stale" style="color: var(--warn)">{{ t('autostart.stale') }}</span>
    <span v-if="error" style="color: var(--bad)">{{ error }}</span>
  </div>
</template>

<style scoped>
.spin {
  animation: aspin 0.9s linear infinite;
}
@keyframes aspin {
  to {
    transform: rotate(360deg);
  }
}
@media (prefers-reduced-motion: reduce) {
  .spin {
    animation: none;
  }
}
</style>
