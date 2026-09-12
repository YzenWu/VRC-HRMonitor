<script setup lang="ts">


import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../../stores/app';
import { hbMainSource } from '../../prefs';
import RollingNumber from '../RollingNumber.vue';

const { t } = useI18n();
const app = useAppStore();
const main = computed(() => app.bpmOf(hbMainSource.value));
const stats = computed(() => app.statsOf(hbMainSource.value));
</script>

<template>
  <div style="display: flex; align-items: baseline; gap: var(--gap-3); flex-wrap: wrap">
    <div>
      <div class="x-muted" style="font-size: 12px">{{ t('hb.current') }}</div>
      <div style="font-size: 40px; font-weight: 700; line-height: 1.05; color: var(--primary)">
        <RollingNumber :value="main || null" />
      </div>
    </div>
    <div>
      <div class="x-muted" style="font-size: 12px">{{ t('hb.min') }}</div>
      <div style="font-size: 18px; font-weight: 650"><RollingNumber :value="stats ? stats.min : null" /></div>
    </div>
    <div>
      <div class="x-muted" style="font-size: 12px">{{ t('hb.average') }}</div>
      <div style="font-size: 18px; font-weight: 650"><RollingNumber :value="stats ? stats.avg : null" /></div>
    </div>
    <div>
      <div class="x-muted" style="font-size: 12px">{{ t('hb.max') }}</div>
      <div style="font-size: 18px; font-weight: 650"><RollingNumber :value="stats ? stats.max : null" /></div>
    </div>
    <div style="flex: 1; min-width: 0" />
    <div style="text-align: right">
      <div class="x-muted" style="font-size: 12px">{{ t('dev.connected') }}</div>
      <div style="font-size: 20px; font-weight: 650"><RollingNumber :value="app.connectedCount" /></div>
    </div>
  </div>
</template>
