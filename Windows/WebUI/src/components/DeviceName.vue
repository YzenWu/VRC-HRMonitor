<script setup lang="ts">





import { computed } from 'vue';
import type { Device } from '../types';

const props = defineProps<{ device: Device }>();


const segs = computed(() =>
  props.device.nameSegs?.length ? props.device.nameSegs : [{ text: props.device.name, hit: false }],
);
</script>

<template>
  <span class="dev-name" v-bubble="device.rawName">
    <template v-for="(s, i) in segs" :key="i">
      <em v-if="s.hit" class="hit">{{ s.text }}</em>
      <span v-else>{{ s.text }}</span>
      <span v-if="i < segs.length - 1">&nbsp;</span>
    </template>
  </span>
</template>

<style scoped>
.dev-name {
  font-weight: 500;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.dev-name .hit {
  font-weight: 700;
  font-style: italic;
  font-size: 1.02em;
}
</style>
