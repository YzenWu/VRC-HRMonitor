import { beforeEach, describe, expect, it, vi } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';

/**
 * PH 0    PH 1    PH 2   Application logic.
 * PH 0 /    PH 1    Replaces the two modules as a whole: "How does    PH 2    state change after receiving the back-end event" is measured and the network varies.
 * The inner layer of the event load ( PH 2 unopened envelope) with the backend of  PH 0.
 * Note that the   PH 0  plant will be elevated to the top of the file and the quoted items will have to be placed in   PH 1.
 */
const mocks = vi.hoisted(() => {
  type Handler = (data: unknown, type: string) => void;
  const bus = new Map<string, Set<Handler>>();
  const wsConnected = { value: false };
  const status = {
    bpm: 72,
    avg: 70,
    scanning: false,
    recording: false,
    devices: [
      { mac: 'AA:BB', name: 'Band', connected: true, bpm: 72 },
      { mac: 'CC:DD', name: 'Other', connected: false, bpm: 0 },
    ],
    osc: { connected: false, sent: 0, fail: 0, recv: 0, ip: '127.0.0.1', port: '9000' },
    hr: { displaySource: 'bpm', window: { visible: false } },
    app: { debug: false, version: '3.0.0' },
    floatCount: 0,
    health: { status: '未知', statusKey: 'hb.status.unknown', restingBpm: 0 },
  };
  return { bus, wsConnected, status };
});

vi.mock('../api/ws', () => ({
  wsConnected: mocks.wsConnected,
  connectWs: () => {},
  onWs: (type: string, h: (d: unknown, t: string) => void) => {
    let s = mocks.bus.get(type);
    if (!s) {
      s = new Set();
      mocks.bus.set(type, s);
    }
    s.add(h);
    return () => s!.delete(h);
  },
  onAnyWs: () => () => {},
}));

vi.mock('../api', () => ({
  api: {
    // Deep copy:   PH 0... will directly hold and rewrite objects in   PH 1, which cannot be contaminated with one another.
    status: () => Promise.resolve(structuredClone(mocks.status)),
    devices: () => Promise.resolve(structuredClone(mocks.status.devices)),
    // PH 0  1: Align the key name with the back-end variable table (formerly   PH 1 is a fictional key, masking   PH 2  error)
    hw: () => Promise.resolve({ vars: { CPU_USAGE: '12.5' } }),
  },
  setUnauthorizedHandler: () => {},
}));

import { useAppStore } from './app';

function emit(type: string, data: unknown): void {
  mocks.bus.get(type)?.forEach((h) => h(data, type));
}

describe('app store', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mocks.bus.clear();
    mocks.wsConnected.value = false;
    vi.useFakeTimers();
  });

  it('sync 后按 /api/status 填充状态', async () => {
    const app = useAppStore();
    await app.sync();
    expect(app.bpm).toBe(72);
    expect(app.avg).toBe(70);
    expect(app.devices.length).toBe(2);
    expect(app.connectedCount).toBe(1);
    expect(app.engineOnline).toBe(true);
    expect(app.health?.statusKey).toBe('hb.status.unknown');
  });

  it('WS 断开时 sync 会补一个曲线点（避免曲线空窗）', async () => {
    const app = useAppStore();
    await app.sync();
    expect(app.curve).toEqual([72]);
  });

  it('heart_rate 事件按真实来源更新主曲线、平均曲线、设备曲线和实时计数', async () => {
    const app = useAppStore();
    mocks.wsConnected.value = true;
    app.start();
    await vi.runOnlyPendingTimersAsync();

    emit('heart_rate', { mac: 'AA:BB', bpm: 88, mainBpm: 74, avg: 76, notifyHz: 1.25, reports: 42 });
    expect(app.bpm).toBe(74);
    expect(app.avg).toBe(76);
    expect(app.curveOf('main').at(-1)).toBe(74);
    expect(app.curveOf('avg').at(-1)).toBe(76);
    expect(app.curveOf('AA:BB').at(-1)).toBe(88);
    expect(app.devices.find((d) => d.mac === 'AA:BB')?.bpm).toBe(88);
    expect(app.devices.find((d) => d.mac === 'AA:BB')?.notifyHz).toBe(1.25);
    expect(app.devices.find((d) => d.mac === 'AA:BB')?.reports).toBe(42);

    emit('heart_rate', { mac: 'AA:BB', bpm: 0 });
    expect(app.bpm).toBe(74);
    expect(app.curveOf('avg').at(-1)).toBe(76);
  });

  it('devices / scan / osc_status / health_status / log 事件各自生效', async () => {
    const app = useAppStore();
    mocks.wsConnected.value = true;
    app.start();
    await vi.runOnlyPendingTimersAsync();

    emit('devices', [{ mac: 'EE:FF', name: 'New', connected: true, bpm: 60 }]);
    expect(app.devices.length).toBe(1);
    expect(app.connectedCount).toBe(1);

    emit('scan', { scanning: true });
    expect(app.scanning).toBe(true);

    emit('osc_status', { connected: true, sent: 5 });
    expect(app.osc?.connected).toBe(true);
    expect(app.osc?.sent).toBe(5);
    expect(app.osc?.ip).toBe('127.0.0.1'); // Local field merge, cannot lose previous configuration

    emit('health_status', { statusKey: 'hb.status.rest', restingBpm: 61 });
    expect(app.health?.statusKey).toBe('hb.status.rest');

    emit('sysinfo_vars', { vars: { GPU_LOAD: '30' } });
    expect(app.sysVars.GPU_LOAD).toBe('30');

    emit('log', { line: '[INFO] hello' });
    expect(app.logs.at(-1)).toBe('[INFO] hello');
  });

  it('曲线最多保留 180 点', async () => {
    const app = useAppStore();
    mocks.wsConnected.value = true;
    app.start();
    await vi.runOnlyPendingTimersAsync();
    for (let i = 1; i <= 200; i++) emit('heart_rate', { mac: 'AA:BB', bpm: 60 + (i % 30) });
    expect(app.curve.length).toBe(180);
  });
});
