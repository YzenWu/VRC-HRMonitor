/**
 * Restores an empty input to its default when editing finishes.
 *
 * The rule runs on `change`, not on every keystroke, so temporarily clearing a field is harmless.
 * Defaults come from `data-default`; number inputs fall back to `min`, then zero.
 * Text fields without `data-default` are ignored because an empty value is meaningful there.
 * Password fields are excluded because clearing one explicitly means "do not change it."
 */

const SKIP_TYPES = new Set(['password', 'checkbox', 'radio', 'file', 'hidden', 'range', 'color', 'button', 'submit']);

function defaultOf(el: HTMLInputElement | HTMLTextAreaElement): string | null {
  const d = el.dataset.default;
  if (d !== undefined) return d;
  if (el instanceof HTMLInputElement && el.type === 'number') return el.min !== '' ? el.min : '0';
  return null;
}

function onChange(e: Event): void {
  const el = e.target as HTMLElement | null;
  if (!(el instanceof HTMLInputElement) && !(el instanceof HTMLTextAreaElement)) return;
  if (el instanceof HTMLInputElement && SKIP_TYPES.has(el.type)) return;
  if (el.value.trim() !== '') return;
  const def = defaultOf(el);
  if (def === null) return;
  el.value = def;
  // Dispatch only `input` so v-model updates before the element's own `change` handler runs.
  el.dispatchEvent(new Event('input', { bubbles: true }));
}

/** Installs the capture-phase handler so defaults are restored before page-level change handlers run. */
export function installEmptyDefault(): void {
  document.addEventListener('change', onChange, true);
}
