/**
 * Tracks unsaved configuration forms so the close dialog can warn before exiting.
 * Each page registers a state snapshot and save function. Dirty state is determined by
 * comparing the current serialized snapshot with the most recently saved one.
 */
import { onUnmounted } from 'vue';

interface Entry {
  dirty: () => boolean;
  save: () => Promise<void>;
}

const registry = new Map<string, Entry>();

export function registerDirty(id: string, entry: Entry): () => void {
  registry.set(id, entry);
  return () => registry.delete(id);
}

/** There is currently the number of unsaved changed forms. */
export function dirtyCount(): number {
  let n = 0;
  for (const e of registry.values()) {
    try {
      if (e.dirty()) n++;
    } catch {
      /* We can't close the window. */
    }
  }
  return n;
}

/** Saves every dirty form; one failure does not prevent the remaining saves. */
export async function saveAllDirty(): Promise<void> {
  for (const e of registry.values()) {
    try {
      if (e.dirty()) await e.save();
    } catch {
      /* Single saving failed to ignore, and the rest continues */
    }
  }
}

/**
 * Registers dirty-state tracking for one configuration form.
 * `getState` returns serializable form state and `save` performs the page's normal save operation.
 * Call `markClean` after loading initial data or completing an external save.
 */
export function useDirtyForm(
  id: string,
  getState: () => unknown,
  save: () => Promise<void>,
): { markClean: () => void } {
  let baseline = '';
  const snapshot = (): string => {
    try {
      return JSON.stringify(getState());
    } catch {
      return '';
    }
  };
  const markClean = (): void => {
    baseline = snapshot();
  };
  markClean();

  const off = registerDirty(id, {
    dirty: () => snapshot() !== baseline,
    save: async () => {
      await save();
      markClean();
    },
  });
  onUnmounted(off);

  return { markClean };
}
