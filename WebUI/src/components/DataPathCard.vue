<script setup lang="ts">









import { onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { FolderOpen } from 'lucide-vue-next';
import { api } from '../api';
import XSelect from './XSelect.vue';

const { t } = useI18n();

type Mode = 'appdata' | 'exedir';

const dataDir = ref('');
const exeDir = ref('');
const defaultDir = ref('');
const mode = ref<Mode>('appdata');
const hint = ref('');
const fail = ref('');

const norm = (a: string): string => a.replace(/[\\/]+$/, '').toLowerCase();
const presetOpts = () => [
  { value: 'appdata', label: t('settings.pathappdata') },
  { value: 'exedir', label: t('settings.pathexedir') },
];

onMounted(() => {
  void api
    .config()
    .then((c) => {
      const p = (c as { paths?: { dataDir?: string; exeDir?: string; defaultDir?: string } }).paths;
      if (!p?.dataDir) return;
      dataDir.value = p.dataDir;
      exeDir.value = p.exeDir ?? '';
      defaultDir.value = p.defaultDir ?? '';
      const d = norm(p.dataDir);
      mode.value = d === norm(p.exeDir ?? '') ? 'exedir' : 'appdata';
    })
    .catch(() => {
      fail.value = t('settings.pathfail');
    });
});

async function save(): Promise<void> {
  const value = mode.value === 'appdata' ? '__appdata__' : '__exedir__';
  try {
    const r = (await api.settings({ paths: { dataDir: value } })) as { ok?: boolean };
    if (r.ok) {
      hint.value = t('settings.pathrestart');
      dataDir.value = mode.value === 'appdata' ? defaultDir.value : exeDir.value;
    } else {
      fail.value = t('settings.pathfail');
    }
  } catch {
    fail.value = t('settings.pathfail');
  }
  window.setTimeout(() => (hint.value = ''), 3200);
  window.setTimeout(() => (fail.value = ''), 3200);
}
</script>

<template>
  <div class="x-card" data-card-id="settings:data-path">
    <div class="x-card-head">
      <FolderOpen class="x-btn-ico" style="width: 14px; height: 14px" />
      {{ t('settings.path') }}
      <span class="x-grow"></span>
      <span v-if="hint" class="x-muted" style="font-weight: 400; font-size: 12px">· {{ hint }}</span>
      <span v-if="fail" style="font-weight: 400; font-size: 12px; color: var(--bad)">· {{ fail }}</span>
    </div>
    <div class="x-card-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <div class="x-gap" style="flex-wrap: wrap">
        <XSelect v-model="mode" style="width: 200px" :items="presetOpts()" />
        <button class="x-btn" @click="() => void save()">{{ t('common.apply') }}</button>
      </div>
      <div class="x-muted x-mono" style="font-size: 11.5px; word-break: break-all">{{ dataDir || t('settings.pathcurrent') }}</div>
      <div class="x-muted" style="font-size: 11.5px">{{ t('settings.pathhint') }}</div>
    </div>
  </div>
</template>
