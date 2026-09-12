/**
 * Adds collapse controls to every `.x-card`, including cards created later.
 * Collapsed state is persisted by route and stable card id, while layout-edit mode temporarily expands all cards.
 * Height and opacity transitions are disabled when reduced motion is enabled.
 */
import { effectScope, watch } from 'vue';
import { layoutEdit } from './stores/layoutEdit';
import { i18n } from './i18n';

const LS_KEY = 'hrm-card-collapsed';
/** The interactive element excluded when the title area is clicked: either of the hits is inappropriately folded. */
const EXCLUDE = 'button, a, input, select, textarea, label, [contenteditable="true"], .x-switch, .x-range, .x-select, .x-btn';
/** Inline chevron used because injected controls are outside the Vue component tree. */
const CHEV_SVG =
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" ' +
  'stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m6 9 6 6 6-6"/></svg>';

interface Cc {
  card: HTMLElement;
  head: HTMLElement;
  bodies: HTMLElement[];
  chev: HTMLButtonElement;
}

function loadKeys(): Set<string> {
  try {
    const arr = JSON.parse(localStorage.getItem(LS_KEY) ?? '[]') as unknown;
    return new Set(Array.isArray(arr) ? (arr.filter((x) => typeof x === 'string') as string[]) : []);
  } catch {
    return new Set();
  }
}
function saveKeys(s: Set<string>): void {
  localStorage.setItem(LS_KEY, JSON.stringify([...s]));
}

/** Stable persistence key. Explicit ids survive translations; the structural fallback never uses visible text. */
function keyOf(card: HTMLElement): string {
  const route = location.hash.split('?')[0] || '#/';
  const explicit = card.dataset.cardId || card.id;
  if (explicit) return `${route}|${explicit}`;
  const parent = card.parentElement;
  const index = parent ? Array.from(parent.children).filter((el) => el.classList.contains('x-card')).indexOf(card) : 0;
  return `${route}|card-${Math.max(0, index)}`;
}

const wired = new WeakSet<HTMLElement>();
const all: Cc[] = [];
let keys = loadKeys();
/** Layout editing temporarily locks every card open without changing persisted state. */
let editMode = false;

function chevTitle(collapsed: boolean): string {
  // The injector is outside component setup, so use the global composer through a narrow type.
  const g = i18n.global as unknown as { t: (key: string) => string };
  return g.t(collapsed ? 'card.expand' : 'card.collapse');
}

function applyState(cc: Cc, collapsed: boolean, animate: boolean): void {
  const { card, head, bodies } = cc;
  card.classList.toggle('is-cc', collapsed);
  head.setAttribute('aria-expanded', String(!collapsed));
  const noAnim = !animate || document.documentElement.dataset.anim === 'off';
  for (const body of bodies) {
    if (noAnim) {
      body.style.height = collapsed ? '0px' : '';
      body.style.opacity = collapsed ? '0' : '';
      continue;
    }
    if (collapsed) {
      body.style.height = `${body.scrollHeight}px`;
      void body.offsetHeight;
      body.style.height = '0px';
      body.style.opacity = '0';
      continue;
    }
    const ts = body.style.transition;
    body.style.transition = 'none';
    body.style.height = 'auto';
    const h = body.offsetHeight;
    body.style.height = '0px';
    void body.offsetHeight;
    body.style.transition = ts;
    body.style.height = `${h}px`;
    body.style.opacity = '1';
    const release = () => {
      if (!card.classList.contains('is-cc')) body.style.height = '';
    };
    body.addEventListener('transitionend', (ev) => {
      if (ev.propertyName === 'height') release();
    }, { once: true });
    window.setTimeout(release, 360);
  }
}

function toggle(cc: Cc): void {
  if (editMode || cc.card.dataset.ccSuppress === 'true') return;
  const next = !cc.card.classList.contains('is-cc');
  if (next) keys.add(keyOf(cc.card));
  else keys.delete(keyOf(cc.card));
  saveKeys(keys);
  applyState(cc, next, true);
  cc.chev.title = chevTitle(next);
}

function wire(card: HTMLElement): void {
  if (wired.has(card)) return;
  // Self-folded/special interactive card (e. g. setup page advanced theme card) statement
  if (card.dataset.noCc !== undefined) return;
  const head = card.querySelector<HTMLElement>(':scope > .x-card-head');
  const bodies = Array.from(card.querySelectorAll<HTMLElement>(':scope > .x-card-body'));
  if (!head || bodies.length === 0) return;
  wired.add(card);

  const identity = keyOf(card).replace(/[^a-zA-Z0-9_-]/g, '-');
  head.setAttribute('role', 'button');
  if (!head.hasAttribute('tabindex')) head.tabIndex = 0;
  bodies.forEach((body, index) => {
    if (!body.id) body.id = `cc-${identity}-${index}`;
  });
  head.setAttribute('aria-controls', bodies.map((body) => body.id).join(' '));

  const chev = document.createElement('button');
  chev.type = 'button';
  chev.className = 'cc-chev';
  chev.innerHTML = CHEV_SVG;
  head.appendChild(chev);

  const cc: Cc = { card, head, bodies, chev };
  all.push(cc);
  chev.addEventListener('click', (e) => {
    e.stopPropagation();
    toggle(cc);
  });
  head.addEventListener('click', (e) => {
    if ((e.target as HTMLElement).closest(EXCLUDE)) return;
    toggle(cc);
  });
  head.addEventListener('keydown', (e) => {
    if (e.key !== 'Enter' && e.key !== ' ') return;
    if ((e.target as HTMLElement).closest(EXCLUDE)) return;
    e.preventDefault();
    toggle(cc);
  });
  // Mounted/folded (durable recovery): no animated direct position
  if (keys.has(keyOf(card))) applyState(cc, true, false);
  else applyState(cc, false, false);
  chev.title = chevTitle(card.classList.contains('is-cc'));
}

function scan(root: ParentNode): void {
  root.querySelectorAll?.('.x-card').forEach((el) => wire(el as HTMLElement));
}

/** Installs card collapsing once during application startup. */
export function installCardCollapse(): void {
  keys = loadKeys();
  for (let i = all.length - 1; i >= 0; i--) {
    if (!all[i].card.isConnected) all.splice(i, 1);
  }
  scan(document.body);
  // Vue rendering, route changes, and conditional content can add cards after startup.
  new MutationObserver((muts) => {
    for (const m of muts) {
      for (const n of m.addedNodes) {
        if (!(n instanceof HTMLElement)) continue;
        if (n.classList.contains('x-card')) wire(n);
        else scan(n);
      }
    }
  }).observe(document.body, { childList: true, subtree: true });

  // Expand all connected cards while editing, then restore persisted state without animation.
  // Keep the effect scope at module level so its watcher remains active after installation returns.
  advScope = effectScope();
  advScope.run(() => {
    watch(layoutEdit, (on) => {
      editMode = on;
      // Clear dead references to unmounted pages by hand
      for (let i = all.length - 1; i >= 0; i--) {
        if (!all[i].card.isConnected) all.splice(i, 1);
      }
      for (const cc of all) {
        if (on) applyState(cc, false, true);
        else applyState(cc, keys.has(keyOf(cc.card)), false);
        cc.chev.title = chevTitle(cc.card.classList.contains('is-cc'));
      }
    });
  });
}

/** Module-level scope retained for the layout-edit watcher. */
let advScope: ReturnType<typeof effectScope> | null = null;
