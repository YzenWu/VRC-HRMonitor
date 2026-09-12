// Generates locale files from the five-column canonical table in lang.ts.
// Hong Kong Traditional reuses the Traditional Chinese source column; Cantonese overrides stay in yue-HK.json.
// Korean, German, French, and Cantonese are maintained manually under src/locales and validated by i18n.spec.ts.
// Do not add those manual dictionaries to OUT.
// Usage: npm run locales
import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const src = readFileSync(resolve(here, '../src/lang.ts'), 'utf8');

const OUT = ['zh-TW', 'zh-HK', 'zh-CN', 'en', 'ja', 'es'];
const COLS = [0, 0, 1, 2, 3, 4];
const rows = [];
// Keys may be quoted (`"tab.overview"`) or bare (`wip`); capture both forms.
const re = /^ {2}(?:"([^"]+)"|([A-Za-z_$][\w$]*))\s*:\s*(\[[^\n]*\]),?$/gm;
let m;
while ((m = re.exec(src)) !== null) {
  rows.push([m[1] ?? m[2], JSON.parse(m[3])]);
}

const outDir = resolve(here, '../src/locales');
mkdirSync(outDir, { recursive: true });
for (let outIndex = 0; outIndex < OUT.length; outIndex++) {
  const col = COLS[outIndex];
  const obj = {};
  for (const [key, vals] of rows) obj[key] = vals[col];
  writeFileSync(resolve(outDir, `${OUT[outIndex]}.json`), JSON.stringify(obj, null, 2) + '\n', 'utf8');
}

console.log(`lang2json ok: ${rows.length} keys -> ${OUT.join(', ')}`);
if (rows.length < 200) {
  console.error(`!! 怀疑解析不全（仅 ${rows.length} 键），请检查 lang.ts 格式`);
  process.exit(1);
}
