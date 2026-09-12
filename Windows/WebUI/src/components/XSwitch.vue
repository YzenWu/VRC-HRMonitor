<script setup lang="ts">





const model = defineModel<boolean>({ required: true });
const props = defineProps<{ label?: string; title?: string; disabled?: boolean }>();
</script>

<template>
  <label class="x-switch" :class="{ on: model, disabled: props.disabled }" v-bubble="props.title">
    <input
      type="checkbox"
      role="switch"
      class="sw-input"
      :checked="model"
      :disabled="props.disabled"
      @change="model = ($event.target as HTMLInputElement).checked"
    />
    <span class="sw-track"><span class="sw-thumb" /></span>
    <span v-if="props.label" class="sw-label">{{ props.label }}</span>
    <slot />
  </label>
</template>

<style scoped>
.x-switch {
  display: inline-flex;
  align-items: center;
  gap: 7px;
  font-size: 12.5px;
  cursor: pointer;
  user-select: none;
}
.x-switch.disabled {
  opacity: 0.5;
  cursor: default;
}

.sw-input {
  position: absolute;
  width: 1px;
  height: 1px;
  opacity: 0;
  margin: 0;
  pointer-events: none;
}
.sw-track {
  position: relative;
  display: inline-block;

  width: calc(34px * var(--sp));
  height: calc(18px * var(--sp));
  flex-shrink: 0;
  /* Round corner follows the theme: When the theme is set to a straight angle, the switch is also variable, consistent design language */
  border-radius: min(calc(9px * var(--sp)), calc(var(--radius) + 3px));
  border: var(--hairline) solid var(--border);
  background: var(--secondary);
  transition: background-color 0.16s linear, border-color 0.16s linear;
}
.x-switch:hover .sw-track {
  border-color: var(--ring);
}
.sw-thumb {
  position: absolute;
  top: calc(2px * var(--sp));
  left: calc(2px * var(--sp));
  width: calc(12px * var(--sp));
  height: calc(12px * var(--sp));
  border-radius: min(calc(6px * var(--sp)), calc(var(--radius) + 1px));
  background: var(--muted-foreground);
  transition: transform 0.16s linear, background-color 0.16s linear;
}
.x-switch.on .sw-track {
  background: var(--primary);
  border-color: transparent;
}
.x-switch.on .sw-thumb {
  transform: translateX(calc(16px * var(--sp)));
  background: var(--primary-foreground);
}
.sw-input:focus-visible + .sw-track {
  outline: 2px solid var(--ring);
  outline-offset: 1px;
}
.sw-label {
  white-space: nowrap;
}
@media (prefers-reduced-motion: reduce) {
  .sw-track,
  .sw-thumb {
    transition: none;
  }
}
</style>
