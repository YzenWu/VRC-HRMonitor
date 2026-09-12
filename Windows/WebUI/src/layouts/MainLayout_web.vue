<script setup lang="ts">
/**
 * Shell exclusive layout: Top imitation   PH 0  titlebar + left navigation + main area.
 * There are only two differences from the browser version ( PH 0), so the whole part is saved instead of a branch:
 *   1. Additional self-drawing title bar (host without system tops, on which window operations depend)
 *   2. Desktop window does not have a narrow-screen drawer/ mask/   PH 0  (the minimum width of the shell is above the breakpoint)
 */
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterView, useRoute } from 'vue-router';
import NavMenu from './NavMenu.vue';
import StatusBar from './StatusBar.vue';
import BrandPill from '../components/BrandPill.vue';
import SafeModeBanner from '../components/SafeModeBanner.vue';
import TitleBar_web from './TitleBar_web.vue';
import { useBrand } from '../composables/useBrand';
import { useAppStore } from '../stores/app';
import RemoteLogin from '../components/RemoteLogin.vue';
import { NAV } from '../router';
import { barH, navCollapsed, navWidth } from '../prefs';
import { setLayoutEdit } from '../stores/layoutEdit';

const { t } = useI18n();
const { brandText } = useBrand();
const route = useRoute();
const app = useAppStore();
const collapsed = navCollapsed;

const EXPANDED_MIN = 160;
const DRAG_MIN = 96;
const DRAG_MAX = 480;
const COLLAPSE_AT = 132;
const navW = navWidth;
const navDragging = ref(false);
watch(
  navW,
  (v) => {
    if (navDragging.value) return;
    const clamped = Math.min(DRAG_MAX, Math.max(EXPANDED_MIN, Number.isFinite(v) ? v : 224));
    if (clamped !== v) navW.value = clamped;
  },
  { immediate: true },
);

let dragStartX = 0;
let dragStartW = 224;
let rememberedW = 224;

function onGripDown(e: PointerEvent): void {
  dragStartX = e.clientX;
  if (collapsed.value) {
    rememberedW = navW.value;
    collapsed.value = false;
    dragStartW = 60;
  } else {
    dragStartW = navW.value;
  }
  navDragging.value = true;
  document.body.style.cursor = 'ew-resize';
  document.body.style.userSelect = 'none';
  (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
}
function onGripMove(e: PointerEvent): void {
  if (!navDragging.value) return;
  navW.value = Math.min(DRAG_MAX, Math.max(DRAG_MIN, dragStartW + (e.clientX - dragStartX)));
}
function onGripUp(): void {
  if (!navDragging.value) return;
  navDragging.value = false;
  document.body.style.cursor = '';
  document.body.style.userSelect = '';
  const w = navW.value;
  if (w < COLLAPSE_AT) {
    collapsed.value = true;
    navW.value = Math.min(DRAG_MAX, Math.max(EXPANDED_MIN, rememberedW));
  } else {
    navW.value = Math.max(EXPANDED_MIN, w);
  }
}

// Automatically exit layout editing when leaving the current page (#9: all   PH 0 izable)
watch(
  () => route.path,
  (p, old) => {
    if (p !== old) setLayoutEdit(false);
  },
);

// Bottom height fine control: 0 = following density (:  PH 0     PH 1 ×  PH 2), >0 overstretched to fixed    PH 3.
// Written on <   PH 0 Inline Style: Statusbar / Sidebar Drop button / Floating units share -- PH 1, all synchronized.
watch(
  barH,
  (v) => {
    const el = document.documentElement;
    if (v > 0) el.style.setProperty('--bar-h', `${v}px`);
    else el.style.removeProperty('--bar-h');
  },
  { immediate: true },
);

/** A panel outside the white list of remote characters: do not render page contents (locally consistently). */
const pageAllowed = computed(() => {
  if (route.path === '/toolkit') return app.remote.loaded && (!app.remote.remote || app.remote.admin);
  const tab = NAV.find((n) => n.path === route.path)?.tab;
  return !tab || app.tabAllowed(tab);
});

/** Path-to-way: The direction of the slide is determined by the proximity of the sidebar (sliding to the next page = right slide, up = left). */
const curPath = ref(route.path);
const pageTx = ref('page');
watch(
  () => route.path,
  (np) => {
    const op = curPath.value;
    curPath.value = np;
    const a = NAV.findIndex((n) => n.path === op);
    const b = NAV.findIndex((n) => n.path === np);
    pageTx.value = a < 0 || b < 0 || a === b ? 'page' : a < b ? 'page-fwd' : 'page-back';
  },
);
</script>

<template>
  <div class="shell-root">
    <TitleBar_web />
    <div class="hrm-app" :class="{ 'nav-collapsed': collapsed }">
      <nav class="hrm-nav" :style="{ '--nav-w': `${navW}px` }">
        <div class="nav-brand">
          <BrandPill :collapsed="collapsed" />
          <span class="brand-title">{{ brandText }}</span>
        </div>
        <NavMenu :collapsed="collapsed" @toggle="collapsed = !collapsed" />
        <div
          class="nav-grip"
          :class="{ dragging: navDragging }"
          @pointerdown="onGripDown"
          @pointermove="onGripMove"
          @pointerup="onGripUp"
          @pointercancel="onGripUp"
        />
      </nav>

      <main class="hrm-main">
        <SafeModeBanner />
        <div class="hrm-page">
          <template v-if="pageAllowed">
            <div class="x-container page-host">
              <RouterView v-slot="{ Component }">
                <Transition :name="pageTx" mode="out-in">
                  <KeepAlive>
                    <component :is="Component" />
                  </KeepAlive>
                </Transition>
              </RouterView>
            </div>
          </template>
          <div v-else class="x-container page-host x-muted" style="display: grid; place-items: center; font-size: 13px">
            {{ t('web.denied') }}
          </div>
        </div>
        <StatusBar />
      </main>
    </div>

    <RemoteLogin />
  </div>
</template>

<style scoped>
/* The title bar is fixed and the rest is given to the original layout (.  PH 0  own   PH 1  %, here to fill the remaining space) */
.shell-root {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
}
.shell-root .hrm-app {
  flex: 1;
  min-height: 0;
  height: auto;
}
.hrm-nav {
  position: relative;
}
.nav-grip {
  position: absolute;
  top: 0;
  right: -3px;
  width: 7px;
  height: 100%;
  cursor: ew-resize;
  z-index: 20;
  touch-action: none;
}
.nav-grip::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  left: 2px;
  width: 3px;
  border-radius: 2px;
  background: var(--ring);
  opacity: 0;
  transition: opacity 0.15s linear;
}
.nav-grip:hover::after,
.nav-grip.dragging::after {
  opacity: 0.35;
}
</style>
