/**
 * Shell Bridge: A window control channel with the built-in   PH 0  host(  PH 1).
 *
 * The host does not have a system title bar, and the drag/minimise/maximize/closure of windows is triggered by a page command:
 *   Page → Host   PH 0  PH 1  '  PH 2 })
 *   Host page   PH 0   PH 1  ' PH 2 | PH 3 })
 * There is no   PH 0  in the browser, all call silent invalid ( PH 1  is   PH 2).
 */
import { ref } from 'vue';

type WebViewBridge = {
  postMessage: (msg: unknown) => void;
  addEventListener: (type: 'message', cb: (e: { data: unknown }) => void) => void;
};

const bridge = (): WebViewBridge | undefined =>
  (window as { chrome?: { webview?: WebViewBridge } }).chrome?.webview;

/** Whether to run inside the inner shell (  PH 0    at the same time to <   PH 1 +   PH 2    class). */
export const inShell = bridge() !== undefined;

/** The host window status (the title bar button icon and the set page rounder slider are shown accordingly). */
export const winState = ref({ maximized: false, fullScreen: false, corner: 10 });

export type ShellCmd =
  | 'win.drag'
  | 'win.min'
  | 'win.max'
  | 'win.close'
  | 'win.close-ready'
  | 'win.close-cancel'
  | 'win.exit'
  | 'win.tray'
  | 'win.fullscreen'
  | 'win.corner'
  | 'win.theme'
  | 'win.debug'
  | 'win.state'
  | 'engine.restart'
  | 'engine.normal'
  | 'engine.safemode';

/** Sends a window order to the host; nothing is done without it. */
export function postShell(cmd: ShellCmd, extra?: Record<string, unknown>): void {
  bridge()?.postMessage({ cmd, ...extra });
}

/** Sets the radius of the round corner of the window (  PH 0, host-end trap to 0~24 and drop the disc). */
export function setShellCorner(px: number): void {
  postShell('win.corner', { px: Math.round(px) });
}

/**
 * Synchronizes the current theme to the host: the bottom colour of the form ( PH 0  that circle) and   PH 1  border/title colour.
 * If you do not synchronize, the window border below the light theme will be system colour (white when activated) and colliding with the page.
 */
export function syncShellTheme(bg: string, dark: boolean): void {
  postShell('win.theme', { bg, dark });
}

/** Sync   PH 0 PH 1: The host will switch   PH 2  with the lower left state bar. */
export function syncShellDebug(on: boolean): void {
  postShell('win.debug', { on });
}

/** Host requests to close (user has lit a red light or pressed   PH 0): The pageshot confirmation box returns   PH 1 /   PH 2. */
const closeHandlers: (() => void)[] = [];
export function onShellCloseRequest(h: () => void): void {
  closeHandlers.push(h);
}

/** Engine exits abnormally (  PH 0 Guard Radio,   PH 1   PH 2!=0):   PH 3   indicates no current crash record. */
export const engineCrash = ref<{ code: number; at: number } | null>(null);

/** Engine restart state (shell side double check:  PH 0  port, failure retried and pushed back). */
export const relaunchState = ref<'idle' | 'restarting' | 'failed'>('idle');

/** Page (  PH 0) point "Reactivate backend": please pull back   PH 1. */
export function relaunchEngine(): void {
  relaunchState.value = 'restarting';
  postShell('engine.restart');
}

/** Drops the crash hint (user selected later/ restarted). */
export function clearEngineCrash(): void {
  engineCrash.value = null;
  relaunchState.value = 'idle';
}

if (inShell) {
  bridge()!.addEventListener('message', (e) => {
    const d = e.data as
      | { type?: string; code?: number; maximized?: boolean; fullScreen?: boolean; corner?: number }
      | null;
    if (d?.type === 'engine.crash') {
      engineCrash.value = { code: Number(d.code) || 0, at: Date.now() };
      return;
    }
    if (d?.type === 'engine.restarting') {
      relaunchState.value = 'restarting';
      return;
    }
    if (d?.type === 'engine.restarted' || d?.type === 'engine.safemode-ok') {
      engineCrash.value = null;
      relaunchState.value = 'idle';
      return;
    }
    if (d?.type === 'engine.restart-failed') {
      relaunchState.value = 'failed';
      if (!engineCrash.value) engineCrash.value = { code: Number(d.code) || 0, at: Date.now() };
      return;
    }
    if (d?.type === 'win.close-request') {
      for (const h of closeHandlers) h();
      return;
    }
    if (d?.type !== 'win.state') return;
    winState.value = {
      maximized: d.maximized === true,
      fullScreen: d.fullScreen === true,
      corner: typeof d.corner === 'number' ? d.corner : winState.value.corner,
    };
  });
  // Once the page is refreshed, ask for a status once.
  postShell('win.state');
}
