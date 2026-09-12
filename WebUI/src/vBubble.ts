/**
 * PH 0 directive (  PH 1 36th round #2): Suspend ≥PH PH 2  Displays details of the control above the mouse with a bullet bubble.
 *
 * Usage:
 *   <   PH 0   PH 1  Calibration</   PH 2
 *   <span v-bubble="{ text: t('hint.x') }">…</span>
 *   <   PH 0   PH 1 < /   PH 2  ← Reversing Element   PH 3  Properties (:  PH 4 →  PH 5  Migration Compatibility)
 *
 * Conduct:   PH 0   PH 1  time;   PH 2  continuous cache of the latest coordinates (securing pop-up point alignment),
 * Post-skirmish tolerance (blisten, readable) has been shown, and large-scale movement (>PH 0) is considered to be off-limits;
 * PH 0 /   PH 1 / Global   PH 2    PH 3    PH 4    (see    PH 5) Cancel or close.
 */
import type { Directive, DirectiveBinding } from 'vue';
import { bubbleCancel, bubbleHide, bubbleShow } from './bubble';

const DELAY = 600;
const MOVE_TOLERANCE = 40;

interface Host {
  text: string;
  timer: number;
  x: number;
  y: number;
  shownAt: [number, number] | null;
}

const hosts = new WeakMap<HTMLElement, Host>();

function textOf(el: HTMLElement, binding: DirectiveBinding<string | { text?: string } | undefined>): string {
  const v = binding.value;
  if (typeof v === 'string' && v) return v;
  if (v && typeof v === 'object' && v.text) return v.text;
  return el.getAttribute('title') ?? '';
}

export const vBubble: Directive<HTMLElement, string | { text?: string } | undefined> = {
  mounted(el, binding) {
    const h: Host = { text: textOf(el, binding), timer: 0, x: 0, y: 0, shownAt: null };
    hosts.set(el, h);

    el.addEventListener('mouseenter', (e) => {
      window.clearTimeout(h.timer);
      if (!h.text) return;
      h.x = e.clientX;
      h.y = e.clientY;
      h.timer = window.setTimeout(() => {
        bubbleShow(h.text, h.x, h.y);
        h.shownAt = [h.x, h.y];
      }, DELAY);
    });

    el.addEventListener('pointermove', (e) => {
      h.x = e.clientX;
      h.y = e.clientY;
      if (h.shownAt && Math.hypot(e.clientX - h.shownAt[0], e.clientY - h.shownAt[1]) > MOVE_TOLERANCE) {
        window.clearTimeout(h.timer);
        h.shownAt = null;
        bubbleHide();
      }
    });

    el.addEventListener('mouseleave', () => {
      window.clearTimeout(h.timer);
      h.shownAt = null;
      bubbleHide();
    });

    el.addEventListener(
      'pointerdown',
      () => {
        window.clearTimeout(h.timer);
        h.shownAt = null;
        bubbleCancel();
      },
      true,
    );
  },
  updated(el, binding) {
    const h = hosts.get(el);
    if (!h) return;
    h.text = textOf(el, binding);
    if (!h.text) {
      window.clearTimeout(h.timer);
      h.timer = 0;
      h.shownAt = null;
      bubbleHide();
    }
  },
  unmounted(el) {
    const h = hosts.get(el);
    if (h) window.clearTimeout(h.timer);
    hosts.delete(el);
    bubbleHide();
  },
};
