<script setup lang="ts">
/** Generic barchart: A set of values is rendered into equal width bars (without a chart library, style follows the theme variable). */
const props = withDefaults(
  defineProps<{
    values: number[];
    labels?: string[];
    height?: number;
    highlightLast?: boolean;
  }>(),
  { height: 120, highlightLast: false },
);

const max = () => Math.max(1, ...props.values);
</script>

<template>
  <div style="display: flex; align-items: flex-end; gap: 2px; width: 100%" :style="{ height: `${props.height}px` }">
    <div
      v-for="(v, i) in props.values"
      :key="i"
      v-bubble="`${props.labels?.[i] ?? i}: ${v}`"
      style="flex: 1; min-width: 3px; display: flex; flex-direction: column; justify-content: flex-end; height: 100%"
    >
      <div
        :style="{
          height: `${Math.max(1, (v / max()) * 100)}%`,
          background:
            props.highlightLast && i === props.values.length - 1 ? 'var(--primary)' : 'color-mix(in oklab, var(--primary) 55%, transparent)',
          borderRadius: '2px 2px 0 0',
        }"
      />
    </div>
  </div>
</template>
