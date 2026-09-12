<script setup lang="ts">






import { onMounted, onUnmounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { RotateCcw, ShieldAlert } from 'lucide-vue-next';
import { useAppStore } from '../stores/app';
import { inShell, postShell } from '../shell';

const { t } = useI18n();
const app = useAppStore();

const taps: number[] = [];
function onKey(e: KeyboardEvent): void {
  if (app.safeMode || e.repeat) return;
  const t0 = e.target as HTMLElement | null;
  if (t0 && (t0.tagName === 'INPUT' || t0.tagName === 'TEXTAREA' || t0.tagName === 'SELECT' || t0.isContentEditable)) return;
  if (e.code !== 'KeyR' || e.ctrlKey || e.metaKey || e.altKey || e.shiftKey) return;
  const now = Date.now();
  taps.push(now);
  while (taps.length > 0 && now - taps[0] > 1000) taps.shift();
  if (taps.length >= 3) {
    taps.length = 0;
    if (inShell) postShell('engine.safemode');
  }
}
onMounted(() => window.addEventListener('keydown', onKey, true));
onUnmounted(() => window.removeEventListener('keydown', onKey, true));
</script>

<template>
  <!-- Show only when safe mode is activated; normal mode zero rendering (entry on settings page) -->
  <div v-if="app.safeMode" class="safe-banner">
    <ShieldAlert class="sb-ico" />
    <span>{{ t('safe.banner') }}</span>
    <span class="x-grow" />
    <button v-if="inShell" class="x-btn sm" @click="postShell('engine.normal')">
      <RotateCcw class="x-btn-ico" />
      {{ t('safe.restart') }}
    </button>
  </div>
</template>

<style scoped>
.safe-banner {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  border-bottom: var(--hairline) solid var(--border);
  background: color-mix(in srgb, var(--warn) 14%, transparent);
  color: var(--foreground);
  font-size: 12.5px;
}
.sb-ico {
  width: 15px;
  height: 15px;
  color: var(--warn);
  flex-shrink: 0;
}
</style>
