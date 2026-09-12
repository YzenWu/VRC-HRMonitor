<script setup lang="ts">






import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { Moon, Sun } from 'lucide-vue-next';
import { useUiStore } from '../stores/ui';
import { resolvedMode } from '../theme';

const { t } = useI18n();
const ui = useUiStore();

const isDark = computed(() => resolvedMode(ui.look) === 'dark');
const custom = computed(() => ui.look.palette === 'custom');

function toggle(): void {
  ui.apply({ mode: isDark.value ? 'light' : 'dark' });
  void ui.push();
}
</script>

<template>
  <button v-if="!custom" class="tt" v-bubble="t('nav.themetoggle')" :aria-label="t('nav.themetoggle')" @click="toggle">
    <Sun v-if="isDark" class="tt-ico" />
    <Moon v-else class="tt-ico" />
  </button>
</template>

<style scoped>
.tt {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 28px;
  height: 28px;
  padding: 0;
  border: var(--hairline) solid var(--border);
  border-radius: 999px;
  background: var(--card);
  color: var(--muted-foreground);
  cursor: pointer;
  transition: background-color 0.15s linear, color 0.15s linear, border-color 0.15s linear;
}
.tt:hover {
  background: var(--accent);
  color: var(--foreground);
  border-color: var(--ring);
}
.tt-ico {
  width: 15px;
  height: 15px;
}
</style>
