<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import {
  Activity,
  Bluetooth,
  ChevronRight,
  Cpu,
  HeartPulse,
  Radio,
  ScrollText,
  Send,
  Server,
} from 'lucide-vue-next';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { hbMode, hbPoints, hbSmooth } from '../prefs';
import { deviceRowClick, deviceRowDblClick } from '../deviceDialog';
import BpmChart from '../components/BpmChart.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();
const app = useAppStore();


const SHORTCUTS = [
  { to: '/heartbeat', tab: 'heartbeat', icon: HeartPulse },
  { to: '/devices', tab: 'devices', icon: Bluetooth },
  { to: '/osc', tab: 'osc', icon: Radio },
  { to: '/pusher', tab: 'pusher', icon: Send },
  { to: '/hwinfo', tab: 'hwinfo', icon: Cpu },
  { to: '/monitor', tab: 'monitor', icon: Activity },
  { to: '/apiserver', tab: 'apiserver', icon: Server },
  { to: '/logs', tab: 'logs', icon: ScrollText },
];


const healthKey = computed(() => {
  const k = String(app.health?.statusKey ?? '');
  return k.startsWith('hb.status.') ? k : 'hb.status.unknown';
});

const STATS = computed(() => [
  { key: 'hb.current', value: app.bpm || '--' },
  { key: 'hb.average', value: app.avg || '--' },
  { key: 'dev.connected', value: app.connectedCount },
  { key: 'hb.overlay', value: app.floatCount },
  { key: 'record.label', value: app.recording ? t('common.on') : t('common.off') },
  { key: 'tab.osc', value: app.osc?.connected ? t('common.on') : t('common.off') },
]);

/** #9 Layout Editor: This page card (statistical entry is a dynamic list, left outside the grid). */
const CARDS: GridCard[] = [
  { id: 'curve', titleKey: 'hb.curve', span: 12 },
  { id: 'devices', titleKey: 'dev.history', span: 12, bodyStyle: 'padding: 0' },
];
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.overview') }}</h1>
        <p class="page-desc">
          {{ app.engineOnline ? t('nav.connected') : t('nav.waiting') }} ·
          {{ t('hb.health') }}: {{ t(healthKey) }}
        </p>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <!-- Statistical cards -->
      <div style="display: flex; gap: var(--gap-2); flex-wrap: wrap">
        <div v-for="s in STATS" :key="s.key" class="x-card x-grow" style="min-width: 124px">
          <div class="x-card-body">
            <div class="x-muted" style="font-size: 12px">{{ t(s.key) }}</div>
            <div style="font-size: 19px; font-weight: 650">{{ s.value }}</div>
          </div>
        </div>
      </div>

      <!-- Heartrate curve + device preview into grid; shortcut entry remains in grid for dynamic list External -->
      <CardGrid view="overview" :cards="CARDS">
        <template #head-curve>
          {{ t('hb.curve') }}
        </template>
        <template #curve>
          <BpmChart :data="app.curve" :height="150" :mode="hbMode" :smooth="hbSmooth" :points="hbPoints" />
          <!-- L666: quick actions live below the chart, not in the title row. -->
          <div class="x-gap" style="flex-wrap: wrap">
            <VrchatStatus compact />
            <span class="x-grow"></span>
            <button class="x-btn sm" @click="() => void api.scan(app.scanning ? 'stop' : 'start').then(() => app.sync())">
              {{ app.scanning ? t('scan.stop') : t('scan.start') }}
            </button>
            <button class="x-btn sm ghost" @click="() => void api.record(app.recording ? 'stop' : 'start').then(() => app.sync())">
              {{ app.recording ? t('record.stop') : t('record.start') }}
            </button>
          </div>
        </template>

        <template #head-devices>
          {{ t('dev.history') }}
          <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ app.devices.length }}</span>
        </template>
        <template #devices>
          <div v-if="app.devices.length === 0" class="x-muted" style="padding: var(--gap-3)">{{ t('dev.empty') }}</div>
          <div
            v-for="d in app.devices.slice(0, 6)"
            :key="d.mac"
            class="x-row"
            :data-dev-mac="d.mac"
            :data-dev-name="d.name"
            :data-dev-connected="d.connected ? '1' : '0'"
            :data-dev-connecting="d.connecting ? '1' : '0'"
            style="cursor: pointer"
            v-bubble="t('dev.clickhint')"
            @click="deviceRowClick(d.mac)"
            @dblclick="deviceRowDblClick(d)"
          >
            <span
              :style="{
                width: '7px',
                height: '7px',
                borderRadius: '50%',
                background: d.connected ? 'var(--good)' : 'var(--muted-foreground)',
                flexShrink: 0,
              }"
            />
            <span style="min-width: 160px; font-weight: 500">{{ d.name }}</span>
            <span class="x-muted x-mono">{{ d.mac }}</span>
            <span class="x-grow"></span>
            <span v-if="d.connected && d.bpm > 0" style="font-weight: 600">{{ d.bpm }} BPM</span>
            <span v-else class="x-muted">{{ d.hasRssi ? `${d.rssi} dBm` : '—' }}</span>
          </div>
        </template>
      </CardGrid>

      <!-- Shortcut -->
      <div style="display: flex; gap: var(--gap-2); flex-wrap: wrap">
        <RouterLink
          v-for="s in SHORTCUTS"
          :key="s.to"
          :to="s.to"
          class="x-card x-hover-card"
          style="min-width: 168px; flex: 1; text-decoration: none; color: inherit"
        >
          <div class="x-card-body x-gap">
            <component :is="s.icon" class="x-btn-ico" style="color: var(--primary)" />
            <span style="font-weight: 600; font-size: 13px">{{ t('tab.' + s.tab) }}</span>
            <span class="x-grow"></span>
            <ChevronRight class="x-btn-ico x-muted" />
          </div>
        </RouterLink>
      </div>
    </div>
  </div>
</template>
