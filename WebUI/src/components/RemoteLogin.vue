<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { Activity, Loader2, LogIn, ShieldAlert } from 'lucide-vue-next';
import { api } from '../api';
import { useAppStore } from '../stores/app';

const { t } = useI18n();
const app = useAppStore();
const username = ref('');
const password = ref('');
const err = ref('');
const busy = ref(false);
/** P6: login returned mustChangePassword — the default password must be migrated before the session is usable. */
const mustChange = ref(false);
const newPass = ref('');
const newPass2 = ref('');

async function submit(): Promise<void> {
  if (busy.value) return;
  busy.value = true;
  err.value = '';
  try {
    const r = await api.remoteLogin(username.value.trim(), password.value);
    if (!r.ok) {
      // Empty user store: the login doubles as initialization; a weak password explains the refusal.
      err.value = r.init ? t('remote.initweak') : t('remote.err');
      return;
    }
    if (r.mustChangePassword) {
      mustChange.value = true;
      password.value = '';
      return;
    }
    await app.afterRemoteLogin();
    password.value = '';
  } catch {
    err.value = t('remote.err');
  } finally {
    busy.value = false;
  }
}

async function submitChange(): Promise<void> {
  if (busy.value) return;
  if (newPass.value.length < 8 || !/[A-Za-z]/.test(newPass.value) || !/\d/.test(newPass.value)) {
    err.value = t('remote.initweak');
    return;
  }
  if (newPass.value !== newPass2.value) {
    err.value = t('web.err.mismatch');
    return;
  }
  busy.value = true;
  err.value = '';
  try {
    const r = await api.remoteUser({ action: 'pass', username: username.value.trim(), password: newPass.value });
    if (!r.ok) throw new Error('fail');
    mustChange.value = false;
    newPass.value = '';
    newPass2.value = '';
    await app.afterRemoteLogin();
  } catch {
    err.value = t('remote.err');
  } finally {
    busy.value = false;
  }
}
</script>

<template>
  <Teleport to="body">
    <Transition name="rm">
      <div v-if="app.loginNeeded" class="rm-scrim">
        <form v-if="!mustChange" class="rm-card x-card" @submit.prevent="submit">

          <div class="rm-brand">
            <Activity class="rm-logo" />
            <span class="rm-app">HeartRateMonitor</span>
          </div>

          <h2 class="rm-title">
            <LogIn class="rm-ico" />
            {{ t('remote.title') }}
          </h2>
          <p class="rm-hint">{{ t('remote.hint') }}</p>

          <label class="rm-label" for="rm-user">{{ t('web.username') }}</label>
          <input
            id="rm-user"
            v-model="username"
            class="x-input rm-field"
            autocomplete="username"
            autofocus
            :placeholder="t('web.username')"
          />

          <label class="rm-label" for="rm-pass">{{ t('web.password') }}</label>
          <input
            id="rm-pass"
            v-model="password"
            class="x-input rm-field"
            type="password"
            autocomplete="current-password"
            :placeholder="t('web.password')"
          />

          <p v-if="err" class="rm-err">{{ err }}</p>

          <button class="x-btn primary rm-submit" type="submit" :disabled="busy">
            <Loader2 v-if="busy" class="x-btn-ico spin" />
            {{ t('remote.submit') }}
          </button>
        </form>

        <!-- P6: default-password admin must set a strong password before the session becomes usable. -->
        <form v-else class="rm-card x-card" @submit.prevent="submitChange">
          <div class="rm-brand">
            <Activity class="rm-logo" />
            <span class="rm-app">HeartRateMonitor</span>
          </div>

          <h2 class="rm-title">
            <ShieldAlert class="rm-ico" />
            {{ t('remote.mustchange') }}
          </h2>
          <p class="rm-hint">{{ t('remote.initweak') }}</p>

          <label class="rm-label" for="rm-np">{{ t('remote.newpass') }}</label>
          <input
            id="rm-np"
            v-model="newPass"
            class="x-input rm-field"
            type="password"
            autocomplete="new-password"
          />

          <label class="rm-label" for="rm-np2">{{ t('remote.newpass2') }}</label>
          <input
            id="rm-np2"
            v-model="newPass2"
            class="x-input rm-field"
            type="password"
            autocomplete="new-password"
          />

          <p v-if="err" class="rm-err">{{ err }}</p>

          <button class="x-btn primary rm-submit" type="submit" :disabled="busy">
            <Loader2 v-if="busy" class="x-btn-ico spin" />
            {{ t('common.save') }}
          </button>
        </form>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>

.rm-scrim {
  position: fixed;
  inset: 0;
  z-index: 3000;
  display: grid;
  place-items: center;
  padding: var(--gap-3);
  background: color-mix(in oklab, var(--background) 58%, transparent);
  backdrop-filter: blur(10px) saturate(115%);
}
.rm-card {
  display: flex;
  flex-direction: column;
  width: min(370px, calc(100vw - 24px));
  padding: var(--gap-4);
  box-shadow: var(--elev-lg);
  /* ....................................................................... */
}
.rm-brand {
  display: flex;
  align-items: center;
  gap: 7px;
  padding-bottom: var(--gap-2);
  margin-bottom: var(--gap-3);
  border-bottom: var(--hairline) solid var(--border);
}
.rm-logo {
  width: 17px;
  height: 17px;
  color: var(--primary);
}
.rm-app {
  font-size: 13px;
  font-weight: 600;
}
.rm-title {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 0 0 6px;
  font-size: 15px;
  font-weight: 650;
}
.rm-ico {
  width: 17px;
  height: 17px;
  color: var(--primary);
}
.rm-hint {
  margin: 0 0 var(--gap-3);
  font-size: 12px;
  color: var(--muted-foreground);
  line-height: 1.55;
}
.rm-label {
  margin-bottom: 5px;
  font-size: 12.5px;
  color: var(--muted-foreground);
}
.rm-field {
  width: 100%;
  margin-bottom: var(--gap-2);
}
.rm-err {
  margin: 2px 0 0;
  font-size: 12.5px;
  color: var(--bad);
}
.rm-submit {
  width: 100%;
  margin-top: var(--gap-3);
}
.spin {
  animation: rm-spin 0.9s linear infinite;
}
@keyframes rm-spin {
  to {
    transform: rotate(360deg);
  }
}
.rm-enter-active,
.rm-leave-active {
  transition: opacity 0.16s linear;
}
.rm-enter-from,
.rm-leave-to {
  opacity: 0;
}
.rm-enter-active .rm-card {
  animation: rm-pop 0.18s linear;
}
@keyframes rm-pop {
  from {
    opacity: 0;
    transform: translateY(-6px) scale(0.985);
  }
}
@media (prefers-reduced-motion: reduce) {
  .rm-enter-active,
  .rm-leave-active {
    transition: none;
  }
  .rm-enter-active .rm-card {
    animation: none;
  }
  .spin {
    animation: none;
  }
}
</style>
