import { defineStore } from 'pinia';
import { computed, onScopeDispose, ref, watch } from 'vue';
import { api } from '../api';
import { markSaveFailed, markSaved } from './notify';
import {
  applyUiTheme,
  clampCorner,
  clampDensity,
  fromLegacyTheme,
  resolvedMode,
  saveLook,
  setSystemTheme,
  storedLook,
  toLegacyTheme,
  watchSystemTheme,
  type UiLook,
  type UiMode,
  type UiPalette,
} from '../theme';
import { setLocale, type Lang } from '../i18n';

/** Appearance preference: Local immediately effective + backend durability (paragraphs   PH 0   PH 1). */
export const useUiStore = defineStore('ui', () => {
  const look = ref<UiLook>(storedLook());
  const lang = ref<Lang>((localStorage.getItem('hrm-lang') as Lang) ?? 'zh-CN');
  /** Brand title template ({ placeholder; empty = default title   PH 0). Endurance in   PH 1. */
  const brand = ref('');
  /** The bright and dark ( PH 0  when   PH 0  is a system value) that is currently in effect to provide the shell synchronised window border colour. */
  const mode = computed(() => resolvedMode(look.value));

  /** PH 0 2 Auto-push gate:   PH 1  Before completion   PH 2  will be filled back and not pushed. */
  let ready = false;
  let pushTimer = 0;

  function apply(patch: Partial<UiLook>): void {
    const next = { ...look.value, ...patch };
    next.cornerRadius = clampCorner(next.cornerRadius);
    next.density = clampDensity(next.density);
    look.value = next;
    applyUiTheme(look.value);
    saveLook(look.value);
  }

  /** System theme change (  PH 0  or backend PH 1 incident): Repainting is only required when following the system. */
  function onSystemTheme(next?: { dark?: boolean; accent?: string }): void {
    if (next && !setSystemTheme(next)) return;
    applyUiTheme(look.value);
  }

  function reloadRestoredTheme(event: Event): void {
    const keys = (event as CustomEvent<{ keys?: string[] }>).detail?.keys ?? [];
    if (!keys.includes('hrm-web-ui')) return;
    look.value = storedLook();
    applyUiTheme(look.value);
  }

  window.addEventListener('hrm:prefs-restored', reloadRestoredTheme);
  onScopeDispose(() => window.removeEventListener('hrm:prefs-restored', reloadRestoredTheme));

  watchSystemTheme(() => onSystemTheme());

  function setLang(next: Lang): void {
    lang.value = next;
    setLocale(next);
  }

  /** Backend   PH 0   Local (first time to align local settings). */
  async function pull(): Promise<void> {
    try {
      const cfg = (await api.config()) as { ui?: Record<string, unknown>; sysTheme?: { dark?: boolean; accent?: string } };
      // System emphasis on colour only ( PH 0 Registration form), drop in and calculate the theme
      if (cfg.sysTheme) setSystemTheme(cfg.sysTheme);
      const ui = cfg.ui;
      if (!ui) {
        applyUiTheme(look.value);
        ready = true;
        return;
      }
      // PH 0 is a new field; the old configuration is only   PH 1, migration by map once
      const legacy = fromLegacyTheme(String(ui.theme ?? ''));
      apply({
        mode: (ui.mode as UiMode) ?? legacy?.mode ?? look.value.mode,
        palette: (ui.palette as UiPalette) ?? legacy?.palette ?? look.value.palette,
        primary: (ui.primary as string) ?? (ui.accent as string) ?? look.value.primary,
        accent: (ui.accent as string) ?? look.value.accent,
        solid: typeof ui.solid === 'boolean' ? ui.solid : look.value.solid,
        bg: (ui.bg as string) ?? look.value.bg,
        panel: (ui.panel as string) ?? look.value.panel,
        fontFamily: (ui.fontFamily as string) ?? look.value.fontFamily,
        monoFontFamily: (ui.monoFontFamily as string) ?? look.value.monoFontFamily,
        cornerRadius: (ui.cornerRadius as number) ?? look.value.cornerRadius,
        density: (ui.density as number) ?? look.value.density,
        animations: typeof ui.animations === 'boolean' ? ui.animations : look.value.animations,
      });
      const l = String(ui.lang ?? '');
      const map: Record<string, Lang> = {
        'zh-tw': 'zh-TW',
        'zh-hk': 'zh-HK',
        'yue-hk': 'yue-HK',
        'zh-cn': 'zh-CN',
        en: 'en',
        ja: 'ja',
        es: 'es',
        ko: 'ko',
        de: 'de',
        fr: 'fr',
      };
      if (map[l]) setLang(map[l]);
      if (typeof ui.brand === 'string') brand.value = ui.brand;
    } catch {
      /* Use local preferences when backend is unavailable */
    }
    // PH 0   2:   PH 1 end (success or failure) to open auto-push - avoid pushing back-filling back to the original / backend when unreachable
    ready = true;
  }

  // PH 0 2: Themes/languages/brand changes automatically shivering backends -   PH 1 ( PH 2)
  // Quietly failed), now united in   PH 0: Successfully lighted "Saved" and failed to highlight the "Unsaved" logo.
  watch([look, brand, lang], () => {
    if (!ready) return;
    window.clearTimeout(pushTimer);
    pushTimer = window.setTimeout(() => {
      void push().then((ok) => (ok ? markSaved() : markSaveFailed()));
    }, 600);
  });

  /** Local whirlwind end lasting.   PH 0 fields are still written back for reading at   PH 1 PH 2  frontend. */
  async function push(): Promise<boolean> {
    const back: Record<Lang, string> = {
      'zh-TW': 'zh-tw',
      'zh-HK': 'zh-hk',
      'yue-HK': 'yue-hk',
      'zh-CN': 'zh-cn',
      en: 'en',
      ja: 'ja',
      es: 'es',
      ko: 'ko',
      de: 'de',
      fr: 'fr',
    };
    try {
      await api.settings({
        ui: {
          lang: back[lang.value],
          mode: look.value.mode,
          palette: look.value.palette,
          theme: toLegacyTheme(look.value),
          primary: look.value.primary,
          accent: look.value.accent,
          solid: look.value.solid,
          bg: look.value.bg,
          panel: look.value.panel,
          fontFamily: look.value.fontFamily,
          monoFontFamily: look.value.monoFontFamily,
          cornerRadius: look.value.cornerRadius,
          density: look.value.density,
          animations: look.value.animations,
          brand: brand.value,
        },
      });
      return true;
    } catch {
      return false;
    }
  }

  return { look, lang, brand, mode, apply, setLang, pull, push, onSystemTheme };
});
