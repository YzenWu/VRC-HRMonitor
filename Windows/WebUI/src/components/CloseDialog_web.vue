<script setup lang="ts">








import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { LogOut, Minimize2, Save } from 'lucide-vue-next';
import { api } from '../api';
import { onShellCloseRequest, postShell } from '../shell';
import { dirtyCount, saveAllDirty } from '../dirty';
import XDialog from './XDialog.vue';

const { t } = useI18n();

const open = ref(false);
const dontAsk = ref(false);
const busy = ref(false);
/** If you have unsaved changes, go ahead and "do you want to save them"? */
const askSave = ref(false);
const pending = ref(0);


const action = ref<'ask' | 'exit' | 'tray'>('ask');

const title = computed(() => (askSave.value ? t('close.unsaved') : t('close.title')));

onMounted(() => {
  void api
    .config()
    .then((c) => {
      const v = String((c as { ui?: { closeAction?: string } }).ui?.closeAction ?? 'ask');
      if (v === 'exit' || v === 'tray' || v === 'ask') action.value = v;
    })
    .catch(() => {
      /* If you can't get an engine, do it as you ask. */
    });

  onShellCloseRequest(() => {
    pending.value = dirtyCount();
    // Remember the selection without saving the contents: direct execution, no further disturbance
    if (action.value !== 'ask' && pending.value === 0) {
      void run(action.value);
      return;
    }
    askSave.value = pending.value > 0;
    dontAsk.value = false;
    open.value = true;
    // The themed dialog is now interactive. Cancel the host's unresponsive-page fallback;
    // the user may take as long as needed to choose an action.
    postShell('win.close-ready');
  });
});

/** Saves all unsaved changes and moves to the second step. */
async function saveThenAsk(): Promise<void> {
  busy.value = true;
  try {
    await saveAllDirty();
  } finally {
    busy.value = false;
  }
  pending.value = dirtyCount();
  askSave.value = false;
  // Save and execute directly if a choice has been recorded before
  if (action.value !== 'ask') {
    void run(action.value);
    open.value = false;
  }
}

/** Discards unsaved changes, enters the second step. */
function discardThenAsk(): void {
  askSave.value = false;
  if (action.value !== 'ask') {
    void run(action.value);
    open.value = false;
  }
}

function cancelClose(): void {
  open.value = false;
  postShell('win.close-cancel');
}

async function run(next: 'exit' | 'tray'): Promise<void> {
  if (busy.value) return;
  busy.value = true;
  let completed = false;
  try {
    if (dontAsk.value && action.value !== next) {
      action.value = next;
      await api.settings({ ui: { closeAction: next } }).catch(() => undefined);
    }
    if (next === 'tray') {
      postShell('win.tray');
    } else {
      try {
        const result = await api.shutdown();
        if (result.ok === false) return;
      } catch {
        return;
      }
      postShell('win.exit');
    }
    completed = true;
  } finally {
    busy.value = false;
    if (completed) open.value = false;
  }
}
</script>

<template>
  <XDialog :open="open" :title="title" size="md" @close="cancelClose">
    <!-- Step 1: Unsaved Changes -->
    <template v-if="askSave">
      <div class="x-muted cb-sub">{{ t('close.unsavedhint', { n: pending }) }}</div>
      <div class="cb-actions">
        <button class="x-btn sm primary" :disabled="busy" @click="() => void saveThenAsk()">
          <Save class="x-btn-ico" />
          {{ t('close.save') }}
        </button>
        <button class="x-btn sm" :disabled="busy" @click="discardThenAsk">{{ t('close.discard') }}</button>
        <button class="x-btn sm ghost" :disabled="busy" @click="cancelClose">{{ t('common.cancel') }}</button>
      </div>
    </template>

    <!-- Step 2: Exit or minimization -->
    <template v-else>
      <div class="x-muted cb-sub">{{ t('close.hint') }}</div>
      <div class="cb-actions">
        <button class="x-btn sm primary" :disabled="busy" @click="() => void run('tray')">
          <Minimize2 class="x-btn-ico" />
          {{ t('close.tray') }}
        </button>
        <button class="x-btn sm danger" :disabled="busy" @click="() => void run('exit')">
          <LogOut class="x-btn-ico" />
          {{ t('close.exit') }}
        </button>
        <button class="x-btn sm ghost" :disabled="busy" @click="cancelClose">{{ t('common.cancel') }}</button>
      </div>
      <label class="cb-remember">
        <input v-model="dontAsk" type="checkbox" />
        {{ t('close.dontask') }}
      </label>
    </template>
  </XDialog>
</template>

<style scoped>
.cb-sub {
  font-size: 12.5px;
}
.cb-actions {
  display: flex;
  gap: var(--gap-2);
  flex-wrap: wrap;
  margin-top: var(--gap-1);
}
.cb-remember {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 12px;
  color: var(--muted-foreground);
  cursor: pointer;
}
</style>
