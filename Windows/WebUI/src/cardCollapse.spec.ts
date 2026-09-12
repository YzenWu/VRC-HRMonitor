import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { installCardCollapse } from './cardCollapse';
import { setLayoutEdit } from './stores/layoutEdit';

const LS_KEY = 'hrm-card-collapsed';

function makeCard(opts: { id: string; noCc?: boolean; bodies?: number; control?: boolean }): HTMLElement {
  const card = document.createElement('div');
  card.className = 'x-card';
  card.dataset.cardId = opts.id;
  if (opts.noCc) card.dataset.noCc = '';
  const head = document.createElement('div');
  head.className = 'x-card-head';
  head.textContent = `标题-${opts.id}`;
  if (opts.control) {
    const button = document.createElement('button');
    button.textContent = '动作';
    head.appendChild(button);
    const input = document.createElement('input');
    head.appendChild(input);
  }
  card.appendChild(head);
  for (let i = 0; i < (opts.bodies ?? 1); i++) {
    const body = document.createElement('div');
    body.className = 'x-card-body';
    body.textContent = `内容-${i}`;
    card.appendChild(body);
  }
  document.body.appendChild(card);
  return card;
}

const tick = () => new Promise<void>((resolve) => setTimeout(resolve, 0));
const headOf = (card: HTMLElement) => card.querySelector<HTMLElement>('.x-card-head')!;
const chevOf = (card: HTMLElement) => card.querySelector<HTMLButtonElement>('.cc-chev');

describe('cardCollapse', () => {
  beforeEach(() => {
    localStorage.clear();
    document.body.innerHTML = '';
    location.hash = '#/cards';
    installCardCollapse();
  });

  afterEach(() => {
    setLayoutEdit(false);
    document.body.innerHTML = '';
  });

  it('注入控件并跳过 data-no-cc 卡片', async () => {
    const card = makeCard({ id: 'normal' });
    const excluded = makeCard({ id: 'excluded', noCc: true });
    await tick();
    expect(chevOf(card)).not.toBeNull();
    expect(chevOf(excluded)).toBeNull();
  });

  it('使用稳定 id 持久化，翻译标题变化后仍恢复', async () => {
    const card = makeCard({ id: 'stable' });
    await tick();
    headOf(card).click();
    expect(card.classList.contains('is-cc')).toBe(true);
    expect(JSON.parse(localStorage.getItem(LS_KEY)!)).toEqual(['#/cards|stable']);

    card.remove();
    const replacement = makeCard({ id: 'stable' });
    headOf(replacement).firstChild!.textContent = 'Translated title';
    await tick();
    expect(replacement.classList.contains('is-cc')).toBe(true);
  });

  it('折叠和展开全部直属 body', async () => {
    const card = makeCard({ id: 'multi', bodies: 3 });
    await tick();
    headOf(card).click();
    const bodies = [...card.querySelectorAll<HTMLElement>(':scope > .x-card-body')];
    expect(bodies).toHaveLength(3);
    expect(bodies.every((body) => body.style.height === '0px' && body.style.opacity === '0')).toBe(true);
    headOf(card).click();
    expect(bodies.every((body) => body.style.opacity === '1')).toBe(true);
  });

  it('整行鼠标与键盘可操作并维护 ARIA', async () => {
    const card = makeCard({ id: 'aria', bodies: 2 });
    await tick();
    const head = headOf(card);
    expect(head.getAttribute('role')).toBe('button');
    expect(head.tabIndex).toBe(0);
    expect(head.getAttribute('aria-expanded')).toBe('true');
    expect(head.getAttribute('aria-controls')?.split(' ')).toHaveLength(2);

    head.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true }));
    expect(card.classList.contains('is-cc')).toBe(true);
    expect(head.getAttribute('aria-expanded')).toBe('false');
    head.dispatchEvent(new KeyboardEvent('keydown', { key: ' ', bubbles: true }));
    expect(card.classList.contains('is-cc')).toBe(false);
  });

  it('标题交互控件与拖拽抑制不会触发折叠', async () => {
    const card = makeCard({ id: 'controls', control: true });
    await tick();
    card.querySelector<HTMLButtonElement>('button:not(.cc-chev)')!.click();
    card.querySelector<HTMLInputElement>('input')!.click();
    expect(card.classList.contains('is-cc')).toBe(false);
    card.dataset.ccSuppress = 'true';
    headOf(card).click();
    expect(card.classList.contains('is-cc')).toBe(false);
    delete card.dataset.ccSuppress;
    chevOf(card)!.click();
    expect(card.classList.contains('is-cc')).toBe(true);
  });

  it('布局编辑强制展开并在退出后恢复持久状态', async () => {
    const card = makeCard({ id: 'layout' });
    await tick();
    headOf(card).click();
    setLayoutEdit(true);
    await tick();
    expect(card.classList.contains('is-cc')).toBe(false);
    headOf(card).click();
    expect(card.classList.contains('is-cc')).toBe(false);
    setLayoutEdit(false);
    await tick();
    expect(card.classList.contains('is-cc')).toBe(true);
  });

  it('卸载后清理失效引用并允许同 id 卡重新挂载', async () => {
    const oldCard = makeCard({ id: 'remount' });
    await tick();
    headOf(oldCard).click();
    oldCard.remove();
    const nextCard = makeCard({ id: 'remount' });
    await tick();
    setLayoutEdit(true);
    await tick();
    expect(nextCard.classList.contains('is-cc')).toBe(false);
    setLayoutEdit(false);
    await tick();
    expect(nextCard.classList.contains('is-cc')).toBe(true);
  });
});
