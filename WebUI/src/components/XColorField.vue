<script setup lang="ts">
import { ref, watch } from 'vue';
import { RotateCcw } from 'lucide-vue-next';

const props = defineProps<{
  modelValue: string;
  defaultValue: string;
  label?: string;
}>();
const emit = defineEmits<{
  (e: 'update:modelValue', value: string): void;
  (e: 'change', value: string): void;
}>();

const text = ref(props.modelValue);
const invalid = ref(false);
const HEX = /^#[0-9a-f]{6}$/i;

watch(() => props.modelValue, (value) => {
  text.value = value;
  invalid.value = false;
});

function commit(value = text.value): void {
  const normalized = value.trim();
  if (!HEX.test(normalized)) {
    invalid.value = true;
    text.value = props.modelValue;
    return;
  }
  const next = normalized.toUpperCase();
  invalid.value = false;
  text.value = next;
  emit('update:modelValue', next);
  emit('change', next);
}

function onColor(event: Event): void {
  commit((event.target as HTMLInputElement).value);
}

function reset(): void {
  text.value = props.defaultValue;
  commit();
}
</script>

<template>
  <div class="x-color-field" :class="{ invalid }">
    <input
      class="x-color-native"
      type="color"
      :value="HEX.test(modelValue) ? modelValue : defaultValue"
      :aria-label="label"
      @input="onColor"
    />
    <input
      v-model="text"
      class="x-input x-mono x-color-text"
      maxlength="7"
      spellcheck="false"
      :aria-label="label"
      :aria-invalid="invalid"
      @blur="commit()"
      @keydown.enter.prevent="commit()"
      @paste="invalid = false"
    />
    <button class="x-btn x-btn-icon" type="button" :aria-label="$t('common.default')" @click="reset">
      <RotateCcw :size="14" />
    </button>
  </div>
</template>

<style scoped>
.x-color-field {
  display: inline-flex;
  align-items: center;
  gap: var(--gap-1);
}
.x-color-native {
  width: 42px;
  height: var(--ctl-h);
  padding: 2px;
  border: var(--hairline) solid var(--input);
  border-radius: calc(var(--radius) - 1px);
  background: var(--popover);
  cursor: pointer;
}
.x-color-text {
  width: 96px;
  text-transform: uppercase;
}
.x-color-field.invalid .x-color-text {
  border-color: var(--bad);
}
</style>
