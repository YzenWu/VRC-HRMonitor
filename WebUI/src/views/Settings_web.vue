<script setup lang="ts">




import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink } from 'vue-router';
import { Maximize2, ChevronDown, RotateCcw, Save } from 'lucide-vue-next';
import { LANGS, LANG_FLAG, LANG_LABEL, type Lang } from '../i18n';
import {
  CORNER_SLIDER_MAX,
  DEFAULT_LOOK,
  DENSITY_SLIDER_MAX,
  DENSITY_SLIDER_MIN,
  MODE_IDS,
  MODE_LABEL,
  PALETTE_IDS,
  PALETTE_LABEL,
  type UiMode,
  type UiPalette,
} from '../theme';
import { useUiStore } from '../stores/ui';
import { useAppStore } from '../stores/app';
import { markSaved } from '../stores/notify';
import { barH, brandPill, chipNameW, listCap, rollMode, statusPop, autoRestart, ROLL_MODES, type BrandPillMode, type RollMode } from '../prefs';
import { api } from '../api';
import { postShell, setShellCorner, winState } from '../shell';
import XSwitch from '../components/XSwitch.vue';
import XSelect from '../components/XSelect.vue';
import XColorField from '../components/XColorField.vue';
import XRange from '../components/XRange.vue';
import XDialog from '../components/XDialog.vue';
import DataPathCard from '../components/DataPathCard.vue';
import AutoStartCard from '../components/AutoStartCard.vue';
import RecordingCard from '../components/RecordingCard.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t, locale } = useI18n();
const ui = useUiStore();
const app = useAppStore();



const CARDS: GridCard[] = [
  { id: 'appearance', titleKey: 'settings.appearance', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'win', titleKey: 'win.section', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'advtheme', titleKey: 'settings.advtheme', span: 12, noCc: true, bodyStyle: 'padding: 0' },
  { id: 'behavior', titleKey: 'settings.behavior', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'autostart', titleKey: 'settings.autostart', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'recording', titleKey: 'settings.recording', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
];

const ABOUT_CARDS: GridCard[] = [
  { id: 'about', titleKey: 'settings.about', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: 4px; font-size: 12.5px' },
];

/** Advanced theme setting (#7/#25) folds; read-only preference, not configuration. */
const advOpen = ref(false);

const LANG_COL: Record<string, number> = { 'zh-TW': 0, 'zh-CN': 1, en: 2, ja: 3, es: 4, ko: 5, de: 6, fr: 7 };
const col = () => LANG_COL[locale.value] ?? 1;
const modeLabel = (id: UiMode) => MODE_LABEL[id][col()];
const paletteLabel = (id: UiPalette) => PALETTE_LABEL[id][col()];

const langOpts = computed(() => LANGS.map((l) => ({ value: l, label: LANG_LABEL[l], prefix: LANG_FLAG[l] })));
const modeOpts = computed(() => MODE_IDS.map((m) => ({ value: m, label: modeLabel(m) })));
const paletteOpts = computed(() => PALETTE_IDS.map((p) => ({ value: p, label: paletteLabel(p) })));

// Top left indicator content (front-end local preferences; delay/average/main rate/number of equipment/connected/closed)
const PILL_IDS: BrandPillMode[] = ['off', 'ping', 'avg', 'main', 'devices', 'connected'];
const pillLabel = (v: BrandPillMode): string =>
  t(
    v === 'off'
      ? 'nav.pilloff'
      : v === 'ping'
        ? 'nav.ping'
        : v === 'avg'
          ? 'hb.average'
          : v === 'main'
            ? 'hb.srcmain'
            : v === 'devices'
              ? 'tab.devices'
              : 'dev.connected',
  );
const pillOpts = computed(() => PILL_IDS.map((v) => ({ value: v, label: pillLabel(v) })));

const rollOpts = computed(() => ROLL_MODES.map((m) => ({ value: m.value, label: t(m.key) })));


const isCustom = computed(() => ui.look.palette === 'custom');
function setCustom(v: boolean): void {
  onPalette(v ? 'custom' : 'default');
}

async function persist(): Promise<void> {
  const ok = await ui.push();
  // "Saved" to unify the bottom of the field and no more local flashing.
  if (ok) markSaved();
}

function onLang(next: string): void {
  ui.setLang(next as Lang);
  void persist();
}
function onMode(next: string): void {
  ui.apply({ mode: next as UiMode });
  void persist();
}
function onPalette(next: string): void {
  ui.apply({ palette: next as UiPalette });
  void persist();
}
function onAnim(next: boolean): void {
  ui.apply({ animations: next });
  void persist();
}


const debug = ref(false);
const debugHint = ref('');
const closeAction = ref<'ask' | 'exit' | 'tray'>('ask');

const CLOSE_IDS = ['ask', 'exit', 'tray'] as const;
const closeLabel = (v: string): string =>
  v === 'exit' ? t('close.exit') : v === 'tray' ? t('close.tray') : t('close.ask');
const closeOpts = computed(() => CLOSE_IDS.map((c) => ({ value: c, label: closeLabel(c) })));

async function onDebug(v: boolean): Promise<void> {
  debug.value = v;
  await api.settings({ app: { debug: v } });
  await app.pullInfo();
  debugHint.value = t('settings.debughint2');
  window.setTimeout(() => (debugHint.value = ''), 2600);
}

async function onCloseAction(v: string): Promise<void> {
  if (v !== 'ask' && v !== 'exit' && v !== 'tray') return;
  closeAction.value = v;
  await api.settings({ ui: { closeAction: v } });
}

// P3: startup update check; serialize writes and reread server state after failures.
const updateCheck = ref(true);
const updateCheckSaving = ref(false);

async function onUpdateCheck(v: boolean): Promise<void> {
  if (updateCheckSaving.value) return;
  updateCheckSaving.value = true;
  updateCheck.value = v;
  try {
    const r = await api.settings({ app: { updateCheck: v } });
    if (r.ok === false) throw new Error(String(r.error ?? ''));
  } catch {
    await app.pullInfo();
    updateCheck.value = app.appInfo?.updateCheck ?? true;
  } finally {
    updateCheckSaving.value = false;
  }
}

onMounted(async () => {
  await app.pullInfo();
  debug.value = app.appInfo?.debug ?? false;
  updateCheck.value = app.appInfo?.updateCheck ?? true;
  void api
    .config()
    .then((c) => {
      const v = String((c as { ui?: { closeAction?: string } }).ui?.closeAction ?? 'ask');
      if (v === 'ask' || v === 'exit' || v === 'tray') closeAction.value = v;
    })
    .catch(() => {

    });
});

// Draft advanced theme setup (#19): changes are pre-drafted to " save and apply " before writing preferences;

interface AdvDraft {
  brand: string;
  pill: BrandPillMode;
  pop: boolean;
  chipW: number;
  barH: number;
  roll: RollMode;
  listCap: number;
}

function advSnapshot(): AdvDraft {
  return {
    brand: ui.brand,
    pill: brandPill.value,
    pop: statusPop.value,
    chipW: chipNameW.value,
    barH: barH.value,
    roll: rollMode.value,
    listCap: listCap.value,
  };
}

function advWrite(v: AdvDraft): void {
  ui.brand = v.brand;
  void persist();
  brandPill.value = v.pill;
  statusPop.value = v.pop;
  chipNameW.value = v.chipW;
  barH.value = v.barH;
  rollMode.value = v.roll;
  listCap.value = v.listCap;
}

const advDraft = ref<AdvDraft>(advSnapshot());
/** Confirms that the bullet window is open (= applied to be confirmed). */
const advConfirming = ref(false);
/** Pre-application snapshot (time-reducing target). */
let advBackup: AdvDraft | null = null;
/** Number of seconds left in countdown. */
const advLeft = ref(15);
let advTimer = 0;

const advDirty = computed(() => JSON.stringify(advDraft.value) !== JSON.stringify(advSnapshot()));

function advApply(): void {
  if (!advDirty.value) return;
  advBackup = advSnapshot();
  advWrite(advDraft.value);
  advConfirming.value = true;
  advLeft.value = 15;
  window.clearInterval(advTimer);
  advTimer = window.setInterval(() => {
    advLeft.value--;
    if (advLeft.value <= 0) advRevert();
  }, 1000);
}

function advKeep(): void {
  window.clearInterval(advTimer);
  advConfirming.value = false;
  advBackup = null;
  markSaved();
}

function advRevert(): void {
  window.clearInterval(advTimer);
  if (advBackup) advWrite(advBackup);
  advDraft.value = advSnapshot();
  advConfirming.value = false;
  advBackup = null;
}

// Collapse card = relinquish unsaved drafts (back to current applied value); confirm that bullet windows are not closed as a result (independent countdown)
watch(advOpen, (open) => {
  if (!open && !advConfirming.value) advDraft.value = advSnapshot();
});

onBeforeUnmount(() => window.clearInterval(advTimer));
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1 v-bubble="t('settings.palettehint')">{{ t('tab.settings') }}</h1>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body">
      <CardGrid view="settings" :cards="CARDS">
        <!-- Appearance -->
        <template #appearance>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.language') }}</label>
            <XSelect
              style="width: 168px"
              :model-value="ui.lang"
              :items="langOpts"
              @update:model-value="(v) => onLang(String(v))"
            />
          </div>

          <!-- Custom theme: Open = Show colour pickers only and hide preset drop-downs; off = restore dark/colored drop-downs -->
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch :model-value="isCustom" :label="t('settings.custom')" style="min-width: 150px" @update:model-value="(v) => setCustom(Boolean(v))" />
            <span class="x-muted" style="font-size: 11.5px">{{ t('settings.customhint') }}</span>
          </div>

          <!-- Darkness and coloring are two separate dimensions, each of which starts with "System compliance "; hides this line when customizing open -->
          <div v-if="!isCustom" class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.thememode') }}</label>
            <XSelect
              v-bubble="t('settings.systemhint')"
              style="width: 128px"
              :model-value="ui.look.mode"
              :items="modeOpts"
              @update:model-value="(v) => onMode(String(v))"
            />

            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.palette') }}</label>
            <XSelect
              style="width: 132px"
              :model-value="ui.look.palette"
              :items="paletteOpts"
              @update:model-value="(v) => onPalette(String(v))"
            />
            <span class="x-muted" style="font-size: 11.5px">{{ t('settings.systemhint') }}</span>
          </div>

          <div v-if="ui.look.palette === 'custom'" class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">{{ t('settings.accent') }}</label>
            <input
              class="x-input"
              type="color"
              style="width: 56px; padding: 2px"
              :value="ui.look.accent"
              @input="ui.apply({ accent: ($event.target as HTMLInputElement).value })"
              @change="persist"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('settings.bg') }}</label>
            <input
              class="x-input"
              type="color"
              style="width: 56px; padding: 2px"
              :value="ui.look.bg"
              @input="ui.apply({ bg: ($event.target as HTMLInputElement).value })"
              @change="persist"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('settings.panel') }}</label>
            <input
              class="x-input"
              type="color"
              style="width: 56px; padding: 2px"
              :value="ui.look.panel"
              @input="ui.apply({ panel: ($event.target as HTMLInputElement).value })"
              @change="persist"
            />
          </div>

          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.corner') }}</label>
            <XRange
              v-bubble="t('settings.cornerhint')"
              :model-value="ui.look.cornerRadius"
              :min="0"
              :max="CORNER_SLIDER_MAX"
              :step="1"
              :hard-min="0"
              :default="DEFAULT_LOOK.cornerRadius"
              suffix="px"
              @update:model-value="(v) => ui.apply({ cornerRadius: v })"
              @change="persist"
            />
          </div>

          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.density') }}</label>
            <XRange
              :model-value="ui.look.density"
              :min="DENSITY_SLIDER_MIN"
              :max="DENSITY_SLIDER_MAX"
              :step="0.1"
              :hard-min="DENSITY_SLIDER_MIN"
              :default="DEFAULT_LOOK.density"
              suffix="×"
              @update:model-value="(v) => ui.apply({ density: v })"
              @change="persist"
            />
            <span class="x-muted" style="font-size: 11.5px">{{ t('settings.densityhint') }}</span>
          </div>

          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.anim') }}</label>
            <XSwitch
              v-bubble="t('settings.animhint')"
              :model-value="ui.look.animations"
              :label="ui.look.animations ? t('common.on') : t('common.off')"
              style="min-width: 92px"
              @update:model-value="(v) => onAnim(v)"
            />
          </div>
        </template>

        <!-- Window (shell exclusive: host window without system topbar, rounded by window area) -->
        <template #win>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('win.corner') }}</label>
            <XRange
              :model-value="winState.corner"
              :min="0"
              :max="24"
              :step="1"
              :hard-min="0"
              :hard-max="24"
              :default="10"
              suffix="px"
              @update:model-value="(v) => setShellCorner(v)"
            />
            <span class="x-muted" style="font-size: 11.5px">{{ t('win.cornerhint') }}</span>
          </div>

          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('close.action') }}</label>
            <XSelect
              v-bubble="t('close.actionhint')"
              style="width: 170px"
              :model-value="closeAction"
              :items="closeOpts"
              @update:model-value="(v) => void onCloseAction(String(v))"
            />
          </div>

          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('win.fullscreen') }}</label>
            <button v-bubble="t('win.fullscreenhint')" class="x-btn sm" @click="postShell('win.fullscreen')">
              <Maximize2 class="x-btn-ico" />
              {{ winState.fullScreen ? t('win.exitfullscreen') : t('win.fullscreen') }}
            </button>
          </div>
        </template>

        <!-- Advanced Theme Settings (#7 #25: nuanced front-end style items, foldable; ready to be saved) -->
        <template #head-advtheme>
          <span v-bubble="t('settings.advthemehint')" style="cursor: pointer" @click="advOpen = !advOpen">
            {{ t('settings.advtheme') }}
          </span>
        </template>

        <template #head-tail-advtheme>
          <button type="button" class="adv-chev" :class="{ open: advOpen }" @click="advOpen = !advOpen">
            <ChevronDown />
          </button>
        </template>
        <template #advtheme>
          <div v-if="advOpen" class="x-card-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">


          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.brand') }}</label>
            <input
              v-bubble="t('settings.brandhint')"
              v-model="advDraft.brand"
              class="x-input x-mono"
              style="width: 300px"
              :placeholder="t('nav.brand')"
            />
          </div>

          <!-- Top left pointer (left side of brand title): Shows delay / average / master heart rate, etc. -->
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.pill') }}</label>
            <XSelect style="width: 168px" v-model="advDraft.pill" :items="pillOpts" />
          </div>

          <!-- Basebar equipment capsule suspension bubble (Close) -->
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.statuspop') }}</label>
            <XSwitch :model-value="advDraft.pop" @update:model-value="(v) => (advDraft.pop = Boolean(v))" />
          </div>


          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.chipw') }}</label>
            <XRange
              v-bubble="t('settings.chipwhint')"
              v-model="advDraft.chipW"
              :min="40"
              :max="320"
              :step="4"
              :hard-min="1"
              :default="108"
              suffix="px"
            />
          </div>

          <!-- Bottom height (0 = following density scaling; >0 fixed pixels, sidebar opening buttons, etc. high) -->
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.barh') }}</label>
            <XRange v-bubble="t('settings.barhhint')" v-model="advDraft.barH" :min="0" :max="56" :step="1" :hard-min="0" :default="0" suffix="px" />
          </div>

          <!-- Numerical animation style (#20 Completion: wheel/ smooth scroll/hidden display 3-story global switch) -->
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.rollmode') }}</label>
            <XSelect style="width: 220px" v-model="advDraft.roll" :items="rollOpts" />
          </div>


          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('settings.listcap') }}</label>
            <XRange v-bubble="t('settings.listcaphint')" v-model="advDraft.listCap" :min="5" :max="50" :step="1" :hard-min="1" :default="10" />
          </div>

          <div class="x-gap">
            <button v-bubble="t('settings.advdraft')" class="x-btn sm primary" :disabled="!advDirty" @click="advApply">
              <Save class="x-btn-ico" />
              {{ t('settings.advsave') }}
            </button>
          </div>
          </div>
        </template>

        <!-- Interface Behaviour -->
        <template #head-behavior>
          {{ t('settings.behavior') }}
        </template>
        <template #behavior>
          <!-- Lines such as brand titles/indicators/capable bubbles have been moved to the Advanced Theme Settings card (#7/#25) -->
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch v-model="autoRestart" :label="t('settings.autorestart')" style="min-width: 150px" />
            <span class="x-muted" style="font-size: 11.5px">{{ t('settings.autorestarthint') }}</span>
          </div>

          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch
              :model-value="debug"
              :label="t('settings.debugmode')"
              style="min-width: 150px"
              @update:model-value="(v) => void onDebug(v)"
            />
            <span class="x-muted" style="font-size: 11.5px">{{ t('settings.debughint') }}</span>
          </div>

          <!-- P3: startup update check (GitHub latest vs the embedded manifest) -->
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch
              v-bubble="t('settings.updatecheckhint')"
              :model-value="updateCheck"
              :label="t('settings.updatecheck')"
              :disabled="updateCheckSaving"
              style="min-width: 150px"
              @update:model-value="(v) => void onUpdateCheck(v)"
            />
          </div>
          <div v-if="debugHint" class="x-muted" style="font-size: 11.5px">{{ debugHint }}</div>

          <!-- Security mode entrance (#18 End: Silent tool, only settings; post-activated top banner + exit button) -->
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="min-width: 96px; font-size: 12.5px">{{ t('safe.mode') }}</label>
            <span class="x-muted" style="font-size: 12px">{{ app.safeMode ? t('safe.active') : t('safe.normal') }}</span>
            <button class="x-btn sm" :disabled="app.safeMode" @click="postShell('engine.safemode')">{{ t('safe.enter') }}</button>
            <span class="x-muted" style="font-size: 11.5px">{{ t('safe.entryhint') }}</span>
          </div>
        </template>

        <!-- Login autostart (P1): Task Scheduler / Run / Startup, multi-select + silent option -->
        <template #autostart>
          <AutoStartCard />
        </template>

        <template #recording>
          <RecordingCard />
        </template>
      </CardGrid>


      <XDialog :open="advConfirming" :title="t('settings.advconfirm')" :desc="t('settings.advtimeout')" size="sm" @close="advRevert">
        <div class="x-muted x-mono" style="font-size: 15px; text-align: center">{{ advLeft }}s</div>
        <template #actions>
          <button class="x-btn sm primary" @click="advKeep">{{ t('settings.advkeep') }}</button>
          <button class="x-btn sm ghost" @click="advRevert">
            <RotateCcw class="x-btn-ico" />
            {{ t('settings.advrevert') }}
          </button>
        </template>
      </XDialog>

      <!-- Storage and data (data directories) -->
      <DataPathCard />


      <CardGrid view="settings-about" :cards="ABOUT_CARDS">
        <template #head-about>
          {{ t('settings.about') }}
        </template>
        <template #about>
          <div>
            <span class="x-muted">{{ t('settings.version') }}:</span> {{ app.appInfo?.version ?? '-' }}
            <span class="x-muted" style="margin-left: 12px">{{ t('settings.mode') }}:</span>
            {{ app.appInfo?.debug ? 'DEBUG' : 'RELEASE' }}
          </div>
          <div>
            <span class="x-muted">{{ t('settings.started') }}:</span> {{ app.appInfo?.startTime ?? '-' }}
          </div>
          <div class="x-mono x-muted">{{ app.appInfo?.baseDir ?? '-' }}</div>
          <div class="x-gap">
            <RouterLink class="x-btn sm ghost" style="text-decoration: none" to="/about">{{ t('settings.gotoabout') }}</RouterLink>
          </div>
        </template>
      </CardGrid>
    </div>
  </div>
</template>

<style scoped>

.adv-chev {
  width: 15px;
  height: 15px;
  padding: 0;
  border: 0;
  background: none;
  color: var(--muted-foreground);
  cursor: pointer;
  opacity: 0.75;
  transform: rotate(-90deg);
  transition: transform 0.2s linear, opacity 0.15s linear;
}
.adv-chev svg {
  width: 100%;
  height: 100%;
}
.adv-chev:hover {
  opacity: 1;
}
.adv-chev.open {
  transform: rotate(0deg);
}
</style>
