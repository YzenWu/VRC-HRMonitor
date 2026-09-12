<script setup lang="ts">
















import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Check, GripVertical, Layout, Minus, Plus, RotateCcw } from 'lucide-vue-next';
import { DEFAULT_LAYOUT, panelById } from '../dashboard/panelRegistry';
import { layoutEdit, setLayoutEdit } from '../stores/layoutEdit';

const { t } = useI18n();

const LS_ORDER = 'hrm-dash-order';
const LS_HIDDEN = 'hrm-dash-hidden';
const LS_SPANS = 'hrm-dash-spans';

function readList(key: string, fallback: string[]): string[] {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return fallback;
    const arr = JSON.parse(raw) as unknown;
    if (!Array.isArray(arr)) return fallback;
    const known = arr.filter((x): x is string => typeof x === 'string' && !!panelById(x));
    return key === LS_ORDER ? [...known, ...fallback.filter((id) => !known.includes(id))] : known;
  } catch {
    return fallback;
  }
}

function readSpans(): Record<string, number> {
  try {
    const raw = localStorage.getItem(LS_SPANS);
    if (!raw) return {};
    const obj = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<string, number> = {};
    let migrated = false;
    for (const [k, v] of Object.entries(obj)) {
      const n = Number(v);
      if (!panelById(k) || !Number.isInteger(n) || n < 1 || n > 12) continue;
      // #21 Old grid migration: the archive of column 1-3 is converted to 12 columns x 4 (new value 4/8/12)
      const span = n <= 3 ? n * 4 : n;
      out[k] = span;
      if (span !== n) migrated = true;
    }
    if (migrated) localStorage.setItem(LS_SPANS, JSON.stringify(out));
    return out;
  } catch {
    return {};
  }
}

const order = ref<string[]>(readList(LS_ORDER, DEFAULT_LAYOUT));
const hidden = ref<string[]>(readList(LS_HIDDEN, []));
const spans = ref<Record<string, number>>(readSpans());

function persist(): void {
  localStorage.setItem(LS_ORDER, JSON.stringify(order.value));
  localStorage.setItem(LS_HIDDEN, JSON.stringify(hidden.value));
  localStorage.setItem(LS_SPANS, JSON.stringify(spans.value));
}

const visible = computed(() => order.value.filter((id) => !hidden.value.includes(id)).map((id) => panelById(id)!));
const hiddenPanels = computed(() => hidden.value.map((id) => panelById(id)!).filter(Boolean));


function spanOf(id: string): number {
  return spans.value[id] ?? panelById(id)!.span;
}


type Bucket = 'wide' | 'narrow' | 'compact';
const bucket = ref<Bucket>('wide');
const queries: [Bucket, string][] = [
  ['compact', '(max-width: 720px)'],
  ['narrow', '(max-width: 1100px)'],
];
const mqls: { q: MediaQueryList; h: () => void }[] = [];
function pickBucket(): Bucket {
  if (mqls[0].q.matches) return 'compact';
  if (mqls[1].q.matches) return 'narrow';
  return 'wide';
}
onMounted(() => {

  if (typeof window.matchMedia !== 'function') return;
  for (const [, q] of queries) {
    const mq = window.matchMedia(q);
    const h = () => {
      bucket.value = pickBucket();
    };
    mq.addEventListener('change', h);
    mqls.push({ q: mq, h });
  }
  bucket.value = pickBucket();
});
onBeforeUnmount(() => {
  for (const { q, h } of mqls) q.removeEventListener('change', h);
  mqls.length = 0;
  cancelPress();
});

const gridCols = computed(() => {
  if (bucket.value === 'compact') return 'minmax(0, 1fr)';
  if (bucket.value === 'narrow') return 'repeat(6, minmax(0, 1fr))';
  return 'repeat(12, minmax(0, 1fr))';
});

/** The effective width below the barrel is half the narrow, single-barrel line and does not exceed the current number of columns (avoid hidden spills). */
function effSpan(id: string): number {
  const s = spanOf(id);
  if (bucket.value === 'compact') return 1;
  if (bucket.value === 'narrow') return Math.min(6, Math.max(1, Math.round(s / 2)));
  return s;
}
function bumpSpan(id: string, delta: number): void {
  const next = Math.min(12, Math.max(1, spanOf(id) + delta));
  spans.value = { ...spans.value, [id]: next };
  persist();
}

function toggleEdit(): void {
  setLayoutEdit(!layoutEdit.value);
}


const dragging = ref('');
const dragOver = ref('');
const gridEl = ref<HTMLElement | null>(null);


const HOLD_MS = 260;
/** Press the lower rear to exceed that value as a normal slide/select and cancel the long press. */
const SLOP_PX = 6;

interface Press {
  id: string;
  x: number;
  y: number;
  timer: number;
  el: HTMLElement;
}
let press: Press | null = null;

const HEAD_INTERACTIVE = 'button, a, input, select, textarea, label, [contenteditable="true"], .x-switch, .x-range, .x-select, .x-btn';

function onHeadDown(id: string, e: PointerEvent): void {
  if (e.button !== 0) return; // Only left
  // Head itself buttons (editing state -/+, hidden) do not trigger Drag Move!
  if ((e.target as HTMLElement).closest(HEAD_INTERACTIVE)) return;
  const el = (e.currentTarget as HTMLElement).closest('.dash-cell') as HTMLElement | null;
  if (!el) return;
  press = { id, x: e.clientX, y: e.clientY, timer: 0, el };
  press.timer = window.setTimeout(beginDrag, HOLD_MS);
  window.addEventListener('pointermove', onWinMove);
  window.addEventListener('pointerup', onWinUp);
  window.addEventListener('pointercancel', onWinUp);
}

function beginDrag(): void {
  if (!press) return;
  dragging.value = press.id;
  press.el.classList.add('is-lifted');
  press.el.dataset.ccSuppress = 'true';
}

function onWinMove(e: PointerEvent): void {
  if (!press) return;
  if (!dragging.value) {
    if (Math.abs(e.clientX - press.x) > SLOP_PX || Math.abs(e.clientY - press.y) > SLOP_PX) {
      cancelPress();
    }
    return;
  }
  // Follow in real time
  const dx = e.clientX - press.x;
  const dy = e.clientY - press.y;
  press.el.style.transform = `translate(${dx}px, ${dy}px)`;

  const hitEl = typeof document.elementFromPoint === 'function'
    ? (document.elementFromPoint(e.clientX, e.clientY) as HTMLElement | null)
    : null;
  const hit = hitEl?.closest('.dash-cell') as HTMLElement | null;
  const hitId = hit?.dataset.id ?? '';
  dragOver.value = hitId && hitId !== press.id ? hitId : '';
}

function onWinUp(): void {
  cancelPress();
}

function cancelPress(): void {
  window.removeEventListener('pointermove', onWinMove);
  window.removeEventListener('pointerup', onWinUp);
  window.removeEventListener('pointercancel', onWinUp);
  if (!press) return;
  if (press.timer) clearTimeout(press.timer);
  press.el.style.transform = '';
  press.el.classList.remove('is-lifted');
  const releasedEl = press.el;
  if (releasedEl.dataset.ccSuppress === 'true') window.setTimeout(() => delete releasedEl.dataset.ccSuppress, 0);
  const fromId = dragging.value;
  const toId = dragOver.value;
  press = null;
  dragging.value = '';
  dragOver.value = '';
  if (fromId && toId) void swapWithFlip(fromId, toId);
}


async function swapWithFlip(fromId: string, toId: string): Promise<void> {
  const cellsBefore = Array.from(gridEl.value?.querySelectorAll<HTMLElement>('.dash-cell') ?? []);
  const before = new Map(cellsBefore.map((c) => [c.dataset.id, c.getBoundingClientRect()] as const));
  const from = order.value.indexOf(fromId);
  const to = order.value.indexOf(toId);
  if (from < 0 || to < 0 || from === to) return;
  const next = order.value.slice();
  next.splice(to, 0, ...next.splice(from, 1));
  order.value = next;
  persist();
  await nextTick();
  if (document.documentElement.dataset.anim === 'off'
    || window.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
  const cellsAfter = Array.from(gridEl.value?.querySelectorAll<HTMLElement>('.dash-cell') ?? []);
  for (const c of cellsAfter) {
    const old = before.get(c.dataset.id);
    if (!old) continue;
    const now = c.getBoundingClientRect();
    const dx = old.left - now.left;
    const dy = old.top - now.top;
    if (!dx && !dy) continue;
    c.style.transition = 'none';
    c.style.transform = `translate(${dx}px, ${dy}px)`;
    requestAnimationFrame(() => {
      c.style.transition = 'transform 0.26s linear';
      c.style.transform = '';
      c.addEventListener('transitionend', () => {
        c.style.transition = '';
      }, { once: true });
    });
  }
}

function hide(id: string): void {
  if (!hidden.value.includes(id)) hidden.value = [...hidden.value, id];
  persist();
}
function show(id: string): void {
  hidden.value = hidden.value.filter((x) => x !== id);
  persist();
}
function reset(): void {
  order.value = [...DEFAULT_LAYOUT];
  hidden.value = [];
  spans.value = {};
  persist();
}
</script>

<template>
  <div class="page-host" :class="{ 'layout-editing': layoutEdit }">
    <header class="page-head">
      <div class="page-head-title">
        <h1 v-bubble="t('dash.hint')">{{ t('tab.dashboard') }}</h1>
      </div>

      <button class="x-btn sm" :class="{ primary: layoutEdit }" v-bubble="layoutEdit ? t('dash.exitEdit') : t('dash.enterEdit')" @click="toggleEdit">
        <Layout class="x-btn-ico" />
        {{ layoutEdit ? t('dash.exitEdit') : t('dash.enterEdit') }}
      </button>
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <!-- Edit State Toolbar: Confirm / Reset / Hide Panel Restoration (normal mode does not appear for edit controls, #22) -->
      <div v-if="layoutEdit" class="x-gap" style="flex-wrap: wrap">
        <button class="x-btn sm primary" @click="() => setLayoutEdit(false)">
          <Check class="x-btn-ico" />
          {{ t('common.ok') }}
        </button>
        <button class="x-btn sm ghost" @click="reset">
          <RotateCcw class="x-btn-ico" />
          {{ t('dash.reset') }}
        </button>
        <template v-if="hiddenPanels.length > 0">
          <span class="x-muted" style="font-size: 12px">{{ t('dash.hidden') }}:</span>
          <button v-for="p in hiddenPanels" :key="p.id" class="x-btn sm" @click="show(p.id)">
            {{ t(p.titleKey) }} +
          </button>
        </template>
      </div>

      <div ref="gridEl" class="dash-grid" :style="{ gridTemplateColumns: gridCols }">
        <div
          v-for="p in visible"
          :key="p.id"
          class="x-card dash-cell"
          :class="{ 'is-over': dragOver === p.id, 'is-dragging': dragging === p.id }"
          :data-id="p.id"
          :data-card-id="`dashboard:${p.id}`"
          :style="{ gridColumn: `span ${effSpan(p.id)}` }"
        >
          <div class="x-card-head dash-head" @pointerdown="onHeadDown(p.id, $event)">
            {{ t(p.titleKey) }}
            <span class="x-grow"></span>
            <!-- Edit state (#22 rendered here only): Resize -/ + and hide -->
            <template v-if="layoutEdit">
              <button class="x-btn sm ghost" v-bubble="`${t('dash.narrower')} (−1)`" @click="bumpSpan(p.id, -1)">
                <Minus class="x-btn-ico" />
              </button>
              <span class="x-muted" style="font-size: 11.5px">{{ spanOf(p.id) }}</span>
              <button class="x-btn sm ghost" v-bubble="`${t('dash.wider')} (+1)`" @click="bumpSpan(p.id, 1)">
                <Plus class="x-btn-ico" />
              </button>
              <button class="x-btn sm ghost" @click="hide(p.id)">{{ t('dash.hide') }}</button>
            </template>
          </div>
          <div class="x-card-body">
            <component :is="p.component" />
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.page-head {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: var(--gap-3);
}
.page-head-title h1 {
  margin: 0;
}
.dash-grid {
  display: grid;
  gap: var(--gap-2);
  /* Columns given by #21 Reaction drum in connection: > 1100 12 / ≤ 1100 6 / ≤720 1 */
  grid-auto-flow: dense;
}
.dash-head {
  cursor: grab;
  user-select: none;

  touch-action: none;
}
.dash-cell.is-dragging {
  opacity: 0.5;
}
.dash-cell.is-lifted {

  pointer-events: none;
  z-index: 50;
  opacity: 0.92;
  box-shadow: var(--elev-lg);
  cursor: grabbing;
}
.dash-cell.is-over {
  outline: 2px dashed var(--primary);
  outline-offset: -2px;
}
/* Layout Editor State: The card floats (shades + sides) to see the editable range */
.layout-editing .dash-cell {
  box-shadow: var(--elev-md);
  outline: var(--hairline) dashed var(--border);
  outline-offset: 2px;
}
.layout-editing .dash-head {
  cursor: grab;
  touch-action: pan-y;
}
</style>
