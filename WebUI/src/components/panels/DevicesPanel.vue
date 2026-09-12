<script setup lang="ts">
/** Panel: Device schematic (up to 6 rows, indicator light and name high-light and device page).
 * Lines can be clicked to open the device details, double-click the connection/disconnect (the same set of triggers as the heart rate page/device page). */
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../../stores/app';
import { deviceRowClick, deviceRowDblClick } from '../../deviceDialog';
import DeviceDot from '../DeviceDot.vue';
import DeviceIcon from '../DeviceIcon.vue';
import DeviceName from '../DeviceName.vue';

const { t } = useI18n();
const app = useAppStore();
</script>

<template>
  <div style="display: flex; flex-direction: column; gap: 2px">
    <div v-if="app.devices.length === 0" class="x-muted" style="font-size: 12.5px">{{ t('dev.empty') }}</div>
    <div
      v-for="d in app.devices.slice(0, 6)"
      :key="d.mac"
      :data-dev-mac="d.mac"
      :data-dev-name="d.name"
      :data-dev-connected="d.connected ? '1' : '0'"
      :data-dev-connecting="d.connecting ? '1' : '0'"
      class="dev-p-row"
      style="display: flex; align-items: center; gap: var(--gap-2); font-size: 12.5px"
      v-bubble="t('dev.clickhint')"
      @click="deviceRowClick(d.mac)"
      @dblclick="deviceRowDblClick(d)"
    >
      <DeviceDot :device="d" :size="7" />
      <DeviceIcon :device="d" :size="14" />
      <DeviceName :device="d" style="min-width: 130px" />
      <span class="x-grow"></span>
      <span v-if="d.connected && d.bpm > 0" style="font-weight: 600">{{ d.bpm }} BPM</span>
      <span v-else class="x-muted">{{ d.hasRssi ? `${d.rssi} dBm` : '—' }}</span>
    </div>
  </div>
</template>

<style scoped>
.dev-p-row {
  padding: 1px 4px;
  border-radius: calc(var(--radius) - 3px);
  cursor: pointer;
  transition: background-color 0.14s linear;
}
.dev-p-row:hover {
  background: var(--secondary);
}
</style>
