<script setup lang="ts">
/**
 * Shell exclusive titlebar (simulation   PH 0): three traffic lamps on the left + centre heading.
 * Host windows are borderless, so drag, minimize, maximize, and close are subject to a   PH 0  return command.
 * Only   PH 0 shell rendering (the browser has its own label bar).
 */
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../stores/app';
import { useBrand } from '../composables/useBrand';
import { postShell, winState } from '../shell';
import { titleH } from '../prefs';
import ThemeToggle from '../components/ThemeToggle.vue';

const { t } = useI18n();
const app = useAppStore();
const { brandText } = useBrand();

const maxTitle = computed(() => (winState.value.maximized ? t('win.restore') : t('win.max')));

const resizing = ref(false);
let resizeStartY = 0;
let resizeStartH = 34;

function onResizeDown(e: PointerEvent): void {
  resizeStartY = e.clientY;
  resizeStartH = titleH.value;
  resizing.value = true;
  document.body.style.cursor = 'ns-resize';
  document.body.style.userSelect = 'none';
  (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
}
function onResizeMove(e: PointerEvent): void {
  if (!resizing.value) return;
  titleH.value = Math.min(96, Math.max(24, Math.round(resizeStartH + e.clientY - resizeStartY)));
}
function onResizeUp(): void {
  if (!resizing.value) return;
  resizing.value = false;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
}

/** Press the empty area to enter the system window to move cycle; button yourself   PH 0 Flow. */
function onDown(e: PointerEvent): void {
  if (e.button !== 0) return;
  postShell('win.drag');
}
</script>

<template>
  <header class="mac-bar" :style="{ height: `${titleH}px` }" @pointerdown="onDown" @dblclick="postShell('win.max')">
    <div class="lights" @pointerdown.stop>
      <button class="light close" v-bubble="t('win.close')" :aria-label="t('win.close')" @click="postShell('win.close')">
        <svg viewBox="0 0 10 10" aria-hidden="true"><path d="M3 3l4 4M7 3l-4 4" /></svg>
      </button>
      <button class="light min" v-bubble="t('win.min')" :aria-label="t('win.min')" @click="postShell('win.min')">
        <svg viewBox="0 0 10 10" aria-hidden="true"><path d="M2.6 5h4.8" /></svg>
      </button>
      <button class="light max" v-bubble="maxTitle" :aria-label="maxTitle" @click="postShell('win.max')">
        <svg viewBox="0 0 10 10" aria-hidden="true">
          <path v-if="winState.maximized" d="M3 5.6h4M5 3.6v4" />
          <path v-else d="M3.2 6.8V3.2h3.6" />
        </svg>
      </button>
    </div>

    <div class="mac-title">
      <span class="brand-dot" />
      <span class="txt">{{ brandText }}</span>
      <span v-if="app.bpm > 0" class="bpm">{{ app.bpm }} BPM</span>
    </div>

    <div class="tail">
      <!-- Right upper corner fast, clear and dark switch (auto-hidden when custom theme is defined); press   PH 0  to prevent the trigger window Drag Move! -->
      <span class="tail-actions" @pointerdown.stop>
        <ThemeToggle />
      </span>
    </div>
    <div
      class="title-grip"
      :class="{ dragging: resizing }"
      @pointerdown.stop="onResizeDown"
      @dblclick.stop
      @pointermove="onResizeMove"
      @pointerup="onResizeUp"
      @pointercancel="onResizeUp"
    />
  </header>
</template>

<style scoped>
.mac-bar {
  position: relative;
  display: flex;
  align-items: center;
  height: 34px;
  flex-shrink: 0;
  padding: 0 10px;
  background: var(--sidebar);
  border-bottom: var(--hairline) solid var(--sidebar-border);
  /* Drag the titlebar as a whole. Text should not be selected */
  user-select: none;
  cursor: default;
}
.title-grip {
  position: absolute;
  left: 0;
  right: 0;
  bottom: -4px;
  height: 8px;
  cursor: ns-resize;
  z-index: 10;
  touch-action: none;
}
.title-grip::after {
  content: '';
  position: absolute;
  left: 0;
  right: 0;
  bottom: 3px;
  height: 2px;
  background: var(--ring);
  opacity: 0;
  transition: opacity 0.15s linear;
}
.title-grip:hover::after,
.title-grip.dragging::after {
  opacity: 0.35;
}

.lights {
  display: flex;
  align-items: center;
  gap: 8px;
  z-index: 1;
}
.light {
  width: 12px;
  height: 12px;
  padding: 0;
  border: 0;
  border-radius: 50%;
  display: inline-grid;
  place-items: center;
  cursor: pointer;
}
.light svg {
  width: 10px;
  height: 10px;
  stroke: rgb(0 0 0 / 55%);
  stroke-width: 1.4;
  stroke-linecap: round;
  fill: none;
  opacity: 0;
  transition: opacity 0.12s linear;
}
/* Symbol only appears when   PH 0   is suspended */
.lights:hover .light svg {
  opacity: 1;
}
.light.close {
  background: #ff5f57;
}
.light.min {
  background: #febc2e;
}
.light.max {
  background: #28c840;
}
.light:active {
  filter: brightness(0.85);
}

/* The title is absolutely medium, not influenced by the width of the left and right sides */
.mac-title {
  position: absolute;
  left: 0;
  right: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  font-size: 12.5px;
  font-weight: 600;
  color: var(--foreground);
  pointer-events: none;
}
.mac-title .brand-dot {
  width: 7px;
  height: 7px;
  border-radius: 50%;
  background: var(--primary);
  box-shadow: 0 0 6px var(--primary);
}
.mac-title .bpm {
  font-weight: 500;
  font-variant-numeric: tabular-nums;
  color: var(--muted-foreground);
}
.tail {
  flex: 1;
  min-width: 0;
  display: flex;
  align-items: center;
  justify-content: flex-end;
}
.tail-actions {
  display: inline-flex;
  align-items: center;
}
</style>
