<script setup lang="ts">









import { computed, nextTick, onBeforeUnmount, onMounted, ref, useId, watch } from 'vue';
import { ChevronDown } from 'lucide-vue-next';

export interface XOpt {
  value: string | number;
  label: string;
  prefix?: string;
  disabled?: boolean;
}

const props = defineProps<{
  modelValue?: string | number | null;
  items: XOpt[];
  placeholder?: string;
}>();
const emit = defineEmits<{ (e: 'update:modelValue', v: string | number): void }>();

const open = ref(false);
const up = ref(false);
const active = ref(0);
const triggerEl = ref<HTMLButtonElement | null>(null);
const rootEl = ref<HTMLElement | null>(null);
const listEl = ref<HTMLElement | null>(null);
const posStyle = ref<Record<string, string>>({});
const uid = useId();
const listId = `xsel-list-${uid}`;
const optionId = (index: number) => `xsel-option-${uid}-${index}`;

const selected = computed(() => props.items.find((item) => item.value === props.modelValue));
const label = computed(() => {
  const mv = props.modelValue;
  if (mv === null || mv === undefined) return props.placeholder ?? '';
  if (selected.value) return selected.value.label;

  return mv === '' ? props.placeholder ?? '' : String(mv);
});

function currentIndex(): number {
  const v = props.modelValue;
  return props.items.findIndex((i) => i.value === v && !i.disabled);
}

function openList(): void {
  if (open.value) return;
  const root = rootEl.value;
  if (!root) return;
  const rect = root.getBoundingClientRect();
  const vh = window.innerHeight;

  const est = Math.min(Math.max(props.items.length, 1), 6.5) * 30 + 10;
  const below = vh - rect.bottom - 8;
  const above = rect.top - 8;
  up.value = below < est && above > below;
  const w = Math.min(Math.max(rect.width, 170), 340);
  const pos: Record<string, string> = { width: `${w}px`, left: `${rect.left}px` };
  if (up.value) {
    pos.bottom = `${vh - rect.top + 4}px`;
    pos.maxHeight = `${Math.max(140, Math.floor(above))}px`;
  } else {
    pos.top = `${rect.bottom + 4}px`;
    pos.maxHeight = `${Math.max(140, Math.floor(below))}px`;
  }
  posStyle.value = pos;
  const idx = currentIndex();
  active.value = idx >= 0 ? idx : props.items.findIndex((item) => !item.disabled);
  if (active.value < 0) active.value = 0;
  open.value = true;
  void nextTick(() => {
    listEl.value?.focus();
    listEl.value?.querySelector<HTMLElement>(`#${optionId(active.value)}`)?.scrollIntoView({ block: 'nearest' });
  });
}

function closeList(): void {
  if (!open.value) return;
  open.value = false;
  triggerEl.value?.focus();
}

function pick(it: XOpt): void {
  if (it.disabled) return;
  emit('update:modelValue', it.value);
  closeList();
}

function onTriggerKey(e: KeyboardEvent): void {
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    if (!open.value) openList();
    const dir = e.key === 'ArrowDown' ? 1 : -1;
    moveActive(dir);
    return;
  }
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault();
    if (open.value) {
      const it = props.items[active.value];
      if (it) pick(it);
    } else openList();
  }
}

function moveActive(dir: 1 | -1): void {
  const items = props.items;
  if (items.length === 0) return;
  let i = active.value;
  for (let n = 0; n < items.length; n++) {
    i = (i + dir + items.length) % items.length;
    if (!items[i].disabled) break;
  }
  active.value = i;
  void nextTick(() => {
    listEl.value?.querySelector<HTMLElement>(`#${optionId(i)}`)?.scrollIntoView({ block: 'nearest' });
  });
}

function onListKey(e: KeyboardEvent): void {
  const items = props.items;
  if (items.length === 0) return;
  if (e.key === 'Escape') {
    e.preventDefault();
    closeList();
    return;
  }
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    moveActive(e.key === 'ArrowDown' ? 1 : -1);
    return;
  }
  if (e.key === 'Enter' || e.key === ' ') {
    e.preventDefault();
    const it = items[active.value];
    if (it && !it.disabled) pick(it);
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

// Sync Highlight Index when list entry data changes (current value first, otherwise stop first)
watch(
  () => props.items,
  () => {
    if (open.value) {
      const idx = currentIndex();
      if (idx >= 0) active.value = idx;
    }
  },
);

defineExpose({ openList, closeList });
</script>

<template>
  <div ref="rootEl" class="xsel">
    <button
      ref="triggerEl"
      type="button"
      class="xsel-trigger"
      role="combobox"
      aria-haspopup="listbox"
      :aria-expanded="open"
      :aria-controls="listId"
      :aria-activedescendant="open ? optionId(active) : undefined"
      :class="[label === '' && placeholder ? 'ph' : '']"
      @click="open ? closeList() : openList()"
      @keydown="onTriggerKey"
    >
      <span v-if="selected?.prefix" class="xsel-prefix" aria-hidden="true">{{ selected.prefix }}</span>
      <span class="xsel-value">{{ label }}</span>
      <ChevronDown class="xsel-caret" :class="{ rot: open }" />
    </button>

    <Teleport to="body">
      <Transition :name="up ? 'xsel-up' : 'xsel-down'">
        <div
          v-if="open"
          :id="listId"
          ref="listEl"
          class="xsel-list"
          role="listbox"
          tabindex="-1"
          :aria-activedescendant="optionId(active)"
          :style="posStyle"
          @keydown="onListKey"
        >
        <div
          v-for="(it, i) in items"
          :key="String(it.value)"
          :id="optionId(i)"
          class="xsel-item"
          :class="{
            sel: String(it.value) === String(modelValue),
            act: i === active,
            dis: it.disabled,
          }"
          role="option"
          :aria-selected="String(it.value) === String(modelValue)"
          :aria-disabled="it.disabled || undefined"
          @pointerenter="!it.disabled && (active = i)"
          @pointerdown.prevent="pick(it)"
        >
          <span v-if="it.prefix" class="xsel-prefix" aria-hidden="true">{{ it.prefix }}</span>
          <span class="xsel-item-label">{{ it.label }}</span>
        </div>
        </div>
      </Transition>
    </Teleport>
  </div>
</template>

<style scoped>
.xsel {
  position: relative;
  display: inline-block;
  vertical-align: middle;
}
.xsel-trigger {
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
.xsel-trigger:hover {
  border-color: var(--ring);
}
.xsel-trigger:focus-visible {
  border-color: var(--ring);
  box-shadow: 0 0 0 2px rgb(var(--c-accent-rgb, 91 157 255) / 22%);
}
.xsel-trigger.ph .xsel-value {
  color: var(--muted-foreground);
}
.xsel-prefix {
  flex: 0 0 auto;
  font-family: "Segoe UI Emoji", "Noto Color Emoji", sans-serif;
  font-size: 15px;
  line-height: 1;
}
.xsel-item-label {
  overflow: hidden;
  text-overflow: ellipsis;
}
.xsel-value {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.xsel-caret {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  color: var(--muted-foreground);
  transition: transform 0.15s linear;
}
.xsel-caret.rot {
  transform: rotate(180deg);
}

/* Blast layer: A fixed position sticker trigger frame below/up. Go in and out of the world. */
.xsel-list {
  position: fixed;
  z-index: 999;
  padding: 4px;
  overflow-y: auto;
  background: var(--popover);
  border: var(--hairline) solid var(--input);
  border-radius: calc(var(--radius) - 1px);
  box-shadow: var(--elev-md);
}
.xsel-down-enter-active,
.xsel-down-leave-active,
.xsel-up-enter-active,
.xsel-up-leave-active {
  transform-origin: center top;
  transition: opacity 0.16s linear, transform 0.16s linear;
}
.xsel-down-leave-active,
.xsel-up-leave-active {
  /* Take it back a little faster than popping up. */
  transition-duration: 0.12s;
}
.xsel-down-enter-active,
.xsel-down-leave-active {
  transform-origin: top center;
}
.xsel-up-enter-active,
.xsel-up-leave-active {
  transform-origin: bottom center;
}
.xsel-down-enter-from,
.xsel-up-enter-from,
.xsel-down-leave-to,
.xsel-up-leave-to {
  opacity: 0;
}
.xsel-down-enter-from {
  transform: translateY(-5px) scale(0.98);
}
.xsel-down-leave-to {
  transform: translateY(-4px) scale(0.985);
}
.xsel-up-enter-from {
  transform: translateY(5px) scale(0.98);
}
.xsel-up-leave-to {
  transform: translateY(4px) scale(0.985);
}
.xsel-item {
  display: flex;
  align-items: center;
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
.xsel-item.sel {
  color: var(--primary);
  font-weight: 600;
}
.xsel-item.act {
  background: var(--secondary);
}
.xsel-item.dis {
  opacity: 0.45;
  cursor: not-allowed;
}
@media (prefers-reduced-motion: reduce) {
  .xsel-down-enter-active,
  .xsel-down-leave-active,
  .xsel-up-enter-active,
  .xsel-up-leave-active {
    transition: none;
  }
  .xsel-caret {
    transition: none;
  }
}
</style>
