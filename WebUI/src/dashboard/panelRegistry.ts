import type { Component } from 'vue';
import BpmPanel from '../components/panels/BpmPanel.vue';
import CurvePanel from '../components/panels/CurvePanel.vue';
import DevicesPanel from '../components/panels/DevicesPanel.vue';
import HealthPanel from '../components/panels/HealthPanel.vue';
import OscPanel from '../components/panels/OscPanel.vue';
import LogPanel from '../components/panels/LogPanel.vue';
import ActionsPanel from '../components/panels/ActionsPanel.vue';
import HwPanel from '../components/panels/HwPanel.vue';

/** Panel definition:     PH 0...to enter    PH 1  layout,    PH 2  to go    PH 3,    PH 4    to be by default the width (12 grids: 4 = 1/3, 8 = 2/3, 12 = line). */
export interface PanelDef {
  id: string;
  titleKey: string;
  component: Component;
  span: number;
}

/** Panel registration form (  PH 0 PH 1   PH 2: add a new panel only here). */
export const PANELS: PanelDef[] = [
  { id: 'bpm', titleKey: 'hb.current', component: BpmPanel, span: 4 },
  { id: 'curve', titleKey: 'hb.curve', component: CurvePanel, span: 8 },
  { id: 'actions', titleKey: 'dash.actions', component: ActionsPanel, span: 4 },
  { id: 'health', titleKey: 'hb.health', component: HealthPanel, span: 4 },
  { id: 'devices', titleKey: 'tab.devices', component: DevicesPanel, span: 4 },
  { id: 'osc', titleKey: 'tab.osc', component: OscPanel, span: 4 },
  { id: 'hw', titleKey: 'tab.hwinfo', component: HwPanel, span: 4 },
  { id: 'log', titleKey: 'tab.logs', component: LogPanel, span: 8 },
];

export const panelById = (id: string): PanelDef | undefined => PANELS.find((p) => p.id === id);

/** Default layout (sequence is the order of rendering). */
export const DEFAULT_LAYOUT = ['bpm', 'curve', 'actions', 'health', 'devices', 'osc', 'hw', 'log'];
