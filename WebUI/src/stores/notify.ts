/**
 * Global light-tip signal (single example at module level, not   PH 0):
 * The "Saved" logo is aligned at the bottom of the column   PH 0 s and blinks on the right side, replacing the in situ hints for setting up the Kari.
 * Only the real trigger disk (  PH 0  real saving / page visible saving successful) is used.
 */
import { ref } from 'vue';

/** Last successful time stamp saved (0 = this session has not been saved). */
export const lastSavedAt = ref(0);

/** Last saved failed time stamp (0 = no failure;   PH 0:   PH 1       PH 2    click when failed,  PH 3   light failed logo until success). */
export const saveFailedAt = ref(0);

/** Saves a successful spot:   PH 0... so that the  PH 1  logo is activated. */
export function markSaved(): void {
  lastSavedAt.value = Date.now();
  saveFailedAt.value = 0;
}

/** Save failed point ( PH 0  2): Failure is no longer silent and the "unsaved" logo appears on the bottom bar until the next successful cleanup. */
export function markSaveFailed(): void {
  saveFailedAt.value = Date.now();
}
