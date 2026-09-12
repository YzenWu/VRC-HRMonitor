import { beforeEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { createI18n } from 'vue-i18n';






const reg = vi.hoisted(() => {

  const stub = (name: string) => ({ name, render: () => name });
  const PANELS = [
    { id: 'a', titleKey: 'p.a', component: stub('A'), span: 1 },
    { id: 'b', titleKey: 'p.b', component: stub('B'), span: 2 },
    { id: 'c', titleKey: 'p.c', component: stub('C'), span: 1 },
  ];
  return { PANELS };
});

vi.mock('../dashboard/panelRegistry', () => ({
  PANELS: reg.PANELS,
  panelById: (id: string) => reg.PANELS.find((p) => p.id === id),
  DEFAULT_LAYOUT: ['a', 'b', 'c'],
}));

import Dashboard from './Dashboard.vue';

const i18n = createI18n({
  legacy: false,
  locale: 'zh-CN',
  messages: {
    'zh-CN': {
      'common.ok': '完成',
      'tab.dashboard': '工作台',
      'dash.hint': '拖动面板标题可重新排序',
      'dash.reset': '重置布局',
      'dash.hidden': '已隐藏面板',
      'dash.hide': '隐藏',
      'dash.enterEdit': '进入编辑',
      'dash.exitEdit': '退出编辑',
      'dash.wider': '加宽',
      'dash.narrower': '收窄',
      'p.a': 'A 面板',
      'p.b': 'B 面板',
      'p.c': 'C 面板',
    },
  },
  messageCompiler: (m) => () => String(m),
});

const LS_ORDER = 'hrm-dash-order';
const LS_HIDDEN = 'hrm-dash-hidden';

const mountDash = () => mount(Dashboard, { global: { plugins: [i18n] } });
const titles = (w: ReturnType<typeof mountDash>) =>
  w.findAll('.dash-head').map((n) => n.text().replace('隐藏', '').trim());

/** The top right corner of the dot page "Into/out" to change the editorial status. */
const toggleEdit = async (w: ReturnType<typeof mountDash>) => {
  const b = w.findAll('button').find((x) => x.text().includes('编辑'))!;
  await b.trigger('click');
};
/** Hides a title panel under the edit (the " hidden " button on the panel only edits the state rendering). */
const hidePanel = async (w: ReturnType<typeof mountDash>, title: string) => {
  const head = w.findAll('.dash-head').find((n) => n.text().includes(title))!;
  const btn = head.findAll('button').find((b) => b.text() === '隐藏')!;
  await btn.trigger('click');
};


function pointerEv(type: string, x: number, y: number): PointerEvent {
  const ev = new Event(type, { bubbles: true }) as PointerEvent;
  Object.defineProperty(ev, 'clientX', { value: x });
  Object.defineProperty(ev, 'clientY', { value: y });
  Object.defineProperty(ev, 'button', { value: 0 });
  return ev;
}

describe('Dashboard 布局', () => {
  beforeEach(() => {
    localStorage.clear();
    vi.restoreAllMocks();
  });

  it('默认按 DEFAULT_LAYOUT 渲染全部面板', () => {
    const w = mountDash();
    expect(titles(w)).toEqual(['A 面板', 'B 面板', 'C 面板']);
    // #22: There should be no "hidden" button at the bottom of the normal mode (non-edited)
    expect(w.find('.dash-head button').exists()).toBe(false);
    w.unmount();
  });

  it('隐藏面板（须编辑态）后从网格移出并可再显示，且写入 localStorage', async () => {
    const w = mountDash();
    await toggleEdit(w); // "Hide" to the editor
    await hidePanel(w, 'A 面板');
    expect(JSON.parse(localStorage.getItem(LS_HIDDEN)!)).toEqual(['a']);

    await toggleEdit(w);
    expect(titles(w)).toEqual(['B 面板', 'C 面板']);


    await toggleEdit(w);
    const restore = w.findAll('button').find((b) => b.text().includes('A 面板'))!;
    await restore.trigger('click');
    await toggleEdit(w);
    expect(titles(w)).toEqual(['A 面板', 'B 面板', 'C 面板']);
    expect(JSON.parse(localStorage.getItem(LS_HIDDEN)!)).toEqual([]);
    w.unmount();
  });

  it('长按 A 拖到 C 上松手：顺序变为 B C A 并持久化（#5 pointer 拖动）', async () => {
    vi.useFakeTimers();
    const w = mountDash();
    const heads = w.findAll('.dash-head');
    const cells = w.findAll('.dash-cell');

    const fromPoint = vi.fn(() => cells[2].element);
    document.elementFromPoint = fromPoint as unknown as typeof document.elementFromPoint;

    try {
      await heads[0].trigger('pointerdown', { button: 0, clientX: 10, clientY: 10 });
      vi.advanceTimersByTime(300); // Over long press window poach into drag dynamic
      window.dispatchEvent(pointerEv('pointermove', 300, 10));
      expect(w.findAll('.dash-cell')[0].classes()).toContain('is-lifted');
      window.dispatchEvent(pointerEv('pointerup', 300, 10));

      await vi.waitFor(() => expect(titles(w)).toEqual(['B 面板', 'C 面板', 'A 面板']));
      expect(JSON.parse(localStorage.getItem(LS_ORDER)!)).toEqual(['b', 'c', 'a']);
      expect(fromPoint).toHaveBeenCalled();
    } finally {
      delete (document as { elementFromPoint?: unknown }).elementFromPoint;
      vi.useRealTimers();
      w.unmount();
    }
  });

  it('按下未满长按窗口即松手 / 滑动超阈值：不进入拖动也不改顺序', async () => {
    vi.useFakeTimers();
    const w = mountDash();
    const heads = w.findAll('.dash-head');

    // One, short and free.
    await heads[0].trigger('pointerdown', { button: 0, clientX: 10, clientY: 10 });
    vi.advanceTimersByTime(80);
    window.dispatchEvent(pointerEv('pointerup', 12, 11));
    expect(titles(w)).toEqual(['A 面板', 'B 面板', 'C 面板']);


    await heads[0].trigger('pointerdown', { button: 0, clientX: 10, clientY: 10 });
    window.dispatchEvent(pointerEv('pointermove', 30, 10));
    vi.advanceTimersByTime(300); // Not even on time.
    window.dispatchEvent(pointerEv('pointerup', 30, 10));
    expect(titles(w)).toEqual(['A 面板', 'B 面板', 'C 面板']);
    expect(w.find('.dash-cell.is-lifted').exists()).toBe(false);

    vi.useRealTimers();
    w.unmount();
  });

  it('重置布局恢复默认顺序并清空隐藏项', async () => {
    localStorage.setItem(LS_ORDER, JSON.stringify(['c', 'b', 'a']));
    localStorage.setItem(LS_HIDDEN, JSON.stringify(['b']));
    const w = mountDash();
    await toggleEdit(w); // Reset buttons only edit state rendering
    const reset = w.findAll('button').find((b) => b.text().includes('重置布局'))!;
    await reset.trigger('click');
    await toggleEdit(w);
    expect(titles(w)).toEqual(['A 面板', 'B 面板', 'C 面板']);
    expect(JSON.parse(localStorage.getItem(LS_ORDER)!)).toEqual(['a', 'b', 'c']);
    w.unmount();
  });

  it('localStorage 里的过期 id 被丢弃，新注册面板自动补到末尾', () => {
    localStorage.setItem(LS_ORDER, JSON.stringify(['c', 'ghost']));
    const w = mountDash();
    expect(titles(w)).toEqual(['C 面板', 'A 面板', 'B 面板']);
    w.unmount();
  });
});
