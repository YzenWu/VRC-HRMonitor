/**
 * The form is automatically saved:   PH 0   drops the disc with any changes in the field, and the Save button is no longer needed.
 *
 * Relation to   PH 0:   PH 1  Registration forms remain (underpass check in front of windows),
 * This calls its   PH 0... after success in saving it, so it will not be "unsaved" for normal use of closed windows.
 *
 * Usage:
 *   const auto = useAutoSave('devices', () => dcfg.value, saveCfg);
 *   Load after completion   PH 0  (this step aligns the baseline and avoids backfilling triggering a saving).
 */
import { onUnmounted, ref, watch, type Ref } from 'vue';
import { useDirtyForm } from '../dirty';
import { markSaveFailed, markSaved } from '../stores/notify';

export interface AutoSave {
  /** Loading/backfilling complete: Aligning the baseline will trigger the preservation of the change from the moment. */
  hydrated: () => void;
  /** Save   PH 0  immediately, close the window or leave the page. */
  flush: () => Promise<void>;
  /** Saving (used to disable buttons/shows). */
  saving: Ref<boolean>;
  /** Failure to save the last one. */
  failed: Ref<boolean>;
}

export function useAutoSave(
  id: string,
  getState: () => unknown,
  save: () => Promise<void>,
  delayMs = 400,
): AutoSave {
  const saving = ref(false);
  const failed = ref(false);
  /** The loading period does not save:   PH 0... the return value of   PH 1 will trigger   PH 2. */
  let hydrating = true;
  /** Scatters of contents for the last real saving: the same as it does for submission directly skipping (and alignment with backend   PH 0). */
  let baseline: string | null = null;
  let timer = 0;
  let retryTimer = 0;
  let hydrateTimer = 0;

  const { markClean } = useDirtyForm(id, getState, save);

  function serialize(): string {
    try {
      return JSON.stringify(getState() ?? null);
    } catch {
      return '';
    }
  }

  async function run(): Promise<void> {
    if (saving.value) {
      // Last time we were flying: once after it's over, we'll have to wait till the last time.
      window.clearTimeout(timer);
      timer = window.setTimeout(() => void run(), delayMs);
      return;
    }
    // The content is the same as the last real saving. No request, no flash "saved" (backfilling/repeated false saving)
    const cur = serialize();
    if (baseline !== null && cur === baseline) {
      markClean();
      failed.value = false;
      return;
    }
    saving.value = true;
    try {
      await save();
      baseline = cur;
      markClean();
      failed.value = false;
      markSaved(); // Saved logo on the right side of the bottombar ( PH 0  highlight)
    } catch {
      failed.value = true;
      markSaveFailed(); // PH 0  2: Failure no longer silent, bottom bar with the "unsaved" badge Indicators
      // PH 0  2 failed automatic retry:  PH 1  before retrying (auto-filling disk after the offline window period of the engine), successful or stopped
      window.clearTimeout(retryTimer);
      retryTimer = window.setTimeout(() => {
        if (failed.value && !hydrating) void run();
      }, 3000);
    } finally {
      saving.value = false;
    }
  }

  watch(
    getState,
    () => {
      if (hydrating) return;
      window.clearTimeout(timer);
      timer = window.setTimeout(() => void run(), delayMs);
    },
    { deep: true },
  );

  // PH 0 2   PH 1 Fixed: Drop the unsaved changes immediately before cutting the background/off/upholstering,
  // There will be no more "out of   PH 0..." click   PH 1  toss."
  const flushNow = (): void => {
    if (hydrating || failed.value) return;
    if (baseline !== null && serialize() === baseline) return;
    window.clearTimeout(timer);
    void run();
  };
  const onVis = (): void => {
    if (document.visibilityState === 'hidden') flushNow();
  };
  document.addEventListener('visibilitychange', onVis);
  window.addEventListener('beforeunload', flushNow);

  onUnmounted(() => {
    window.clearTimeout(timer);
    window.clearTimeout(retryTimer);
    window.clearTimeout(hydrateTimer);
    document.removeEventListener('visibilitychange', onVis);
    window.removeEventListener('beforeunload', flushNow);
  });

  const unseal = (): void => {
    hydrating = false;
    baseline = serialize();
    markClean();
  };

  // PH 0 2   PH 1  Overtime: When engine offline/backfill data sources are delayed (e.g.   PH 2 dependent   PH 3),
  // PH 0  automatically unsealed — Seal period changes are no longer swallowed silently (dissolved to state as baseline).
  hydrateTimer = window.setTimeout(() => {
    if (hydrating) unseal();
  }, 5000);

  return {
    hydrated: () => {
      // Let the   PH 0 reverse wash off first, then unseal the seal and record the baseline (first state after filling = stored state)
      window.clearTimeout(hydrateTimer);
      window.setTimeout(unseal, 0);
    },
    flush: async () => {
      window.clearTimeout(timer);
      await run();
    },
    saving,
    failed,
  };
}
