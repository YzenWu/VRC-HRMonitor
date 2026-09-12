<script setup lang="ts">
/**
 * Shared VRChat status + launch control (plan #674 / E1).
 * Reuses GET /api/vrchat and the `vrchat_status` WS push; the launch button calls
 * POST /api/vrchat/launch and is disabled while the process is running or cooling down.
 * `compact` keeps only the dot, state text and CPU for tight toolbars/card headers.
 */
import { computed, onBeforeUnmount, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Play } from 'lucide-vue-next';
import { api } from '../api';
import { onWs } from '../api/ws';

defineProps<{ compact?: boolean }>();
const { t } = useI18n();

interface VrStatus {
  running?: boolean;
  pid?: number;
  cpuPct?: number;
  memoryMb?: number;
  responding?: boolean;
  startTime?: string;
}
const vr = ref<VrStatus | null>(null);
const launching = ref(false);
/** Brief cooldown after a successful launch: the WS push may lag, this blocks double clicks. */
const cooling = ref(false);
const err = ref('');
let off: (() => void) | null = null;
let errTimer = 0;
let coolTimer = 0;

onMounted(() => {
  void api
    .vrchat()
    .then((d) => {
      vr.value = (d ?? {}) as VrStatus;
    })
    .catch(() => {
      /* Keep the empty state when the old engine lacks the endpoint */
    });
  off = onWs('vrchat_status', (d) => {
    vr.value = (d ?? {}) as VrStatus;
  });
});
onBeforeUnmount(() => {
  off?.();
  window.clearTimeout(errTimer);
  window.clearTimeout(coolTimer);
});

const startLocal = computed(() => {
  const iso = vr.value?.startTime;
  if (!iso) return '';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
});

function flashErr(msg: string): void {
  err.value = msg;
  window.clearTimeout(errTimer);
  errTimer = window.setTimeout(() => (err.value = ''), 4000);
}

async function launch(): Promise<void> {
  if (vr.value?.running || launching.value || cooling.value) return; // E1: never a second copy while running
  launching.value = true;
  try {
    const r = (await api.vrchatLaunch()) as { ok?: boolean; error?: string };
    if (r.ok) {
      // The engine pushes a snapshot right after launch; refresh once more as a safety net.
      cooling.value = true;
      window.clearTimeout(coolTimer);
      coolTimer = window.setTimeout(() => (cooling.value = false), 10000);
      void api
        .vrchat()
        .then((d) => {
          vr.value = (d ?? {}) as VrStatus;
        })
        .catch(() => {});
    } else {
      flashErr(r.error ? `${t('vrc.launchfail')} (${r.error})` : t('vrc.launchfail'));
    }
  } catch {
    flashErr(t('vrc.launchfail'));
  } finally {
    launching.value = false;
  }
}
</script>

<template>
  <div class="x-gap vr-box" style="flex-wrap: wrap; align-items: center">
    <span class="vr-dot" :class="{ on: vr?.running }" />
    <b style="font-size: 12.5px">{{ t('vrc.status') }}</b>
    <span style="font-size: 12px" :style="{ color: vr?.running ? 'var(--good)' : 'var(--muted-foreground)' }">
      {{ vr?.running ? t('vrc.running') : t('vrc.notrunning') }}
    </span>
    <template v-if="vr?.running">
      <span v-if="!compact" class="x-muted" style="font-size: 12px">{{ t('vrc.pid') }} <b class="x-mono">{{ vr.pid }}</b></span>
      <span class="x-muted" style="font-size: 12px">{{ t('vrc.cpu') }} <b class="x-mono">{{ vr.cpuPct }}%</b></span>
      <span v-if="!compact" class="x-muted" style="font-size: 12px">{{ t('vrc.mem') }} <b class="x-mono">{{ vr.memoryMb }} MB</b></span>
      <span v-if="!compact" style="font-size: 12px" :style="{ color: vr.responding ? 'var(--good)' : 'var(--destructive)' }">
        {{ vr.responding ? t('vrc.ok') : t('vrc.stuck') }}
      </span>
      <span v-if="!compact && startLocal" class="x-muted" style="font-size: 12px">
        {{ t('vrc.started') }} <b class="x-mono">{{ startLocal }}</b>
      </span>
    </template>
    <button
      class="x-btn sm"
      :disabled="vr?.running || launching || cooling"
      v-bubble="t('vrc.launchhint')"
      @click="() => void launch()"
    >
      <Play class="x-btn-ico" />
      {{ t('vrc.launch') }}
    </button>
    <span v-if="err" style="font-size: 12px; color: var(--destructive)">{{ err }}</span>
  </div>
</template>

<style scoped>
.vr-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
  background: var(--muted-foreground);
}
.vr-dot.on {
  background: var(--good);
}
</style>
