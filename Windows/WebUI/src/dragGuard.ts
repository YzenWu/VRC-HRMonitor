/**
 * Prevents accidental native dragging of controls, text, and images out of the page.
 * Explicitly draggable elements, such as dashboard panels, remain unaffected.
 */
export function installDragGuard(): void {
  document.addEventListener(
    'dragstart',
    (e) => {
      const el = e.target as HTMLElement | null;
      if (!el || !el.closest) return;
      // Preserve intentional drag-and-drop elements.
      if (el.closest('[draggable="true"]')) return;
      e.preventDefault();
    },
    true,
  );
}
