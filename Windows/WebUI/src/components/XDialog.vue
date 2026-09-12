<script setup lang="ts">








const props = defineProps<{
  open: boolean;
  title?: string;
  desc?: string;

  size?: 'sm' | 'md' | 'lg';

  dismissable?: boolean;
}>();
const emit = defineEmits<{ close: [] }>();

const W = { sm: 360, md: 460, lg: 560 };
</script>

<template>
  <Teleport to="body">
    <Transition name="xdlg">
      <div
        v-if="props.open"
        class="xdlg-scrim"
        @click.self="props.dismissable === false ? undefined : emit('close')"
      >
        <div class="xdlg-card" :style="{ width: `min(${W[props.size ?? 'md']}px, calc(100vw - 24px))` }">
          <div v-if="props.title" class="xdlg-title">{{ props.title }}</div>
          <div v-if="props.desc" class="xdlg-desc">{{ props.desc }}</div>
          <slot />
          <div v-if="$slots.actions" class="xdlg-actions"><slot name="actions" /></div>
        </div>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
/* Fuzzy background: Darken only the current theme background and no black rectangle */
.xdlg-scrim {
  position: fixed;
  inset: 0;
  z-index: 950;
  display: grid;
  place-items: center;
  padding: var(--gap-3);
  background: color-mix(in oklab, var(--background) 58%, transparent);
  backdrop-filter: blur(10px) saturate(115%);
}
.xdlg-card {
  display: flex;
  flex-direction: column;
  gap: var(--gap-2);
  padding: var(--gap-4);
  border: var(--hairline) solid var(--border);
  border-radius: calc(var(--radius) + 2px);
  background: var(--card);
  color: var(--foreground);
  box-shadow: var(--elev-lg);
}
.xdlg-title {
  font-weight: 650;
  font-size: 15px;
}
.xdlg-desc {
  margin: 0;
  font-size: 12.5px;
  line-height: 1.55;
  color: var(--muted-foreground);
}
.xdlg-actions {
  display: flex;
  gap: var(--gap-2);
  flex-wrap: wrap;
  margin-top: var(--gap-1);
}
.xdlg-enter-active,
.xdlg-leave-active {
  transition: opacity 0.16s linear;
}
.xdlg-enter-from,
.xdlg-leave-to {
  opacity: 0;
}
.xdlg-enter-active .xdlg-card {
  animation: xdlg-pop 0.18s linear;
}
@keyframes xdlg-pop {
  from {
    opacity: 0;
    transform: translateY(-6px) scale(0.985);
  }
}
@media (prefers-reduced-motion: reduce) {
  .xdlg-enter-active,
  .xdlg-leave-active {
    transition: none;
  }
  .xdlg-enter-active .xdlg-card {
    animation: none;
  }
}
</style>
