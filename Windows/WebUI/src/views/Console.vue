<script setup lang="ts">
import { nextTick, onMounted, onUnmounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

/** #9 Layout editing: the bare terminal card fills the remaining page height. */
const CARDS: GridCard[] = [{ id: 'term', bare: true, span: 12 }];

/**
 * TTY-style terminal: the input remains the final line in the scrolling output.
 * Submitting echoes the command and its output into the same history stream.
 */
const { t } = useI18n();
const app = useAppStore();
const lines = ref<string[]>([]);
const input = ref('');
const history = ref<string[]>([]);
const hIdx = ref(-1);
const box = ref<HTMLDivElement | null>(null);
const inputEl = ref<HTMLInputElement | null>(null);
/** Command names used for Tab completion; populated once from `help` when mounted. */
const cmdNames = ref<string[]>([]);

function banner(): string[] {
  const v = app.appInfo?.version ?? '';
  return [
    `HeartRateMonitor · OSC Pusher   ${v ? 'v' + v : ''}   ${app.appInfo?.debug ? 'DEBUG' : 'RELEASE'}`,
    '',
    t('console.ready'),
    t('console.hint'),
    '',
  ];
}

function scroll(instant = false): void {
  const fn = () => {
    if (box.value) box.value.scrollTop = box.value.scrollHeight;
  };
  if (instant) fn();
  else requestAnimationFrame(fn);
}

function focusInput(): void {
  void nextTick(() => inputEl.value?.focus());
}

onMounted(() => {
  lines.value = banner();
  focusInput();
  void loadCommands();

  window.addEventListener('keydown', onPageKey, true);
});
onUnmounted(() => {
  lines.value = [];
  window.removeEventListener('keydown', onPageKey, true);
});


async function loadCommands(): Promise<void> {
  try {
    const r = await api.cli('help');
    const names = (r.lines ?? [])
      .map((l: string) => l.trim().split(/\s+/)[0])
      .filter((w: string) => /^[a-z?]+$/.test(w));
    cmdNames.value = [...new Set(names)];
  } catch {
    /* Engine offline: Skip completion */
  }
}


function complete(): void {
  const v = input.value;
  const firstEnd = v.indexOf(' ');
  const head = (firstEnd === -1 ? v : v.slice(0, firstEnd)).toLowerCase();
  const rest = firstEnd === -1 ? '' : v.slice(firstEnd);
  if (!head) return;
  const cands = cmdNames.value.filter((c) => c.startsWith(head));
  if (cands.length === 0) return;
  if (cands.length === 1) {
    input.value = firstEnd === -1 ? `${cands[0]} ` : `${cands[0]}${rest}`;
    return;
  }
  let prefix = cands[0];
  for (const c of cands) while (!c.startsWith(prefix)) prefix = prefix.slice(0, -1);
  if (prefix.length > head.length) input.value = firstEnd === -1 ? prefix : `${prefix}${rest}`;
}


function clearScreen(): void {
  lines.value = banner();
  scroll();
  focusInput();
}


function onPageKey(e: KeyboardEvent): void {
  const el = document.activeElement as HTMLElement | null;
  if (el && el !== inputEl.value && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable)) return;
  if (e.key === 'Tab') {
    e.preventDefault();
    focusInput();
    complete();
  } else if (e.ctrlKey && !e.shiftKey && !e.altKey && (e.key === 'l' || e.key === 'L')) {
    e.preventDefault();
    clearScreen();
  }
}

async function exec(cmd: string): Promise<void> {
  const line = cmd.trim();
  if (!line) return;
  lines.value = [...lines.value, `> ${line}`];
  history.value = [...history.value, line];
  hIdx.value = -1;
  input.value = '';

  if (line.toLowerCase() === 'clear') {
    lines.value = banner();
    focusInput();
    return;
  }
  scroll();
  try {
    const r = await api.cli(line);
    lines.value = [...lines.value, ...(r.lines ?? [])];
    if (!r.ok) lines.value = [...lines.value, t('console.failed')];
  } catch {
    lines.value = [...lines.value, t('console.failed')];
  }
  if (lines.value.length > 800) lines.value = lines.value.slice(-800);
  scroll();
  focusInput();
}

function onKey(e: KeyboardEvent): void {
  if (e.key === 'Enter') {
    e.preventDefault();
    void exec(input.value);
  } else if (e.key === 'ArrowUp') {
    e.preventDefault();
    if (history.value.length === 0) return;
    hIdx.value = hIdx.value < 0 ? history.value.length - 1 : Math.max(0, hIdx.value - 1);
    input.value = history.value[hIdx.value] ?? '';
    scroll();
  } else if (e.key === 'ArrowDown') {
    e.preventDefault();
    if (hIdx.value < 0) return;
    hIdx.value = hIdx.value + 1;
    if (hIdx.value >= history.value.length) {
      hIdx.value = -1;
      input.value = '';
    } else {
      input.value = history.value[hIdx.value] ?? '';
    }
    scroll();
  }
}


function lineColor(l: string): string {
  if (l.startsWith('>')) return 'var(--primary)';
  if (/error|failed|fail|失败|失敗|エラー|fallo/i.test(l)) return 'var(--bad)';
  if (/warn(ing)?|警告|エラー|aviso/i.test(l)) return 'var(--warn)';
  return 'inherit';
}
</script>

<template>
  <div class="page-host" style="height: 100%">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1 v-bubble="t('console.hint') + '\n' + t('console.history') + '\n' + t('console.keys')">{{ t('tab.console') }}</h1>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2); flex: 1; min-height: 0">
      <div class="x-gap">
        <button class="x-btn sm" @click="() => void exec('help')">help</button>
        <button class="x-btn sm" @click="() => void exec('status')">status</button>
        <button class="x-btn sm ghost" @click="clearScreen">{{ t('common.clear') }}</button>
        <span class="x-grow"></span>
        <span class="x-muted" style="font-size: 12px">
          {{ app.engineOnline ? t('nav.connected') : t('nav.waiting') }}
        </span>
      </div>


      <CardGrid view="console" :cards="CARDS" fill>
        <template #term>
          <div
            ref="box"
            class="x-card term"
            @click="focusInput"
          >
            <div v-for="(l, i) in lines" :key="i" class="term-line" :style="{ color: lineColor(l) }">{{ l }}</div>
            <div class="term-prompt">
              <span class="term-tag x-mono">hrm</span><span class="term-caret x-mono">></span>
              <input
                ref="inputEl"
                v-model="input"
                class="term-input x-mono"
                :placeholder="t('console.hint')"
                spellcheck="false"
                autocomplete="off"
                @keydown="onKey"
              />
            </div>
          </div>
        </template>
      </CardGrid>
    </div>
  </div>
</template>

<style scoped>
.term {
  height: 100%;
  min-height: 220px;
  overflow: auto;
  padding: var(--gap-2);
  font-size: 12.5px;
  line-height: 1.55;
  cursor: text;
  display: flex;
  flex-direction: column;
  align-items: stretch;
}
.term-line {
  white-space: pre-wrap;
  word-break: break-all;
  flex-shrink: 0;
}
.term-prompt {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-shrink: 0;
  padding-top: 2px;
}
.term-tag {
  color: var(--primary);
  font-weight: 600;
}
.term-caret {
  color: var(--primary);
}
.term-input {
  flex: 1;
  min-width: 60px;
  background: transparent;
  border: 0;
  outline: none;
  color: inherit;
  font-size: inherit;
  line-height: inherit;
  padding: 0;
  caret-color: var(--primary);
}
</style>
