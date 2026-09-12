<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterView, useRoute } from 'vue-router';
import { Menu } from 'lucide-vue-next';
import NavMenu from './NavMenu.vue';
import StatusBar from './StatusBar.vue';
import BrandPill from '../components/BrandPill.vue';
import SafeModeBanner from '../components/SafeModeBanner.vue';
import ThemeToggle from '../components/ThemeToggle.vue';
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

/** A panel outside the white list of remote characters: Do not render page contents and show unlicensed places (locally undisplayed). */
const pageAllowed = computed(() => {
  if (route.path === '/toolkit') return app.remote.loaded && (!app.remote.remote || app.remote.admin);
  const tab = NAV.find((n) => n.path === route.path)?.tab;
  return !tab || app.tabAllowed(tab);
});

/** The narrow screen breakpoint corresponds to the   PH 1 disconnection of   PH 0 */
const NARROW = 860;
/** Internal   PH 0 shells are always handled by desktop (high   PH 1  lower  PH 2  width below breakpoint, but it is not a cell phone) */
const inShell = document.documentElement.classList.contains('in-shell');
const isNarrow = (): boolean => !inShell && window.innerWidth <= NARROW;
const narrow = ref(isNarrow());
const collapsed = navCollapsed;

// A narrow screen enters the default drawer for the first time and avoids covering; the desktop follows the user 's last selection
if (narrow.value) collapsed.value = true;

// Thirty-second round of supplement #36: sidebar width can be dragged to adjust + critical self-folding -
/** Expands the lower concomfort width limit (tow through a critical automatic fold and re-extension to at least this width). */
const EXPANDED_MIN = 160;
/** The minimum value allowed by the drag process (lower than the folding threshold to drag out the result). */
const DRAG_MIN = 96;
const DRAG_MAX = 480;
/** Drag and release, and if below that threshold, automatically fold. */
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
  // Collapse starts to drag: immediately enter to expand and consider width from   PH 0   to allow real-time preview
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
  // Draging through threshold → Auto-folding (width given to   PH 0  folding); restoring memory width for next extension
  if (w < COLLAPSE_AT) {
    collapsed.value = true;
    navW.value = Math.min(DRAG_MAX, Math.max(EXPANDED_MIN, rememberedW));
  } else {
    navW.value = Math.max(EXPANDED_MIN, w);
  }
}

// Auto-exact layout editing when leaving the current page (#9: all   PH 0  editable; page with   PH 1  cache, unheard residual edit style)
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

/** Path-to-way: The direction of the slide is determined by the proximity of the sidebar (sliding to the next page = right slide, up = left).
    已知区间外的跳转（如直达首页）退化为淡入。 */
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

/** It's only when the bottom drawer opens up. */
const showScrim = computed(() => narrow.value && !collapsed.value);
/** A floating menu button when the bottom drawer is closed (otherwise there is no entrance) */
const showFab = computed(() => narrow.value && collapsed.value);

function onResize(): void {
  const n = isNarrow();
  if (n === narrow.value) return;
  narrow.value = n;
  // Automatically close from wide to narrow screen, resume expansion in reverse
  collapsed.value = n;
}

/** Automatically close drawers after narrow-screen navigation. */
watch(
  () => route.path,
  () => {
    if (narrow.value) collapsed.value = true;
  },
);

onMounted(() => window.addEventListener('resize', onResize));
onUnmounted(() => window.removeEventListener('resize', onResize));
</script>

<template>
  <div class="hrm-app" :class="{ 'nav-collapsed': collapsed }">
    <nav class="hrm-nav" :style="{ '--nav-w': `${navW}px` }">
      <div class="nav-brand">
        <BrandPill :collapsed="collapsed" />
        <span class="brand-title">{{ brandText }}</span>
      </div>
      <NavMenu :collapsed="collapsed" @toggle="collapsed = !collapsed" />
      <!-- #36 Width Drag handle: Expands the drag-and-write width, and fold-and-right drags; drags through the threshold automatically fold/expands -->
      <div
        class="nav-grip"
        :class="{ dragging: navDragging }"
        @pointerdown="onGripDown"
        @pointermove="onGripMove"
        @pointerup="onGripUp"
        @pointercancel="onGripUp"
      />
    </nav>

    <button v-if="showScrim" class="nav-scrim" :aria-label="t('nav.collapse')" @click="collapsed = true"></button>
    <button v-if="showFab" class="nav-fab" :aria-label="t('nav.expand')" @click="collapsed = false">
      <Menu class="x-btn-ico" />
    </button>

    <main class="hrm-main">
      <SafeModeBanner />
      <!-- Quick, clear and dark right corner switch (auto hidden when custom theme is defined) -->
      <div class="tt-float"><ThemeToggle /></div>
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

    <RemoteLogin />
  </div>
</template>

<style scoped>
/* Quick, bright and dark right upper corner switch: floating capsules, avoid rolling the content area */
.tt-float {
  position: fixed;
  top: calc(8px * var(--sp));
  right: calc(10px * var(--sp));
  z-index: 30;
}
/* Sidebar width drag handle (#36): Show fine bars when suspended/draw and horizontally adjust navigation width */
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
