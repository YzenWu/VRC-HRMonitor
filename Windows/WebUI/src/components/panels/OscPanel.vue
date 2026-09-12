<script setup lang="ts">

import { useI18n } from 'vue-i18n';
import { api } from '../../api';
import { useAppStore } from '../../stores/app';
import { useOscStore } from '../../stores/osc';

const { t } = useI18n();
const app = useAppStore();
const osc = useOscStore();
</script>

<template>
  <div style="display: flex; flex-direction: column; gap: var(--gap-2); font-size: 12.5px">
    <div class="x-gap">
      <button class="x-btn sm primary" @click="() => void api.oscConnect(!app.osc?.connected).then(() => app.sync())">
        {{ app.osc?.connected ? t('osc.disconnect') : t('osc.connect') }}
      </button>
      <span class="x-muted x-mono">{{ app.osc?.ip }}:{{ app.osc?.port }}</span>
    </div>
    <div class="x-muted x-mono">
      sent {{ app.osc?.sent ?? 0 }} · fail {{ app.osc?.fail ?? 0 }} · recv {{ app.osc?.recv ?? 0 }}
    </div>
    <div class="x-muted">{{ osc.list.length }} {{ t('osc.paramcount') }}</div>
  </div>
</template>
