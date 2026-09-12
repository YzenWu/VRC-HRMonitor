import { beforeEach, describe, expect, it } from 'vitest';
import {
  applyUiTheme,
  clampCorner,
  clampDensity,
  DEFAULT_LOOK,
  fromLegacyTheme,
  MODE_IDS,
  MODE_LABEL,
  PALETTE_IDS,
  PALETTE_LABEL,
  resolvedMode,
  saveLook,
  setSystemTheme,
  storedLook,
  system,
  toLegacyTheme,
} from './theme';

/**
 * The theme is two-dimensional:   PH 0, separate from   PH 1, the first is   PH 1.
 * The lock here is "The properties/variants injected into <   PH 0 > are aligned with   PH 1 > the Chooser".
 */
describe('theme', () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.removeAttribute('style');
    document.documentElement.removeAttribute('data-mode');
    document.documentElement.removeAttribute('data-palette');
    document.documentElement.removeAttribute('data-anim');
    document.documentElement.className = '';
    system.dark = true;
    system.accent = '#5b9dff';
  });

  it('两个维度的 id 与八语言标签齐全，首项都是 system', () => {
    expect(MODE_IDS).toEqual(['system', 'dark', 'light']);
    expect(PALETTE_IDS).toEqual(['system', 'default', 'forest', 'sunset', 'ocean', 'violet', 'custom']);
    for (const id of MODE_IDS) expect(MODE_LABEL[id].filter((s: string) => s.trim() !== '').length).toBe(8);
    for (const id of PALETTE_IDS) expect(PALETTE_LABEL[id].filter((s: string) => s.trim() !== '').length).toBe(8);
  });

  it('mode=system 跟随系统明暗，显式 dark/light 覆盖系统值', () => {
    applyUiTheme({ ...DEFAULT_LOOK, mode: 'system' });
    expect(document.documentElement.dataset.mode).toBe('dark');
    expect(document.documentElement.classList.contains('dark')).toBe(true);

    setSystemTheme({ dark: false });
    applyUiTheme({ ...DEFAULT_LOOK, mode: 'system' });
    expect(document.documentElement.dataset.mode).toBe('light');
    expect(document.documentElement.classList.contains('dark')).toBe(false);

    applyUiTheme({ ...DEFAULT_LOOK, mode: 'dark' });
    expect(resolvedMode({ ...DEFAULT_LOOK, mode: 'dark' })).toBe('dark');
    expect(document.documentElement.dataset.mode).toBe('dark');
  });

  it('palette=system 用 Windows 强调色，custom 用用户色', () => {
    setSystemTheme({ accent: '#ff8800' });
    applyUiTheme({ ...DEFAULT_LOOK, palette: 'system' });
    const s = document.documentElement.style;
    expect(document.documentElement.dataset.palette).toBe('system');
    expect(s.getPropertyValue('--c-accent-rgb').trim()).toBe('255 136 0');

    applyUiTheme({ ...DEFAULT_LOOK, palette: 'custom', accent: '#101010', bg: '#101010', panel: '#202020' });
    expect(s.getPropertyValue('--c-accent-rgb').trim()).toBe('16 16 16');
    expect(s.getPropertyValue('--c-bg').trim()).toBe('#101010');
    expect(s.getPropertyValue('--c-panel').trim()).toBe('#202020');
  });

  it('强调色上的文字色按亮度取反（浅色强调色配深字）', () => {
    const s = document.documentElement.style;
    applyUiTheme({ ...DEFAULT_LOOK, palette: 'custom', accent: '#ffffff' });
    expect(s.getPropertyValue('--c-accent-fg').trim()).toBe('#111418');
    applyUiTheme({ ...DEFAULT_LOOK, palette: 'custom', accent: '#101010' });
    expect(s.getPropertyValue('--c-accent-fg').trim()).toBe('#ffffff');
  });

  it('非法 accent 回退到默认蓝，圆角有 2px 下限，密度写入 --sp', () => {
    applyUiTheme({ ...DEFAULT_LOOK, palette: 'custom', accent: 'not-a-color', cornerRadius: 0, density: 1.4 });
    const s = document.documentElement.style;
    expect(s.getPropertyValue('--c-accent-rgb').trim()).toBe('91 157 255');
    expect(s.getPropertyValue('--radius').trim()).toBe('2px');
    expect(s.getPropertyValue('--sp').trim()).toBe('1.4');
  });

  it('圆角/密度上限放开：保留下限防炸，非法值回落默认', () => {
    expect(clampCorner(65536)).toBe(65536);
    expect(clampCorner(99999)).toBe(99999); // The upper limit has been released and no more fixed values are caught.
    expect(clampCorner(-5)).toBe(0);
    expect(clampCorner(7.6)).toBe(8);
    expect(clampCorner('abc')).toBe(DEFAULT_LOOK.cornerRadius);

    expect(clampDensity(65536)).toBe(65536);
    expect(clampDensity(9999999)).toBe(9999999); // The ceiling has been released.
    // Lower limit follows the density slider minimum to keep the interface usable.
    expect(clampDensity(0)).toBe(0.8);
    expect(clampDensity('abc')).toBe(DEFAULT_LOOK.density);

    applyUiTheme({ ...DEFAULT_LOOK, cornerRadius: 99999, density: 9999 });
    const s = document.documentElement.style;
    expect(s.getPropertyValue('--radius').trim()).toBe('99999px');
    expect(s.getPropertyValue('--sp').trim()).toBe('9999');
  });

  it('animations=false 写入 data-anim=off（globals.css 的动画总闸）', () => {
    applyUiTheme({ ...DEFAULT_LOOK, animations: true });
    expect(document.documentElement.dataset.anim).toBe('on');
    applyUiTheme({ ...DEFAULT_LOOK, animations: false });
    expect(document.documentElement.dataset.anim).toBe('off');
  });

  it('旧 theme 字段与新 mode/palette 双向映射', () => {
    expect(fromLegacyTheme('forest')).toEqual({ mode: 'dark', palette: 'forest' });
    expect(fromLegacyTheme('light')).toEqual({ mode: 'light', palette: 'default' });
    expect(fromLegacyTheme('nope')).toBeNull();

    expect(toLegacyTheme({ ...DEFAULT_LOOK, mode: 'light', palette: 'default' })).toBe('light');
    expect(toLegacyTheme({ ...DEFAULT_LOOK, mode: 'dark', palette: 'sunset' })).toBe('sunset');
    expect(toLegacyTheme({ ...DEFAULT_LOOK, mode: 'dark', palette: 'custom' })).toBe('custom');
  });

  it('storedLook 合并默认值，老配置按 theme 迁移，坏 JSON 不炸', () => {
    saveLook({ ...DEFAULT_LOOK, palette: 'forest' });
    expect(storedLook().palette).toBe('forest');
    expect(storedLook().accent).toBe(DEFAULT_LOOK.accent);

    localStorage.setItem('hrm-web-ui', JSON.stringify({ density: 0.1 }));
    expect(storedLook().density).toBe(0.8);

    localStorage.setItem('hrm-web-ui', JSON.stringify({ theme: 'sunset', density: 0.1 }));
    expect(storedLook().mode).toBe('dark');
    expect(storedLook().palette).toBe('sunset');
    expect(storedLook().density).toBe(0.8);

    localStorage.setItem('hrm-web-ui', '{ broken');
    expect(storedLook()).toEqual(DEFAULT_LOOK);
  });
});
