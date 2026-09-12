<script lang="ts">

export interface GridCard {
  id: string;

  titleKey?: string;
  /** Default width (12 columns; omitted = 12 rows). */
  span?: number;

  bare?: boolean;

  cls?: string;

  style?: string;

  bodyStyle?: string;

  noCc?: boolean;
}
</script>

<script setup lang="ts">









import { computed, nextTick, onBeforeUnmount, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Check, GripVertical, Minus, Plus, RotateCcw } from 'lucide-vue-next';
import { layoutEdit, setLayoutEdit } from '../stores/layoutEdit';

const props = defineProps<{ view: string; cards: GridCard[]; fill?: boolean }>();
const { t } = useI18n();

const byId = (id: string): GridCard | undefined => props.cards.find((c) => c.id === id);
const DEFAULT_LAYOUT = computed(() => props.cards.map((c) => c.id));

const LS = {
  order: `hrm-cards-${props.view}-order`,
  hidden: `hrm-cards-${props.view}-hidden`,
  spans: `hrm-cards-${props.view}-spans`,
};

function readList(key: string, fallback: string[]): string[] {
  try {
    const raw = localStorage.getItem(key);
    if (!raw) return fallback;
    const arr = JSON.parse(raw) as unknown;
    if (!Array.isArray(arr)) return fallback;
    const known = arr.filter((x): x is string => typeof x === 'string' && !!byId(x));
    return key === LS.order ? [...known, ...fallback.filter((id) => !known.includes(id))] : known;
  } catch {
    return fallback;
  }
}

function readSpans(): Record<string, number> {
  try {
    const raw = localStorage.getItem(LS.spans);
    if (!raw) return {};
    const obj = JSON.parse(raw) as Record<string, unknown>;
    const out: Record<string, number> = {};
    for (const [k, v] of Object.entries(obj)) {
      const n = Number(v);
      if (!byId(k) || !Number.isInteger(n) || n < 1 || n > 12) continue;
      out[k] = n;
    }
    return out;
  } catch {
    return {};
  }
}

function reloadRestoredPrefs(event: Event): void {
  const keys = (event as CustomEvent<{ keys?: string[] }>).detail?.keys ?? [];
  if (!Object.values(LS).some((key) => keys.includes(key))) return;
  order.value = readList(LS.order, DEFAULT_LAYOUT.value);
  hidden.value = readList(LS.hidden, []);
  spans.value = readSpans();
}

const order = ref<string[]>(readList(LS.order, DEFAULT_LAYOUT.value));
const hidden = ref<string[]>(readList(LS.hidden, []));
const spans = ref<Record<string, number>>(readSpans());

function persist(): void {
  localStorage.setItem(LS.order, JSON.stringify(order.value));
  localStorage.setItem(LS.hidden, JSON.stringify(hidden.value));
  localStorage.setItem(LS.spans, JSON.stringify(spans.value));
}

const visible = computed(() => order.value.filter((id) => !hidden.value.includes(id)).map((id) => byId(id)!));
const hiddenCards = computed(() => hidden.value.map((id) => byId(id)).filter((x): x is GridCard => !!x));


function spanOf(id: string): number {
  const c = byId(id);
  return spans.value[id] ?? Math.min(12, Math.max(1, c?.span ?? 12));
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
  window.addEventListener('hrm:prefs-restored', reloadRestoredPrefs);
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
  window.removeEventListener('hrm:prefs-restored', reloadRestoredPrefs);
  for (const { q, h } of mqls) q.removeEventListener('change', h);
  mqls.length = 0;
  cancelPress();
});

const gridCols = computed(() => {
  if (bucket.value === 'compact') return 'minmax(0, 1fr)';
  if (bucket.value === 'narrow') return 'repeat(6, minmax(0, 1fr))';
  return 'repeat(12, minmax(0, 1fr))';
});

/** The effective width below the barrel is: a narrow barrel is half, a single barrel is in full row and does not exceed the current number of columns. */
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


const dragging = ref('');
const dragOver = ref('');
const gridEl = ref<HTMLElement | null>(null);

const HOLD_MS = 260;
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
  if (e.button !== 0) return;
  if ((e.target as HTMLElement).closest(HEAD_INTERACTIVE)) return;
  const el = (e.currentTarget as HTMLElement).closest('.dash-cell') as HTMLElement | null;
  if (!el || el.dataset.noCc !== undefined) return;
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
  const card = press.el.matches('.x-card') ? press.el : press.el.querySelector<HTMLElement>('.x-card');
  if (card) card.dataset.ccSuppress = 'true';
}

function onWinMove(e: PointerEvent): void {
  if (!press) return;
  if (!dragging.value) {
    if (Math.abs(e.clientX - press.x) > SLOP_PX || Math.abs(e.clientY - press.y) > SLOP_PX) {
      cancelPress();
    }
    return;
  }
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
  const card = press.el.matches('.x-card') ? press.el : press.el.querySelector<HTMLElement>('.x-card');
  if (card?.dataset.ccSuppress === 'true') window.setTimeout(() => delete card.dataset.ccSuppress, 0);
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
  order.value = [...DEFAULT_LAYOUT.value];
  hidden.value = [];
  spans.value = {};
  persist();
}
</script>

<template>
  <div class="card-grid-host" :class="{ 'layout-editing': layoutEdit, 'is-fill': fill }">
    <!-- Edit State Toolbar: Confirm / Reset / Hide Restore (normal mode zero edit control) -->
    <div v-if="layoutEdit" class="x-gap" style="flex-wrap: wrap">
      <button class="x-btn sm primary" @click="() => setLayoutEdit(false)">
        <Check class="x-btn-ico" />
        {{ t('common.ok') }}
      </button>
      <button class="x-btn sm ghost" @click="reset">
        <RotateCcw class="x-btn-ico" />
        {{ t('dash.reset') }}
      </button>
      <template v-if="hiddenCards.length > 0">
        <span class="x-muted" style="font-size: 12px">{{ t('dash.hidden') }}:</span>
        <button v-for="c in hiddenCards" :key="c.id" class="x-btn sm" @click="show(c.id)">
          {{ c.titleKey ? t(c.titleKey) : c.id }} +
        </button>
      </template>
    </div>

    <div ref="gridEl" class="dash-grid" :style="{ gridTemplateColumns: gridCols }">
      <div
        v-for="c in visible"
        :key="c.id"
        class="dash-cell"
        :class="[c.bare ? '' : 'x-card', c.cls, { 'is-over': dragOver === c.id, 'is-dragging': dragging === c.id }]"
        :data-id="c.id"
        :data-card-id="`${view}:${c.id}`"
        :data-no-cc="c.noCc ? '' : undefined"
        :style="[c.style, { gridColumn: `span ${effSpan(c.id)}` }]"
      >
        <template v-if="!c.bare">
          <div class="x-card-head dash-head" @pointerdown="onHeadDown(c.id, $event)">
            <slot :name="`head-${c.id}`" :title="c.titleKey ? t(c.titleKey) : ''">
              {{ c.titleKey ? t(c.titleKey) : '' }}
            </slot>
            <span class="x-grow"></span>
            <template v-if="layoutEdit">
              <button class="x-btn sm ghost" v-bubble="`${t('dash.narrower')} (−1)`" @click="bumpSpan(c.id, -1)">
                <Minus class="x-btn-ico" />
              </button>
              <span class="x-muted" style="font-size: 11.5px">{{ spanOf(c.id) }}</span>
              <button class="x-btn sm ghost" v-bubble="`${t('dash.wider')} (+1)`" @click="bumpSpan(c.id, 1)">
                <Plus class="x-btn-ico" />
              </button>
              <button class="x-btn sm ghost" @click="hide(c.id)">{{ t('dash.hide') }}</button>
            </template>

            <slot :name="`head-tail-${c.id}`"></slot>
          </div>
          <div class="x-card-body" :style="c.bodyStyle">
            <slot :name="c.id" />
          </div>
        </template>
        <template v-else>
          <slot :name="c.id" />
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.card-grid-host {
  display: flex;
  flex-direction: column;
  gap: var(--gap-2);
}

.card-grid-host.is-fill {
  flex: 1;
  min-height: 0;
}
.card-grid-host.is-fill .dash-grid {
  flex: 1;
  min-height: 0;
  grid-auto-rows: 1fr;
}
.dash-grid {
  display: grid;
  gap: var(--gap-2);
  grid-auto-flow: dense;
}
.dash-head {
  cursor: grab;
  user-select: none;
  touch-action: pan-y;
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

.layout-editing .dash-cell {
  box-shadow: var(--elev-md);
  outline: var(--hairline) dashed var(--border);
  outline-offset: 2px;
}
.layout-editing .dash-head {
  cursor: grab;
}
</style>
