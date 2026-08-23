import type { ReticleSpec } from '@/bridge/types';

const ALPHA = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';

type Field = [keyof ReticleSpec, number, number, number];
const FIELDS: Field[] = [
  ['dot',            0,   1, 1],
  ['dotSize',        0,  10, 2],
  ['gap',            0,  40, 1],
  ['length',         0,  50, 1],
  ['thickness',      1,  14, 2],
  ['tilt',           0,  45, 1],
  ['outline',        0,   1, 1],
  ['outlineWidth',   0,   6, 2],
  ['opacity',        0, 100, 1],
  ['scale',         50, 200, 1 / 5],
  ['permanent',      0,   1, 1],
  ['hipfireSeconds', 0,  30, 2],
  ['ring',           0,   1, 1],
  ['ringRadius',     3,  40, 1],
  ['ringThickness',  0.5, 8, 2],
];
const V1_FIELD_COUNT = 12;

export const DEFAULT_RETICLE: ReticleSpec = {
  dot: true, dotSize: 2, gap: 8, length: 14, thickness: 3, tilt: 0,
  outline: true, outlineWidth: 1, opacity: 100, scale: 100,
  colorMain: '#ffffff', colorAds: '#ff1c21',
  permanent: true, hipfireSeconds: 0, code: '',
  ring: false, ringRadius: 10, ringThickness: 1.5,
};

const clamp = (v: number, min: number, max: number) => Math.min(max, Math.max(min, v));

function encInt(n: number, width = 2): string {
  n = Math.round(n);
  let s = '';
  for (let i = 0; i < width; i++) { s = ALPHA[((n % 32) + 32) % 32] + s; n = Math.floor(n / 32); }
  return s;
}
function decInt(s: string): number {
  let n = 0;
  for (const c of s) { const i = ALPHA.indexOf(c); if (i < 0) return NaN; n = n * 32 + i; }
  return n;
}
const hex6 = (c: string) => (c.replace('#', '').toUpperCase() + '000000').slice(0, 6).replace(/[^0-9A-F]/g, '0');

function checksum(payload: string): string {
  let sum = 0;
  for (const c of payload) sum = (sum + c.charCodeAt(0)) % 32;
  return ALPHA[sum];
}

export function encodeReticle(spec: ReticleSpec): string {
  const asBool = (v: unknown) => (v ? 1 : 0);
  const nums = FIELDS.map(([k, mn, mx, sc]) => {
    const raw = typeof spec[k] === 'boolean' ? asBool(spec[k]) : (spec[k] as number);
    return encInt(clamp(raw, mn, mx) * sc, 2);
  }).join('');
  const cols = hex6(spec.colorMain) + hex6(spec.colorAds);
  const body = nums + cols;
  const payload = checksum(body) + body;
  const groups = payload.match(/.{1,5}/g) ?? [];
  return 'KNK-' + groups.join('-');
}

export function decodeReticle(code: string): ReticleSpec | null {
  const raw = code.trim().toUpperCase();
  if (!raw.startsWith('KNK-')) return null;
  const payload = raw.slice(4).replace(/-/g, '');
  const isV2 = payload.length === 1 + FIELDS.length * 2 + 12;
  const isV1 = payload.length === 1 + V1_FIELD_COUNT * 2 + 12;
  if (!isV2 && !isV1) return null;
  const fields = isV2 ? FIELDS : FIELDS.slice(0, V1_FIELD_COUNT);
  const chk = payload[0];
  const body = payload.slice(1);
  if (checksum(body) !== chk) return null;

  const out: Record<string, unknown> = { ...DEFAULT_RETICLE };
  let p = 0;
  for (const [k, mn, mx, sc] of fields) {
    const v = decInt(body.slice(p, p + 2)); p += 2;
    if (Number.isNaN(v)) return null;
    const real = clamp(v / sc, mn, mx);
    out[k as string] = (k === 'dot' || k === 'outline' || k === 'permanent' || k === 'ring') ? real >= 0.5 : real;
  }
  const main = body.slice(p, p + 6); p += 6;
  const ads = body.slice(p, p + 6);
  if (!/^[0-9A-F]{6}$/.test(main) || !/^[0-9A-F]{6}$/.test(ads)) return null;
  out.colorMain = '#' + main.toLowerCase();
  out.colorAds = '#' + ads.toLowerCase();
  out.code = encodeReticle(out as unknown as ReticleSpec);
  return out as unknown as ReticleSpec;
}
