import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useAppStore } from '../stores/app';
import { useUiStore } from '../stores/ui';

/**
 * Top left brand title: Default   PH 0 PH 1; users can fit   PH 2  templates in settings,
 * Supports placeholder (same as the   PH 0  template at the back end):
 *   - System information variables (capable in the variable table, e. g. {  PH 0), updated by   PH 1 incident;
 *   - Frontend real-time indicators (not case-sensitive): {  PH 0  backend delay,   PH 1,   PH 1
 *     {bpm}/{avg}/{devices}/{connected}/{version}。
 * Undefined placeholder renders an empty string (consistent with the   PH 0 act) without brackets.
 */
export function useBrand() {
  const { t } = useI18n();
  const app = useAppStore();
  const ui = useUiStore();

  const brandText = computed(() => {
    const tpl = (ui.brand ?? '').trim();
    if (!tpl) return t('nav.brand');
    const sys: Record<string, string> = app.sysVars ?? {};
    const metrics: Record<string, string> = {
      ping: app.ping > 0 ? String(app.ping) : '—',
      oscRx: String(app.osc?.recv ?? 0),
      oscSent: String(app.osc?.sent ?? 0),
      oscFail: String(app.osc?.fail ?? 0),
      bpm: app.bpm > 0 ? String(app.bpm) : '—',
      avg: app.avg > 0 ? String(app.avg) : '—',
      devices: String(app.devices.length),
      connected: String(app.connectedCount),
      version: app.appInfo?.version ?? '',
    };
    return tpl.replace(/\{([^{}]+)\}/g, (_, raw: string) => {
      const key = raw.trim();
      if (!key) return '';
      const low = key.toLowerCase();
      if (low in metrics) return metrics[low];
      if (key in sys) return sys[key];
      for (const k of Object.keys(sys)) {
        if (k.toLowerCase() === low) return sys[k];
      }
      return '';
    });
  });

  return { brandText };
}
