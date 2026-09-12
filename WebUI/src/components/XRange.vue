<script setup lang="ts">






import { computed } from 'vue';

const props = defineProps<{
  modelValue: number;
  min: number;
  max: number;
  step: number;
  hardMin: number;

  hardMax?: number;
  /** Saves the default value when empty. */
  default: number;
  suffix?: string;
}>();
const emit = defineEmits<{ 'update:modelValue': [number]; change: [] }>();

/** The value falls within the slider cover to show the slider. */
const inRange = computed(() => props.modelValue >= props.min && props.modelValue <= props.max);

function onSlide(e: Event): void {
  emit('update:modelValue', Number((e.target as HTMLInputElement).value));
}

function onNumber(e: Event): void {
  const raw = (e.target as HTMLInputElement).value.trim();
  if (raw === '') return; // Empty to global return rule
  const n = Number(raw);
  if (!Number.isFinite(n)) return;
  const hi = props.hardMax ?? Infinity;
  emit('update:modelValue', Math.min(hi, Math.max(props.hardMin, n)));
  emit('change');
}
</script>

<template>
  <span class="x-range">
    <Transition name="xr">
      <input
        v-if="inRange"
        class="xr-slider"
        type="range"
        :min="min"
        :max="max"
        :step="step"
        :value="modelValue"
        @input="onSlide"
        @change="emit('change')"
      />
    </Transition>
    <input
      class="x-input x-mono xr-num"
      type="number"
      :min="hardMin"
      :max="hardMax"
      :step="step"
      :value="modelValue"
      :data-default="String(props.default)"
      @change="onNumber"
    />
    <span v-if="suffix" class="x-muted xr-suffix">{{ suffix }}</span>
  </span>
</template>

<style scoped>
.x-range {
  display: inline-flex;
  align-items: center;
}

input.xr-slider {
  width: calc(132px * var(--sp));
  min-width: 0;
  margin-right: var(--gap-2);
  transition: width 0.22s linear, margin-right 0.22s linear, opacity 0.16s linear;
}
.xr-enter-from,
.xr-leave-to {
  width: 0;
  margin-right: 0;
  opacity: 0;
}
.xr-num {
  width: 104px;
}
.xr-suffix {
  margin-left: 6px;
  font-size: 11.5px;
}
</style>
