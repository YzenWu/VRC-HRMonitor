<script setup lang="ts">





import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { LayoutDashboard, Radio, RefreshCw, Table, Trash2 } from 'lucide-vue-next';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { useOscStore } from '../stores/osc';
import { listCap } from '../prefs';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';
import VrchatStatus from '../components/VrchatStatus.vue';

const { t } = useI18n();
const app = useAppStore();
const osc = useOscStore();


const CARDS: GridCard[] = [
  { id: 'avatar', titleKey: 'osc.avatar', span: 4 },
  { id: 'world', titleKey: 'osc.world', span: 4 },
  { id: 'state', titleKey: 'osc.state', span: 4 },
  { id: 'motion', titleKey: 'osc.motion', span: 4 },
  { id: 'gesture', titleKey: 'osc.gesture', span: 4 },
  { id: 'tracking', titleKey: 'osc.tracking', span: 4 },
  { id: 'custom', titleKey: 'osc.custom', span: 12 },
];

const tab = ref<'overview' | 'data'>((localStorage.getItem('hrm-osc-tab') as 'overview' | 'data') || 'overview');
function setTab(v: 'overview' | 'data'): void {
  tab.value = v;
  localStorage.setItem('hrm-osc-tab', v);
}

onMounted(() => {
  osc.start();
  void osc.pull();
});

const P = '/avatar/parameters/';

/** Take values and format them: Boolean displays switches/ switches, float points retain 3 bits, no place is shown. */
function show(addr: string, digits = 3): string {
  const v = osc.value(addr);
  if (v === undefined || v === null) return '—';
  if (typeof v === 'boolean') return v ? t('common.on') : t('common.off');
  if (typeof v === 'number') return Number.isInteger(v) ? String(v) : v.toFixed(digits);
  return String(v);
}

/** Division definition: Address → Displays labels (labels are directly by parameter name and need not be translated across languages). */
const STATE_PARAMS = ['AFK', 'Seated', 'Grounded', 'Upright', 'MuteSelf', 'InStation', 'VRMode', 'TrackingType', 'Voice'];
const MOTION_PARAMS = ['VelocityMagnitude', 'VelocityX', 'VelocityY', 'VelocityZ', 'AngularY', 'ScaleFactor'];
const GESTURE_PARAMS = ['GestureLeft', 'GestureRight', 'GestureLeftWeight', 'GestureRightWeight', 'VRCEmote'];


const avatarParamCount = computed(() => osc.list.filter((i) => i.addr.startsWith(P)).length);

const trackingRows = computed(() => osc.list.filter((i) => i.addr.startsWith('/tracking/')));

const builtin = new Set([...STATE_PARAMS, ...MOTION_PARAMS, ...GESTURE_PARAMS]);
const customParams = computed(() =>
  osc.list.filter((i) => i.addr.startsWith(P) && !builtin.has(i.addr.slice(P.length))),
);


function latest(args?: unknown[]): string {
  if (!Array.isArray(args) || args.length === 0) return '-';
  const v = args[args.length - 1];
  if (v === null || v === undefined) return '-';
  if (typeof v === 'object') {
    const o = v as { v?: unknown };
    return o.v === null || o.v === undefined ? '-' : String(o.v);
  }
  return String(v);
}
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1 v-bubble="t('osc.pagehint')">{{ t('tab.osc') }}</h1>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">

      <div class="x-subtabs">
        <button class="x-subtab" :class="{ active: tab === 'overview' }" @click="setTab('overview')">
          <LayoutDashboard class="x-btn-ico" />
          {{ t('osc.taboverview') }}
        </button>
        <button class="x-subtab" :class="{ active: tab === 'data' }" @click="setTab('data')">
          <Table class="x-btn-ico" />
          {{ t('osc.tabdata') }}
          <span class="x-muted">{{ osc.list.length }}</span>
        </button>
      </div>


      <div class="x-gap" style="flex-wrap: wrap">
        <button class="x-btn primary" @click="() => void api.oscConnect(!app.osc?.connected).then(() => app.sync())">
          <Radio class="x-btn-ico" />
          {{ app.osc?.connected ? t('osc.disconnect') : t('osc.connect') }}
        </button>
        <button class="x-btn" @click="() => void api.record(app.recording ? 'stop' : 'start').then(() => app.sync())">
          {{ app.recording ? t('record.stop') : t('record.start') }}
        </button>
        <button class="x-btn ghost" @click="() => void osc.pull()">
          <RefreshCw class="x-btn-ico" />
          {{ t('common.refresh') }}
        </button>
        <button class="x-btn ghost" @click="() => void osc.clear()">
          <Trash2 class="x-btn-ico" />
          {{ t('common.clear') }}
        </button>
        <VrchatStatus compact />
        <span class="x-muted" style="font-size: 12px">
          {{ app.osc?.ip }}:{{ app.osc?.port }} · recv:{{ app.osc?.receivePort }} · {{ osc.recv }}
        </span>
      </div>

      <!-- == sync, corrected by elderman == @elder man -->
      <template v-if="tab === 'overview'">
        <CardGrid view="osc" :cards="CARDS">
          <!-- Avatar -->
          <template #avatar>
            <div class="kv">
              <div class="row">
                <span class="k">{{ t('osc.avatarid') }}</span>
                <span class="v x-mono">{{ show('/avatar/change') }}</span>
              </div>
              <div class="row">
                <span class="k">{{ t('osc.paramtotal') }}</span>
                <span class="v">{{ avatarParamCount }}</span>
              </div>
              <div class="row">
                <span class="k">VRCEmote</span>
                <span class="v x-mono">{{ show(P + 'VRCEmote') }}</span>
              </div>
            </div>
          </template>

          <!-- World/ Room -->
          <template #head-world>
            <span v-bubble="t('osc.worldhint')">{{ t('osc.world') }}</span>
          </template>
          <template #world>
            <div class="kv">
              <div class="row">
                <span class="k">InStation</span>
                <span class="v x-mono">{{ show(P + 'InStation') }}</span>
              </div>
              <div class="row">
                <span class="k">Seated</span>
                <span class="v x-mono">{{ show(P + 'Seated') }}</span>
              </div>
            </div>
          </template>

          <!-- Status -->
          <template #state>
            <div class="kv">
              <div v-for="p in STATE_PARAMS" :key="p" class="row">
                <span class="k x-mono">{{ p }}</span>
                <span class="v x-mono">{{ show(P + p) }}</span>
              </div>
            </div>
          </template>

          <!-- Sports -->
          <template #motion>
            <div class="kv">
              <div v-for="p in MOTION_PARAMS" :key="p" class="row">
                <span class="k x-mono">{{ p }}</span>
                <span class="v x-mono">{{ show(P + p) }}</span>
              </div>
            </div>
          </template>

          <!-- Gestures -->
          <template #gesture>
            <div class="kv">
              <div v-for="p in GESTURE_PARAMS" :key="p" class="row">
                <span class="k x-mono">{{ p }}</span>
                <span class="v x-mono">{{ show(P + p) }}</span>
              </div>
            </div>
          </template>


          <template #head-tracking>
            {{ t('osc.tracking') }}
            <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ trackingRows.length }}</span>
          </template>
          <template #tracking>
            <div class="kv">
              <div v-if="trackingRows.length === 0" class="x-muted" style="font-size: 12.5px">
                {{ t('osc.trackinghint') }}
              </div>
              <div v-for="r in trackingRows.slice(0, 12)" :key="r.addr" class="row">
                <span class="k x-mono">{{ r.addr.replace('/tracking/', '') }}</span>
                <span class="v x-mono">{{ latest(r.args) }}</span>
              </div>
            </div>
          </template>

          <!-- Custom Arguments -->
          <template #head-custom>
            {{ t('osc.custom') }}
            <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ customParams.length }}</span>
          </template>
          <template #custom>
            <div v-if="customParams.length === 0" class="x-muted" style="font-size: 12.5px">
              {{ t('osc.noparams') }}
            </div>
            <div v-else class="chips">
              <span v-for="r in customParams.slice(0, 60)" :key="r.addr" class="chip" v-bubble="`${r.addr} ×${r.count}`">
                <span class="x-mono">{{ r.addr.slice(P.length) }}</span>
                <b class="x-mono">{{ latest(r.args) }}</b>
              </span>
            </div>
          </template>
        </CardGrid>
      </template>

      <!-- == sync, corrected by elderman == -->
      <template v-else>
        <div v-if="osc.list.length === 0" class="x-card">
          <div class="x-card-body x-muted">{{ t('osc.noparams') }}</div>
        </div>

        <div v-for="[seg, rows] in osc.groups" :key="seg" class="x-card" :data-card-id="`osc-monitor:${seg}`">
          <div class="x-card-head">
            /{{ seg }}
            <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ rows.length }}</span>
          </div>
          <div
            class="x-card-body x-cap-scroll"
            style="padding: 0"
            :style="{ '--cap-rows': listCap, '--cap-row': 'calc(27px * var(--sp))' }"
          >
            <div v-for="r in rows" :key="r.addr" class="data-row">
              <span class="x-mono x-grow" style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
                {{ r.addr }}
              </span>
              <span class="x-mono" style="min-width: 120px; text-align: right; font-weight: 600">
                {{ latest(r.args) }}
              </span>
              <span class="x-muted x-mono" style="min-width: 76px; text-align: right">×{{ r.count }}</span>
            </div>
          </div>
        </div>
      </template>
    </div>
  </div>
</template>

<style scoped>
.kv {
  display: flex;
  flex-direction: column;
  gap: 3px;
  font-size: 12.5px;
}
.kv .row {
  display: flex;
  align-items: baseline;
  gap: var(--gap-2);
}
.kv .k {
  color: var(--muted-foreground);
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.kv .v {
  font-weight: 600;
  text-align: right;
  min-width: 72px;
}
.kv .hint {
  margin: var(--gap-1) 0 0;
  font-size: 11.5px;
}
.chips {
  display: flex;
  flex-wrap: wrap;
  gap: 5px;
}
.chip {
  display: inline-flex;
  align-items: baseline;
  gap: 5px;
  padding: 2px 8px;
  border: var(--hairline) solid var(--border);
  border-radius: 999px;
  background: var(--secondary);
  font-size: 12px;
}
.data-row {
  display: flex;
  align-items: center;
  gap: var(--gap-2);
  padding: 4px var(--gap-3);
  border-bottom: var(--hairline) solid var(--border);
  font-size: 12.5px;
}
</style>
