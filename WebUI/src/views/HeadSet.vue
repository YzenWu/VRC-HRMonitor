<script setup lang="ts">
import { useI18n } from 'vue-i18n';
import { Glasses } from 'lucide-vue-next';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();

/** #9 Layout Editor: This page of cards (original card unheaded, grided and added). */
const CARDS: GridCard[] = [
  {
    id: 'plan',
    titleKey: 'tab.headset',
    span: 12,
    bodyStyle: 'display: flex; align-items: center; gap: var(--gap-3); padding: var(--gap-4)',
  },
];
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.headset') }}</h1>
        <p class="page-desc">{{ t('wip') }}</p>
      </div>
      <EditLayoutBtn />
    </header>
    <div class="page-body">
      <CardGrid view="headset" :cards="CARDS">
        <template #plan>
          <Glasses style="width: 28px; height: 28px; color: var(--muted-foreground)" />
          <div>
            <div v-bubble="t('headset.plan')" style="font-weight: 600">{{ t('wip') }}</div>
          </div>
        </template>
      </CardGrid>
    </div>
  </div>
</template>
