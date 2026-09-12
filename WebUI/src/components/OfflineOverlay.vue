<script setup lang="ts">






import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { AlertTriangle, Download, Loader2, Power, X } from 'lucide-vue-next';
import { attempt, gaveUp, lastError, MAX_ATTEMPTS, offline, retryNow } from '../api/watchdog';
import { inShell, postShell } from '../shell';

const { t } = useI18n();
const copied = ref(false);
const exported = ref('');

const detail = computed(() =>
  [
    `time: ${new Date().toISOString()}`,
    `url: ${location.href}`,
    `attempts: ${attempt.value}/${MAX_ATTEMPTS}`,
    `error: ${lastError.value || 'unknown'}`,
    `ua: ${navigator.userAgent}`,
  ].join('\n'),
);

async function copyDetail(): Promise<void> {
  try {
    await navigator.clipboard.writeText(detail.value);
  } catch {

    const sel = window.getSelection();
    const range = document.createRange();
    const el = document.getElementById('offline-detail');
    if (el && sel) {
      range.selectNodeContents(el);
      sel.removeAllRanges();
      sel.addRange(range);
    }
  }
  copied.value = true;
  window.setTimeout(() => (copied.value = false), 1600);
}


function exportConfig(): void {
  const data: Record<string, unknown> = {};
  for (let i = 0; i < localStorage.length; i++) {
    const k = localStorage.key(i);
    if (!k || !(k.startsWith('hrm-') || k.startsWith('hb-'))) continue;
    const raw = localStorage.getItem(k) ?? '';

    try {
      data[k] = JSON.parse(raw);
    } catch {
      data[k] = raw;
    }
  }
  const blob = new Blob([JSON.stringify({ exportedAt: new Date().toISOString(), prefs: data }, null, 2)], {
    type: 'application/json',
  });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `hrm-webui-prefs-${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
  a.click();
  URL.revokeObjectURL(url);
  exported.value = a.download;
}

function closeTab(): void {
  window.close();
}


function exitApp(): void {
  if (inShell) postShell('win.close');
  else window.close();
}
</script>

<template>
  <Transition name="mask">
    <div v-if="offline || gaveUp" class="offline-mask">
      <!-- Reconnection: Fuzzy layer directly medium information, no second layer background -->
      <div v-if="!gaveUp" class="offline-live">
        <Loader2 class="spin" />
        <div class="title">{{ t('net.reconnecting') }}</div>
        <div class="x-muted sub">{{ attempt }} / {{ MAX_ATTEMPTS }}</div>
        <div v-if="lastError" class="x-mono err">{{ lastError }}</div>
      </div>

      <!-- Waiver: error message (no card background, only fuzzy support) -->
      <div v-else class="offline-box">
        <AlertTriangle class="warn-ico" />
        <div class="title">{{ t('net.failed') }}</div>
        <div class="x-muted sub">{{ t('net.failedhint') }}</div>
        <pre id="offline-detail" class="x-mono detail" v-bubble="t('net.copy')" @click="() => void copyDetail()">{{ detail }}</pre>
        <div class="x-muted sub">{{ copied ? t('net.copied') : t('net.copy') }}</div>
        <!-- Operation line (sequence: export configuration / exit program / retry; browser add one more level tab) -->
        <div class="x-gap" style="justify-content: center; flex-wrap: wrap">
          <button class="x-btn sm" @click="exportConfig">
            <Download class="x-btn-ico" />
            {{ t('net.exportcfg') }}
          </button>
          <button class="x-btn sm ghost" @click="exitApp">
            <Power class="x-btn-ico" />
            {{ t('close.exit') }}
          </button>
          <button v-if="!inShell" class="x-btn sm ghost" @click="closeTab">
            <X class="x-btn-ico" />
            {{ t('net.closetab') }}
          </button>
          <button class="x-btn sm primary" @click="retryNow">{{ t('net.retry') }}</button>
        </div>
        <div v-if="exported" class="x-muted sub x-mono">{{ exported }}</div>
      </div>
    </div>
  </Transition>
</template>

<style scoped>

.offline-mask {
  position: fixed;
  inset: 0;
  z-index: 900;
  display: grid;
  place-items: center;
  padding: var(--gap-3);
  background: transparent;
  backdrop-filter: blur(10px) saturate(115%);
}
/* Reconnection: Put icons and text directly on the blur layer, with no card */
.offline-live {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--gap-2);
  text-align: center;
  max-width: 460px;
}
/* The error message after relinquishing is also floating on the fuzzy layer without its own background/boundary/shade */
.offline-box {
  min-width: 260px;
  max-width: 560px;
  padding: var(--gap-4);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--gap-2);
  text-align: center;
}
.title {
  font-weight: 650;
  font-size: 15px;
}
.sub {
  font-size: 12px;
}
.err {
  font-size: 11.5px;
  color: var(--muted-foreground);
  word-break: break-all;
}
.detail {
  width: 100%;
  margin: 0;
  padding: var(--gap-2);
  max-height: 160px;
  overflow: auto;
  text-align: left;
  font-size: 11.5px;
  white-space: pre-wrap;
  word-break: break-all;
  border-bottom: var(--hairline) solid var(--border);
  color: var(--foreground);
  cursor: copy;
}
.spin {
  width: 34px;
  height: 34px;
  color: var(--primary);
  animation: spin 0.9s linear infinite;
}
.warn-ico {
  width: 30px;
  height: 30px;
  color: var(--bad);
}
@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
.mask-enter-active,
.mask-leave-active {
  transition: opacity 0.18s linear;
}
.mask-enter-from,
.mask-leave-to {
  opacity: 0;
}
@media (prefers-reduced-motion: reduce) {
  .spin {
    animation: none;
  }
  .mask-enter-active,
  .mask-leave-active {
    transition: none;
  }
}
</style>
