<script setup lang="ts">








import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { api } from '../api';
import { useAppStore } from '../stores/app';
import XSwitch from '../components/XSwitch.vue';
import XSelect from '../components/XSelect.vue';
import XRange from '../components/XRange.vue';
import XDialog from '../components/XDialog.vue';
import CardGrid, { type GridCard } from '../components/CardGrid.vue';
import EditLayoutBtn from '../components/EditLayoutBtn.vue';

const { t } = useI18n();
const app = useAppStore();

/** #9 Layout Editor: This page card (non-administrator's rejection tipcard is conditioned and left outside the grid). */
const CARDS: GridCard[] = [
  { id: 'session', titleKey: 'web.session', span: 12, bodyStyle: 'padding: 0' },
  { id: 'config', titleKey: 'web.config', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'users', titleKey: 'web.users', span: 12, bodyStyle: 'display: flex; flex-direction: column; gap: var(--gap-2)' },
  { id: 'sessions', titleKey: 'web.sessions', span: 12, bodyStyle: 'padding: 0' },
];

const roleOpts = [
  { value: 'admin', label: t('web.role.admin') },
  { value: 'user', label: t('web.role.user') },
];

interface UserRow {
  username?: string;
  role?: string;
  tabs?: string[];
}
interface SessionRow {
  id?: string;
  tokenHint?: string;
  username?: string;
  role?: string;
  remoteIp?: string;
  created?: string;
  lastSeen?: string;
  self?: boolean;
}

interface RemoteCfg {
  enabled?: boolean;
  wanEnabled?: boolean;
  idleMinutes?: number;
  port?: number;
  boundAll?: boolean;
  sourceZone?: string;
  adminInitialized?: boolean;
  adminDefaultPassword?: boolean;
  wanReady?: boolean;
  note?: string;
  safeMode?: boolean;
  scheme?: 'http' | 'https';
  url?: string;
  certificateThumbprint?: string;
  requireHttpsForRemote?: boolean;
  restartRequired?: boolean;
}

const cfg = ref<RemoteCfg>({});
const users = ref<UserRow[]>([]);
const sessions = ref<SessionRow[]>([]);
const hint = ref('');

/** L666: session detail fields shown inside the card body (page-open time backs the local loopback session). */
const pageOpenedAt = `${new Date().toISOString()}`;
const selfSession = computed(() => sessions.value.find((s) => s.self));
const sessionLoginAt = computed(() => selfSession.value?.created ?? (app.remote.remote ? '' : pageOpenedAt));
const sessionIp = computed(() => selfSession.value?.remoteIp ?? '127.0.0.1');
const sessionZone = computed(() => (app.remote.remote ? app.remote.sourceZone || 'public' : 'loopback'));

let hydrating = true;

function flash(msg: string): void {
  hint.value = msg;
  window.setTimeout(() => (hint.value = ''), 1800);
}

async function load(): Promise<void> {
  try {
    const [c, u, s] = await Promise.all([
      api.webSecurityConfig(),
      api.remoteUsers(),
      api.remoteSessions(),
    ]);
    hydrating = true;
    cfg.value = (c.config ?? {}) as RemoteCfg;
    users.value = (u.users ?? []) as UserRow[];
    sessions.value = (s.sessions ?? []) as SessionRow[];
  } catch {
    /* Non-administrator/not login: backend 403, empty table maintained */
  } finally {

    window.setTimeout(() => (hydrating = false), 0);
  }
}

async function saveConfig(): Promise<void> {
  const r = (await api.webSecurityConfig({
    enabled: cfg.value.enabled,
    wanEnabled: cfg.value.wanEnabled,
    idleMinutes: Number(cfg.value.idleMinutes),
    scheme: cfg.value.scheme,
    certificateThumbprint: cfg.value.certificateThumbprint,
    requireHttpsForRemote: cfg.value.requireHttpsForRemote,
  })) as { ok?: boolean; config?: RemoteCfg };
  if (r.ok && r.config) {
    hydrating = true;
    cfg.value = r.config;
    window.setTimeout(() => (hydrating = false), 0);
  }
  flash(r.ok ? t('common.saved') : 'ERR');
}


let saveTimer = 0;
watch(
  cfg,
  () => {
    if (hydrating) return;
    const p = Number(cfg.value.port);
    if (!(p > 0 && p < 65536)) return;
    window.clearTimeout(saveTimer);
    saveTimer = window.setTimeout(() => void saveConfig(), 400);
  },
  { deep: true },
);


// ---- P6 WAN flow: the switch cannot save directly — the risk dialog (and, when needed, the
// admin password gate) must complete first. Weak/default passwords block saving server-side too.
const wanDlg = ref(false);
const wanMode = ref<'init' | 'pass' | 'confirm'>('confirm');
const wanUser = ref('');
const wanPass = ref('');
const wanPass2 = ref('');
const wanAck = ref(false);
const wanErr = ref('');
const wanBusy = ref(false);

function openWan(): void {
  if (cfg.value.wanEnabled) {
    // Turning WAN off is safe and needs no dialog.
    cfg.value.wanEnabled = false;
    return;
  }
  wanPass.value = '';
  wanPass2.value = '';
  wanAck.value = false;
  wanErr.value = '';
  wanMode.value = !cfg.value.adminInitialized ? 'init' : cfg.value.wanReady ? 'confirm' : 'pass';
  wanUser.value = wanMode.value === 'pass' ? (users.value.find((u) => u.role === 'admin')?.username ?? 'admin') : '';
  wanDlg.value = true;
}

async function submitWan(): Promise<void> {
  if (wanBusy.value) return;
  if (!wanAck.value) return;
  wanErr.value = '';
  if (wanMode.value !== 'confirm') {
    if (wanPass.value.length < 8 || !/[A-Za-z]/.test(wanPass.value) || !/\d/.test(wanPass.value)) {
      wanErr.value = t('remote.initweak');
      return;
    }
    if (wanPass.value !== wanPass2.value) {
      wanErr.value = t('web.err.mismatch');
      return;
    }
  }
  wanBusy.value = true;
  try {
    if (wanMode.value === 'init') {
      const r = (await api.remoteUser({ action: 'init', username: wanUser.value.trim(), password: wanPass.value })) as { ok?: boolean };
      if (!r.ok) throw new Error('init');
    } else if (wanMode.value === 'pass') {
      const r = (await api.remoteUser({ action: 'pass', username: wanUser.value, password: wanPass.value })) as { ok?: boolean };
      if (!r.ok) throw new Error('pass');
    }
    const r = (await api.remoteConfig({ wanEnabled: true })) as { ok?: boolean; config?: RemoteCfg };
    if (!r.ok) throw new Error('wan');
    if (r.config) {
      hydrating = true;
      cfg.value = r.config;
      window.setTimeout(() => (hydrating = false), 0);
    }
    wanDlg.value = false;
    flash(t('common.saved'));
    await load();
  } catch {
    wanErr.value = t('web.wanweak');
  } finally {
    wanBusy.value = false;
  }
}

type DlgMode = '' | 'add' | 'pass' | 'del';
const dlg = ref<DlgMode>('');
const dlgUser = ref('');
const dlgPass = ref('');
const dlgPass2 = ref('');
const dlgRole = ref('user');
const dlgTabs = ref('');
const dlgErr = ref('');
const dlgBusy = ref(false);

function openAdd(): void {
  dlg.value = 'add';
  dlgUser.value = '';
  dlgPass.value = '';
  dlgPass2.value = '';
  dlgRole.value = 'user';
  dlgTabs.value = '';
  dlgErr.value = '';
}

function openPass(u: UserRow): void {
  dlg.value = 'pass';
  dlgUser.value = u.username ?? '';
  dlgPass.value = '';
  dlgPass2.value = '';
  dlgErr.value = '';
}

function openDel(u: UserRow): void {
  dlg.value = 'del';
  dlgUser.value = u.username ?? '';
  dlgErr.value = '';
}

function closeDlg(): void {
  dlg.value = '';
  // The cipher doesn't leave a moment in the memory.
  dlgPass.value = '';
  dlgPass2.value = '';
}

async function submitDlg(): Promise<void> {
  if (dlgBusy.value) return;
  dlgErr.value = '';
  if (dlg.value === 'del') {
    dlgBusy.value = true;
    try {
      const r = await api.remoteUser({ action: 'remove', username: dlgUser.value });
      if (!r.ok) throw new Error('fail');
      closeDlg();
      await load();
    } catch {
      dlgErr.value = t('web.err.lastadmin');
    } finally {
      dlgBusy.value = false;
    }
    return;
  }

  if (dlg.value === 'add' && dlgUser.value.trim() === '') {
    dlgErr.value = t('web.err.name');
    return;
  }
  if (dlgPass.value.length < 4) {
    dlgErr.value = t('web.err.short');
    return;
  }
  if (dlgPass.value !== dlgPass2.value) {
    dlgErr.value = t('web.err.mismatch');
    return;
  }
  dlgBusy.value = true;
  try {
    const payload =
      dlg.value === 'add'
        ? {
            action: 'add',
            username: dlgUser.value.trim(),
            password: dlgPass.value,
            role: dlgRole.value,
            tabs: dlgTabs.value.split(',').map((s) => s.trim()).filter(Boolean),
          }
        : { action: 'pass', username: dlgUser.value, password: dlgPass.value };
    const r = await api.remoteUser(payload);
    if (!r.ok) throw new Error('fail');
    closeDlg();
    flash(t('common.saved'));
    await load();
  } catch {
    dlgErr.value = t('web.err.failed');
  } finally {
    dlgBusy.value = false;
  }
}

async function setRole(u: UserRow, role: string): Promise<void> {
  await api.remoteUser({ action: 'role', username: u.username, role });
  flash(t('common.saved'));
  await load();
}

async function setTabs(u: UserRow, tabsText: string): Promise<void> {
  const tabs = tabsText.split(',').map((s) => s.trim()).filter(Boolean);
  await api.remoteUser({ action: 'tabs', username: u.username, tabs });
  flash(t('common.saved'));
  await load();
}

async function kick(s: SessionRow): Promise<void> {
  if (!s.id) return;
  await api.remoteKick(s.id);
  await load();
}

function fmtDate(iso?: string): string {
  if (!iso) return '—';
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString();
}

let timer = 0;
onMounted(() => {
  void load();
  timer = window.setInterval(() => void load(), 5000);
});
onUnmounted(() => {
  window.clearInterval(timer);
  window.clearTimeout(saveTimer);
});
</script>

<template>
  <div class="page-host">
    <header class="page-head with-actions">
      <div class="page-head-title">
        <h1>{{ t('tab.web') }}</h1>
        <p class="page-desc">{{ t('web.hint') }}</p>
      </div>
      <!-- No grid for non-administrator, buttons hidden with grid -->
      <EditLayoutBtn v-if="app.remote.admin !== false" />
    </header>

    <div v-if="app.remote.admin === false" class="x-card">
      <div class="x-card-body x-muted">{{ t('web.denied') }}</div>
    </div>
    <div v-else class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <CardGrid view="web" :cards="CARDS">
        <!-- Current Session (L667: title row keeps only the title; all details live in the body) -->
        <template #head-session>
          {{ t('web.session') }}
          <span v-if="hint" style="color: var(--good); font-weight: 400; font-size: 12px">· {{ hint }}</span>
        </template>
        <template #session>
          <div class="x-row" style="flex-wrap: wrap; padding: var(--gap-2) var(--gap-3); row-gap: 4px">
            <span class="x-muted" style="font-size: 12.5px">{{ t('web.username') }}:</span>
            <span class="x-mono" style="font-size: 12.5px; font-weight: 600">
              {{ app.remote.username || 'local' }}
            </span>
            <span class="x-muted" style="font-size: 12.5px">{{ t('web.role') }}:</span>
            <span style="font-size: 12.5px">{{ app.remote.role === 'admin' ? t('web.role.admin') : t('web.role.user') }}</span>
            <span class="x-muted" style="font-size: 12.5px">{{ t('web.ip') }}:</span>
            <span class="x-mono" style="font-size: 12.5px">{{ sessionIp }}</span>
            <span class="x-muted" style="font-size: 12.5px">{{ t('web.zone') }}:</span>
            <span class="x-mono" style="font-size: 12.5px">{{ t('web.zone.' + sessionZone) }}</span>
            <span v-if="sessionLoginAt" class="x-muted" style="font-size: 12.5px">{{ t('web.loginat') }}:</span>
            <span v-if="sessionLoginAt" class="x-mono" style="font-size: 12.5px">{{ fmtDate(sessionLoginAt) }}</span>
            <span class="x-muted" style="font-size: 12.5px">{{ t('web.bootat') }}:</span>
            <span class="x-mono" style="font-size: 12.5px">{{ fmtDate(app.appInfo?.startTime) }}</span>
            <span class="x-grow"></span>
            <button v-if="app.remote.remote" class="x-btn sm ghost" @click="() => void app.logout()">
              {{ t('web.logout') }}
            </button>
          </div>
        </template>

        <!-- Access configuration (P6 single listener: LAN/WAN decided per request source zone) -->
        <template #head-config>
          <span v-bubble="t('common.autosave')">{{ t('web.config') }}</span>
        </template>
        <template #config>
          <div class="x-muted" style="font-size: 11.5px">{{ t('web.portinfo') }}</div>
          <div v-if="!cfg.adminInitialized" class="x-muted" style="font-size: 12px">
            {{ t('web.initlocal') }}
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">Scheme</label>
            <XSelect
              :model-value="cfg.scheme ?? 'http'"
              style="width: 120px"
              :items="[{ value: 'http', label: 'HTTP' }, { value: 'https', label: 'HTTPS' }]"
              @update:model-value="(v) => (cfg.scheme = String(v) as 'http' | 'https')"
            />
            <input
              v-model="cfg.certificateThumbprint"
              class="x-input x-mono x-grow"
              style="min-width: 260px"
              placeholder="Certificate thumbprint (CurrentUser/LocalMachine My)"
            />
            <XSwitch
              :model-value="cfg.requireHttpsForRemote !== false"
              label="Require HTTPS for remote"
              @update:model-value="(v) => (cfg.requireHttpsForRemote = v)"
            />
          </div>
          <div class="x-muted x-mono" style="font-size: 11px">
            {{ cfg.url }}<span v-if="cfg.restartRequired"> · Restart required</span>
          </div>
          <div v-if="cfg.scheme === 'https'" class="x-muted x-mono" style="font-size: 11px">
            HTTP.sys binding must be configured by an administrator: netsh http add sslcert ipport=0.0.0.0:{{ cfg.port }} certhash=THUMBPRINT appid={YOUR-APP-GUID}
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <XSwitch
              :model-value="!!cfg.enabled"
              :label="t('web.lan')"
              :disabled="!cfg.adminInitialized"
              style="min-width: 200px"
              v-bubble="t('web.lanhint')"
              @update:model-value="(v) => (cfg.enabled = v)"
            />
            <XSwitch
              :model-value="!!cfg.wanEnabled"
              :label="t('web.wan')"
              :disabled="!cfg.adminInitialized"
              v-bubble="t('web.wanhint')"
              @update:model-value="() => openWan()"
            />
          </div>
          <!-- Source-zone and bind state: loopback always works; LAN/WAN visibility depends on the single listener's bind. -->
          <div class="x-gap" style="flex-wrap: wrap; font-size: 12px">
            <span class="x-muted">{{ t('web.zone') }}:</span>
            <span class="x-mono">{{ t('web.zone.' + (cfg.sourceZone ?? 'loopback')) }}</span>
            <span class="x-muted">·</span>
            <span :style="{ color: cfg.boundAll ? 'var(--good)' : 'var(--warn)' }">
              {{ cfg.boundAll ? t('web.boundall') : t('web.boundloopback') }}
            </span>
            <span v-if="cfg.port" class="x-muted x-mono">:{{ cfg.port }}</span>
          </div>
          <!-- Admin password state gates the WAN switch. -->
          <div class="x-gap" style="flex-wrap: wrap; font-size: 12px">
            <span class="x-muted">{{ t('web.adminpass') }}:</span>
            <span v-if="!cfg.adminInitialized" style="color: var(--warn)">{{ t('web.noadmin') }}</span>
            <span v-else-if="cfg.adminDefaultPassword" style="color: var(--destructive)">{{ t('web.adminpassdefault') }}</span>
            <span v-else style="color: var(--good)">{{ t('web.adminpassok') }}</span>
          </div>
          <div class="x-gap" style="flex-wrap: wrap">
            <label class="x-muted" style="font-size: 12.5px">{{ t('web.idle') }}</label>
            <XRange
              :model-value="cfg.idleMinutes ?? 240"
              :min="0"
              :max="1440"
              :step="10"
              :hard-min="0"
              :default="240"
              suffix="min"
              @update:model-value="(v) => (cfg.idleMinutes = v)"
            />
          </div>
          <div v-if="!cfg.boundAll" class="x-muted x-mono" style="font-size: 11px">{{ cfg.note }}</div>
        </template>

        <!-- Library (L666: add button moved into the body; header keeps only title + count) -->
        <template #head-users>
          {{ t('web.users') }}
          <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ users.length }}</span>
        </template>
        <template #users>
          <div class="x-gap" style="padding-top: 4px">
            <button class="x-btn sm primary" @click="openAdd">{{ t('web.adduser') }}</button>
          </div>
          <div v-for="(u, i) in users" :key="i" class="x-gap" style="flex-wrap: wrap; padding-top: 4px">
            <span class="x-mono" style="min-width: 140px">{{ u.username }}</span>
            <XSelect
              style="width: 130px"
              :model-value="u.role ?? 'user'"
              :items="roleOpts"
              v-bubble="t('web.usershint')"
              @update:model-value="(v) => void setRole(u, String(v))"
            />
            <input
              :value="(u.tabs ?? []).join(',')"
              class="x-input x-mono x-grow"
              style="min-width: 180px"
              :placeholder="t('web.tabs')"
              @change="setTabs(u, ($event.target as HTMLInputElement).value)"
            />
            <button class="x-btn sm" @click="openPass(u)">{{ t('web.changepass') }}</button>
            <button class="x-btn sm danger" @click="openDel(u)">{{ t('web.deluser') }}</button>
          </div>
        </template>

        <!-- Session -->
        <template #head-sessions>
          {{ t('web.sessions') }}
          <span class="x-muted" style="font-weight: 400; font-size: 12px">· {{ sessions.length }}</span>
        </template>
        <template #sessions>
          <div v-for="(s, i) in sessions" :key="i" class="x-row" style="flex-wrap: wrap">
            <span class="x-mono" style="min-width: 120px">{{ s.username }}</span>
            <span class="x-muted" style="min-width: 70px">{{ s.role }}</span>
            <span class="x-mono x-muted" style="min-width: 90px">{{ s.tokenHint }}…</span>
            <span class="x-muted" style="min-width: 110px">{{ t('web.ip') }}: {{ s.remoteIp }}</span>
            <span class="x-muted x-grow" style="text-align: right; white-space: nowrap">
              {{ t('web.last') }}: {{ fmtDate(s.lastSeen) }}
            </span>
            <span v-if="s.self" class="x-muted" style="color: var(--good)">· {{ t('web.self') }}</span>
            <button v-if="!s.self" class="x-btn sm danger" @click="() => void kick(s)">{{ t('web.kick') }}</button>
          </div>
          <div v-if="sessions.length === 0" class="x-muted" style="padding: var(--gap-3)">{{ t('common.none') }}</div>
        </template>
      </CardGrid>
    </div>


    <XDialog
      :open="wanDlg"
      :title="t('web.wantitle')"
      :desc="t('web.wanhint')"
      size="sm"
      :dismissable="!wanBusy"
      @close="wanDlg = false"
    >
      <template v-if="wanMode !== 'confirm'">
        <label v-if="wanMode === 'init'" class="dlg-label">{{ t('web.username') }}</label>
        <input
          v-if="wanMode === 'init'"
          v-model="wanUser"
          class="x-input"
          style="width: 100%"
          autocomplete="username"
        />
        <label class="dlg-label">{{ t('web.wanpass') }}</label>
        <input v-model="wanPass" class="x-input" type="password" style="width: 100%" autocomplete="new-password" />
        <label class="dlg-label">{{ t('web.wanpass2') }}</label>
        <input v-model="wanPass2" class="x-input" type="password" style="width: 100%" autocomplete="new-password" />
      </template>
      <label class="x-gap" style="align-items: flex-start; margin-top: var(--gap-2)">
        <input v-model="wanAck" type="checkbox" />
        <span>{{ t('web.wanconfirm') }}</span>
      </label>
      <p v-if="wanErr" class="dlg-err">{{ wanErr }}</p>
      <template #actions>
        <button class="x-btn sm primary" :disabled="wanBusy || !wanAck" @click="() => void submitWan()">
          {{ t('web.wanok') }}
        </button>
        <button class="x-btn sm ghost" :disabled="wanBusy" @click="wanDlg = false">{{ t('common.cancel') }}</button>
      </template>
    </XDialog>

    <XDialog
      :open="dlg !== ''"
      :title="dlg === 'add' ? t('web.adduser') : dlg === 'pass' ? t('web.changepass') : t('web.deluser')"
      :desc="dlg === 'del' ? t('web.delhint', { name: dlgUser }) : t('web.passhint')"
      size="sm"
      :dismissable="!dlgBusy"
      @close="closeDlg"
    >
      <template v-if="dlg === 'add'">
        <label class="dlg-label">{{ t('web.username') }}</label>
        <input v-model="dlgUser" class="x-input" style="width: 100%" autocomplete="off" />
      </template>

      <template v-if="dlg !== 'del'">
        <label class="dlg-label">{{ t('web.password') }}</label>
        <input v-model="dlgPass" class="x-input" type="password" style="width: 100%" autocomplete="new-password" />
        <label class="dlg-label">{{ t('web.password2') }}</label>
        <input v-model="dlgPass2" class="x-input" type="password" style="width: 100%" autocomplete="new-password" />
      </template>

      <template v-if="dlg === 'add'">
        <label class="dlg-label">{{ t('web.role') }}</label>
        <XSelect v-model="dlgRole" style="width: 100%" :items="roleOpts" />
        <label class="dlg-label">{{ t('web.tabs') }}</label>
        <input v-model="dlgTabs" class="x-input x-mono" style="width: 100%" placeholder="heartbeat,osc,pusher" />
      </template>

      <p v-if="dlgErr" class="dlg-err">{{ dlgErr }}</p>

      <template #actions>
        <button
          class="x-btn sm"
          :class="dlg === 'del' ? 'danger' : 'primary'"
          :disabled="dlgBusy"
          @click="() => void submitDlg()"
        >
          {{ dlg === 'del' ? t('web.deluser') : t('common.save') }}
        </button>
        <button class="x-btn sm ghost" :disabled="dlgBusy" @click="closeDlg">{{ t('common.cancel') }}</button>
      </template>
    </XDialog>
  </div>
</template>

<style scoped>
.dlg-label {
  margin-top: var(--gap-1);
  font-size: 12.5px;
  color: var(--muted-foreground);
}
.dlg-err {
  margin: var(--gap-1) 0 0;
  font-size: 12.5px;
  color: var(--bad);
}
</style>
