/** Application context-menu item. Separator items use only `sep`; other fields apply to actions. */
import { reactive } from 'vue';

export interface CtxItem {
  sep?: true;
  label?: string;
  icon?: string;
  danger?: boolean;
  disabled?: boolean;
  onClick?: () => void;
}

/**
 * Context-menu state must be reactive because templates read its coordinates, items, and visibility directly.
 */
export const ctxState = reactive({ x: 0, y: 0, open: false, items: [] as CtxItem[] });

export function openCtx(e: MouseEvent, items: CtxItem[]): void {
  ctxState.x = e.clientX;
  ctxState.y = e.clientY;
  ctxState.items = items;
  ctxState.open = true;
}

export function closeCtx(): void {
  ctxState.open = false;
}
