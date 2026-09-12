import { describe, expect, it } from 'vitest';
import { TABLE_KEYS } from './lang';
import { NAV } from './router';
import { DEFAULT_LAYOUT, PANELS } from './dashboard/panelRegistry';
import zhTw from './locales/zh-TW.json';
import zhHk from './locales/zh-HK.json';
import yueHk from './locales/yue-HK.json';
import zhCn from './locales/zh-CN.json';
import en from './locales/en.json';
import ja from './locales/ja.json';
import es from './locales/es.json';
import ko from './locales/ko.json';
import de from './locales/de.json';
import fr from './locales/fr.json';

/** Full dictionaries must exactly match the canonical lang.ts key contract. */
const DICTS: Record<string, Record<string, string>> = {
  'zh-TW': zhTw as Record<string, string>,
  'zh-HK': zhHk as Record<string, string>,
  'zh-CN': zhCn as Record<string, string>,
  en: en as Record<string, string>,
  ja: ja as Record<string, string>,
  es: es as Record<string, string>,
  ko: ko as Record<string, string>,
  de: de as Record<string, string>,
  fr: fr as Record<string, string>,
};

describe('i18n locales', () => {
  it('lang.ts 的键集非空且无重复', () => {
    expect(TABLE_KEYS.length).toBeGreaterThan(200);
    expect(new Set(TABLE_KEYS).size).toBe(TABLE_KEYS.length);
  });

  for (const [lang, dict] of Object.entries(DICTS)) {
    it(`${lang} 与 lang.ts 键集一致且无空值`, () => {
      expect(Object.keys(dict).length).toBe(TABLE_KEYS.length);
      const missing = TABLE_KEYS.filter((k) => dict[k] === undefined);
      expect(missing).toEqual([]);
      const empty = TABLE_KEYS.filter((k) => String(dict[k]).trim() === '');
      expect(empty).toEqual([]);
    });
  }

  it('粤语覆盖键属于规范键集，合并香港繁体后覆盖完整', () => {
    const overrides = yueHk as Record<string, string>;
    const canonical = new Set(TABLE_KEYS);
    expect(Object.keys(overrides).length).toBeGreaterThan(0);
    expect(Object.keys(overrides).filter((key) => !canonical.has(key))).toEqual([]);
    expect(Object.values(overrides).filter((value) => String(value).trim() === '')).toEqual([]);

    const merged = { ...(zhHk as Record<string, string>), ...overrides };
    expect(TABLE_KEYS.filter((key) => merged[key] === undefined)).toEqual([]);
    expect(TABLE_KEYS.filter((key) => String(merged[key]).trim() === '')).toEqual([]);
  });

  it('健康状态 key 覆盖后端 HealthService.StatusKey 的全部取值', () => {
    for (const k of ['sleep', 'rest', 'active', 'excited', 'unknown']) {
      expect(TABLE_KEYS).toContain(`hb.status.${k}`);
    }
  });

  it('侧栏 15 个 Tab 与 Dashboard 面板标题都有译文', () => {
    expect(NAV.length).toBe(15);
    for (const n of NAV) expect(TABLE_KEYS).toContain(`tab.${n.tab}`);
    for (const p of PANELS) expect(TABLE_KEYS).toContain(p.titleKey);
  });
});

describe('dashboard panelRegistry', () => {
  it('面板 id 唯一，span 为 1~12 整数（#21 十二列栅格）', () => {
    const ids = PANELS.map((p) => p.id);
    expect(new Set(ids).size).toBe(ids.length);
    for (const p of PANELS) {
      expect(Number.isInteger(p.span)).toBe(true);
      expect(p.span).toBeGreaterThanOrEqual(1);
      expect(p.span).toBeLessThanOrEqual(12);
    }
  });

  it('DEFAULT_LAYOUT 与注册表一一对应（不多不少）', () => {
    expect([...DEFAULT_LAYOUT].sort()).toEqual(PANELS.map((p) => p.id).sort());
  });
});
