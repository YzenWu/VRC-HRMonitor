import { createApp } from 'vue';
import { createPinia } from 'pinia';
import App from './App.vue';
import App_web from './App_web.vue';
import { router } from './router';
import { routerWeb } from './router_web';
import { i18n } from './i18n';
import { applyUiTheme } from './theme';
import { installEmptyDefault } from './emptyDefault';
import { installDragGuard } from './dragGuard';
import { inShell } from './shell';
import { installCardCollapse } from './cardCollapse';
import { startPrefsSync } from './prefs';
import { installBubbleDismiss } from './bubble';
import { vBubble } from './vBubble';
import './styles/globals.css';

applyUiTheme();
// Full stop rule: the input box is emptied and the default value is returned (except for password box)
installEmptyDefault();
// Left drag and drag protection: Controls/photos/text cannot be dragged out of the page ( PH 0 mounted address)
installDragGuard();

// Internal   PH 0 shell tag: shell windows at high   PH 1    PH 2  may be less than 860 width (physical width / Zoom Multiplier),
// But it is a desktop window, and narrow-screen drawers and browsers like "Close Tab" are not appropriate.
if (inShell) {
  document.documentElement.classList.add('in-shell');
}

// Inlet diversion: shell   PH 0   copy (self-drawn   PH 1 titlebar + window setting page), browser takes original entrance
const app = createApp(inShell ? App_web : App);
app.use(createPinia());
app.use(inShell ? routerWeb : router);
app.use(i18n);
// PH 0 #2: Suspended Bubble Directive (  PH 1 text”, non-value retreat   PH 2  attribute)+ Global Closer (roll/ roll/   PH 3)
app.directive('bubble', vBubble);
installBubbleDismiss();
app.mount('#app');

// Full station card folding (#10): Mount all backlinks.   PH 0   (with route-to-turn new page cards)
installCardCollapse();
// PH 0  2 View bias backend mirror: capture all   PH 1 /   PH 2 prefix   PH 3  into the vibrating   PH 4,
// Backfill locally missing keys on startup (cross device/reload without losing layout)
startPrefsSync();
