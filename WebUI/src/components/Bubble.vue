<script setup lang="ts">





import { computed, nextTick, ref, watch } from 'vue';
import { bubble } from '../bubble';

const root = ref<HTMLDivElement | null>(null);

const size = ref({ w: 220, h: 40 });

const pos = computed(() => {
  const { w, h } = size.value;
  const center = Math.min(Math.max(bubble.x, 110), window.innerWidth - 110);
  const left = Math.max(4, center - w / 2);
  const above = bubble.y - h - 10;
  const top = above >= 8 ? above : bubble.y + 18;
  return { left, top };
});

watch(
  () => bubble.visible,
  async (v) => {
    if (!v) return;
    await nextTick();
    if (root.value) size.value = { w: root.value.offsetWidth, h: root.value.offsetHeight };
  },
);
</script>

<template>
  <Teleport to="body">
    <Transition name="bub">
      <div
        v-if="bubble.visible"
        ref="root"
        class="bubble"
        :style="{ left: pos.left + 'px', top: pos.top + 'px' }"
      >
        {{ bubble.text }}
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.bubble {
  position: fixed;
  z-index: 1200;
  max-width: 280px;
  padding: 5px 10px;
  border: var(--hairline) solid var(--border);
  border-radius: var(--radius, 8px);
  background: var(--popover, var(--card));
  box-shadow: var(--elev-lg, 0 6px 18px rgb(0 0 0 / 0.22));
  color: var(--foreground);
  font-size: 12px;
  line-height: 1.5;
  pointer-events: none;
  white-space: pre-line;
}
.bub-enter-active,
.bub-leave-active {
  transition: opacity 0.14s linear, transform 0.14s linear;
}
.bub-enter-from,
.bub-leave-to {
  opacity: 0;
  transform: translateY(3px);
}
@media (prefers-reduced-motion: reduce) {
  .bub-enter-active,
  .bub-leave-active {
    transition: none;
  }
}
</style>
