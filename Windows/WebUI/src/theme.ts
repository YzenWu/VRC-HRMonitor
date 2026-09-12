/** Theme / Appearance: Set the   PH 0  preference to <   PH 1   PH 2 /   PH 3 /   PH 4  Variable (  PH 5  semantic alignment   PH 6  PH 7).
 *  The dark mode (  PH 0) and the theme colour (  PH 1) are independent of each other, both of which begin with the following:
 *    mode    = system | dark | light
 *    palette = system | default | forest | sunset | ocean | violet | custom
 *  PH 0  from   PH 1  (Deep reading of registration form  PH 2, emphasis on colour reading   PH 3   PH 4, sent down from backend).
 *  Rounded angles (the slider covers only 0 ~ 16, the input box has no limit) →    PH 0; density (the slider covers only 0.8 ~1.4, the input box has no upper limit, the bottom limit is 0.8) →    PH 1  (spacing/control scaling).
 *  PH 0    at <    PH 1      PH 2,    PH 3   turn off the entire station transition and key frames. */

export type UiMode = 'system' | 'dark' | 'light';
export type UiPalette = 'system' | 'default' | 'forest' | 'sunset' | 'ocean' | 'violet' | 'custom';
/** A single theme identifier from the old configuration (relay   PH 0  for external reading). */
export type UiTheme = 'dark' | 'light' | 'forest' | 'sunset' | 'custom';

export interface UiLook {
  mode: UiMode;
  palette: UiPalette;
  primary: string;
  accent: string;
  solid: boolean;
  bg: string;
  panel: string;
  fontFamily: string;
  monoFontFamily: string;
  cornerRadius: number;
  density: number;
  /** Global animation switch.  PH 0  = turn off the entire station transition/key frame animation in exchange for the most direct response. */
  animations: boolean;
}

/** Slider-covered slots (in excess of the post-set page automatically hides the slider, leaving only the input box; the input box is not capped). */
export const CORNER_SLIDER_MAX = 16;
export const DENSITY_SLIDER_MIN = 0.8;
export const DENSITY_SLIDER_MAX = 1.4;

/** (b) Legitimation of round angles: non-negative integer, no upper limit (leaving of caps: consistent with backend path, protection against negative   PH 0). */
export function clampCorner(v: unknown): number {
  const n = Math.round(Number(v));
  if (!Number.isFinite(n)) return DEFAULT_LOOK.cornerRadius;
  return Math.max(0, n);
}

/** Density legitimation: slider minimum 0.8, no ceiling (avoids unusably small spacing and controls). */
export function clampDensity(v: unknown): number {
  const n = Number(v);
  if (!Number.isFinite(n)) return DEFAULT_LOOK.density;
  return Math.max(DENSITY_SLIDER_MIN, n);
}

export const MODE_IDS: UiMode[] = ['system', 'dark', 'light'];
export const PALETTE_IDS: UiPalette[] = ['system', 'default', 'forest', 'sunset', 'ocean', 'violet', 'custom'];

export const MODE_LABEL: Record<UiMode, [string, string, string, string, string, string, string, string]> = {
  system: ['遵循系統', '遵循系统', 'Follow system', 'システムに従う', 'Seguir al sistema', '시스템 따르기', 'System folgen', 'Suivre le système'],
  dark: ['暗色', '暗色', 'Dark', 'ダーク', 'Oscuro', '어두운', 'Dunkel', 'Sombre'],
  light: ['亮色', '亮色', 'Light', 'ライト', 'Claro', '밝은', 'Hell', 'Clair'],
};

export const PALETTE_LABEL: Record<UiPalette, [string, string, string, string, string, string, string, string]> = {
  system: ['遵循系統', '遵循系统', 'Follow system', 'システムに従う', 'Seguir al sistema', '시스템 따르기', 'System folgen', 'Suivre le système'],
  default: ['預設', '默认', 'Default', 'デフォルト', 'Predeterminado', '기본', 'Standard', 'Défaut'],
  forest: ['森林', '森林', 'Forest', 'フォレスト', 'Bosque', '포레스트', 'Wald', 'Forêt'],
  sunset: ['落日', '落日', 'Sunset', 'サンセット', 'Atardecer', '석양', 'Sonnenuntergang', 'Crépuscule'],
  ocean: ['海藍', '海蓝', 'Ocean', 'オーシャン', 'Océano', '바다', 'Ozean', 'Océan'],
  violet: ['紫羅蘭', '紫罗兰', 'Violet', 'バイオレット', 'Violeta', '보라', 'Violett', 'Violet'],
  custom: ['自訂', '自定义', 'Custom', 'カスタム', 'Personalizado', '사용자 지정', 'Benutzerdefiniert', 'Personnalisé'],
};

export const DEFAULT_LOOK: UiLook = {
  mode: 'system',
  palette: 'system',
  primary: '#5b9dff',
  accent: '#8b5cf6',
  solid: false,
  bg: '#14171b',
  panel: '#1c2128',
  fontFamily: "'Segoe UI', 'Microsoft YaHei', system-ui, -apple-system, sans-serif",
  monoFontFamily: "Consolas, 'Cascadia Mono', monospace",
  cornerRadius: 8,
  density: 1,
  animations: true,
};

const LOOK_KEY = 'hrm-web-ui';

/** System theme snapshot: Deep from   PH 0, emphasised color from the back end ( PH 1 Registration form). */
export const system = { dark: true, accent: '#5b9dff' };

/** The old   PH 0 fields are new   PH 1  (used when first reading the old configuration). */
const LEGACY: Record<string, { mode: UiMode; palette: UiPalette }> = {
  dark: { mode: 'dark', palette: 'default' },
  light: { mode: 'light', palette: 'default' },
  forest: { mode: 'dark', palette: 'forest' },
  sunset: { mode: 'dark', palette: 'sunset' },
  custom: { mode: 'dark', palette: 'custom' },
};

export function fromLegacyTheme(theme: string): { mode: UiMode; palette: UiPalette } | null {
  return LEGACY[theme] ?? null;
}

/** New   PH 0   Old   PH 1 field (relay   PH 2  for   PH 3  frontend). */
export function toLegacyTheme(look: UiLook): UiTheme {
  if (look.palette === 'custom') return 'custom';
  if (look.palette === 'forest') return 'forest';
  if (look.palette === 'sunset') return 'sunset';
  return resolvedMode(look) === 'light' ? 'light' : 'dark';
}

/** The light and dark ( PH 0 time to take the system value) that actually takes effect. */
export function resolvedMode(look: UiLook): 'dark' | 'light' {
  if (look.mode === 'system') return system.dark ? 'dark' : 'light';
  return look.mode;
}

function hasStoredPrimary(): boolean {
  try {
    const raw = JSON.parse(localStorage.getItem(LOOK_KEY) || '{}') as Partial<UiLook>;
    return 'primary' in raw;
  } catch {
    return false;
  }
}

/** The color emphasis that actually enters into force. Legacy look objects without `primary`
 * keep treating `accent` as the primary color; current objects use the dedicated primary field. */
export function resolvedAccent(look: UiLook): string {
  if (look.palette === 'system') return system.accent;
  if (look.palette === 'custom') {
    if (hasStoredPrimary()) return look.primary;
    return look.accent !== DEFAULT_LOOK.accent ? look.accent : look.primary;
  }
  return '';
}

export function storedLook(): UiLook {
  try {
    const raw = localStorage.getItem(LOOK_KEY);
    if (raw) {
      const o = JSON.parse(raw) as Partial<UiLook> & { theme?: string };
      const primary = o.primary ?? o.accent ?? DEFAULT_LOOK.primary;
      // Old version saves   PH 0, one-time migration
      if (!o.mode && !o.palette && o.theme) {
        const m = fromLegacyTheme(o.theme);
        if (m) {
          const look = { ...DEFAULT_LOOK, ...o, ...m, primary };
          return { ...look, density: clampDensity(look.density) };
        }
      }
      const look = { ...DEFAULT_LOOK, ...o, primary };
      return { ...look, density: clampDensity(look.density) };
    }
  } catch {
    /* ignore */
  }
  return { ...DEFAULT_LOOK };
}

export function saveLook(look: UiLook): void {
  localStorage.setItem(LOOK_KEY, JSON.stringify(look));
}

function hexToRgb(hex: string): [number, number, number] | null {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return null;
  const n = parseInt(m[1], 16);
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255];
}

function mix(rgb: [number, number, number], target: number, amount: number): string {
  const channel = (v: number) => Math.round(v + (target - v) * amount);
  return `rgb(${channel(rgb[0])} ${channel(rgb[1])} ${channel(rgb[2])})`;
}

export function customSurfaces(look: UiLook): { bg: string; panel: string } {
  if (!look.solid) return { bg: look.bg, panel: look.panel };
  const rgb = hexToRgb(look.primary) ?? [91, 157, 255];
  return resolvedMode(look) === 'dark'
    ? { bg: mix(rgb, 0, 0.82), panel: mix(rgb, 0, 0.72) }
    : { bg: mix(rgb, 255, 0.88), panel: mix(rgb, 255, 0.96) };
}

/** Highlighted text colour: Select one by relative brightness and secure button text readable ( PH 0  emphasis on dark and shallow colour). */
function onAccent(rgb: [number, number, number]): string {
  const lum = (0.299 * rgb[0] + 0.587 * rgb[1] + 0.114 * rgb[2]) / 255;
  return lum > 0.62 ? '#111418' : '#ffffff';
}

export function applyUiTheme(look: UiLook = storedLook()): void {
  const root = document.documentElement;
  const mode = resolvedMode(look);
  root.dataset.mode = mode;
  root.dataset.palette = look.palette;
  // A few third-party styles still look at.  PH 0 PH 1  (e. g.   PH 2 link)
  root.classList.toggle('dark', mode === 'dark');
  // Animated main gate:   PH 0...   PH 1... will put all   PH 2... to zero
  root.dataset.anim = look.animations === false ? 'off' : 'on';

  const style = root.style;
  const primaryHex = resolvedAccent(look) || look.primary;
  const primaryRgb = hexToRgb(primaryHex) ?? [91, 157, 255];
  const accentRgb = hexToRgb(look.accent) ?? primaryRgb;
  const surfaces = customSurfaces(look);

  style.setProperty('--c-accent', `rgb(${primaryRgb[0]} ${primaryRgb[1]} ${primaryRgb[2]})`);
  style.setProperty('--c-accent-rgb', `${primaryRgb[0]} ${primaryRgb[1]} ${primaryRgb[2]}`);
  style.setProperty('--c-accent-fg', onAccent(primaryRgb));
  style.setProperty('--c-secondary-accent', `rgb(${accentRgb[0]} ${accentRgb[1]} ${accentRgb[2]})`);
  style.setProperty('--c-secondary-accent-rgb', `${accentRgb[0]} ${accentRgb[1]} ${accentRgb[2]}`);
  style.setProperty('--c-bg', surfaces.bg);
  style.setProperty('--c-panel', surfaces.panel);
  style.setProperty('--font-body', look.fontFamily.trim() || DEFAULT_LOOK.fontFamily);
  style.setProperty('--font-mono', look.monoFontFamily.trim() || DEFAULT_LOOK.monoFontFamily);

  // Rounded angle reserved   PH 0  lower limit (same as the old version to avoid the edges of the control at extreme 0 values)
  style.setProperty('--radius', `${Math.max(2, clampCorner(look.cornerRadius))}px`);
  style.setProperty('--sp', `${clampDensity(look.density)}`);
}

/**
 * System theme follows: Deep and shallow   PH 0  real-time listening, with emphasis on color coming from the   PH 1  event.
 * PH 0   was given to the caller ( PH 1) to re-establish   PH 2  to avoid the reverse dependence on   PH 3.
 */
export function watchSystemTheme(onChange: () => void): void {
  try {
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    system.dark = mq.matches;
    mq.addEventListener('change', (e) => {
      system.dark = e.matches;
      onChange();
    });
  } catch {
    /* No old environment   PH 0: Keep Default Dark */
  }
}

/** System theme snapshots sent down the backend (emphasis on colour only   PH 0 registration form). */
export function setSystemTheme(next: { dark?: boolean; accent?: string }): boolean {
  let changed = false;
  if (typeof next.dark === 'boolean' && next.dark !== system.dark) {
    system.dark = next.dark;
    changed = true;
  }
  if (typeof next.accent === 'string' && hexToRgb(next.accent) && next.accent !== system.accent) {
    system.accent = next.accent;
    changed = true;
  }
  return changed;
}
