<script setup lang="ts">
/** Panel: Health status + static benchmark. */
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../../stores/app';

const { t } = useI18n();
const app = useAppStore();

const healthKey = computed(() => {
  const k = String(app.health?.statusKey ?? '');
  return k.startsWith('hb.status.') ? k : 'hb.status.unknown';
});
</script>

<template>
  <div style="display: flex; flex-direction: column; gap: 4px; font-size: 12.5px">
    <div style="font-size: 22px; font-weight: 650">{{ t(healthKey) }}</div>
    <div class="x-muted">
      {{ t('hb.baseline') }}:
      <span class="x-mono">{{ app.health?.restingBpm || '—' }}</span>
    </div>
    <div class="x-muted">{{ t('record.label') }}: {{ app.recording ? t('common.on') : t('common.off') }}</div>
  </div>
</template>
