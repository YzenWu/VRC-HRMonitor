<script setup lang="ts">
/** Panel: Fast action (scan / record / suspension window switch). */
import { useI18n } from 'vue-i18n';
import { api } from '../../api';
import { useAppStore } from '../../stores/app';
import FloatToggle from '../FloatToggle.vue';

const { t } = useI18n();
const app = useAppStore();
</script>

<template>
  <div class="x-gap" style="flex-wrap: wrap">
    <button class="x-btn sm primary" @click="() => void api.scan(app.scanning ? 'stop' : 'start').then(() => app.sync())">
      {{ app.scanning ? t('scan.stop') : t('scan.start') }}
    </button>
    <button class="x-btn sm" @click="() => void api.record(app.recording ? 'stop' : 'start').then(() => app.sync())">
      {{ app.recording ? t('record.stop') : t('record.start') }}
    </button>
    <FloatToggle />
  </div>
</template>
