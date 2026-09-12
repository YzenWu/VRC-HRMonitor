<script setup lang="ts">









import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import type { Device } from '../types';

const props = defineProps<{ device: Device; size?: number }>();
const { t } = useI18n();

const px = computed(() => `${props.size ?? 8}px`);

const state = computed<'idle' | 'connecting' | 'ok' | 'weak' | 'critical'>(() => {
  const d = props.device;
  if (d.connected) {

    if (d.signal === 'critical') return 'critical';
    if (d.signal === 'weak') return 'weak';
    return 'ok';
  }
  return d.connecting ? 'connecting' : 'idle';
});

const color = computed(() => {
  switch (state.value) {
    case 'ok':
      return 'var(--good)';
    case 'weak':
      return 'var(--warn)';
    case 'critical':
      return 'var(--bad)';
    case 'connecting':
      return 'var(--foreground)';
    default:
      return 'transparent';
  }
});

const label = computed(() => {
  switch (state.value) {
    case 'ok':
      return t('dev.connected');
    case 'weak':
      return t('dev.signalweak');
    case 'critical':
      return t('dev.signalcritical');
    case 'connecting':
      return t('dev.connecting');
    default:
      return t('dev.disconnected');
  }
});
</script>

<template>
  <span
    class="dev-dot"
    :class="{ pulsing: state === 'connecting' }"
    v-bubble="label"
    :style="{ width: px, height: px, background: color }"
  />
</template>

<style scoped>
.dev-dot {
  display: inline-block;
  border-radius: 50%;
  flex-shrink: 0;
  /* Unconnected to hollow circle: constant borders, transparent background */
  border: 1.5px solid var(--muted-foreground);
  box-sizing: border-box;
  transition: background-color 0.2s linear, border-color 0.2s linear;
}
.dev-dot.pulsing {
  animation: dot-pulse 1.1s linear infinite;
}
@keyframes dot-pulse {
  0%,
  100% {
    opacity: 1;
    transform: scale(1);
  }
  50% {
    opacity: 0.45;
    transform: scale(0.82);
  }
}
@media (prefers-reduced-motion: reduce) {
  .dev-dot.pulsing {
    animation: none;
  }
}
</style>
