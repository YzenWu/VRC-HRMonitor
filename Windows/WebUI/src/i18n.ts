import { createI18n } from 'vue-i18n';
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

/** Supported runtime locales, including Hong Kong Traditional Chinese and Cantonese. */
export const LANGS = ['zh-TW', 'zh-HK', 'yue-HK', 'zh-CN', 'en', 'ja', 'es', 'ko', 'de', 'fr'] as const;
export type Lang = (typeof LANGS)[number];

export const LANG_LABEL: Record<Lang, string> = {
  'zh-TW': '繁體中文（台灣）',
  'zh-HK': '繁體中文（香港）',
  'yue-HK': '粵語（香港）',
  'zh-CN': '简体中文',
  en: 'English',
  ja: '日本語',
  es: 'Español',
  ko: '한국어',
  de: 'Deutsch',
  fr: 'Français',
};

export const LANG_FLAG: Record<Lang, string> = {
  'zh-TW': '🇹🇼',
  'zh-HK': '🇭🇰',
  'yue-HK': '🇭🇰',
  'zh-CN': '🇨🇳',
  en: '🇬🇧',
  ja: '🇯🇵',
  es: '🇪🇸',
  ko: '🇰🇷',
  de: '🇩🇪',
  fr: '🇫🇷',
};

const messages: Record<string, Record<string, string>> = {
  'zh-TW': zhTw as unknown as Record<string, string>,
  'zh-HK': zhHk as unknown as Record<string, string>,
  'yue-HK': {
    ...(zhHk as unknown as Record<string, string>),
    ...(yueHk as Record<string, string>),
  },
  'zh-CN': zhCn as unknown as Record<string, string>,
  en: en as unknown as Record<string, string>,
  ja: ja as unknown as Record<string, string>,
  es: es as unknown as Record<string, string>,
  ko: ko as unknown as Record<string, string>,
  de: de as unknown as Record<string, string>,
  fr: fr as unknown as Record<string, string>,
};

function initialLocale(): Lang {
  const saved = localStorage.getItem('hrm-lang');
  if (saved && (LANGS as readonly string[]).includes(saved)) return saved as Lang;
  return 'zh-CN';
}

export const i18n = createI18n({
  legacy: false,
  locale: initialLocale(),
  fallbackLocale: 'zh-CN',
  // The translation form is flat   PH 0  (e. g.   PH 1),   PH 3  parsing natural matching
  messages: messages as never,
  // The case is a pure static text (same table as   PH 0) and does not use a plug/complex.
  // The default compiler will throw {...} ‘when plug-in: { PH 0 ` in’,  PH 1,  PH 2...
  // To render the entire page failed, a placeholder such as {  PH 0  `/ {variant} ` would also be replaced by an empty string. This post is part of our special coverage Syria Protests 2011.
  messageCompiler: (message) => () => (typeof message === 'string' ? message : String(message)),
});

export function setLocale(lang: Lang): void {
  i18n.global.locale.value = lang;
  localStorage.setItem('hrm-lang', lang);
  document.documentElement.lang = lang === 'zh-TW' ? 'zh-Hant-TW' : lang === 'zh-HK' ? 'zh-Hant-HK' : lang === 'yue-HK' ? 'yue-Hant-HK' : lang.toLowerCase();
}
