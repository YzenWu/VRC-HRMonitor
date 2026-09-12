/** PH 0 incident bus: single connection + automatic reconnection, distributed to the subscriber by   PH 1  ( PH 2    PH 3  symmetry). */
import { ref } from 'vue';
import { notifyDown } from './watchdog';
import { i18n } from '../i18n';

/** Backend envelope:   PH 0  Issue {  PH 1,   PH 2,   PH 3; the next line response is   PH 4,  PH 5,   PH 6,   PH 7,  PH 8. */
interface WsEnvelope {
  type?: string;
  data?: unknown;
}

/** The subscriber was given   PH 0 Inner Layer - Event Fields are all in   PH 1, with only   PH 2  at the top. */
type Handler = (data: unknown, type: string) => void;

const handlers = new Map<string, Set<Handler>>();
const anyHandlers = new Set<Handler>();
let socket: WebSocket | null = null;
let retry = 0;
let timer: number | null = null;

/** Connection status (must be   PH 0, otherwise relying on   PH 1  will not be recalculated). */
export const wsConnected = ref(false);

/** PH 0  for external translation of components   PH 1  with full information sheet type, where a direct call in the module triggers a deep inference;
 *  Here you can see (narrow signature of   PH 0). */
function tr(key: string): string {
  return (i18n.global as unknown as { t: (k: string) => string }).t(key);
}

function dispatch(type: string, data: unknown): void {
  handlers.get(type)?.forEach((h) => h(data, type));
  anyHandlers.forEach((h) => h(data, type));
}

export function connectWs(): void {
  if (socket && (socket.readyState === WebSocket.OPEN || socket.readyState === WebSocket.CONNECTING)) return;
  // PH 0 Participated remote session fingerprint (same as   PH 1  request); local request ignores both parameters
  const tz = -new Date().getTimezoneOffset();
  const url = `${location.protocol === 'https:' ? 'wss' : 'ws'}://${location.host}/ws?tz=${tz}&lang=${encodeURIComponent(navigator.language || '')}`;
  try {
    socket = new WebSocket(url);
  } catch {
    scheduleReconnect();
    return;
  }
  socket.onopen = () => {
    retry = 0;
    wsConnected.value = true;
    dispatch('__open', null);
  };
  socket.onclose = () => {
    wsConnected.value = false;
    dispatch('__close', null);
    // Disconnection is the fastest signal in the back.
    notifyDown(tr('net.wsdown'));
    scheduleReconnect();
  };
  socket.onerror = () => {
    /* PH 0... will then trigger it, not repeat here. */
  };
  socket.onmessage = (ev) => {
    try {
      const env = JSON.parse(ev.data as string) as WsEnvelope;
      if (typeof env.type === 'string') dispatch(env.type, env.data);
    } catch {
      /* Ignore non-  PH 0 frame */
    }
  };
}

function scheduleReconnect(): void {
  if (timer !== null) return;
  const delay = Math.min(1000 * 2 ** retry, 15000);
  retry += 1;
  timer = window.setTimeout(() => {
    timer = null;
    connectWs();
  }, delay);
}

/** Actively disconnect and stop reconnection (calls when remote sessions fail/out and avoid repeated reconnections to 401). */
export function stopWs(): void {
  retry = 0;
  if (timer !== null) {
    window.clearTimeout(timer);
    timer = null;
  }
  if (socket) {
    try {
      socket.onclose = null;
      socket.close();
    } catch {
      /* ignore */
    }
    socket = null;
  }
  wsConnected.value = false;
}

export function onWs(type: string, handler: Handler): () => void {
  let set = handlers.get(type);
  if (!set) {
    set = new Set();
    handlers.set(type, set);
  }
  set.add(handler);
  return () => set!.delete(handler);
}

export function onAnyWs(handler: Handler): () => void {
  anyHandlers.add(handler);
  return () => anyHandlers.delete(handler);
}
