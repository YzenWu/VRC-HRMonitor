<script setup lang="ts">
/** Shell-only root component with a custom title bar, close confirmation, and host theme synchronization. */
import { onMounted, watch } from 'vue';
import MainLayout_web from './layouts/MainLayout_web.vue';
import OfflineOverlay from './components/OfflineOverlay.vue';
import ContextMenu from './components/ContextMenu.vue';
import CrashOverlay from './components/CrashOverlay.vue';
import DeviceDialog from './components/DeviceDialog.vue';
import CloseDialog_web from './components/CloseDialog_web.vue';
import Bubble from './components/Bubble.vue';
import { useAppStore } from './stores/app';
import { useUiStore } from './stores/ui';
import { syncShellDebug, syncShellTheme } from './shell';

const app = useAppStore();
const ui = useUiStore();

/**
 * Converts any CSS color to three sRGB byte values.
 * Theme variables may use modern color functions that preserve a wide color gamut.
 * A 1x1 canvas lets the browser perform the color-space conversion exactly as rendered.
 */
function toRgb(css: string): [number, number, number] {
  const m = /^rgba?\(\s*([\d.]+)[,\s]+([\d.]+)[,\s]+([\d.]+)/i.exec(css);
  if (m) return [Number(m[1]), Number(m[2]), Number(m[3])];
  try {
    const cv = document.createElement('canvas');
    cv.width = 1;
    cv.height = 1;
    const ctx = cv.getContext('2d');
    if (ctx) {
      // Set a sentinel first; unsupported colors leave it unchanged and therefore fail parsing.
      ctx.fillStyle = '#010203';
      ctx.fillStyle = css;
      ctx.fillRect(0, 0, 1, 1);
      const d = ctx.getImageData(0, 0, 1, 1).data;
      if (!(d[0] === 1 && d[1] === 2 && d[2] === 3)) return [d[0], d[1], d[2]];
    }
  } catch {
    /* Fall back when canvas pixel access is unavailable, such as in private mode. */
  }
  return [0x1c, 0x21, 0x28];
}

/** The colour of the background actually rendered on the page is given to the host, so that the colours are fully consistent. */
function pushTheme(): void {
  const rgb = toRgb(getComputedStyle(document.body).backgroundColor);
  const hex = `#${rgb.map((v) => Math.round(v).toString(16).padStart(2, '0')).join('')}`;
  syncShellTheme(hex, ui.mode === 'dark');
}

onMounted(() => {
  app.onSysTheme((t) => ui.onSystemTheme(t));
  app.start();
  void ui.pull().then(() => requestAnimationFrame(pushTheme));
  requestAnimationFrame(pushTheme);
});

// Keep host window borders synchronized with theme mode, palette, and custom colors.
watch(() => [ui.mode, ui.look.palette, ui.look.bg, ui.look.panel, ui.look.accent], () => requestAnimationFrame(pushTheme));
// Tell the host whether debug-only UI such as DevTools and status hints should be enabled.
watch(() => app.appInfo?.debug === true, (on) => syncShellDebug(on), { immediate: true });
</script>

<template>
  <MainLayout_web />
  <OfflineOverlay />
  <CrashOverlay />
  <DeviceDialog />
  <ContextMenu />
  <CloseDialog_web />
  <!-- Single global hover bubble rendered from the directive-driven singleton. -->
  <Bubble />
</template>
