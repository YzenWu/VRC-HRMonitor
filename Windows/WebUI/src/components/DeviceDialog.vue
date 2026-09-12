<script setup lang="ts">








import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { Link2, MonitorPlay, Pencil, Power, Unplug, X } from 'lucide-vue-next';
import { api } from '../api';
import { closeDeviceDialog, dialogMac, renameRequest } from '../deviceDialog';
import { hbMode, hbPoints, hbSmooth } from '../prefs';
import { useAppStore } from '../stores/app';
import BpmChart from './BpmChart.vue';
import RollingNumber from './RollingNumber.vue';
import DeviceDot from './DeviceDot.vue';
import DeviceIcon from './DeviceIcon.vue';
import DeviceName from './DeviceName.vue';
import XDialog from './XDialog.vue';

const { t } = useI18n();
const router = useRouter();
const app = useAppStore();

type Tab = 'overview' | 'conn' | 'history';
const tab = ref<Tab>('overview');

const device = computed(() => app.devices.find((d) => d.mac === dialogMac.value) ?? null);

const curve = computed(() => (dialogMac.value ? app.deviceCurves[dialogMac.value] ?? [] : []));

const stats = computed(() => (dialogMac.value ? app.statsOf(dialogMac.value) : null));


interface DevStat {
  count: number;
  avg: number;
  min: number;
  max: number;
  last: string;
}
const db = ref<DevStat | null>(null);
const dbLoading = ref(false);

async function loadDb(mac: string): Promise<void> {
  dbLoading.value = true;
  db.value = null;
  try {
    const r = (await api.monitor(0, 8)) as { devices?: (DevStat & { mac?: string })[] };
    const hit = (r.devices ?? []).find((d) => d.mac && d.mac.toLowerCase() === mac.toLowerCase());
    db.value = hit
      ? { count: hit.count, avg: hit.avg, min: hit.min, max: hit.max, last: hit.last ?? '' }
      : null;
  } catch {
    db.value = null;
  } finally {
    dbLoading.value = false;
  }
}

// Open a flash of the latest device snapshot (name/sign/frequency/allowance) to avoid old lists
watch(
  dialogMac,
  (v) => {
    tab.value = 'overview';
    db.value = null;
    if (v) void app.refreshDevices();
  },
  { immediate: true },
);
// Only when you cut to Heartrate History.
watch(
  tab,
  (v) => {
    if (v === 'history' && dialogMac.value) void loadDb(dialogMac.value);
  },
  { immediate: true },
);
// Right-click menu "Rename": Open details and go straight to rename input
watch(renameRequest, () => {
  const d = device.value;
  if (!d) return;
  tab.value = 'overview';
  renameText.value = d.name;
  renameFor.value = true;
});

const hz = (v: number): string => (v > 0 ? `${v.toFixed(2)} Hz` : '—');

function toggle(): void {
  const d = device.value;
  if (!d || d.connecting) return;
  void (d.connected ? api.disconnect(d.mac) : api.connect(d.mac)).then(() => app.sync());
}
function toggleFloat(): void {
  const d = device.value;
  if (!d) return;
  void (app.floatIds.includes(d.mac) ? api.floatClose(d.mac) : api.floatOpen(d.mac)).then(() => app.sync());
}
function toggleSave(): void {
  const d = device.value;
  if (!d) return;
  void api.save(d.mac, !d.saved).then(() => app.sync());
}
function goDevices(): void {
  const d = device.value;
  closeDeviceDialog();

  void router.push({ path: '/devices', query: d ? { hl: d.mac } : {} });
}

// - Rename (Themes dialogue box, consistent with device page)
const renameFor = ref(false);
const renameText = ref('');
function startRename(): void {
  if (!device.value) return;
  renameText.value = device.value.name;
  renameFor.value = true;
}
async function submitRename(): Promise<void> {
  const d = device.value;
  const v = renameText.value.trim();
  renameFor.value = false;
  if (!d || v === '' || v === d.name) return;
  await api.rename(d.mac, v);
  await app.sync();
}
</script>

<template>
  <Teleport to="body">
    <Transition name="dlg">
      <div v-if="dialogMac" class="dev-dlg-mask" @click.self="closeDeviceDialog">
        <div v-if="device" class="dev-dlg-card">
          <!-- Head: Icon + Name + Status Light + Close -->
          <div class="head">
            <DeviceDot :device="device" :size="8" />
            <DeviceIcon :device="device" />
            <DeviceName :device="device" class="x-grow" style="min-width: 0" v-bubble="t('dev.clickhint')" />
            <span class="x-muted x-mono mac">{{ device.mac }}</span>
            <button class="x-btn sm ghost" v-bubble="t('win.close')" @click="closeDeviceDialog">
              <X class="x-btn-ico" />
            </button>
          </div>


          <div class="x-subtabs" style="align-self: flex-start">
            <button class="x-subtab" :class="{ active: tab === 'overview' }" @click="tab = 'overview'">
              {{ t('dev.taboverview') }}
            </button>
            <button class="x-subtab" :class="{ active: tab === 'conn' }" @click="tab = 'conn'">
              {{ t('dev.tabconn') }}
            </button>
            <button class="x-subtab" :class="{ active: tab === 'history' }" @click="tab = 'history'">
              {{ t('dev.tabhr') }}
            </button>
          </div>

          <!-- Overview: current values/statistics + standalone curve + identifying information -->
          <template v-if="tab === 'overview'">
            <div class="stats-row">
              <div>
                <div class="x-muted lbl">{{ t('hb.current') }}</div>
                <div class="big" :class="{ live: device.connected && device.bpm > 0 }">
                  <RollingNumber :value="device.connected && device.bpm > 0 ? device.bpm : null" />
                </div>
              </div>
              <div class="stat">
                <div class="x-muted lbl">{{ t('hb.min') }}</div>
                <div class="val"><RollingNumber :value="stats ? stats.min : null" /></div>
              </div>
              <div class="stat">
                <div class="x-muted lbl">{{ t('hb.average') }}</div>
                <div class="val"><RollingNumber :value="stats ? stats.avg : null" /></div>
              </div>
              <div class="stat">
                <div class="x-muted lbl">{{ t('hb.max') }}</div>
                <div class="val"><RollingNumber :value="stats ? stats.max : null" /></div>
              </div>
              <div class="x-grow" />
            </div>

            <div class="chart">
              <BpmChart :data="curve" :height="140" :smooth="hbSmooth" :points="hbPoints" :mode="hbMode" />
            </div>
          </template>

          <!-- Connection log: Connect status and identification information -->
          <div v-else-if="tab === 'conn'" class="conn-grid">
            <div class="info">
              <span class="x-muted">{{ t('dev.state') }}</span>
              <b>
                {{ device.connecting ? t('dev.connecting') : device.connected ? t('dev.connected') : device.noHrChar ? t('dev.nohrchar') : t('dev.disconnected') }}
              </b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.type') }}</span>
              <b class="x-mono">{{ device.type || device.category }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.mac') }}</span>
              <b class="x-mono" style="user-select: text">{{ device.mac }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.rssi') }}</span>
              <b>{{ device.hasRssi ? `${device.rssi} dBm` : '—' }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.advrate') }}</span>
              <b class="x-mono">{{ hz(device.advHz) }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.notifyrate') }}</span>
              <b class="x-mono">{{ hz(device.notifyHz) }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.reports') }}</span>
              <b class="x-mono">{{ device.reports || 0 }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('dev.autoreconnect') }}</span>
              <b>{{ device.reconnecting ? t('dev.connecting') : t('common.off') }}</b>
            </div>
            <div class="info">
              <span class="x-muted">{{ t('record.label') }}</span>
              <b>{{ device.saved ? t('common.enabled') : t('common.disabled') }}</b>
            </div>
          </div>


          <div v-else class="hist">
            <div v-if="dbLoading" class="x-muted">{{ t('net.reconnecting') }}</div>
            <template v-else-if="db && db.count > 0">
              <div class="stats-row">
                <div class="stat">
                  <div class="x-muted lbl">{{ t('dev.reports') }}</div>
                  <div class="val">{{ db.count }}</div>
                </div>
                <div class="stat">
                  <div class="x-muted lbl">{{ t('hb.min') }}</div>
                  <div class="val">{{ db.min }}</div>
                </div>
                <div class="stat">
                  <div class="x-muted lbl">{{ t('hb.average') }}</div>
                  <div class="val">{{ db.avg }}</div>
                </div>
                <div class="stat">
                  <div class="x-muted lbl">{{ t('hb.max') }}</div>
                  <div class="val">{{ db.max }}</div>
                </div>
              </div>
              <div class="x-muted" style="font-size: 11.5px">
                {{ t('dev.lastreport') }}:
                <span class="x-mono">{{ db.last ? db.last.replace('T', ' ').slice(0, 19) : '—' }}</span>
              </div>
            </template>
            <div v-else class="x-muted" style="font-size: 12.5px">{{ t('dev.norecords') }}</div>
          </div>


          <div class="actions">
            <button class="x-btn sm primary" :disabled="device.connecting" @click="toggle">
              <component :is="device.connected ? Unplug : Power" class="x-btn-ico" />
              {{ device.connecting ? t('dev.connecting') : device.connected ? t('dev.disconnect') : t('dev.connect') }}
            </button>
            <button class="x-btn sm" :disabled="!device.connected" @click="toggleFloat">
              <MonitorPlay class="x-btn-ico" />
              {{ app.floatIds.includes(device.mac) ? t('float.closedev') : t('float.opendev') }}
            </button>
            <button class="x-btn sm ghost" @click="toggleSave">
              {{ device.saved ? t('dev.unsave') : t('dev.save') }}
            </button>
            <button class="x-btn sm ghost" @click="startRename">
              <Pencil class="x-btn-ico" />
              {{ t('dev.rename') }}
            </button>
            <button class="x-btn sm ghost" @click="goDevices">
              <Link2 class="x-btn-ico" />
              {{ t('tab.devices') }}
            </button>
            <span class="x-grow" />
            <button class="x-btn sm ghost" @click="closeDeviceDialog">{{ t('common.close') }}</button>
          </div>
        </div>

        <!-- Rename Dialogue -->
        <XDialog
          v-if="device"
          :open="renameFor"
          :title="t('dev.rename')"
          :desc="device.mac"
          size="sm"
          @close="renameFor = false"
        >
          <input
            v-model="renameText"
            class="x-input"
            style="width: 100%"
            :placeholder="t('dev.alias')"
            @keyup.enter="() => void submitRename()"
          />
          <template #actions>
            <button class="x-btn sm primary" @click="() => void submitRename()">{{ t('common.save') }}</button>
            <button class="x-btn sm ghost" @click="renameFor = false">{{ t('common.cancel') }}</button>
          </template>
        </XDialog>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.dev-dlg-mask {
  position: fixed;
  inset: 0;
  z-index: 950;
  display: grid;
  place-items: center;
  padding: var(--gap-3);
  background: rgb(0 0 0 / 35%);
}
.dev-dlg-card {
  width: min(560px, 100%);
  max-height: calc(100vh - 48px);
  overflow-y: auto;
  padding: var(--gap-3);
  border: var(--hairline) solid var(--border);
  border-radius: var(--radius);
  background: var(--popover, var(--card));
  box-shadow: var(--elev-lg);
  display: flex;
  flex-direction: column;
  gap: var(--gap-3);
}
.head {
  display: flex;
  align-items: center;
  gap: var(--gap-2);
  font-size: 14px;
  font-weight: 650;
}
.head .mac {
  font-size: 11px;
  font-weight: 400;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  max-width: 200px;
}
.stats-row {
  display: flex;
  align-items: flex-end;
  gap: var(--gap-3);
  flex-wrap: wrap;
}
.stats-row .lbl {
  font-size: 11.5px;
}
.big {
  font-size: 40px;
  font-weight: 700;
  line-height: 1.05;
  color: var(--muted-foreground);
  font-variant-numeric: tabular-nums;
}
.big.live {
  color: var(--primary);
}
.stat .val {
  font-size: 20px;
  font-weight: 650;
  font-variant-numeric: tabular-nums;
}
.stats-row .hint {
  font-size: 11px;
  padding-bottom: 2px;
}
.chart {
  border: var(--hairline) solid var(--border);
  border-radius: calc(var(--radius) - 2px);
  background: var(--secondary);
  padding: var(--gap-1);
}
.conn-grid,
.hist {
  display: flex;
  flex-direction: column;
  gap: var(--gap-2);
}
.conn-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(150px, 1fr));
  gap: var(--gap-2);
}
.info {
  display: flex;
  flex-direction: column;
  gap: 1px;
  font-size: 12px;
}
.info span {
  font-size: 11px;
}
.info b {
  font-weight: 600;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.hist .stats-row .stat .val {
  font-size: 22px;
}
.actions {
  display: flex;
  align-items: center;
  gap: var(--gap-2);
  flex-wrap: wrap;
  border-top: var(--hairline) solid var(--border);
  padding-top: var(--gap-2);
}
.dlg-enter-active,
.dlg-leave-active {
  transition: opacity 0.16s linear;
}
.dlg-enter-from,
.dlg-leave-to {
  opacity: 0;
}
@media (prefers-reduced-motion: reduce) {
  .dlg-enter-active,
  .dlg-leave-active {
    transition: none;
  }
}
</style>
