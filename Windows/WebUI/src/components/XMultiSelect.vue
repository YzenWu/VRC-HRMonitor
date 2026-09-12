<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { Check, ChevronDown } from 'lucide-vue-next';

/** Multi-select combobox option (P1 autostart methods). */
export interface XMOpt {
  value: string;
  label: string;
}

const props = defineProps<{
  modelValue: string[];
  items: XMOpt[];
  placeholder?: string;
  disabled?: boolean;
}>();
const emit = defineEmits<{ (e: 'update:modelValue', v: string[]): void }>();

const open = ref(false);
const up = ref(false);
const rootEl = ref<HTMLElement | null>(null);
const listEl = ref<HTMLElement | null>(null);
const posStyle = ref<Record<string, string>>({});

/** Trigger label: joined selected labels, or the placeholder when nothing is selected. */
const label = computed(() => {
  const sel = props.items.filter((i) => props.modelValue.includes(i.value));
  return sel.length > 0 ? sel.map((i) => i.label).join(', ') : props.placeholder ?? '';
});

function isSelected(v: string): boolean {
  return props.modelValue.includes(v);
}

function toggle(v: string): void {
  if (props.disabled) return;
  const next = isSelected(v)
    ? props.modelValue.filter((x) => x !== v)
    : [...props.modelValue, v];
  // Keep the option order stable regardless of click order.
  const ordered = props.items.map((i) => i.value).filter((x) => next.includes(x));
  emit('update:modelValue', ordered);
}

function openList(): void {
  if (props.disabled || open.value) return;
  const root = rootEl.value;
  if (!root) return;
  const rect = root.getBoundingClientRect();
  const vh = window.innerHeight;
  const est = Math.min(Math.max(props.items.length, 1), 6.5) * 30 + 10;
  const below = vh - rect.bottom - 8;
  const above = rect.top - 8;
  up.value = below < est && above > below;
  const w = Math.min(Math.max(rect.width, 190), 360);
  const pos: Record<string, string> = { width: `${w}px`, left: `${rect.left}px` };
  if (up.value) {
    pos.bottom = `${vh - rect.top + 4}px`;
    pos.maxHeight = `${Math.max(140, Math.floor(above))}px`;
  } else {
    pos.top = `${rect.bottom + 4}px`;
    pos.maxHeight = `${Math.max(140, Math.floor(below))}px`;
  }
  posStyle.value = pos;
  open.value = true;
  void nextTick(() => listEl.value?.focus());
}

function closeList(): void {
  if (!open.value) return;
  open.value = false;
  rootEl.value?.querySelector('button')?.focus();
}

function onTriggerKey(e: KeyboardEvent): void {
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp' || e.key === 'Enter' || e.key === ' ') {
    e.preventDefault();
    openList();
  }
}

function onListKey(e: KeyboardEvent): void {
  if (e.key === 'Escape') {
    e.preventDefault();
    closeList();
  }
}

function onDown(e: PointerEvent): void {
  if (!open.value) return;
  const t = e.target as Node;
  if (rootEl.value?.contains(t) || listEl.value?.contains(t)) return;
  closeList();
}

function onScrollAny(): void {
  if (open.value) closeList();
}

function onResize(): void {
  if (open.value) closeList();
}

onMounted(() => {
  window.addEventListener('pointerdown', onDown, true);
  window.addEventListener('scroll', onScrollAny, true);
  window.addEventListener('resize', onResize);
});
onBeforeUnmount(() => {
  window.removeEventListener('pointerdown', onDown, true);
  window.removeEventListener('scroll', onScrollAny, true);
  window.removeEventListener('resize', onResize);
});
</script>

<template>
  <div ref="rootEl" class="xmsel" :class="{ disabled: props.disabled }">
    <button
      type="button"
      class="xmsel-trigger"
      :class="[label === '' && placeholder ? 'ph' : '']"
      :disabled="props.disabled"
      @click="open ? closeList() : openList()"
      @keydown="onTriggerKey"
    >
      <span class="xmsel-value">{{ label }}</span>
      <ChevronDown class="xmsel-caret" :class="{ rot: open }" />
    </button>

    <Teleport to="body">
      <Transition :name="up ? 'xmsel-up' : 'xmsel-down'">
        <div v-if="open" ref="listEl" class="xmsel-list" :style="posStyle" tabindex="-1" @keydown="onListKey">
          <div
            v-for="it in items"
            :key="it.value"
            class="xmsel-item"
            role="option"
            :aria-selected="isSelected(it.value)"
            @pointerdown.prevent="toggle(it.value)"
          >
            <span class="xmsel-box" :class="{ on: isSelected(it.value) }">
              <Check v-if="isSelected(it.value)" class="xmsel-check" />
            </span>
            <span class="xmsel-label">{{ it.label }}</span>
          </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
.xmsel {
  position: relative;
  display: inline-block;
  vertical-align: middle;
}
.xmsel-trigger {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 6px;
  width: 100%;
  height: var(--ctl-h);
  padding: 0 calc(8px * var(--sp));
  border-radius: calc(var(--radius) - 1px);
  border: var(--hairline) solid var(--input);
  background: var(--popover);
  color: var(--foreground);
  font: inherit;
  font-size: 13px;
  outline: none;
  cursor: pointer;
  text-align: left;
  transition: border-color 0.15s linear, box-shadow 0.15s linear;
}
.xmsel-trigger:hover {
  border-color: var(--ring);
}
.xmsel.disabled {
  opacity: 0.5;
}
.xmsel-trigger:disabled {
  cursor: default;
}
.xmsel-trigger:focus-visible {
  border-color: var(--ring);
  box-shadow: 0 0 0 2px rgb(var(--c-accent-rgb, 91 157 255) / 22%);
}
.xmsel-trigger.ph .xmsel-value {
  color: var(--muted-foreground);
}
.xmsel-value {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.xmsel-caret {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  color: var(--muted-foreground);
  transition: transform 0.15s linear;
}
.xmsel-caret.rot {
  transform: rotate(180deg);
}

.xmsel-list {
  position: fixed;
  z-index: 999;
  padding: 4px;
  overflow-y: auto;
  background: var(--popover);
  border: var(--hairline) solid var(--input);
  border-radius: calc(var(--radius) - 1px);
  box-shadow: var(--elev-md);
  outline: none;
}
.xmsel-item {
  display: flex;
  align-items: center;
  gap: 8px;
  height: calc(28px * var(--sp));
  padding: 0 calc(8px * var(--sp));
  border-radius: calc(var(--radius) - 3px);
  font-size: 13px;
  color: var(--foreground);
  cursor: pointer;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.xmsel-item:hover {
  background: var(--secondary);
}
.xmsel-box {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 15px;
  height: 15px;
  flex-shrink: 0;
  border-radius: calc(var(--radius) - 4px);
  border: var(--hairline) solid var(--input);
  background: var(--background);
}
.xmsel-box.on {
  background: var(--primary);
  border-color: var(--primary);
  color: var(--primary-foreground);
}
.xmsel-check {
  width: 11px;
  height: 11px;
}
.xmsel-down-enter-active,
.xmsel-down-leave-active,
.xmsel-up-enter-active,
.xmsel-up-leave-active {
  transform-origin: center top;
  transition: opacity 0.16s linear, transform 0.16s linear;
}
.xmsel-down-leave-active,
.xmsel-up-leave-active {
  transition-duration: 0.12s;
}
.xmsel-up-enter-active,
.xmsel-up-leave-active {
  transform-origin: bottom center;
}
.xmsel-down-enter-from,
.xmsel-up-enter-from,
.xmsel-down-leave-to,
.xmsel-up-leave-to {
  opacity: 0;
}
.xmsel-down-enter-from {
  transform: translateY(-5px) scale(0.98);
}
.xmsel-down-leave-to {
  transform: translateY(-4px) scale(0.985);
}
.xmsel-up-enter-from {
  transform: translateY(5px) scale(0.98);
}
.xmsel-up-leave-to {
  transform: translateY(4px) scale(0.985);
}
@media (prefers-reduced-motion: reduce) {
  .xmsel-down-enter-active,
  .xmsel-down-leave-active,
  .xmsel-up-enter-active,
  .xmsel-up-leave-active,
  .xmsel-caret {
    transition: none;
  }
}
</style>
