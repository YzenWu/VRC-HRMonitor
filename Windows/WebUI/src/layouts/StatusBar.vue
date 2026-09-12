<script setup lang="ts">
/**
 * Bottom state bar: Current page on the left and engine/   PH 0  state, with heart rate broken down by device.
 * Round 29 #3: Equipment capsules support suspension animation + air bubble short mail ( PH 0  off).
 * Thirty-first round #2: Click = open device details, double-click = connect/disconnect (consistent with the syntax of the device list);
 * The long device name is often rolled horizontally ( PH 0, #39 is no longer suspended). #3: High =     PH 1      density factor, etc. higher than the sidebar button.
 */
import { computed, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';
import { Check } from 'lucide-vue-next';
import { tabOf } from '../router';
import { useAppStore } from '../stores/app';
import { deviceRowClick, deviceRowDblClick } from '../deviceDialog';
import { barH, chipNameW, statusPop } from '../prefs';
import { lastSavedAt, saveFailedAt } from '../stores/notify';
import DeviceDot from '../components/DeviceDot.vue';
import BpmChart from '../components/BpmChart.vue';

const { t } = useI18n();
const route = useRoute();
const app = useAppStore();

/** Bottom Bar Right: Global "Saved" logo (any page actually saved successfully →PH 0 light PH 1). */
const savedFlash = ref(false);
let savedTimer = 0;
watch(lastSavedAt, (ts) => {
  if (!ts) return;
  savedFlash.value = true;
  window.clearTimeout(savedTimer);
  savedTimer = window.setTimeout(() => (savedFlash.value = false), 1600);
});
onUnmounted(() => {
  window.clearTimeout(savedTimer);
  window.clearTimeout(chipClickTimer);
});

/** Bottom bar, right: the latest engine log (class colour + cut, complete text goes   PH 0). */
const lastLog = computed(() => app.logs[app.logs.length - 1] ?? '');
const lastLogShort = computed(() => (lastLog.value.length > 48 ? `${lastLog.value.slice(0, 48)}…` : lastLog.value));
/** Log grade colour:   PH 0 Orange/   PH 1   PH 2 Red / The rest is weakened. */
const logTone = computed(() => {
  const s = lastLog.value;
  if (/(\[(ERROR|FAIL)\]|\b(ERROR|FAIL)\b)/i.test(s)) return 'err';
  if (/(\[WARN\]|\bWARN\b)/i.test(s)) return 'warn';
  return '';
});

const currentTab = computed(() => {
  const tab = tabOf(route.path);
  return tab ? t('tab.' + tab) : '';
});

/** Connected equipment (detailed); return showing average position when not connected. */
const connected = computed(() => app.devices.filter((d) => d.connected));

/** This threshold value is used to determine whether a rolling copy is enabled. */
const NAME_WIDE = 12;
const isLongName = (s: string): boolean => s.length > NAME_WIDE;

// ---- L679 mini device card: replaces the plain-text chip bubble with curve + stats + signal. ----
interface MiniDev {
  mac: string;
  name: string;
  bpm: number;
  hasRssi: boolean;
  rssi: number;
  notifyHz: number;
  /** Anchor coordinates for positioning (pointer position at hover time). */
  x: number;
  y: number;
}
const mini = ref<MiniDev | null>(null);
let miniTimer = 0;
const MINI_DELAY = 350;
/** Mini-card width; kept in sync with the scoped CSS below. */
const MINI_W = 260;

const miniDev = computed(() => app.devices.find((d) => d.mac === mini.value?.mac) ?? null);
const miniCurve = computed(() => (mini.value ? app.deviceCurves[mini.value.mac] ?? [] : []));
const miniStats = computed(() => (mini.value ? app.statsOf(mini.value.mac) : null));
const miniStyle = computed(() => {
  const m = mini.value;
  if (!m) return {};
  // Above the bar, clamped into the viewport; the value reads fine even while the strip scrolls.
  const left = Math.min(Math.max(m.x - MINI_W / 2, 6), window.innerWidth - MINI_W - 6);
  const top = Math.max(6, m.y - 190);
  return { left: `${left}px`, top: `${top}px`, width: `${MINI_W}px` };
});

function onChipEnter(d: { mac: string; name: string; bpm: number; hasRssi: boolean; rssi: number; notifyHz: number }, e: PointerEvent): void {
  if (!statusPop.value) return;
  window.clearTimeout(miniTimer);
  miniTimer = window.setTimeout(() => {
    mini.value = { mac: d.mac, name: d.name, bpm: d.bpm, hasRssi: d.hasRssi, rssi: d.rssi, notifyHz: d.notifyHz, x: e.clientX, y: e.clientY };
  }, MINI_DELAY);
}
function onChipLeave(): void {
  window.clearTimeout(miniTimer);
  mini.value = null;
}
onUnmounted(() => {
  window.clearTimeout(miniTimer);
  mini.value = null;
});

// Click/ Double-click semantics (Thirteenth round #2); Click = Open the device details (including location across the page), double-click = Connection/ Disconnect.
// Delay single-shot with   PH 0: double-click first, double-click when hit.
let chipClickTimer = 0;
function onChipClick(mac: string): void {
  window.clearTimeout(chipClickTimer);
  chipClickTimer = window.setTimeout(() => {
    chipClickTimer = 0;
    deviceRowClick(mac);
  }, 220);
}
function onChipDbl(mac: string): void {
  window.clearTimeout(chipClickTimer);
  chipClickTimer = 0;
  const d = app.devices.find((x) => x.mac === mac);
  if (d) deviceRowDblClick(d);
}

const resizing = ref(false);
let resizeStartY = 0;
let resizeStartH = 0;

function onResizeDown(e: PointerEvent): void {
  resizeStartY = e.clientY;
  resizeStartH = barH.value > 0 ? barH.value : (e.currentTarget as HTMLElement).parentElement!.getBoundingClientRect().height;
  resizing.value = true;
  document.body.style.cursor = 'ns-resize';
  document.body.style.userSelect = 'none';
  (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
}
function onResizeMove(e: PointerEvent): void {
  if (!resizing.value) return;
  barH.value = Math.min(96, Math.max(18, Math.round(resizeStartH + resizeStartY - e.clientY)));
}
function onResizeUp(): void {
  if (!resizing.value) return;
  resizing.value = false;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
}
</script>

<template>
  <div class="hrm-status" :style="{ '--chip-name-w': `${chipNameW}px` }">
    <div
      class="status-grip"
      :class="{ dragging: resizing }"
      @pointerdown="onResizeDown"
      @pointermove="onResizeMove"
      @pointerup="onResizeUp"
      @pointercancel="onResizeUp"
    />
    <span class="fixed">{{ currentTab }}</span>
    <span class="sep">·</span>
    <span class="fixed" :style="{ color: app.engineOnline ? 'var(--good)' : 'var(--muted-foreground)' }">
      {{ app.engineOnline ? t('nav.connected') : t('nav.waiting') }}
    </span>
    <span class="sep">·</span>

    <!-- Device breakdown heart rate: can scroll horizontally in unlimited quantities; capsule suspension animation + click to switch connection
         L679: hovering a chip shows a mini device card (curve, stats, signal) instead of a plain-text bubble. -->
    <div class="hr-strip">
      <template v-if="connected.length > 0">
        <span
          v-for="d in connected"
          :key="d.mac"
          class="hr-chip"
          :class="{ 'no-pop': !statusPop }"
          @click="onChipClick(d.mac)"
          @dblclick="onChipDbl(d.mac)"
          @pointerenter="onChipEnter(d, $event)"
          @pointerleave="onChipLeave"
        >
          <DeviceDot :device="d" :size="6" />
          <span v-if="isLongName(d.name)" class="chip-name-box">
            <span class="chip-marquee"><span class="dup">{{ d.name }}</span><span class="dup">{{ d.name }}</span></span>
          </span>
          <span v-else class="chip-name">{{ d.name }}</span>
          <b class="chip-bpm">{{ d.bpm || '--' }}</b>
        </span>
        <span v-if="connected.length > 1" v-bubble="t('hb.average')" class="hr-chip avg">
          <span class="chip-name">{{ t('hb.average') }}</span>
          <b class="chip-bpm">{{ app.avg || '--' }}</b>
        </span>
      </template>
      <span v-else class="x-muted">{{ t('dev.notconnected') }}</span>
    </div>

    <!-- L679 mini device card: curve + window stats + signal, teleported so the strip's scroll clip cannot cut it. -->
    <Teleport to="body">
      <Transition name="mini">
        <div v-if="mini" class="dev-mini" :style="miniStyle">
          <div class="dev-mini-head">
            <DeviceDot :device="miniDev ?? ({} as never)" :size="7" />
            <b class="dev-mini-name">{{ mini.name }}</b>
            <span class="x-grow"></span>
            <b class="dev-mini-bpm x-mono">{{ mini.bpm > 0 ? mini.bpm : '--' }} BPM</b>
          </div>
          <BpmChart :data="miniCurve" :height="64" mode="line" :smooth="1" :points="180" />
          <div class="dev-mini-stats">
            <span>{{ t('hb.min') }} <b class="x-mono">{{ miniStats?.min ?? '—' }}</b></span>
            <span>{{ t('hb.average') }} <b class="x-mono">{{ miniStats?.avg ?? '—' }}</b></span>
            <span>{{ t('hb.max') }} <b class="x-mono">{{ miniStats?.max ?? '—' }}</b></span>
          </div>
          <div class="dev-mini-foot">
            <span class="x-mono">{{ mini.mac }}</span>
            <span class="x-grow"></span>
            <span>{{ t('dev.rssi') }} <b class="x-mono">{{ mini.hasRssi ? `${mini.rssi} dBm` : '—' }}</b></span>
            <span>· {{ t('dev.notifyrate') }} <b class="x-mono">{{ mini.notifyHz > 0 ? `${mini.notifyHz.toFixed(2)} Hz` : '—' }}</b></span>
          </div>
        </div>
      </Transition>
    </Teleport>

    <span class="sep">·</span>
    <span class="fixed x-muted">{{ t('dev.connected') }} {{ connected.length }}</span>
    <span class="sep">·</span>
    <span class="fixed x-muted">{{ app.wsOnline ? 'WS' : 'WS ×' }}</span>
    <span class="sep">·</span>
    <span class="fixed x-muted">v{{ app.appInfo?.version ?? '-' }}</span>
    <span class="sep">·</span>
    <!-- Saved logo: Saved successful light   PH 0  (replaced in situ "saved" for setting up Kari) -->
    <Transition name="sbfl">
      <span v-if="savedFlash" v-bubble="t('common.saved')" class="fixed saved-badge">
        <Check class="sb-ico" />
        {{ t('common.saved') }}
      </span>
    </Transition>
    <!-- The latest engine log: grade colour + cut, full text suspension bubble -->
    <span v-if="lastLog" class="sep">·</span>
    <span v-if="lastLog" v-bubble="lastLog" class="fixed log" :class="logTone">{{ lastLogShort }}</span>
  </div>
</template>

<style scoped>
/* L679 mini device card (teleported to body): linear show/hide so the global animation gate
   and prefers-reduced-motion both stay honored; interrupting simply re-targets from the current frame. */
.dev-mini {
  position: fixed;
  z-index: 1150;
  display: flex;
  flex-direction: column;
  gap: 6px;
  padding: 10px 12px;
  border: var(--hairline) solid var(--border);
  border-radius: calc(var(--radius) - 2px);
  background: var(--popover, var(--card));
  box-shadow: var(--elev-lg, 0 6px 18px rgb(0 0 0 / 0.22));
  color: var(--foreground);
  font-size: 12px;
  pointer-events: none;
}
.dev-mini-head {
  display: flex;
  align-items: center;
  gap: 6px;
}
.dev-mini-name {
  overflow: hidden;
  font-size: 12.5px;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.dev-mini-bpm {
  font-size: 13px;
}
.dev-mini-stats,
.dev-mini-foot {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
  color: var(--muted-foreground);
}
.dev-mini-foot {
  font-size: 11px;
}
.dev-mini-foot .x-mono,
.dev-mini-stats .x-mono {
  color: var(--foreground);
}
.mini-enter-active,
.mini-leave-active {
  transition: opacity 120ms linear, transform 120ms linear;
}
.mini-enter-from,
.mini-leave-to {
  opacity: 0;
  transform: translateY(4px);
}
@media (prefers-reduced-motion: reduce) {
  .mini-enter-active,
  .mini-leave-active {
    transition: none;
  }
}

.status-grip {
  position: absolute;
  top: -4px;
  left: 0;
  right: 0;
  height: 8px;
  cursor: ns-resize;
  z-index: 20;
  touch-action: none;
}
.status-grip::after {
  content: '';
  position: absolute;
  top: 3px;
  left: 0;
  right: 0;
  height: 2px;
  background: var(--ring);
  opacity: 0;
  transition: opacity 0.15s linear;
}
.status-grip:hover::after,
.status-grip.dragging::after {
  opacity: 0.35;
}
/* Fixed areas do not engage in compression, only heart bars shrink and roll */
.fixed {
  flex-shrink: 0;
  white-space: nowrap;
}
/* - Right "Saved" logo */
.saved-badge {
  display: inline-flex;
  align-items: center;
  gap: 3px;
  padding: 1px 7px;
  border: var(--hairline) solid color-mix(in srgb, var(--good) 45%, transparent);
  border-radius: 999px;
  color: var(--good);
  background: color-mix(in srgb, var(--good) 10%, transparent);
  font-size: 11px;
  font-weight: 600;
}
.saved-badge .sb-ico {
  width: 11px;
  height: 11px;
}
/* PH 0 2 Failed: Red Edge Commons (same emblem skeleton, go   PH 1 scintillation) */
.saved-badge.unsaved {
  border-color: color-mix(in srgb, var(--destructive) 45%, transparent);
  color: var(--destructive);
  background: color-mix(in srgb, var(--destructive) 10%, transparent);
}
/* - Up-to-date log on right: grade colour - */
.log {
  max-width: 340px;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--muted-foreground);
  font-family: var(--mono, ui-monospace, Consolas, monospace);
  font-size: 11px;
}
.log.err {
  color: var(--destructive);
}
.log.warn {
  color: var(--warn);
}
.sbfl-enter-active,
.sbfl-leave-active {
  transition: opacity 0.18s linear, transform 0.18s linear;
}
.sbfl-enter-from,
.sbfl-leave-to {
  opacity: 0;
  transform: translateY(4px);
}
@media (prefers-reduced-motion: reduce) {
  .sbfl-enter-active,
  .sbfl-leave-active {
    transition: none;
  }
}
.hr-strip {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  gap: var(--gap-2);
  overflow-x: auto;
  overflow-y: hidden;
  scrollbar-width: none;
  white-space: nowrap;
  /* 悬停上浮/放大原本被滚动容器的 overflow 裁剪：
     padding 撑开裁剪盒、负 margin 保持原布局，悬停胶囊得以完整显示；
     position + z-index 让悬停中的胶囊覆盖两侧相邻控件 */
  position: relative;
  z-index: 2;
  padding: 4px 6px;
  margin: -4px -6px;
}
.hr-strip::-webkit-scrollbar {
  height: 0;
}
.hr-chip {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  padding: 1px 7px;
  border: var(--hairline) solid var(--border);
  border-radius: 999px;
  background: var(--secondary);
  flex-shrink: 0;
  cursor: pointer;
  transition: background-color 0.15s linear, border-color 0.15s linear, box-shadow 0.15s linear, transform 0.12s linear;
}
/* Suspend animation: a small upsliding of capsules + bright light;   PH 0 degraded to normal   PH 1  background colour at close */
.hr-chip:hover:not(.no-pop) {
  background: var(--accent);
  border-color: var(--ring);
  box-shadow: 0 0 0 1px var(--ring), 0 2px 6px rgb(var(--c-accent-rgb, 91 157 255) / 35%);
  transform: translateY(-1px);
  animation: chip-wiggle 0.24s linear;
}
.hr-chip:hover.no-pop {
  background: var(--accent);
}
.hr-chip.avg {
  background: transparent;
  border-style: dashed;
  cursor: default;
}
@keyframes chip-wiggle {
  0% {
    transform: translateY(0) scale(1);
  }
  45% {
    transform: translateY(-2px) scale(1.06);
  }
  100% {
    transform: translateY(-1px) scale(1);
  }
}
.chip-name {
  max-width: var(--chip-name-w, 108px);
  overflow: hidden;
  text-overflow: ellipsis;
}
/* #2/#39: Longer device name (>12 words, replaying rolling copy) always rolling horizontally as long as it exceeds width, no longer waiting for suspension */
.chip-name-box {
  max-width: var(--chip-name-w, 108px);
  overflow: hidden;
  display: inline-flex;
}
.chip-name-box .chip-marquee {
  display: inline-flex;
  white-space: nowrap;
  will-change: transform;
  animation: chip-scroll 7s linear infinite;
}
.chip-name-box .dup {
  flex-shrink: 0;
  padding-right: 14px;
}
@keyframes chip-scroll {
  to {
    transform: translateX(-50%);
  }
}
/* PH 0 stops when   PH 0  turns on the reduced dynamic effect (the interface animated switch has been taken over by   PH 1  full stop gate) */
@media (prefers-reduced-motion: reduce) {
  .chip-name-box .chip-marquee {
    animation: none;
  }
}
.chip-bpm {
  color: var(--foreground);
  font-weight: 650;
  font-variant-numeric: tabular-nums;
}

/* Suspend bubbles have been migrated to global   PH 0 ( PH 1 #2) and no longer exist here.  PH 2 */
@media (prefers-reduced-motion: reduce) {
  .hr-chip:hover:not(.no-pop) {
    animation: none;
  }
}
</style>
