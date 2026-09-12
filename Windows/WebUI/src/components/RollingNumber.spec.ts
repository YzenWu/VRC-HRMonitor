import { afterEach, describe, expect, it, vi } from 'vitest';
import { mount } from '@vue/test-utils';
import { nextTick } from 'vue';
import RollingNumber from './RollingNumber.vue';
import { rollMode } from '../prefs';


function shimRaf(stepMs = 3000): void {
  vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
    cb(performance.now() + stepMs);
    return 1;
  });
  vi.stubGlobal('cancelAnimationFrame', () => {});
}

afterEach(() => {
  vi.unstubAllGlobals();
  delete document.documentElement.dataset.anim;
  rollMode.value = 'roll';
});

describe('RollingNumber', () => {
  it('空态 → 首个数值：anim 开也直接落位，不再卡占位符（#38 回归）', async () => {
    document.documentElement.dataset.anim = 'on';
    shimRaf();
    const w = mount(RollingNumber, { props: { value: null } });
    expect(w.text()).toBe('--');
    await w.setProps({ value: 60 });
    await nextTick();
    expect(w.text()).toBe('60');
  });

  it('anim 关（直出）：空态 → 数值立即可见', async () => {
    document.documentElement.dataset.anim = 'off';
    const w = mount(RollingNumber, { props: { value: null } });
    await w.setProps({ value: 60 });
    await nextTick();
    expect(w.text()).toBe('60');
  });

  it('数值变化：anim 开滚动到新终值（roll 档）', async () => {
    document.documentElement.dataset.anim = 'on';
    shimRaf();
    const w = mount(RollingNumber, { props: { value: 60 } });
    expect(w.text()).toBe('60');
    await w.setProps({ value: 72 });
    await nextTick();
    expect(w.text()).toBe('72');
  });

  it('回到空态显示占位符', async () => {
    document.documentElement.dataset.anim = 'off';
    const w = mount(RollingNumber, { props: { value: 60 } });
    await w.setProps({ value: null });
    await nextTick();
    expect(w.text()).toBe('--');
  });

  it('fade 档：立即落位到新值（不做数值滚动）', async () => {
    document.documentElement.dataset.anim = 'on';
    rollMode.value = 'fade';
    shimRaf();
    const w = mount(RollingNumber, { props: { value: null } });
    await w.setProps({ value: 60 });
    await nextTick();
    expect(w.text()).toBe('60');
    await w.setProps({ value: 72 });
    await nextTick();
    expect(w.text()).toBe('72');
  });

  it('odometer 档：逐位滚轮列数随位数增减，数量与数值位一致', async () => {
    document.documentElement.dataset.anim = 'on';
    rollMode.value = 'odometer';
    const w = mount(RollingNumber, { props: { value: 60 } });
    expect(w.find('.odo').exists()).toBe(true);
    expect(w.findAll('.odo-col').length).toBe(2);
    await w.setProps({ value: 100 });
    await nextTick();
    expect(w.findAll('.odo-col').length).toBe(3);
  });

  it('odometer 档：滚轮窗口实际显示的数字与数值各位一致（首帧对位回归，SEQ_MID 须为 10 的倍数）', async () => {
    document.documentElement.dataset.anim = 'on';
    rollMode.value = 'odometer';
    const SEQ = '0123456789'.repeat(5);
    const digitsOf = (s: string) => s.split('');
    const visibleDigits = (w: ReturnType<typeof mount>) =>
      w.findAll('.odo-strip').map((s) => {
        const k = Number((s.element as HTMLElement).style.getPropertyValue('--k'));
        expect(Number.isInteger(k)).toBe(true);
        return SEQ[k];
      });
    const w = mount(RollingNumber, { props: { value: 60 } });
    expect(visibleDigits(w)).toEqual(digitsOf('60'));
    await w.setProps({ value: 72 });
    await nextTick();
    expect(visibleDigits(w)).toEqual(digitsOf('72'));
    await w.setProps({ value: 105 });
    await nextTick();
    expect(visibleDigits(w)).toEqual(digitsOf('105'));
  });

  it('odometer 档 + anim 关：退化为静态文本直出', async () => {
    document.documentElement.dataset.anim = 'off';
    rollMode.value = 'odometer';
    const w = mount(RollingNumber, { props: { value: 60 } });
    await nextTick();
    expect(w.find('.odo').exists()).toBe(false);
    expect(w.text()).toBe('60');
  });
});
