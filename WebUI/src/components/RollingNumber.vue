<script setup lang="ts">








import { computed, onUnmounted, ref, watch } from 'vue';
import { rollMode } from '../prefs';

const props = withDefaults(
  defineProps<{

    value?: number | null;
    /** Keeps decimal places. */
    decimals?: number;
    /** empty placeholder. */
    placeholder?: string;
  }>(),
  { value: null, decimals: 0, placeholder: '--' },
);

/** Digital belt (each column): '0'..'9' repeats, allowing the wheel to move/reverse around without crossing. */
const SEQ = '0123456789'.repeat(5);


const SEQ_MID = 20;

const shown = ref<number | null>(null);
const empty = computed(() => props.value == null || !Number.isFinite(props.value));
const text = computed(() => {
  if (empty.value || shown.value === null) return props.placeholder;
  return shown.value.toFixed(props.decimals);
});
/** Change hints (scrutinizing); */
const pulse = ref(false);

const animOff = ref(motionOff());

function motionOff(): boolean {
  return document.documentElement.dataset.anim === 'off'
    || (window.matchMedia?.('(prefers-reduced-motion: reduce)').matches ?? false);
}



interface OdoCol {
  sig: number;
  ch: string;
  digit: boolean;
  pos: number;
}
const odoCols = ref<OdoCol[]>([]);

const prevSig = new Map<number, { ch: string; pos: number }>();

function nearestPos(digit: number, from: number): number {

  const cand: number[] = [];
  for (let k = 0; k < 5; k++) cand.push(digit + 10 * k);
  let best = cand[0];
  let bestD = Infinity;
  for (const c of cand) {
    const d = Math.abs(c - from);
    // Scroll forward at the same distance (input/incremental)
    if (d < bestD || (d === bestD && c > best)) {
      bestD = d;
      best = c;
    }
  }
  return best;
}

function odoRender(v: number): void {
  const s = v.toFixed(0);
  const cols: OdoCol[] = [];
  const next = new Map<number, { ch: string; pos: number }>();
  for (let sig = 0; sig < s.length; sig++) {
    const ch = s[s.length - 1 - sig];
    const d = ch >= '0' && ch <= '9' ? ch.charCodeAt(0) - 48 : -1;
    if (d < 0) {
      // Non-numeric characters such as negative numbers: static columns, directly down (no roller)
      cols.push({ sig, ch, digit: false, pos: 0 });
      next.set(sig, { ch, pos: 0 });
      continue;
    }
    const old = prevSig.get(sig);
    const pos = old && old.ch >= '0' && old.ch <= '9' ? nearestPos(d, old.pos) : SEQ_MID + d;
    cols.push({ sig, ch, digit: true, pos });
    next.set(sig, { ch, pos });
  }
  prevSig.clear();
  for (const [k, v2] of next) prevSig.set(k, v2);
  odoCols.value = cols.sort((a, b) => b.sig - a.sig);
}

let rafId = 0;

function cancel(): void {
  if (rafId !== 0) {
    cancelAnimationFrame(rafId);
    rafId = 0;
  }
}

function flash(): void {
  pulse.value = false;
  requestAnimationFrame(() => (pulse.value = true));
}

/** Smooth scroll: Scroll from the current displayed value to the new value (activation as it is). */
function rollTo(to: number): void {
  cancel();
  const from = shown.value;


  if (from === null) {
    shown.value = to;
    return;
  }
  if (from === to) return;
  const dur = Math.min(1500, Math.max(320, 300 + Math.abs(to - from) * 14));
  const t0 = performance.now();
  flash();
  const step = (now: number) => {
    const k = Math.min(1, Math.max(0, (now - t0) / dur));
    shown.value = from + (to - from) * k;
    if (k >= 1) {
      shown.value = to;
      rafId = 0;
    } else {
      rafId = requestAnimationFrame(step);
    }
  };
  rafId = requestAnimationFrame(step);
}

/** Purely invisible: immediately set + a pulse, without a numerical scroll. */
function fadeTo(to: number): void {
  cancel();
  if (shown.value !== to) {
    shown.value = to;
    flash();
  }
}

function onValue(v: number): void {
  const off = motionOff();
  animOff.value = off;
  if (rollMode.value === 'odometer' && props.decimals === 0 && !off) {
    odoRender(v);
    return;
  }
  odoCols.value = [];
  if (rollMode.value === 'fade') fadeTo(v);
  else rollTo(v);
}

watch(
  () => props.value,
  (v) => {
    if (v == null || !Number.isFinite(v)) {
      cancel();
      shown.value = null;
      odoCols.value = [];
      prevSig.clear();
      return;
    }
    const off = motionOff();
    animOff.value = off;
    if (off) {
      cancel();
      shown.value = v;
      odoCols.value = [];
      return;
    }
    onValue(v);
  },
  { immediate: true },
);

// User Midway Swap Style: Immediately recalculate at current value (Critical Crew unhistoric, direct current value)
watch(rollMode, () => {
  const v = props.value;
  if (v == null || !Number.isFinite(v)) return;
  if (motionOff()) {
    cancel();
    shown.value = v;
    odoCols.value = [];
    return;
  }
  prevSig.clear();
  onValue(v);
});

onUnmounted(() => {
  cancel();
  prevSig.clear();
});
</script>

<template>
  <span class="roll-num" :class="{ pulse }" @animationend="pulse = false">

    <span v-if="!empty && !animOff && rollMode === 'odometer' && decimals === 0" class="odo">
      <span v-for="c in odoCols" :key="c.sig" class="odo-col">
        <template v-if="c.digit">
          <span
            class="odo-strip"
            :style="{
              '--k': c.pos,
              transitionDelay: `${c.sig * 26}ms`,
            }"
          >
            <span v-for="(d, i) in SEQ" :key="i" class="odo-d">{{ d }}</span>
          </span>
        </template>
        <span v-else class="odo-c">{{ c.ch }}</span>
      </span>
    </span>
    <template v-else>{{ text }}</template>
  </span>
</template>

<style scoped>
.roll-num {
  display: inline-block;
  font-variant-numeric: tabular-nums;
}

.roll-num.pulse {
  animation: roll-flash 340ms linear;
}
@keyframes roll-flash {
  0% {
    opacity: 1;
  }
  25% {
    opacity: 0.3;
  }
  100% {
    opacity: 1;
  }
}

.odo {
  display: inline-flex;
  align-items: baseline;
}
.odo-col {
  position: relative;
  display: inline-block;
  height: 1em;
  overflow: hidden;
  text-align: center;
  vertical-align: baseline;
}
.odo-d {
  display: block;
  width: 1ch;
  height: 1em;
  line-height: 1em;
  text-align: center;
}
.odo-strip {
  position: absolute;
  left: 0;
  top: 0;

  transform: translateY(calc(var(--k) * -1em));
  transition: transform 0.52s linear;
  will-change: transform;
}
.odo-c {
  display: inline-block;
  height: 1em;
  line-height: 1em;
  padding: 0 0.1em;
}
</style>
