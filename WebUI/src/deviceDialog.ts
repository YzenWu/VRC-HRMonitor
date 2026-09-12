/**
 * Shared device-details dialog state. Device rows open it on a single click and toggle connections on a double click.
 * Module-level state lets device tables, heart-rate lists, dashboard panels, and context menus use one dialog.
 */
import { ref } from 'vue';
import type { Device } from './types';
import { api } from './api';
import { useAppStore } from './stores/app';

/** MAC address of the device currently shown, or `null` when the dialog is closed. */
export const dialogMac = ref<string | null>(null);

export function openDeviceDialog(mac: string): void {
  dialogMac.value = mac;
}

/** Incremented when an external action requests that the dialog enter rename mode. */
export const renameRequest = ref(0);

/** Opens the device details and focuses rename mode. */
export function requestRename(mac: string): void {
  dialogMac.value = mac;
  renameRequest.value++;
}

export function closeDeviceDialog(): void {
  dialogMac.value = null;
}

/** Delays the single-click action so a double click can cancel it. */
const singleTimer = new Map<string, number>();
const CLICK_DELAY = 240;

/** Avoids opening or toggling a device while the user is selecting text. */
function hasTextSelection(): boolean {
  const sel = window.getSelection();
  return !!sel && !sel.isCollapsed;
}

export function deviceRowClick(mac: string): void {
  if (hasTextSelection()) return;
  const prev = singleTimer.get(mac);
  if (prev !== undefined) {
    window.clearTimeout(prev);
    singleTimer.delete(mac);
  }
  const t = window.setTimeout(() => {
    singleTimer.delete(mac);
    openDeviceDialog(mac);
  }, CLICK_DELAY);
  singleTimer.set(mac, t);
}

/** Cancels the pending single click and toggles the device connection directly. */
export function deviceRowDblClick(d: Device): void {
  const prev = singleTimer.get(d.mac);
  if (prev !== undefined) {
    window.clearTimeout(prev);
    singleTimer.delete(d.mac);
  }
  if (d.connecting) return;
  const app = useAppStore();
  void (d.connected ? api.disconnect(d.mac) : api.connect(d.mac)).then(() => app.sync());
}
