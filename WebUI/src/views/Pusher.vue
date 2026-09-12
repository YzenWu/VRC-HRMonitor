<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { Plus, Radio, Send, Trash2 } from 'lucide-vue-next';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import { useAutoSave } from '../composables/useAutoSave';
import { markSaved } from '../stores/notify';
import XSelect from '../components/XSelect.vue';
import XSwitch from '../components/XSwitch.vue';
import XRange from '../components/XRange.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';
import VrchatStatus from '../components/VrchatStatus.vue';

const { t } = useI18n();
const app = useAppStore();

/** #9 Layout Editor: This page card (connection + statistical toolbar left outside the grid; original status card unheaded, grided and recapitulated). */
const CARDS: GridCard[] = [
  { id: 'vrstatus', titleKey: 'vrc.status', span: 12, style: 'padding: var(--gap-2) var(--gap-3)', bodyStyle: 'padding: 0' },
  { id: 'osc', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'preview', titleKey: 'push.preview', span: 12, bodyStyle: 'padding: 0' },
  { id: 'custom', titleKey: 'push.custom', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'webhook', span: 12, bodyStyle: 'padding: 0' },
];


onMounted(() => {
  void refreshPreview();
  previewInt = window.setInterval(() => void refreshPreview(), 1000);
});
onBeforeUnmount(() => {
  if (previewInt !== null) window.clearInterval(previewInt);
  if (previewTimer !== null) window.clearTimeout(previewTimer);
});






const custom = ref<{
  address: string;
  text: string;
  kind: string;
  flag: boolean;
  pauseSec: number;
}>({ address: '/chatbox/input', text: '', kind: 'text', flag: true, pauseSec: 3 });
const customHint = ref('');
async function customSend(): Promise<void> {
  if (custom.value.kind !== 'bool' && !custom.value.text.trim()) return;
  const r = (await api.oscCustom({
    ip: form.value.ip,
    port: form.value.port,
    address: custom.value.address,
    text: custom.value.text,
    kind: custom.value.kind as 'text' | 'float' | 'bool',
    flag: custom.value.flag,
    pauseMs: custom.value.pauseSec * 1000,
  })) as { ok?: boolean };
  customHint.value = r.ok ? t('common.saved') : 'FAIL';
  window.setTimeout(() => (customHint.value = ''), 1600);
}

const KIND_OPTS = computed(() => [
  { value: 'text' as const, label: t('push.kindtext') },
  { value: 'float' as const, label: t('push.kindfloat') },
  { value: 'bool' as const, label: t('push.kindbool') },
]);


const form = ref({
  ip: '',
  port: '9000',
  address: '',
  intervalMs: '1000',
  receivePort: '9001',
  autoStart: false,
  template: '',
});
const hint = ref('');

const filled = ref(false);

watch(
  () => app.osc,
  (o) => {
    if (!o || filled.value) return;
    filled.value = true;
    form.value = {
      ip: o.ip,
      port: String(o.port ?? ''),
      address: o.address,
      intervalMs: String(o.intervalMs ?? ''),
      receivePort: String(o.receivePort ?? ''),
      autoStart: !!o.autoStart,
      template: o.template,
    };
  },
  { immediate: true },
);

async function save(): Promise<void> {
  await api.oscConfig({ ...form.value });
  await app.sync();
  markSaved(); // Basebar Unified Saved
}


const auto = useAutoSave('pusher-osc', () => form.value, save);
watch(
  filled,
  (v) => {
    if (v) auto.hydrated();
  },
  { immediate: true },
);


const previewText = ref('');
let previewTimer: number | null = null;
let previewInt: number | null = null;

/** E1: Unicode code-point count of the rendered preview (a surrogate pair such as emoji counts as one). */
const previewChars = computed(() => (previewText.value ? Array.from(previewText.value).length : 0));

async function refreshPreview(): Promise<void> {
  const tpl = form.value.template;
  if (!tpl.trim()) {
    previewText.value = '';
    return;
  }
  if (!app.engineOnline) return; // Engine Offline: Keep old previews to avoid triggers of break-up determinations per second
  try {
    const r = (await api.sysinfoQuery(tpl)) as { ok?: boolean; text?: string };
    previewText.value = r?.text ?? '';
  } catch {
    /* Keep old previews when engine cannot reach */
  }
}
watch(
  () => form.value.template,
  () => {
    if (previewTimer !== null) window.clearTimeout(previewTimer);
    previewTimer = window.setTimeout(() => {
      previewTimer = null;
      void refreshPreview();
    }, 400);
  },
);

async function test(): Promise<void> {

  const r = (await api.oscTest(form.value.ip, form.value.port, form.value.address, 'HeartRateMonitor OSC test')) as {
    ok?: boolean;
  };
  flash(`${t('common.test')} ${r.ok ? 'OK' : 'FAIL'}`);
}

function flash(msg: string): void {
  hint.value = msg;
  window.setTimeout(() => (hint.value = ''), 1600);
}


interface Hook {
  name?: string;
  url?: string;
  headers?: string;
  body?: string;
  triggers?: string[];
  enabled?: boolean;
  [k: string]: unknown;
}
const hooks = ref<Hook[]>([]);
const loadedHooks = ref(false);
const testResult = ref('');

async function loadHooks(): Promise<void> {
  try {
    const r = (await api.webhooks()) as { list?: Hook[] };
    hooks.value = r.list ?? [];
  } catch {
    hooks.value = [];
  }
  loadedHooks.value = true;
}
void loadHooks();


async function hookAction(action: string, item?: Hook, index = -1): Promise<void> {
  const r = (await api.webhook({ action, item, index })) as { ok?: boolean; status?: number; error?: string };
  if (action === 'test') {
    testResult.value = r.ok ? `HTTP ${r.status}` : (r.error ?? 'fail');
    window.setTimeout(() => (testResult.value = ''), 2600);
    return;
  }
  await loadHooks();
}

const addHook = () =>
  void hookAction('add', {
    name: 'new-hook',
    url: 'http://127.0.0.1/hook',
    headers: '{"Content-Type":"application/json"}',
    body: '{"bpm":{bpm}}',
    triggers: ['heart_rate_updated'],
    enabled: false,
  });

const stats = computed(() => ({
  sent: app.osc?.sent ?? 0,
  fail: app.osc?.fail ?? 0,
  recv: app.osc?.recv ?? 0,
}));
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1 v-bubble="'OSC → VRChat · Webhook ' + t('api.webhookhint')">{{ t('tab.pusher') }}</h1>
      </div>
      <EditLayoutBtn />
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <!-- Connect + Statistics (toolbar, no grid) -->
      <div class="x-gap" style="flex-wrap: wrap">
        <button class="x-btn primary" @click="() => void api.oscConnect(!app.osc?.connected).then(() => app.sync())">
          <Radio class="x-btn-ico" />
          {{ app.osc?.connected ? t('osc.disconnect') : t('osc.connect') }}
        </button>
        <button class="x-btn" @click="() => void test()">
          <Send class="x-btn-ico" />
          {{ t('common.test') }}
        </button>
        <span class="x-muted" style="font-size: 12px">
          sent {{ stats.sent }} · fail {{ stats.fail }} · recv {{ stats.recv }}
        </span>
        <span v-if="hint" style="font-size: 12px; color: var(--good)">{{ hint }}</span>
      </div>

      <CardGrid view="pusher" :cards="CARDS">

        <template #vrstatus>
          <VrchatStatus />
        </template>


        <template #head-osc>
          <span v-bubble="t('common.autosave')">OSC</span>
        </template>
        <template #osc>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch
              v-model="form.autoStart"
              :label="t('osc.autostart')"
              v-bubble="t('osc.autostarthint')"
            />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">IP</label>
            <input v-model="form.ip" class="x-input" style="width: 150px" data-default="127.0.0.1" />
            <label class="x-muted" style="font-size: 12.5px">Port</label>
            <XRange
              :model-value="Number(form.port)"
              :min="1024"
              :max="65535"
              :step="1"
              :hard-min="1"
              :hard-max="65535"
              :default="9000"
              @update:model-value="(v) => (form.port = String(v))"
            />
            <label class="x-muted" style="font-size: 12.5px">Recv</label>
            <XRange
              :model-value="Number(form.receivePort)"
              :min="1024"
              :max="65535"
              :step="1"
              :hard-min="1"
              :hard-max="65535"
              :default="9001"
              @update:model-value="(v) => (form.receivePort = String(v))"
            />
            <label class="x-muted" style="font-size: 12.5px">{{ t('api.interval') }}</label>
            <XRange
              :model-value="Number(form.intervalMs)"
              :min="20"
              :max="5000"
              :step="10"
              :hard-min="20"
              :default="1000"
              suffix="ms"
              @update:model-value="(v) => (form.intervalMs = String(v))"
            />
          </div>
          <div class="x-gap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">Address</label>
            <input v-model="form.address" class="x-input x-grow x-mono" data-default="/chatbox/input" />
          </div>
          <div>
            <div class="x-muted" style="font-size: 12.5px; margin-bottom: var(--gap-1)">
              {{ t('common.value') }} · {{ '{变量名}' }}
            </div>
            <textarea
              v-model="form.template"
              class="x-input x-mono"
              rows="7"
              style="width: 100%"
            />
          </div>
        </template>

        <!-- Template Preview (#4: Real-time rendering {Varial}, refreshing every second) -->
        <template #head-preview>
          <span v-bubble="t('push.previewhint')">{{ t('push.preview') }}</span>
        </template>
        <template #preview>
          <div
            v-if="previewText"
            class="x-mono x-grow"
            style="padding: var(--gap-2) var(--gap-3); white-space: pre-wrap; word-break: break-word; line-height: 1.6; font-size: 12.5px; max-height: 220px; overflow: auto"
          >{{ previewText }}</div>
          <div v-else class="x-muted" style="padding: var(--gap-3); font-size: 12.5px">
            {{ t('push.previewempty') }}
          </div>
          <!-- E1: ChatBox limit hint — warn past 134 of the 144 cap, never blocks sending. -->
          <div
            class="x-gap"
            style="padding: 0 var(--gap-3) var(--gap-2); font-size: 11.5px; align-items: center"
          >
            <span class="x-mono" :style="{ color: previewChars > 134 ? 'var(--warn)' : 'var(--muted-foreground)' }">
              {{ previewChars }}/144
            </span>
            <span v-if="previewChars > 134" style="color: var(--warn)">{{ t('push.charwarn') }}</span>
          </div>
        </template>

        <!-- Custom Send (one-time text/parameter; pause-second defense template covered per second) -->
        <template #head-custom>
          {{ t('push.custom') }}
          <span v-if="customHint" class="x-muted" style="font-size: 12px; font-weight: 400">· {{ customHint }}</span>
        </template>
        <template #custom>
          <div class="x-gap">
            <label class="x-muted" style="font-size: 12.5px; min-width: 64px">Address</label>
            <input v-model="custom.address" class="x-input x-mono x-grow" data-default="/chatbox/input" />
            <label class="x-muted" style="font-size: 12.5px">{{ t('push.kindlabel') }}</label>
            <XSelect v-model="custom.kind" :items="KIND_OPTS" style="width: 132px" />
          </div>

          <textarea
            v-if="custom.kind === 'text'"
            v-model="custom.text"
            class="x-input x-mono"
            rows="3"
            style="width: 100%; height: auto; padding: var(--gap-2); resize: vertical; line-height: 1.5"
          />
          <div v-else-if="custom.kind === 'float'" class="x-gap">
            <input
              v-model="custom.text"
              class="x-input x-mono x-grow"
              placeholder="0.5 · {BPM} · {CPU_USAGE}"
              data-default="0"
              v-bubble="t('push.floatval')"
            />
          </div>
          <div v-else class="x-gap">
            <span class="x-muted" style="font-size: 12.5px">{{ t('push.boolval') }}</span>
            <XSwitch v-model="custom.flag" />
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">{{ t('push.pause') }}</label>
            <XRange
              :model-value="custom.pauseSec"
              :min="0"
              :max="120"
              :step="1"
              :hard-min="0"
              :default="0"
              suffix="s"
              @update:model-value="(v) => (custom.pauseSec = v)"
            />
            <button
              class="x-btn primary"
              v-bubble="t('push.customhint')"
              :disabled="custom.kind !== 'bool' && !custom.text.trim()"
              @click="() => void customSend()"
            >
              <Send class="x-btn-ico" />
              {{ t('push.custom') }}
            </button>
          </div>
        </template>

        <!-- Webhook -->
        <template #head-webhook>
          Webhook
          <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ hooks.length }}</span>
          <span v-if="testResult" class="x-mono" style="font-size: 12px; color: var(--good)">{{ testResult }}</span>
          <span class="x-grow"></span>
          <button class="x-btn sm" @click="addHook">
            <Plus class="x-btn-ico" />
            {{ t('common.add') }}
          </button>
        </template>
        <template #webhook>
          <div v-if="loadedHooks && hooks.length === 0" class="x-muted" style="padding: var(--gap-3)">
            {{ t('common.none') }}
          </div>
          <div v-for="(h, i) in hooks" :key="i" class="x-row">
            <span
              :style="{
                width: '7px',
                height: '7px',
                borderRadius: '50%',
                background: h.enabled ? 'var(--good)' : 'var(--muted-foreground)',
                flexShrink: 0,
              }"
            />
            <span style="min-width: 120px; font-weight: 500">{{ h.name || '(unnamed)' }}</span>
            <span class="x-mono x-grow" style="overflow: hidden; text-overflow: ellipsis; white-space: nowrap">
              {{ h.url }}
            </span>
            <span class="x-muted" style="min-width: 150px">{{ (h.triggers ?? []).join(', ') }}</span>
            <button class="x-btn sm" @click="() => void hookAction('update', { ...h, enabled: !h.enabled }, i)">
              {{ h.enabled ? t('common.disabled') : t('common.enabled') }}
            </button>
            <button class="x-btn sm ghost" @click="() => void hookAction('test', h, i)">{{ t('common.test') }}</button>
            <button class="x-btn sm danger" @click="() => void hookAction('delete', h, i)">
              <Trash2 class="x-btn-ico" />
            </button>
          </div>
        </template>
      </CardGrid>
    </div>
  </div>
</template>
