<script setup lang="ts">







import { computed, onMounted, onUnmounted } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';
import { Copy, Layout, Link2, MonitorPlay, Power, RefreshCw, Unplug, Activity, Ban, Pencil, Save } from 'lucide-vue-next';
import { closeCtx, ctxState, openCtx, type CtxItem } from '../contextmenu';
import { openDeviceDialog, requestRename } from '../deviceDialog';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { inShell } from '../shell';
import { layoutEdit } from '../stores/layoutEdit';

const { t } = useI18n();
const router = useRouter();
const app = useAppStore();


const EDITABLE_PATHS = new Set([
  '/overview', '/dashboard', '/heartbeat', '/devices', '/osc', '/pusher',
  '/hwinfo', '/headset', '/apiserver', '/web', '/settings', '/logs', '/console', '/monitor',
]);
const canEditLayout = computed(() => EDITABLE_PATHS.has(router.currentRoute.value.path));


const menuStyle = computed(() => {
  const vw = window.innerWidth || document.documentElement.clientWidth || 0;
  const vh = window.innerHeight || document.documentElement.clientHeight || 0;
  return {
    left: `${Math.min(ctxState.x, Math.max(0, vw - 220))}px`,
    top: `${Math.min(ctxState.y, Math.max(0, vh - 60))}px`,
  };
});

function doCopy(): void {
  try {
    const sel = window.getSelection();
    if (sel && !sel.isCollapsed) void navigator.clipboard.writeText(sel.toString());
    else void navigator.clipboard.writeText(''); // No constituency: empty operation
  } catch {
    /* ignore */
  }
}

function isEditable(t: EventTarget | null): boolean {
  const el = t instanceof Element ? t : null;
  if (!el) return false;
  const tag = el.tagName;
  return (
    tag === 'INPUT' ||
    tag === 'TEXTAREA' ||
    tag === 'SELECT' ||
    (el as HTMLElement).isContentEditable === true
  );
}


function deviceOf(t: EventTarget | null): {
  mac: string;
  name: string;
  connected: boolean;
  connecting: boolean;
} | null {
  const el = t instanceof Element ? t.closest('[data-dev-mac]') : null;
  if (!el) return null;
  return {
    mac: el.getAttribute('data-dev-mac') ?? '',
    name: el.getAttribute('data-dev-name') ?? el.getAttribute('data-dev-mac') ?? '',
    connected: el.getAttribute('data-dev-connected') === '1',
    connecting: el.getAttribute('data-dev-connecting') === '1',
  };
}


function navOf(t: EventTarget | null): string | null {
  const el = t instanceof Element ? t.closest('[data-nav-tab]') : null;
  return el ? el.getAttribute('data-nav-tab') : null;
}

function run(fn: () => void): void {
  closeCtx();
  fn();
}

function onContext(e: MouseEvent): void {
  // Editable Controls: Browser keeps raw menus; Shells give themselves a copy Bottom
  if (isEditable(e.target)) {
    if (!inShell) return;
    e.preventDefault();
    openCtx(e, [{ label: t('common.copy'), icon: 'copy', onClick: doCopy }]);
    return;
  }
  e.preventDefault();

  const dev = deviceOf(e.target);
  if (dev) {

    const live = app.devices.find((d) => d.mac === dev.mac);
    const saved = live?.saved ?? false;
    const connected = live?.connected ?? dev.connected;
    const liveFloat = live ? app.floatIds.includes(dev.mac) : false;
    const items: CtxItem[] = [
      { label: dev.name || dev.mac, disabled: true },
      { sep: true },
      {
        label: connected ? t('dev.disconnect') : t('dev.connect'),
        icon: connected ? 'unplug' : 'power',
        disabled: dev.connecting,
        onClick: () =>
          run(() => {
            void (connected ? api.disconnect(dev.mac) : api.connect(dev.mac)).then(() => app.sync());
          }),
      },
      {
        label: t('dev.detail'),
        icon: 'activity',
        onClick: () =>
          run(() => {
            openDeviceDialog(dev.mac);
          }),
      },
      {
        label: t('float.opendev'),
        icon: 'monitor',
        disabled: !connected,
        onClick: () =>
          run(() => {
            void api.floatOpen(dev.mac).then(() => app.sync());
          }),
      },
      { sep: true },
      {
        label: t('dev.rename'),
        icon: 'pencil',
        onClick: () =>
          run(() => {
            requestRename(dev.mac);
          }),
      },
      {
        label: saved ? t('dev.unsave') : t('dev.save'),
        icon: 'save',
        onClick: () =>
          run(() => {
            void api.save(dev.mac, !saved).then(() => app.sync());
          }),
      },
      {
        label: t('dev.block'),
        icon: 'ban',
        danger: true,
        onClick: () =>
          run(() => {
            void api.block(dev.mac).then(() => app.sync());
          }),
      },
      { sep: true },
      {
        label: t('tab.devices'),
        icon: 'link',
        onClick: () =>
          run(() => {
            void router.push('/devices');
          }),
      },
    ];
    openCtx(e, items);
    return;
  }


  const navTab = navOf(e.target);
  if (navTab) {
    const items: CtxItem[] = [{ label: t('tab.' + navTab), disabled: true }, { sep: true }];
    const navPath = navTab === 'dashboard' ? '/dashboard' : `/${navTab}`;
    if (EDITABLE_PATHS.has(navPath)) {
      items.push(
        {
          label: layoutEdit.value ? t('dash.exitEdit') : t('dash.enterEdit'),
          icon: 'layout',
          onClick: () =>
            run(() => {
              if (router.currentRoute.value.path !== navPath) void router.push(navPath);
              layoutEdit.value = !layoutEdit.value;
            }),
        },
        { sep: true },
      );
    }
    items.push({
      label: t('common.goto'),
      icon: 'link',
      onClick: () =>
        run(() => {
          void router.push(navPath);
        }),
    });
    openCtx(e, items);
    return;
  }

  const items: CtxItem[] = [];
  if (canEditLayout.value) {
    items.push(
      {
        label: layoutEdit.value ? t('dash.exitEdit') : t('dash.enterEdit'),
        icon: 'layout',
        onClick: () => run(() => (layoutEdit.value = !layoutEdit.value)),
      },
      { sep: true },
    );
  }
  items.push({ label: t('common.refresh'), icon: 'refresh', onClick: () => run(() => window.location.reload()) });
  openCtx(e, items);
}

function onDown(e: PointerEvent): void {
  if (!ctxState.open) return;
  const menu = document.getElementById('app-ctx-menu');
  if (menu && e.target instanceof Node && menu.contains(e.target)) return;
  closeCtx();
}
function onKey(e: KeyboardEvent): void {
  if (e.key === 'Escape') closeCtx();
}

const ICONS: Record<string, unknown> = {
  copy: Copy,
  layout: Layout,
  unplug: Unplug,
  power: Power,
  monitor: MonitorPlay,
  link: Link2,
  refresh: RefreshCw,
  activity: Activity,
  pencil: Pencil,
  save: Save,
  ban: Ban,
};

onMounted(() => {
  window.addEventListener('contextmenu', onContext, true);
  window.addEventListener('pointerdown', onDown, true);
  window.addEventListener('keydown', onKey, true);
  window.addEventListener('blur', closeCtx);
});
onUnmounted(() => {
  window.removeEventListener('contextmenu', onContext, true);
  window.removeEventListener('pointerdown', onDown, true);
  window.removeEventListener('keydown', onKey, true);
  window.removeEventListener('blur', closeCtx);
});
</script>

<template>
  <Teleport to="body">
    <Transition name="ctx-pop">
      <div
        v-if="ctxState.open"
        id="app-ctx-menu"
        class="ctx-menu"
        :style="menuStyle"
      >
        <template v-for="(it, i) in ctxState.items" :key="i">
          <div v-if="it.sep" class="ctx-sep" />
          <button
            v-else
            type="button"
            class="ctx-item"
            :class="{ danger: it.danger, disabled: it.disabled }"
            :disabled="it.disabled"
            @click="it.onClick"
          >
            <component :is="it.icon ? ICONS[it.icon] : null" v-if="it.icon && ICONS[it.icon]" class="ctx-ico" />
            {{ it.label }}
          </button>
        </template>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.ctx-pop-enter-active,
.ctx-pop-leave-active {
  transition: opacity 120ms linear, transform 120ms linear;
  transform-origin: top left;
}
.ctx-pop-enter-from,
.ctx-pop-leave-to {
  opacity: 0;
  transform: translateY(-4px) scale(0.98);
}

.ctx-menu {
  position: fixed;
  z-index: 1100;
  min-width: 200px;
  max-width: 280px;
  padding: 4px;
  border: var(--hairline) solid var(--border);
  border-radius: calc(var(--radius) - 2px);
  background: var(--popover, var(--card));
  box-shadow: var(--elev-lg);
  font-size: 12.5px;
}
.ctx-item {
  display: flex;
  align-items: center;
  gap: 8px;
  width: 100%;
  padding: 5px 8px;
  border: 0;
  border-radius: calc(var(--radius) - 4px);
  background: transparent;
  color: var(--foreground);
  text-align: left;
  cursor: pointer;
  white-space: nowrap;
}
.ctx-item:hover:not(.disabled) {
  background: var(--secondary);
}
.ctx-item.danger {
  color: var(--bad);
}
.ctx-item.disabled {
  opacity: 0.45;
  cursor: default;
}
.ctx-ico {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
}
.ctx-sep {
  height: 1px;
  margin: 4px 6px;
  background: var(--border);
}
</style>
