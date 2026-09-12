import { createRouter, createWebHashHistory, type RouteRecordRaw } from 'vue-router';

/** Sidebar Navigator (in the same order as local   PH 0   PH 1 12  PH 2;   PH 3  for   PH 4  add a panelized workspace,   PH 5  Default landing page). */
export const NAV: { path: string; tab: string }[] = [
  { path: '/overview', tab: 'overview' },
  { path: '/dashboard', tab: 'dashboard' },
  { path: '/heartbeat', tab: 'heartbeat' },
  { path: '/devices', tab: 'devices' },
  { path: '/osc', tab: 'osc' },
  { path: '/pusher', tab: 'pusher' },
  { path: '/hwinfo', tab: 'hwinfo' },
  { path: '/headset', tab: 'headset' },
  { path: '/apiserver', tab: 'apiserver' },
  { path: '/web', tab: 'web' },
  { path: '/settings', tab: 'settings' },
  { path: '/logs', tab: 'logs' },
  { path: '/console', tab: 'console' },
  { path: '/monitor', tab: 'monitor' },
  { path: '/about', tab: 'about' },
];

export const TOOLKIT = { path: '/toolkit', tab: 'toolkit' };

/** Page view map ( PH 0  needs static to analyse, item by item). */
const VIEWS: Record<string, () => Promise<unknown>> = {
  overview: () => import('./views/Overview.vue'),
  dashboard: () => import('./views/Dashboard.vue'),
  heartbeat: () => import('./views/Heartbeat.vue'),
  devices: () => import('./views/Devices.vue'),
  osc: () => import('./views/OscMonitor.vue'),
  pusher: () => import('./views/Pusher.vue'),
  hwinfo: () => import('./views/Hardware.vue'),
  headset: () => import('./views/HeadSet.vue'),
  apiserver: () => import('./views/ApiServer.vue'),
  web: () => import('./views/Web.vue'),
  settings: () => import('./views/Settings.vue'),
  logs: () => import('./views/Logs.vue'),
  console: () => import('./views/Console.vue'),
  monitor: () => import('./views/Monitor.vue'),
  about: () => import('./views/About.vue'),
};

export const tabOf = (path: string): string => NAV.find((n) => n.path === path)?.tab ?? '';

export const ROUTES: RouteRecordRaw[] = [
  { path: '/', redirect: '/heartbeat' },
  ...NAV.map((n) => ({ path: n.path, component: VIEWS[n.tab], meta: { tab: n.tab } })),
  { path: TOOLKIT.path, component: () => import('./views/Toolkit.vue'), meta: { tab: TOOLKIT.tab } },
];

export const router = createRouter({
  history: createWebHashHistory(),
  routes: ROUTES,
});
