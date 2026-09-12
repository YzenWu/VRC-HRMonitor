<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { CircleDot, ExternalLink, GitCommit, GitFork, Github, RefreshCw, Star } from 'lucide-vue-next';
import { api, type GitHubInfo } from '../api';
import { useAppStore } from '../stores/app';

const { t } = useI18n();
const app = useAppStore();
const github = ref<GitHubInfo | null>(null);
const refreshing = ref(false);
const requestError = ref('');

const repoSlug = computed(() => {
  const url = (app.release?.repo ?? '').trim();
  if (!url) return '';
  const m = /^https?:\/\/github\.com\/([^/]+\/[^/]+?)(?:\.git)?\/?$/i.exec(url);
  return m ? m[1] : (url.includes('://') ? '' : url);
});
const ghProfile = computed(() => repoSlug.value ? (app.release?.repo || `https://github.com/${repoSlug.value}`) : '');
const ghUserProfile = computed(() => (app.release?.authorUrl || '').trim());
const projectName = computed(() => (app.release?.projectName || 'HeartRateMonitor').trim());
const authorName = computed(() => (app.release?.author || '—').trim());
const snapshot = computed(() => github.value?.snapshot ?? null);
const rel = computed(() => snapshot.value?.latestRelease ?? null);
const update = computed(() => github.value?.update ?? null);
const buildTime = computed(() => {
  const value = app.release?.buildTimeUtc ?? '';
  return value && value !== 'auto' ? value : '';
});
const thumbnail = computed(() => {
  const value = (app.release?.thumbnail || '').trim().replace(/\\/g, '/').replace(/^\.\//, '');
  return value && !value.includes('..') && !value.includes('://') ? `/${value.replace(/^\/+/, '')}` : '';
});
const licenseHref = computed(() => {
  const context = app.release?.licenseContext?.trim();
  return context || (repoSlug.value ? `https://github.com/${repoSlug.value}/LICENSE` : '');
});
const componentRows = computed(() => {
  const c = app.release?.components;
  return [
    ['engine', c?.engine],
    ['webui', c?.webui],
    ['cli', c?.cli],
    ['dump', c?.dump],
  ];
});
const consistency = computed(() => app.appInfo?.componentsOk === true
  ? t('about.consistent')
  : app.appInfo?.componentsOk === false ? t('about.inconsistent') : t('about.unchecked'));
const cacheStatus = computed(() => snapshot.value?.fetchedAt
  ? t('about.cachedat', { time: fmtDateTime(snapshot.value.fetchedAt) })
  : t('about.waiting'));

async function loadGithub(refresh = false): Promise<void> {
  refreshing.value = refresh;
  requestError.value = '';
  try {
    github.value = refresh ? await api.githubRefresh() : await api.github();
    if (github.value.ok === false) requestError.value = String((github.value as { error?: string }).error || t('about.refreshfail'));
  } catch (error) {
    requestError.value = error instanceof Error ? error.message : t('about.refreshfail');
  } finally {
    refreshing.value = false;
  }
}

function fmt(n?: number): string {
  if (n === undefined || Number.isNaN(n)) return '—';
  return n >= 1000 ? `${(n / 1000).toFixed(n >= 10000 ? 0 : 1)}k` : String(n);
}
function shortSha(value?: string): string {
  return value ? value.slice(0, 7) : '—';
}
function fmtDate(iso?: string): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleDateString();
}
function fmtDateTime(iso?: string): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : `${date.toLocaleDateString()} ${date.toLocaleTimeString()}`;
}

onMounted(() => void loadGithub());
</script>

<template>
  <div class="page-host">
    <header class="page-head">
      <h1>{{ t('tab.about') }}</h1>
      <p class="page-desc" v-bubble="t('about.intro')">{{ repoSlug || t('about.notpublished') }}</p>
    </header>

    <div class="page-body" style="display: flex; flex-direction: column; gap: var(--gap-2)">
      <div class="x-card" data-card-id="about:project">
        <div class="x-card-head">
          <Github class="x-btn-ico" />
          {{ t('about.project') }}
          <span class="x-grow"></span>
          <a v-if="ghProfile" class="x-btn sm" :href="ghProfile" target="_blank" rel="noreferrer">
            {{ repoSlug }} <ExternalLink class="x-btn-ico" />
          </a>
        </div>
        <div class="x-card-body project-summary">
          <div class="ab-avatar"><img v-if="thumbnail" :src="thumbnail" alt="" /></div>
          <div class="project-copy">
            <div class="ab-name">{{ projectName }}</div>
            <div class="x-muted author-line">{{ t('about.author') }}: {{ authorName }}</div>
            <div class="ab-stats">
              <span class="ab-stat" v-bubble="t('about.stars')"><Star /> {{ fmt(snapshot?.stars) }}</span>
              <span class="ab-stat" v-bubble="t('about.forks')"><GitFork /> {{ fmt(snapshot?.forks) }}</span>
              <span class="ab-stat" v-bubble="t('about.issues')"><CircleDot /> {{ fmt(snapshot?.issuesOpen) }}/{{ fmt(snapshot?.issuesTotal) }}</span>
              <span class="ab-stat" v-bubble="snapshot?.latestCommitTime ? fmtDateTime(snapshot.latestCommitTime) : t('about.commit')"><GitCommit /> {{ shortSha(snapshot?.latestCommitSha) }}</span>
            </div>
          </div>
        </div>
        <div class="x-card-body" style="padding: 0">
          <div v-if="ghProfile" class="x-row">
            <span class="x-muted ab-k">{{ t('about.repo') }}</span>
            <a class="x-grow x-mono ab-link" :href="ghProfile" target="_blank" rel="noreferrer">{{ ghProfile }}</a>
          </div>
          <div v-if="ghUserProfile" class="x-row">
            <span class="x-muted ab-k">{{ t('about.author') }}</span>
            <a class="x-grow x-mono ab-link" :href="ghUserProfile" target="_blank" rel="noreferrer">{{ ghUserProfile }}</a>
          </div>
        </div>
      </div>

      <div class="x-card" data-card-id="about:update-center">
        <div class="x-card-head">
          <RefreshCw class="x-btn-ico" :class="{ spinning: refreshing }" />
          {{ t('about.updatecenter') }}
          <span class="x-grow"></span>
          <button class="x-btn sm" :disabled="refreshing" @click="loadGithub(true)">{{ t('about.refresh') }}</button>
        </div>
        <div class="x-card-body" style="padding: 0">
          <div class="x-row">
            <span class="x-muted ab-k">{{ t('about.status') }}</span>
            <span>{{ update?.available ? t('about.available') : update?.current ? t('about.current') : update?.skipped ? t('about.skipped') : t('about.unknown') }}</span>
          </div>
          <div class="x-row">
            <span class="x-muted ab-k">{{ t('about.cache') }}</span>
            <span class="x-mono">{{ cacheStatus }}</span>
          </div>
          <div v-if="refreshing" class="x-row">
            <span class="x-muted ab-k">{{ t('about.status') }}</span>
            <span>{{ t('about.refreshing') }}</span>
          </div>
          <div v-if="snapshot?.stale" class="x-row">
            <span class="x-muted ab-k">{{ t('about.status') }}</span>
            <span class="status-error">{{ t('about.stale') }}</span>
          </div>
          <div v-if="requestError || snapshot?.error" class="x-row">
            <span class="x-muted ab-k">{{ t('about.error') }}</span>
            <span class="x-mono status-error">{{ requestError || snapshot?.error }}</span>
          </div>
        </div>
      </div>

      <div class="x-card" data-card-id="about:release-info">
        <div class="x-card-head">{{ t('about.relinfo') }}</div>
        <div class="x-card-body" style="padding: 0">
          <div class="x-row"><span class="x-muted ab-k">{{ t('about.releasename') }}</span><span class="x-mono">{{ app.release?.releaseName || '—' }}</span></div>
          <div class="x-row"><span class="x-muted ab-k">{{ t('settings.version') }}</span><span class="x-mono">{{ app.release?.releaseVersion || app.appInfo?.version || '—' }}</span></div>
          <div class="x-row"><span class="x-muted ab-k">{{ t('about.buildtime') }}</span><span class="x-mono" v-bubble="buildTime ? `${buildTime} (UTC)` : ''">{{ buildTime ? fmtDateTime(buildTime) : '—' }}</span></div>
          <div class="x-row"><span class="x-muted ab-k">{{ t('about.note') }}</span><span>{{ app.release?.buildNote || '—' }}</span></div>
          <div class="x-row"><span class="x-muted ab-k">{{ t('about.target') }}</span><span class="x-mono">{{ app.release?.buildTarget || '—' }}</span></div>
          <div class="x-row"><span class="x-muted ab-k">{{ t('about.license') }}</span><a v-if="licenseHref" class="x-mono ab-link" :href="licenseHref" target="_blank" rel="noreferrer">{{ app.release?.license || '—' }}</a><span v-else>{{ app.release?.license || '—' }}</span></div>
        </div>
      </div>

      <div class="x-card" data-card-id="about:components">
        <div class="x-card-head">{{ t('about.components') }}<span class="x-grow"></span><span class="component-state" :class="{ bad: app.appInfo?.componentsOk === false }">{{ consistency }}</span></div>
        <div class="x-card-body" style="padding: 0">
          <div v-for="row in componentRows" :key="row[0]" class="x-row"><span class="x-muted ab-k">{{ row[0] }}</span><span class="x-mono">{{ row[1] || '—' }}</span></div>
        </div>
      </div>

      <div class="x-card" data-card-id="about:runtime">
        <div class="x-card-head">{{ t('about.runtime') }}</div>
        <div class="x-card-body" style="padding: 0">
          <div class="x-row"><span class="x-muted ab-k">{{ t('settings.started') }}</span><span class="x-mono">{{ app.appInfo?.startTime ?? '—' }}</span></div>
          <div v-if="!app.remote.remote" class="x-row"><span class="x-muted ab-k">{{ t('settings.dir') }}</span><span class="x-mono x-grow path-value">{{ app.appInfo?.baseDir ?? '—' }}</span></div>
          <div class="x-row"><span class="x-muted ab-k">Web</span><span class="x-mono">Vue 3 · Vite · vue-i18n · WebView2</span></div>
        </div>
      </div>

      <div class="x-card" data-card-id="about:latest">
        <div class="x-card-head">
          <ExternalLink class="x-btn-ico" /> {{ t('about.latest') }}
          <span class="x-grow"></span>
          <a v-if="rel?.url" class="x-btn sm" :href="rel.url" target="_blank" rel="noreferrer">{{ t('about.releases') }} <ExternalLink class="x-btn-ico" /></a>
        </div>
        <template v-if="rel">
          <div class="x-card-body release-summary">
            <div class="rel-title">{{ rel.name || rel.tag }} <span class="x-mono x-muted rel-tag">{{ rel.tag }}</span></div>
            <div class="x-muted release-date">{{ t('about.published') }}: {{ fmtDate(rel.publishedAt) }}</div>
          </div>
          <div v-if="rel.body" class="x-card-body rel-body">{{ rel.body }}</div>
        </template>
        <div v-else class="x-card-body"><div class="x-muted">{{ t('about.norelease') }}</div></div>
      </div>

      <div class="ab-license-foot"><span>{{ t('about.licensefoot') }}</span><a v-if="licenseHref" :href="licenseHref" target="_blank" rel="noreferrer">LICENSE</a></div>
    </div>
  </div>
</template>

<style scoped>
.project-summary {
  display: flex;
  gap: var(--gap-3);
  align-items: center;
  flex-wrap: wrap;
}
.project-copy {
  flex: 1;
  min-width: 220px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}
.author-line, .release-date { font-size: 12px; }
.path-value { word-break: break-all; }
.release-summary { display: flex; flex-direction: column; gap: var(--gap-2); }
.status-error, .component-state.bad { color: var(--destructive, #dc2626); }
.component-state { font-size: 12px; color: var(--muted-foreground); }
.spinning { animation: about-spin 0.8s linear infinite; }
@keyframes about-spin { to { transform: rotate(360deg); } }
.ab-license-foot {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 8px 0 2px;
  font-size: 11.5px;
  color: var(--muted-foreground);
}
.ab-license-foot a {
  color: var(--primary);
  text-decoration: none;
}
.ab-k {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-width: 132px;
}
.ab-ico {
  width: 13px;
  height: 13px;
}
.ab-avatar {
  width: 72px;
  height: 72px;
  border-radius: 50%;
  flex-shrink: 0;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
  background: color-mix(in oklab, var(--primary) 14%, transparent);
  border: 1px solid var(--border);
}
.ab-avatar img {
  width: 100%;
  height: 100%;
  object-fit: cover;
}
.ab-initial {
  font-size: 28px;
  font-weight: 700;
  color: var(--primary);
}
.ab-name {
  font-size: 20px;
  font-weight: 700;
}
.ab-stats {
  display: flex;
  flex-wrap: wrap;
  gap: var(--gap-2);
  align-items: center;
}
.ab-stat {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  color: var(--muted-foreground);
  background: var(--muted);
  padding: 2px 8px;
  border-radius: 999px;
}
.ab-stat svg {
  width: 12px;
  height: 12px;
}
.ab-link {
  text-decoration: none;
  color: var(--primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.rel-title {
  display: flex;
  align-items: baseline;
  gap: 8px;
  font-size: 15px;
  font-weight: 650;
}
.rel-tag {
  font-size: 12px;
}
.rel-body {
  font-size: 12.5px;
  line-height: 1.55;
  white-space: pre-wrap;
  word-break: break-word;
  max-height: 220px;
  overflow-y: auto;
}
</style>
