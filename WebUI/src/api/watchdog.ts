/**
 * Backend Connectivity Watch: Any request/ PH 0  will cut the front end to a "offline" state.
 * Then   PH 0  is the upper limit of   PH 0; 30 consecutive trips; all failures enter the "renouncement" state.
 * Shows error details and configuration export by   PH 0.
 *
 * Why is the time limit of   PH 0: this loop request is normally completed at 1 PH 1, more than   PH 2
 * It is clear that the back-end process is no longer in place or is stuck to death, and that continuing to wait will only keep the mask longer.
 */
import { ref } from 'vue';
import { inShell, relaunchEngine, relaunchState } from '../shell';
import { autoRestart } from '../prefs';

/** Single expedition timed out (ms). */
export const PROBE_TIMEOUT_MS = 100;
/** Maximum number of missions. */
export const MAX_ATTEMPTS = 30;
/** Interval between two expeditions (ms): allow time for backend restarts, 30 times over approximately 15 seconds. */
const RETRY_GAP_MS = 400;

/** The back end is inaccessible (in the mask display). */
export const offline = ref(false);
/** Failed 30 times (show error panel). */
export const gaveUp = ref(false);
/** This is the first of its kind (1. PH 0). */
export const attempt = ref(0);
/** Last bug description (click to copy in panel). */
export const lastError = ref('');

let running = false;
let timer: number | null = null;

/** Discovery:   PH 0   cannot get 200, even if it fails; overtime with   PH 1  Forced interception. */
async function probe(): Promise<boolean> {
  const ctl = new AbortController();
  const to = window.setTimeout(() => ctl.abort(), PROBE_TIMEOUT_MS);
  try {
    const res = await fetch('/api/status', { signal: ctl.signal, cache: 'no-store' });
    return res.ok;
  } catch {
    return false;
  } finally {
    window.clearTimeout(to);
  }
}

/** Restore callback: Registered by   PH 0  (reconnected   PH 1 + replay). */
let onRecover: (() => void) | null = null;
export function setRecoverHandler(fn: () => void): void {
  onRecover = fn;
}

async function loop(): Promise<void> {
  if (running) return;
  running = true;
  try {
    for (let i = 1; i <= MAX_ATTEMPTS; i++) {
      attempt.value = i;
      if (await probe()) {
        offline.value = false;
        gaveUp.value = false;
        attempt.value = 0;
        lastError.value = '';
        onRecover?.();
        return;
      }
      // There's no need to wait after the last failure.
      if (i < MAX_ATTEMPTS) await new Promise((r) => (timer = window.setTimeout(r, RETRY_GAP_MS)));
    }
    gaveUp.value = true;
    // Backend confirmation   PH 0: Start backend with an "Auto Reboot" and inside the shell, reboot the engine and continue low frequency detection until it returns
    if (autoRestart.value && inShell && relaunchState.value === 'idle') {
      relaunchEngine();
      void pollUntilUp();
    }
  } finally {
    running = false;
    timer = null;
  }
}

/** Recovery detection during automatic reboot: every   PH 0  probe, the back end is released from the offline state (  PH 1 reconnection is given to   PH 2). */
let resumeTimer: number | null = null;
async function pollUntilUp(): Promise<void> {
  if (resumeTimer !== null) return;
  resumeTimer = window.setInterval(async () => {
    if (await probe()) {
      if (resumeTimer !== null) {
        window.clearInterval(resumeTimer);
        resumeTimer = null;
      }
      notifyUp();
    }
  }, 1500);
}

/** Report a failure (request abnormal /   PH 0  unconnected): Start the detection cycle when you first enter an offline state. */
export function notifyDown(reason: unknown): void {
  const msg = reason instanceof Error ? reason.message : String(reason ?? '');
  if (msg) lastError.value = msg;
  if (offline.value) return;
  offline.value = true;
  gaveUp.value = false;
  void loop();
}

/** One success report: any successful response is directly offline. */
export function notifyUp(): void {
  if (resumeTimer !== null) {
    window.clearInterval(resumeTimer);
    resumeTimer = null;
  }
  if (!offline.value && !gaveUp.value) return;
  offline.value = false;
  gaveUp.value = false;
  attempt.value = 0;
  lastError.value = '';
  if (timer !== null) {
    window.clearTimeout(timer);
    timer = null;
  }
}

/** Undo it manually and then try it again (the error panel 's "retry" button). */
export function retryNow(): void {
  gaveUp.value = false;
  offline.value = true;
  void loop();
}
