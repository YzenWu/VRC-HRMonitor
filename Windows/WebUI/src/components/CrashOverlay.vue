<script setup lang="ts">





import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { AlertTriangle, Loader2, Power, RefreshCw, RotateCcw, X } from 'lucide-vue-next';
import { clearEngineCrash, engineCrash, inShell, postShell, relaunchEngine, relaunchState } from '../shell';

const { t } = useI18n();

const show = computed(() => engineCrash.value !== null);
const busy = computed(() => relaunchState.value === 'restarting');
const failed = computed(() => relaunchState.value === 'failed');

function restart(): void {
  if (busy.value) return;
  relaunchEngine();
}
function exitApp(): void {
  clearEngineCrash();
  postShell('win.close');
}
</script>

<template>
  <Teleport to="body">
    <Transition name="mask">
      <div v-if="show" class="crash-mask">
        <div class="crash-box">
          <Loader2 v-if="busy" class="spin" />
          <AlertTriangle v-else class="warn-ico" />
          <div class="title">{{ busy ? t('crash.retrying') : failed ? t('crash.retryfailtitle') : t('crash.title') }}</div>
          <div class="x-muted sub">{{ busy ? '' : failed ? t('crash.retryfailbody') : t('crash.body') }}</div>
          <div v-if="!busy && engineCrash" class="x-mono sub code">exit code: {{ engineCrash.code ?? '-' }}</div>
          <div class="x-gap" style="justify-content: center; flex-wrap: wrap">
            <button class="x-btn sm primary" :disabled="busy" @click="restart">
              <component :is="busy ? Loader2 : failed ? RotateCcw : RefreshCw" class="x-btn-ico" />
              {{ failed ? t('crash.retry') : t('crash.restart') }}
            </button>
            <button v-if="inShell" class="x-btn sm ghost" :disabled="busy" @click="exitApp">
              <Power class="x-btn-ico" />
              {{ t('close.exit') }}
            </button>
            <button class="x-btn sm ghost" :disabled="busy" @click="clearEngineCrash">
              <X class="x-btn-ico" />
              {{ t('crash.later') }}
            </button>
          </div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.crash-mask {
  position: fixed;
  inset: 0;
  z-index: 980;
  display: grid;
  place-items: center;
  padding: var(--gap-3);
  background: transparent;
  backdrop-filter: blur(8px) saturate(110%);
}
.crash-box {
  min-width: 260px;
  max-width: 440px;
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
.code {
  font-size: 11.5px;
  color: var(--muted-foreground);
}
.warn-ico {
  width: 30px;
  height: 30px;
  color: var(--bad);
}
.spin {
  width: 30px;
  height: 30px;
  color: var(--primary);
  animation: spin 0.9s linear infinite;
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
  .mask-enter-active,
  .mask-leave-active {
    transition: none;
  }
  .spin {
    animation: none;
  }
}
</style>
