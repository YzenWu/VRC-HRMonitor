<script setup lang="ts">
import { onMounted } from 'vue';
import MainLayout from './layouts/MainLayout.vue';
import OfflineOverlay from './components/OfflineOverlay.vue';
import ContextMenu from './components/ContextMenu.vue';
import CrashOverlay from './components/CrashOverlay.vue';
import DeviceDialog from './components/DeviceDialog.vue';
import Bubble from './components/Bubble.vue';
import { useAppStore } from './stores/app';
import { useUiStore } from './stores/ui';

const app = useAppStore();
const ui = useUiStore();

onMounted(() => {
  // Reapply the theme when the backend reports a system theme change.
  app.onSysTheme((t) => ui.onSystemTheme(t));
  app.start();
  void ui.pull();
});
</script>

<template>
  <MainLayout />
  <OfflineOverlay />
  <CrashOverlay />
  <DeviceDialog />
  <ContextMenu />
  <!-- Single global hover bubble rendered from the directive-driven singleton. -->
  <Bubble />
</template>
