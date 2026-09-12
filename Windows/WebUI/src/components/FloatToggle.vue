<script setup lang="ts">


import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import XSwitch from './XSwitch.vue';

const { t } = useI18n();
const app = useAppStore();

const disabled = computed(() => !app.floatMain && !app.devices.some((d) => d.connected));
const model = computed({
  get: () => app.floatMain,
  set: (v: boolean) => {

    void (v ? api.floatOpen() : api.floatClose())
      .then(() => app.sync())
      .catch(() => app.sync());
  },
});
</script>

<template>
  <XSwitch v-model="model" :label="t('float.main')" :disabled="disabled" v-bubble="t('float.main')" />
</template>
