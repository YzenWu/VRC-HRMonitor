<script setup lang="ts">

import { computed } from 'vue';
import { useAppStore } from '../../stores/app';
import BpmChart from '../BpmChart.vue';
import { hbMode, hbPoints, hbSmooth, hbSource } from '../../prefs';

const app = useAppStore();
const data = computed(() => app.curveOf(hbSource.value));
</script>

<template>
  <BpmChart :data="data" :height="150" :smooth="hbSmooth" :points="hbPoints" :mode="hbMode" />
</template>
