<script setup lang="ts">






import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../stores/app';
import { brandPill, hbMainSource, type BrandPillMode } from '../prefs';

const props = defineProps<{ collapsed?: boolean }>();

const { t } = useI18n();
const app = useAppStore();

const LABEL: Record<BrandPillMode, string> = {
  off: 'nav.pilloff',
  ping: 'nav.ping',
  avg: 'hb.average',
  main: 'hb.srcmain',
  devices: 'tab.devices',
  connected: 'dev.connected',
};

/** Lightpoint colour + Display text (No data is always grey –). */
const st = computed<{ color: string; text: string }>(() => {
  const mode = brandPill.value;
  switch (mode) {
    case 'ping': {
      const v = app.ping;
      const color = v <= 0 ? 'var(--muted-foreground)' : v <= 15 ? 'var(--good)' : v <= 100 ? 'var(--warn)' : 'var(--bad)';
      return { color, text: v > 0 ? `${v} ms` : '—' };
    }
    case 'avg': {
      const v = app.avg;
      return { color: v > 0 ? 'var(--info)' : 'var(--muted-foreground)', text: v > 0 ? String(v) : '—' };
    }
    case 'main': {
      const v = app.bpmOf(hbMainSource.value);
      return { color: v > 0 ? 'var(--primary)' : 'var(--muted-foreground)', text: v > 0 ? String(v) : '—' };
    }
    case 'devices': {
      const v = app.devices.length;
      return { color: v > 0 ? 'var(--info)' : 'var(--muted-foreground)', text: String(v) };
    }
    case 'connected': {
      const v = app.connectedCount;
      return { color: v > 0 ? 'var(--good)' : 'var(--muted-foreground)', text: String(v) };
    }
    default:
      return { color: 'var(--primary)', text: '' };
  }
});

const mode = brandPill;
/** Select "Close" to leave only the lights; the folding state still shows a numerical capsule. */
const capsule = computed(() => mode.value !== 'off');

const hint = computed(() => {
  const label = t(LABEL[mode.value]);
  const has = st.value.text !== '' && st.value.text !== '—';
  return has ? `${label} · ${st.value.text}` : label;
});
</script>

<template>
  <span class="bp" :class="{ cap: capsule, collapsed }" v-bubble="hint" aria-hidden="true">
    <span class="bp-dot" :style="{ background: st.color, boxShadow: `0 0 6px ${st.color}` }" />
    <span v-if="capsule && st.text" class="bp-txt">{{ st.text }}</span>
  </span>
</template>

<style scoped>
.bp {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  flex-shrink: 0;
  height: calc(16px * var(--sp));

  border-radius: var(--radius);
  user-select: none;
}
/* The capsule appearance is only enabled when text is shown; keep a pure light point on "Close/Fold" */
.bp.cap {
  max-width: 100%;
  min-width: 0;
  padding: 0 7px;
  border: var(--hairline) solid var(--border);
  background: var(--secondary);
}
.bp.collapsed.cap {
  max-width: 52px;
  padding: 0 5px;
}
.bp-dot {
  width: calc(6px * var(--sp));
  height: calc(6px * var(--sp));
  border-radius: 50%;
  flex-shrink: 0;
}
.bp-txt {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 11px;
  font-weight: 650;
  line-height: 1;
  color: var(--foreground);
  font-variant-numeric: tabular-nums;
  white-space: nowrap;
}
</style>
