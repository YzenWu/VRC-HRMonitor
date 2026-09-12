import { defineStore } from 'pinia';
import { computed, ref } from 'vue';
import { api } from '../api';
import { onWs } from '../api/ws';
import type { OscParamItem } from '../types';

/** PH 0 Aggregation of the receiving parameters: The full amount of   PH 1 is then increased by   PH 1  (the back end   PH 2  is the cumulative value and the direct replacement is not cumulative). */
export const useOscStore = defineStore('osc', () => {
  const items = ref<Map<string, OscParamItem>>(new Map());
  const recv = ref(0);

  const list = computed(() =>
    [...items.value.values()].sort((a, b) => b.count - a.count || a.addr.localeCompare(b.addr)),
  );

  /** Group by address starter (/   PH 0   PH 1) for card presentation. */
  const groups = computed(() => {
    const g = new Map<string, OscParamItem[]>();
    for (const it of list.value) {
      const seg = it.addr.split('/').filter(Boolean)[0] ?? '(root)';
      const arr = g.get(seg) ?? [];
      arr.push(it);
      g.set(seg, arr);
    }
    return [...g.entries()].sort((a, b) => b[1].length - a[1].length);
  });

  function merge(payload: { items?: OscParamItem[]; recv?: number }): void {
    if (Array.isArray(payload.items)) {
      const next = new Map(items.value);
      for (const it of payload.items) {
        if (it && typeof it.addr === 'string') next.set(it.addr, it);
      }
      // There's a range of addresses, so we don't run too long.
      items.value = next.size > 600 ? new Map([...next].slice(-600)) : next;
    }
    if (typeof payload.recv === 'number') recv.value = payload.recv;
  }

  async function pull(): Promise<void> {
    try {
      const p = (await api.oscParams()) as { items?: OscParamItem[]; recv?: number };
      items.value = new Map();
      merge(p);
    } catch {
      /* Keep old table when backend is not reached */
    }
  }

  async function clear(): Promise<void> {
    await api.oscParamsClear();
    items.value = new Map();
  }

  let started = false;
  function start(): void {
    if (started) return;
    started = true;
    onWs('osc_params', (d) => merge((d ?? {}) as { items?: OscParamItem[]; recv?: number }));
  }

  /** Takes the most recent value of an address (each shape at the back end is marked as {  PH 0 type,   PH 1 value} and takes the last parameter). */
  function value(addr: string): unknown {
    const args = items.value.get(addr)?.args;
    if (!Array.isArray(args) || args.length === 0) return undefined;
    const last = args[args.length - 1];
    if (last !== null && typeof last === 'object') return (last as { v?: unknown }).v;
    return last;
  }

  /** Have you received that address? */
  const has = (addr: string): boolean => items.value.has(addr);

  /** cumulative number of bars at a given address. */
  const count = (addr: string): number => items.value.get(addr)?.count ?? 0;

  return { items, recv, list, groups, pull, clear, start, merge, value, has, count };
});
