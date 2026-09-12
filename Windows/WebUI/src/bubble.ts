/**
 * Global hover bubble singleton (#2).
 * The `v-bubble` directive drives its state and a single component renders it.
 */
import { reactive } from 'vue';

export const bubble = reactive({
  visible: false,
  text: '',
  /** Latest pointer coordinates captured while hovering. */
  x: 0,
  y: 0,
});

/** Prevents the bubble from immediately reopening after an interaction. */
let suppressUntil = 0;

export function bubbleShow(text: string, x: number, y: number): void {
  if (!text || Date.now() < suppressUntil) return;
  bubble.text = text;
  bubble.x = x;
  bubble.y = y;
  bubble.visible = true;
}

export function bubbleHide(): void {
  bubble.visible = false;
}

/** Hides the bubble and briefly suppresses reopening after a click or key press. */
export function bubbleCancel(): void {
  suppressUntil = Date.now() + 400;
  bubble.visible = false;
}

/** Installs global wheel, scroll, and Escape-key handlers that dismiss the bubble. */
export function installBubbleDismiss(): void {
  window.addEventListener('wheel', bubbleCancel, { passive: true, capture: true });
  window.addEventListener('scroll', bubbleCancel, { passive: true, capture: true });
  window.addEventListener(
    'keydown',
    (e) => {
      if (e.key === 'Escape') bubbleCancel();
    },
    true,
  );
}
