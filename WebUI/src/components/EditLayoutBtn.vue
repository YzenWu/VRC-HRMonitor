<script setup lang="ts">




import { useI18n } from 'vue-i18n';
import { Layout } from 'lucide-vue-next';
import { layoutEdit, setLayoutEdit } from '../stores/layoutEdit';

const { t } = useI18n();
</script>

<template>
  <button class="x-btn sm" :class="{ primary: layoutEdit }" @click="setLayoutEdit(!layoutEdit)">
    <Layout class="x-btn-ico" />
    {{ layoutEdit ? t('dash.exitEdit') : t('dash.enterEdit') }}
  </button>
</template>
