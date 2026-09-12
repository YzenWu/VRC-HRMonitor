<script setup lang="ts">

import { onMounted, onUnmounted, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import type { CurveMode } from '../prefs';

const props = withDefaults(
  defineProps<{
    data: number[];
    height?: number;

    smooth?: number;

    points?: number;

    mode?: CurveMode;
  }>(),
  { height: 160, smooth: 1, points: 0, mode: 'area' },
);
const { t, locale } = useI18n();

const canvas = ref<HTMLCanvasElement | null>(null);
let ro: ResizeObserver | null = null;


function series(): number[] {
  const raw = props.data.filter((n) => Number.isFinite(n) && n > 0);
  const pointCount = Math.max(0, Math.floor(props.points));
  const win = pointCount > 0 ? raw.slice(-pointCount) : raw;
  const k = Math.max(1, Math.floor(props.smooth));
  if (k <= 1) return win;
  return win.map((_, i) => {
    const from = Math.max(0, i - k + 1);
    const slice = win.slice(from, i + 1);
    return slice.reduce((a, b) => a + b, 0) / slice.length;
  });
}

function cssVar(name: string, fallback: string): string {
  const v = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
  return v || fallback;
}


function animsEnabled(): boolean {
  return document.documentElement.dataset.anim !== 'off'
    && !window.matchMedia?.('(prefers-reduced-motion: reduce)').matches;
}

/** Draw a frame: the parameter is a sequence that has been normalized (<2 valid points draw grids + empty scripts only). */
function paint(pts: number[]): void {
  lastPaintedPts = pts.slice();
  const el = canvas.value;
  if (!el) return;
  const parent = el.parentElement;
  if (!parent) return;

  const dpr = window.devicePixelRatio || 1;
  const w = parent.clientWidth;
  const h = props.height;
  el.width = Math.max(1, Math.floor(w * dpr));
  el.height = Math.max(1, Math.floor(h * dpr));
  el.style.width = `${w}px`;
  el.style.height = `${h}px`;

  const ctx = el.getContext('2d');
  if (!ctx) return;
  ctx.scale(dpr, dpr);
  ctx.clearRect(0, 0, w, h);

  const border = cssVar('--border', 'rgba(255,255,255,0.1)');
  const accent = cssVar('--primary', '#5b9dff');
  const muted = cssVar('--muted-foreground', '#888');
  const font = cssVar('--font-body', '"Segoe UI", "Microsoft YaHei", sans-serif');

  // Grid
  ctx.strokeStyle = border;
  ctx.lineWidth = 1;
  for (let i = 1; i < 4; i++) {
    const y = (h / 4) * i;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(w, y);
    ctx.stroke();
  }

  if (pts.length < 2) {
    ctx.fillStyle = muted;
    ctx.font = `12px ${font}`;
    ctx.fillText(t('hb.waiting'), 8, h / 2);
    return;
  }

  const min = Math.min(...pts);
  const max = Math.max(...pts);
  const span = Math.max(8, max - min);
  const lo = min - span * 0.15;
  const hi = max + span * 0.15;
  const x = (i: number) => (i / (pts.length - 1)) * w;
  const y = (v: number) => h - ((v - lo) / (hi - lo)) * h;

  if (props.mode === 'bars') {

    const bw = Math.max(1, w / pts.length - 1);
    ctx.fillStyle = accent;
    ctx.globalAlpha = 0.75;
    pts.forEach((v, i) => {
      const top = y(v);
      ctx.fillRect(x(i) - bw / 2, top, bw, h - top);
    });
    ctx.globalAlpha = 1;
  } else if (props.mode === 'dots') {
    ctx.fillStyle = accent;
    pts.forEach((v, i) => {
      ctx.beginPath();
      ctx.arc(x(i), y(v), 1.8, 0, Math.PI * 2);
      ctx.fill();
    });
  } else if (props.mode === 'step') {
    // Stairs: each segment flattens to the next sampling point before mutation, and the value is maintained until the next update; the floor is paved with a semi-transparent area
    ctx.beginPath();
    ctx.moveTo(x(0), h);
    ctx.lineTo(x(0), y(pts[0]));
    for (let i = 1; i < pts.length; i++) {
      ctx.lineTo(x(i), y(pts[i - 1]));
      ctx.lineTo(x(i), y(pts[i]));
    }
    ctx.lineTo(x(pts.length - 1), h);
    ctx.closePath();
    ctx.fillStyle = accent;
    ctx.globalAlpha = 0.1;
    ctx.fill();
    ctx.globalAlpha = 1;

    ctx.beginPath();
    ctx.moveTo(x(0), y(pts[0]));
    for (let i = 1; i < pts.length; i++) {
      ctx.lineTo(x(i), y(pts[i - 1]));
      ctx.lineTo(x(i), y(pts[i]));
    }
    ctx.strokeStyle = accent;
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';
    ctx.stroke();
  } else {

    const needFill = props.mode === 'area' || props.mode === 'gradient';
    if (needFill) {
      ctx.beginPath();
      ctx.moveTo(x(0), h);
      pts.forEach((v, i) => ctx.lineTo(x(i), y(v)));
      ctx.lineTo(x(pts.length - 1), h);
      ctx.closePath();
      if (props.mode === 'gradient') {
        const grad = ctx.createLinearGradient(0, 0, 0, h);
        grad.addColorStop(0, accent);
        grad.addColorStop(1, 'transparent');
        ctx.fillStyle = grad;
        ctx.globalAlpha = 0.26;
      } else {
        ctx.fillStyle = accent;
        ctx.globalAlpha = 0.1;
      }
      ctx.fill();
      ctx.globalAlpha = 1;
    }

    ctx.beginPath();
    pts.forEach((v, i) => (i === 0 ? ctx.moveTo(x(i), y(v)) : ctx.lineTo(x(i), y(v))));
    ctx.strokeStyle = accent;
    ctx.lineWidth = 2;
    ctx.lineJoin = 'round';
    ctx.stroke();
  }

  // Endpoint (all modes are marked for current value position).
  const lastX = x(pts.length - 1);
  const lastY = y(pts[pts.length - 1]);
  ctx.beginPath();
  ctx.arc(lastX, lastY, 3.5, 0, Math.PI * 2);
  ctx.fillStyle = accent;
  ctx.fill();

  // Polar Marker
  ctx.fillStyle = muted;
  ctx.font = `11px ${font}`;
  ctx.fillText(`${max}`, 4, 12);
  ctx.fillText(`${min}`, 4, h - 4);
}


let rafId = 0;
let animGen = 0;
let lastPaintedPts: number[] | null = null;
let prevPts: number[] | null = null;

function cancelAnim(): void {
  animGen++;
  if (rafId !== 0) {
    cancelAnimationFrame(rafId);
    rafId = 0;
  }
}

/** New data/parameter changes: same long (window full) = the old frame as a whole moves left to take the old value; otherwise considered additional, the end point grows from the old end value. */
function onSeriesChange(): void {
  const next = series();
  const prev = prevPts;
  const interrupted = rafId !== 0;
  const current = interrupted && lastPaintedPts && lastPaintedPts.length >= 2 ? lastPaintedPts : null;
  prevPts = next;
  cancelAnim();

  const canTween = (current !== null || (prev !== null && prev.length >= 2)) && next.length >= 2;
  if (animsEnabled() && canTween) {
    const gen = animGen;
    const t0 = performance.now();
    const dur = 260;
    const sameLen = prev?.length === next.length;
    const step = (now: number) => {
      if (gen !== animGen) return;
      const k = Math.min(1, Math.max(0, (now - t0) / dur));
      const cur = next.map((v, i) => {
        const base = current
          ? (i < current.length ? current[i] : current[current.length - 1])
          : (sameLen
            ? (i + 1 < prev!.length ? prev![i + 1] : prev![prev!.length - 1])
            : (i < prev!.length ? prev![i] : prev![prev!.length - 1]));
        return base + (v - base) * k;
      });
      paint(cur);
      if (k >= 1) {
        rafId = 0;
        return;
      }
      rafId = requestAnimationFrame(step);
    };
    rafId = requestAnimationFrame(step);
  } else {
    paint(next);
  }
}

watch(
  () => [props.data.slice(), props.smooth, props.points, props.mode] as const,
  onSeriesChange,
  { deep: false },
);
// An empty script will be redrawn immediately after cutting the language.
watch(locale, onSeriesChange);

onMounted(() => {
  onSeriesChange();
  if (canvas.value?.parentElement) {
    ro = new ResizeObserver(() => {
      cancelAnim();
      paint(series());
    });
    ro.observe(canvas.value.parentElement);
  }
});
onUnmounted(() => {
  cancelAnim();
  ro?.disconnect();
});
</script>

<template>
  <div style="width: 100%">
    <canvas ref="canvas" />
  </div>
</template>
