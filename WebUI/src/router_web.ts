import { createRouter, createWebHashHistory, type RouteRecordRaw } from 'vue-router';
import { NAV, TOOLKIT } from './router';

/**
 * Shell exclusive router: the navigation item is fully consistent with the browser version, with only   PH 0 copy of the shell-specific differences
 * (Currently setting host window options such as " Window corner / fullscreen " only for pages.
 */
const VIEWS_WEB: Record<string, () => Promise<unknown>> = {
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
  settings: () => import('./views/Settings_web.vue'),
  logs: () => import('./views/Logs.vue'),
  console: () => import('./views/Console.vue'),
  monitor: () => import('./views/Monitor.vue'),
  about: () => import('./views/About.vue'),
};

export const ROUTES_WEB: RouteRecordRaw[] = [
  { path: '/', redirect: '/heartbeat' },
  ...NAV.map((n) => ({ path: n.path, component: VIEWS_WEB[n.tab], meta: { tab: n.tab } })),
  { path: TOOLKIT.path, component: () => import('./views/Toolkit.vue'), meta: { tab: TOOLKIT.tab } },
];

export const routerWeb = createRouter({
  history: createWebHashHistory(),
  routes: ROUTES_WEB,
});
