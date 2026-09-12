<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Save } from 'lucide-vue-next';
import { api, type RecordingConfig, type RecordingConfigPatch } from '../api';
import XRange from './XRange.vue';
import XSwitch from './XSwitch.vue';

const { t } = useI18n();
const BACKENDS = ['sqlite', 'jsonl', 'csv'] as const;
const CATEGORIES = ['avatar', 'vrchat', 'devices', 'heartRate', 'hardware'] as const;
type Category = (typeof CATEGORIES)[number];

const config = ref<RecordingConfig | null>(null);
const loading = ref(true);
const saving = ref(false);
const message = ref('');
const failed = ref(false);

const categoryKeys: Record<Category, string> = {
  avatar: 'record.avatar',
  vrchat: 'record.vrchat',
  devices: 'record.devices',
  heartRate: 'record.heart',
  hardware: 'record.hardware',
};

const backendSet = computed(() => new Set(config.value?.backends ?? []));

function applyConfig(value: RecordingConfig): void {
  config.value = value;
}

async function load(): Promise<void> {
  loading.value = true;
  try {
    applyConfig(await api.recordConfig());
  } catch {
    failed.value = true;
    message.value = t('record.savefail');
  } finally {
    loading.value = false;
  }
}

function setBackend(name: string, enabled: boolean): void {
  if (!config.value) return;
  const next = new Set(config.value.backends);
  if (enabled) next.add(name);
  else next.delete(name);
  config.value.backends = [...next];
}

async function save(): Promise<void> {
  if (!config.value || saving.value) return;
  saving.value = true;
  failed.value = false;
  message.value = '';
  const patch: RecordingConfigPatch = {
    backends: [...config.value.backends],
    enabled: { ...config.value.enabled },
    retentionDays: { ...config.value.retentionDays },
  };
  try {
    const result = await api.recordConfig(patch);
    if (!result.ok) throw new Error('save failed');
    applyConfig(result);
    message.value = t('record.saved');
  } catch {
    failed.value = true;
    message.value = t('record.savefail');
    try {
      applyConfig(await api.recordConfig());
    } catch {
      // Preserve the save failure when rereading also fails.
    }
  } finally {
    saving.value = false;
    window.setTimeout(() => (message.value = ''), 3200);
  }
}

onMounted(load);
</script>

<template>
  <div v-if="config" class="record-card">
    <div class="record-section">
      <label class="record-label">{{ t('record.backends') }}</label>
      <div class="x-gap" style="flex-wrap: wrap">
        <XSwitch
          v-for="backend in BACKENDS"
          :key="backend"
          :model-value="backendSet.has(backend)"
          :label="t(`record.${backend}`)"
          :disabled="saving"
          @update:model-value="(value) => setBackend(backend, value)"
        />
      </div>
    </div>

    <div class="x-muted record-hint">{{ t('record.hint') }}</div>

    <div class="record-section">
      <label class="record-label">{{ t('record.categories') }}</label>
      <div v-for="category in CATEGORIES" :key="category" class="record-row">
        <XSwitch
          v-model="config.enabled[category]"
          :label="t(categoryKeys[category])"
          :disabled="saving"
          class="record-category"
        />
        <label class="x-muted record-retention">{{ t('record.retention') }}</label>
        <XRange
          v-model="config.retentionDays[category]"
          :min="0"
          :max="3650"
          :step="1"
          :hard-min="0"
          :default="0"
          @change="() => undefined"
        />
        <span v-if="config.retentionDays[category] === 0" class="x-muted record-forever">{{ t('record.forever') }}</span>
      </div>
    </div>

    <div class="record-section">
      <label class="record-label">{{ t('record.directory') }}</label>
      <span class="x-muted x-mono record-directory">{{ config.directory || '-' }}</span>
    </div>

    <div class="x-gap">
      <button class="x-btn sm primary" :disabled="saving || loading" @click="save">
        <Save class="x-btn-ico" />
        {{ t('common.save') }}
      </button>
      <span v-if="message" :class="failed ? 'record-error' : 'x-muted'">{{ message }}</span>
    </div>
  </div>
  <div v-else class="x-muted" :class="{ 'record-error': failed }">{{ message }}</div>
</template>

<style scoped>
.record-card,
.record-section {
  display: flex;
  flex-direction: column;
  gap: var(--gap-2);
}
.record-label {
  font-size: 12.5px;
  font-weight: 600;
}
.record-hint,
.record-directory,
.record-error {
  font-size: 11.5px;
}
.record-row {
  display: flex;
  align-items: center;
  gap: var(--gap-2);
  flex-wrap: wrap;
}
.record-category {
  min-width: 150px;
}
.record-retention {
  min-width: 72px;
  font-size: 12px;
}
.record-forever {
  font-size: 11.5px;
}
.record-directory {
  word-break: break-all;
}
.record-error {
  color: var(--bad);
}
</style>
