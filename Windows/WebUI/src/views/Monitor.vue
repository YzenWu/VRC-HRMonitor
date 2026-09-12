<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Download, RefreshCw } from 'lucide-vue-next';
import { api } from '../api';
import BarChart from '../components/BarChart.vue';
import XSelect from '../components/XSelect.vue';
import XRange from '../components/XRange.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();

interface HrStats {
  count: number;
  min: number;
  max: number;
  avg: number;
  median: number;
  sd: number;
  first: string;
  last: string;
  histogram: { from: number; to: number; count: number }[];
  trend: { hour: string; count: number; avg: number; min: number; max: number }[];
}
interface Report {
  hours: number;
  hr: HrStats;
  health: { count: number; items: { status: string; times: number; seconds: number; percent: number }[] };
  devices: { mac: string; name: string; count: number; avg: number; min: number; max: number; last: string }[];
  osc: { count: number; top: { addr: string; count: number; last: string }[] };
  db: { file: string; recording: boolean };
}

const RANGES: { hours: number; key: string }[] = [
  { hours: 1, key: 'mon.r1h' },
  { hours: 6, key: 'mon.r6h' },
  { hours: 24, key: 'mon.r24h' },
  { hours: 72, key: 'mon.r3d' },
  { hours: 168, key: 'mon.r7d' },
  { hours: 720, key: 'mon.r30d' },
  { hours: 0, key: 'mon.rall' },
];
const FORMATS = ['txt', 'json', 'yaml', 'csv'];
const rangesOpts = computed(() => RANGES.map((r) => ({ value: r.hours, label: t(r.key) })));
const formatsOpts = computed(() => FORMATS.map((v) => ({ value: v, label: v.toUpperCase() })));

const hours = ref(24);
const buckets = ref(16);
const format = ref('json');
const report = ref<Report | null>(null);
const loading = ref(false);
const hint = ref('');

async function load(): Promise<void> {
  loading.value = true;
  try {
    report.value = (await api.monitor(hours.value, buckets.value)) as unknown as Report;
  } catch {
    report.value = null;
  } finally {
    loading.value = false;
  }
}

async function exportReport(): Promise<void> {
  const r = (await api.monitorExport({ hours: hours.value, buckets: buckets.value, format: format.value })) as {
    ok?: boolean;
    file?: string;
  };
  hint.value = r.ok ? `${t('mon.exported')}: ${r.file ?? ''}` : t('mon.exportfail');
  window.setTimeout(() => (hint.value = ''), 3200);
}

onMounted(() => void load());

const hasData = computed(() => (report.value?.hr.count ?? 0) > 0);
const trendValues = computed(() => report.value?.hr.trend.map((p) => p.avg) ?? []);
const trendLabels = computed(() => report.value?.hr.trend.map((p) => p.hour) ?? []);
const histValues = computed(() => report.value?.hr.histogram.map((h) => Number(h.count)) ?? []);
const histLabels = computed(() => report.value?.hr.histogram.map((h) => `${h.from}-${h.to}`) ?? []);


const statusKey = (s: string) => {
  const k = String(s ?? '').toLowerCase();
  if (k.includes('sleep') || s.includes('睡眠')) return 'hb.status.sleep';
  if (k.includes('rest') || s.includes('静息') || s.includes('靜息')) return 'hb.status.rest';
  if (k.includes('excit') || s.includes('兴奋') || s.includes('興奮')) return 'hb.status.excited';
  if (k.includes('active') || s.includes('活跃') || s.includes('活躍')) return 'hb.status.active';
  return 'hb.status.unknown';
};

function dur(sec: number): string {
  if (sec < 60) return `${Math.round(sec)}s`;
  if (sec < 3600) return `${Math.round(sec / 60)}m`;
  const h = Math.floor(sec / 3600);
  const m = Math.round((sec % 3600) / 60);
  return `${h}h${m > 0 ? ` ${m}m` : ''}`;
}


const STATS = computed(() => {
  const hr = report.value?.hr;
  return [
    { key: 'mon.min', value: hr?.min ?? 0 },
    { key: 'hb.average', value: hr?.avg ?? 0 },
    { key: 'mon.median', value: hr?.median ?? 0 },
    { key: 'mon.max', value: hr?.max ?? 0 },
    { key: 'mon.sd', value: hr?.sd ?? 0 },
    { key: 'mon.samples', value: hr?.count ?? 0 },
  ];
});


const CARDS: GridCard[] = [
  { id: 'trend', titleKey: 'mon.trend', span: 12 },
  { id: 'histogram', titleKey: 'mon.histogram', span: 12 },
  { id: 'health', titleKey: 'mon.healthdist', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: 6px' },
  { id: 'devices', titleKey: 'mon.devicedist', span: 12, bodyStyle: 'padding: 0' },
  { id: 'osc', titleKey: 'mon.osctop', span: 12, bodyStyle: 'padding: 0' },
];
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.monitor') }}</h1>
        <p class="page-desc">
          {{ t('mon.last') }}: {{ report?.hr.last || '-' }}
          <span v-if="hint" style="color: var(--good)"> · {{ hint }}</span>
        </p>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <!-- Toolbar -->
      <div class="x-gap" style="flex-wrap: wrap">
        <label class="x-muted" style="font-size: 12.5px">{{ t('mon.range') }}</label>
        <XSelect
          style="width: 132px"
          :model-value="hours"
          :items="rangesOpts"
          @update:model-value="(v) => { hours = Number(v); void load(); }"
        />
        <label class="x-muted" style="font-size: 12.5px">{{ t('mon.buckets') }}</label>
        <XRange
          :model-value="buckets"
          :min="4"
          :max="64"
          :step="1"
          :hard-min="4"
          :default="12"
          @update:model-value="(v) => (buckets = v)"
          @change="load"
        />
        <button class="x-btn ghost" @click="() => void load()">
          <RefreshCw class="x-btn-ico" />
          {{ t('common.refresh') }}
        </button>
        <span class="x-vsep" />
        <XSelect v-model="format" style="width: 96px" :items="formatsOpts" />
        <button class="x-btn primary" @click="() => void exportReport()">
          <Download class="x-btn-ico" />
          {{ t('mon.exportreport') }}
        </button>
      </div>

      <div v-if="loading" class="x-card"><div class="x-card-body x-muted">{{ t('mon.loading') }}</div></div>
      <div v-else-if="!hasData" class="x-card"><div class="x-card-body x-muted">{{ t('mon.nodata') }}</div></div>

      <template v-else>
        <!-- Statistics 6 card -->
        <div style="display: flex; gap: var(--gap-2); flex-wrap: wrap">
          <div v-for="c in STATS" :key="c.key" class="x-card x-grow" style="min-width: 128px">
            <div class="x-card-body">
              <div class="x-muted" style="font-size: 12px">{{ t(c.key) }}</div>
              <div style="font-size: 20px; font-weight: 650">{{ c.value }}</div>
            </div>
          </div>
        </div>

        <CardGrid view="monitor" :cards="CARDS">
          <!-- Trends -->
          <template #head-trend>
            {{ t('mon.trend') }}
            <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ trendValues.length }}</span>
          </template>
          <template #trend>
            <BarChart :values="trendValues" :labels="trendLabels" :height="130" highlight-last />
            <div class="x-muted x-mono" style="display: flex; justify-content: space-between; font-size: 11px; margin-top: 4px">
              <span>{{ trendLabels[0] }}</span>
              <span>{{ trendLabels[trendLabels.length - 1] }}</span>
            </div>
          </template>

          <!-- Histogram -->
          <template #histogram>
            <BarChart :values="histValues" :labels="histLabels" :height="120" />
            <div class="x-muted x-mono" style="display: flex; justify-content: space-between; font-size: 11px; margin-top: 4px">
              <span>{{ report?.hr.min }}</span>
              <span>{{ report?.hr.max }}</span>
            </div>
          </template>

          <!-- Health distribution -->
          <template #health>
            <div v-if="(report?.health.items.length ?? 0) === 0" class="x-muted">{{ t('common.none') }}</div>
            <div v-for="h in report?.health.items" :key="h.status" class="x-gap" style="font-size: 12.5px">
              <span style="min-width: 72px">{{ t(statusKey(h.status)) }}</span>
              <div style="flex: 1; height: 8px; background: var(--muted); border-radius: 999px; overflow: hidden">
                <div :style="{ width: `${h.percent}%`, height: '100%', background: 'var(--primary)' }" />
              </div>
              <span class="x-mono x-muted" style="min-width: 120px; text-align: right">
                {{ dur(h.seconds) }} · {{ h.percent }}% · ×{{ h.times }}
              </span>
            </div>
          </template>

          <!-- Distribution of equipment -->
          <template #devices>
            <div v-for="d in report?.devices" :key="d.mac" class="x-row">
              <span style="min-width: 150px; font-weight: 500">{{ d.name || d.mac }}</span>
              <span class="x-muted x-mono" style="min-width: 140px">{{ d.mac }}</span>
              <span class="x-mono">{{ d.min }}–{{ d.max }} · {{ d.avg }}</span>
              <span class="x-grow"></span>
              <span class="x-muted x-mono">×{{ d.count }}</span>
            </div>
          </template>

          <!-- OSC Top -->
          <template #head-osc>
            {{ t('mon.osctop') }}
            <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ report?.osc.count ?? 0 }}</span>
          </template>
          <template #osc>
            <div v-if="(report?.osc.top.length ?? 0) === 0" class="x-muted" style="padding: var(--gap-3)">
              {{ t('common.none') }}
            </div>
            <div v-for="o in report?.osc.top" :key="o.addr" class="x-row">
              <span class="x-mono x-grow" style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                {{ o.addr }}
              </span>
              <span class="x-muted x-mono">×{{ o.count }}</span>
            </div>
          </template>
        </CardGrid>
      </template>
    </div>
  </div>
</template>
