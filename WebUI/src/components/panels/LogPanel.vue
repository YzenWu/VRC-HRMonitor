<script setup lang="ts">

import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../../stores/app';

const { t } = useI18n();
const app = useAppStore();

const lines = computed(() => app.logs.slice(-12));

function lineColor(l: string): string {
  if (l.includes('[ERROR]')) return 'var(--bad)';
  if (l.includes('[WARN]')) return 'var(--warn)';
  return 'inherit';
}
</script>

<template>
  <div class="x-mono" style="display: flex; flex-direction: column; gap: 1px; font-size: 11.5px; line-height: 1.5">
    <div v-if="lines.length === 0" class="x-muted">{{ t('common.none') }}</div>
    <div
      v-for="(l, i) in lines"
      :key="i"
      :style="{ color: lineColor(l), overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }"
    >
      {{ l }}
    </div>
  </div>
</template>
