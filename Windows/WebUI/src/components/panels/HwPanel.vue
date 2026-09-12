<script setup lang="ts">

import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../../stores/app';

const { t } = useI18n();
const app = useAppStore();



const KEYS = ['CPU_USAGE', 'CPU_TEMP_MAX', 'CPU_FREQ_GHz', 'GPU_USAGE0', 'RAM_USED_GB', 'RAM_PERCENT'];

const rows = computed(() => KEYS.filter((k) => app.sysVars[k] !== undefined).map((k) => [k, app.sysVars[k]] as const));
</script>

<template>
  <div style="display: flex; flex-direction: column; gap: 2px; font-size: 12.5px">
    <div v-if="rows.length === 0" class="x-muted">{{ t('nav.waiting') }}</div>
    <div v-for="[k, v] in rows" :key="k" class="x-gap">
      <span class="x-mono x-muted" style="min-width: 130px">{{ k }}</span>
      <span class="x-mono">{{ v }}</span>
    </div>
  </div>
</template>
