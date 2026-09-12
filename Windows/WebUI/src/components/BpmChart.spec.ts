import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createI18n } from 'vue-i18n';
import BpmChart from './BpmChart.vue';






const i18n = createI18n({
  legacy: false,
  locale: 'zh-CN',
  messages: { 'zh-CN': { 'hb.waiting': '等待心率数据...' } },
  messageCompiler: (m) => () => String(m),
});

interface Rec {

  pts: [number, number][];
  texts: string[];
}

function stubCanvas(): Rec {
  const rec: Rec = { pts: [], texts: [] };
  const ctx = {
    scale: () => {},
    clearRect: () => {},
    beginPath: () => {},
    moveTo: (x: number, y: number) => rec.pts.push([x, y]),
    lineTo: (x: number, y: number) => rec.pts.push([x, y]),
    stroke: () => {},
    closePath: () => {},
    fill: () => {},
    arc: () => {},
    fillText: (s: string) => rec.texts.push(s),
    createLinearGradient: () => ({ addColorStop: () => {} }),
    strokeStyle: '',
    fillStyle: '',
    lineWidth: 0,
    lineJoin: '',
    globalAlpha: 1,
    font: '',
  };
  HTMLCanvasElement.prototype.getContext = vi.fn(() => ctx) as never;
  Object.defineProperty(HTMLElement.prototype, 'clientWidth', { configurable: true, value: 600 });
  globalThis.ResizeObserver = class {
    observe(): void {}
    disconnect(): void {}
  } as never;
  return rec;
}


const GRID_OPS = 6;

describe('BpmChart', () => {

  beforeEach(() => {
    document.documentElement.dataset.anim = 'off';
  });
  afterEach(() => {
    delete document.documentElement.dataset.anim;
  });

  it('数据不足两点时只画网格并渲染空态文案', () => {
    const rec = stubCanvas();
    const w = mount(BpmChart, { props: { data: [72] }, global: { plugins: [i18n] } });
    expect(w.find('canvas').exists()).toBe(true);
    expect(rec.pts.length).toBe(GRID_OPS);
    expect(rec.texts).toEqual(['等待心率数据...']);
    w.unmount();
  });

  it('points 只取最近 N 点，smooth 改变折线取值但不改变点数', async () => {
    const rec = stubCanvas();
    const data = Array.from({ length: 50 }, (_, i) => 60 + (i % 10) * 3);
    const w = mount(BpmChart, { props: { data, points: 10, smooth: 1 }, global: { plugins: [i18n] } });


    const raw = rec.pts.slice(GRID_OPS);
    expect(raw.length).toBe(22);
    const before = JSON.stringify(raw);

    rec.pts.length = 0;
    rec.texts.length = 0;
    await w.setProps({ smooth: 5 });
    const after = rec.pts.slice(GRID_OPS);
    expect(after.length).toBe(22); // No change in points
    expect(JSON.stringify(after)).not.toBe(before); // The take-up was averaged, the shape changed.
    w.unmount();
  });

  it('非正数与非有限值被过滤掉（有效点不足 2 个走空态）', () => {
    const rec = stubCanvas();
    const w = mount(BpmChart, {
      props: { data: [0, -1, Number.NaN, 70, 0] },
      global: { plugins: [i18n] },
    });
    expect(rec.pts.length).toBe(GRID_OPS);
    expect(rec.texts).toEqual(['等待心率数据...']);
    w.unmount();
  });

  it('极值标注取的是平滑后的最大/最小值', () => {
    const rec = stubCanvas();
    const w = mount(BpmChart, { props: { data: [60, 80, 100], smooth: 1 }, global: { plugins: [i18n] } });
    expect(rec.texts).toEqual(['100', '60']);
    w.unmount();
  });

  it('data-anim=on 时数据变化走补间帧（几何随时间推进到终帧）；off 时直出终帧', async () => {
    delete document.documentElement.dataset.anim;
    const pending: FrameRequestCallback[] = [];
    const raf = vi.spyOn(window, 'requestAnimationFrame').mockImplementation((cb) => {
      pending.push(cb);
      return pending.length;
    });
    const caf = vi.spyOn(window, 'cancelAnimationFrame').mockImplementation(() => {});

    const rec = stubCanvas();
    const w = mount(BpmChart, { props: { data: [60, 80, 100], smooth: 1 }, global: { plugins: [i18n] } });
    // There's no old sequence to fill in.
    expect(rec.pts.length).toBeGreaterThan(GRID_OPS);
    expect(pending.length).toBe(0);

    rec.pts.length = 0;
    await w.setProps({ data: [60, 84, 104] });
    expect(pending.length).toBeGreaterThan(0); // Scheduled patches

    // Let's take a frame to the frame, then we'll push the backlog to the end.
    pending.splice(0).forEach((cb) => cb(performance.now()));
    const startPts = JSON.stringify(rec.pts);
    expect(rec.pts.length).toBeGreaterThan(GRID_OPS);
    let guard = 0;
    while (pending.length && guard++ < 60) {
      pending.splice(0).forEach((cb) => cb(performance.now() + 1000));
    }
    expect(rec.pts.length).toBeGreaterThan(GRID_OPS);
    expect(JSON.stringify(rec.pts)).not.toBe(startPts); // The patch drive geometrically with plugs
    w.unmount();

    raf.mockRestore();
    caf.mockRestore();
    delete document.documentElement.dataset.anim;
  });
});
