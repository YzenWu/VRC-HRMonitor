<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch, type Component } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRoute } from 'vue-router';
import {
  Activity,
  Bluetooth,
  ChevronsLeft,
  ChevronsRight,
  Cpu,
  Database,
  FileJson,
  Gamepad2,
  Glasses,
  Globe,
  Grid2x2,
  HeartPulse,
  Images,
  Info,
  LayoutDashboard,
  Radio,
  ScrollText,
  Send,
  Server,
  Settings,
  Terminal,
  Wrench,
} from 'lucide-vue-next';
import { NAV } from '../router';
import { useAppStore } from '../stores/app';

const props = defineProps<{ collapsed: boolean }>();
const emit = defineEmits<{ toggle: [] }>();
const { t } = useI18n();
const route = useRoute();
const app = useAppStore();

/** Double-click the navigation space = close/expand (the same entry as the top right arrow button). */
function onNavDbl(e: MouseEvent): void {
  const el = e.target as Element | null;
  if (!el || !el.closest) return;
  if (el.closest('a, button, input')) return;
  emit('toggle');
}

const ICONS: Record<string, Component> = {
  overview: LayoutDashboard,
  dashboard: Grid2x2,
  heartbeat: HeartPulse,
  devices: Bluetooth,
  osc: Radio,
  pusher: Send,
  hwinfo: Cpu,
  headset: Glasses,
  web: Globe,
  apiserver: Server,
  settings: Settings,
  logs: ScrollText,
  console: Terminal,
  monitor: Activity,
  about: Info,
};

/** Remote Role White List Filter Navigation (local/administrator shows all). */
const items = computed(() => {
  const tabs = app.remote.tabs;
  if (!tabs) return NAV;
  return NAV.filter((n) => tabs.includes(n.tab));
});

// ---- P10 Toolkit dock: sits bottom-left, right above the collapse button. The remote role
// whitelist never contains "toolkit" (backend AllTabsExceptRestricted), so the dock stays
// local/admin-only while the main list keeps its own filter.
const toolkitOpen = ref(false);
const showToolkit = computed(() => app.remote.loaded && (!app.remote.remote || app.remote.admin));
const toolkitItems = [
  { tool: 'config', icon: FileJson },
  { tool: 'logs', icon: ScrollText },
  { tool: 'cache', icon: Database },
  { tool: 'photos', icon: Images },
  { tool: 'game', icon: Gamepad2 },
  { tool: 'process', icon: Activity },
];
// Any navigation away from the pop (e.g. a nav item click while it is open) collapses it.
watch(
  () => route.fullPath,
  () => (toolkitOpen.value = false),
);

// ---- L680 sliding tab indicator: the theme-colored active background becomes a shared bar that
// glides vertically from the previous tab to the next one. Linear interpolation; a click during a
// glide simply re-targets (CSS transitions resume from the current frame = built-in interruption).
const menuEl = ref<HTMLElement | null>(null);
const indTop = ref(0);
const indH = ref(0);
/** False while positioning without animation (first paint, tab-filter change, resize snap). */
const indReady = ref(false);
let ro: ResizeObserver | null = null;

function positionIndicator(animate: boolean): void {
  const root = menuEl.value;
  if (!root) return;
  const el = root.querySelector<HTMLElement>('.nav-item.active');
  if (!el) {
    indReady.value = false;
    return;
  }
  // A redirect can mount the menu before any item is active. In that case the first route update
  // must snap into place and reveal the indicator instead of leaving opacity at zero forever.
  if (!animate || !indReady.value) {
    // Snap: disable the transition for this frame so mounts and list changes don't slide.
    indReady.value = false;
    indTop.value = el.offsetTop;
    indH.value = el.offsetHeight;
    void nextTick(() => (indReady.value = true));
    return;
  }
  indTop.value = el.offsetTop;
  indH.value = el.offsetHeight;
}

watch(
  () => route.path,
  () => void nextTick(() => positionIndicator(true)),
);
// Tab-list changes (remote role filter) re-layout the menu: snap, don't glide.
watch(items, () => void nextTick(() => positionIndicator(false)));

onMounted(() => {
  void nextTick(() => positionIndicator(false));
  ro = new ResizeObserver(() => positionIndicator(true));
  if (menuEl.value) ro.observe(menuEl.value);
});
onBeforeUnmount(() => {
  ro?.disconnect();
  ro = null;
});
</script>

<template>
  <div ref="menuEl" class="nav-menu" @dblclick="onNavDbl">
    <!-- L680: shared indicator behind the items; the active item keeps only its text color. -->
    <div
      class="nav-ind"
      :class="{ ready: indReady }"
      :style="{ top: `${indTop}px`, height: `${indH}px` }"
      aria-hidden="true"
    />
    <RouterLink
      v-for="n in items"
      :key="n.tab"
      :to="n.path"
      class="nav-item"
      :data-nav-tab="n.tab"
      :class="{ active: route.path === n.path }"
    >
      <component :is="ICONS[n.tab]" class="nav-ico" />
      <span class="nav-label">{{ t('tab.' + n.tab) }}</span>
    </RouterLink>
  </div>

  <div v-if="showToolkit" class="toolkit-dock">
    <Transition name="tkpop">
      <div v-if="toolkitOpen" class="toolkit-pop">
        <RouterLink
          v-for="item in toolkitItems"
          :key="item.tool"
          :to="`/toolkit?tool=${item.tool}`"
          class="nav-item"
          v-bubble="t(`toolkit.${item.tool}desc`)"
          @click="toolkitOpen = false"
        >
          <component :is="item.icon" class="nav-ico" />
          <span class="nav-label">{{ t(`toolkit.${item.tool}`) }}</span>
        </RouterLink>
      </div>
    </Transition>
    <button
      class="nav-item toolkit-main"
      :class="{ active: route.path === '/toolkit' }"
      v-bubble="props.collapsed ? t('tab.toolkit') : ''"
      @click="toolkitOpen = !toolkitOpen"
    >
      <Wrench class="nav-ico" />
      <span class="nav-label">{{ t('tab.toolkit') }}</span>
    </button>
  </div>

  <div class="nav-foot">
    <button
      class="x-btn ghost sm foot-btn"
      v-bubble="props.collapsed ? t('nav.expand') : t('nav.collapse')"
      @click="emit('toggle')"
    >
      <ChevronsLeft v-if="!props.collapsed" class="x-btn-ico" />
      <ChevronsRight v-else class="x-btn-ico" />
    </button>
  </div>
</template>

<style scoped>
.nav-menu {
  position: relative;
}
.nav-ind {
  position: absolute;
  left: var(--gap-2);
  right: var(--gap-2);
  z-index: 0;
  border-radius: calc(var(--radius) - 2px);
  background: var(--sidebar-accent);
  opacity: 0;
  pointer-events: none;
  /* L680: linear interpolation for both axes; mid-glide clicks re-target from the current frame. */
  transition: top 180ms linear, height 180ms linear, opacity 120ms linear;
}
.nav-ind.ready {
  opacity: 1;
}
.nav-ind:not(.ready) {
  transition: none;
}
.nav-item {
  position: relative;
  z-index: 1;
}
/* The active item drops its own background so the gliding indicator stays visible. */
.nav-item.active {
  background: transparent;
  color: var(--sidebar-accent-foreground);
}
@media (prefers-reduced-motion: reduce) {
  .nav-ind.ready {
    transition: none;
  }
}

.toolkit-dock { position: relative; flex-shrink: 0; padding: 0 var(--gap-2) var(--gap-1); }
.toolkit-main { width: 100%; border: 0; background: transparent; font: inherit; }
.toolkit-pop { position: absolute; left: var(--gap-2); right: var(--gap-2); bottom: 100%; z-index: 5; padding: var(--gap-1); border: var(--hairline) solid var(--sidebar-border); border-radius: var(--radius); background: var(--sidebar); box-shadow: var(--elev-md); }
.toolkit-pop .nav-item { margin: 2px 0; }
/* P10: the pop glides up linearly from the dock button; both the global data-anim switch
   and the OS reduced-motion preference collapse it to an instant show/hide. */
.tkpop-enter-active,
.tkpop-leave-active {
  transition: opacity 0.16s linear, transform 0.16s linear;
}
.tkpop-enter-from,
.tkpop-leave-to {
  opacity: 0;
  transform: translateY(10px);
}
:global(html[data-anim='off']) .tkpop-enter-active,
:global(html[data-anim='off']) .tkpop-leave-active {
  transition: none;
}
@media (prefers-reduced-motion: reduce) {
  .tkpop-enter-active,
  .tkpop-leave-active {
    transition: none;
  }
}

/* High button, bottom bar, etc.: Height = -- PH 0  (the same benchmark as the bottom bar) with only the centered arrows left in the folding pattern */
.nav-foot .foot-btn {
  width: 100%;
  height: var(--bar-h);
  min-height: 0;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
</style>
