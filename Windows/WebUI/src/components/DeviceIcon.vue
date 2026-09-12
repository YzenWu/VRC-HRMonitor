<script setup lang="ts">


import { computed } from 'vue';
import { Bluetooth, HeartPulse, Headphones, Lightbulb } from 'lucide-vue-next';
import type { Device } from '../types';

const props = defineProps<{ device: Device; size?: number }>();

const hr = computed(() => props.device.hrMarked === true);
const icon = computed(() => {
  if (hr.value) return HeartPulse;
  switch (props.device.category) {
    case 'hr':
      return HeartPulse;
    case 'audio':
      return Headphones;
    case 'home':
      return Lightbulb;
    default:
      return Bluetooth;
  }
});

const color = computed(() => (hr.value ? 'var(--primary)' : 'var(--muted-foreground)'));
const px = computed(() => `${props.size ?? 15}px`);
</script>

<template>
  <component
    :is="icon"
    v-bubble="device.type"
    :style="{ width: px, height: px, color, flexShrink: 0 }"
  />
</template>
