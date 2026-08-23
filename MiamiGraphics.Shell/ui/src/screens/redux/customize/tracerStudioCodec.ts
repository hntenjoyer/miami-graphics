export interface StudioChannelTweak {
  channel: string;
  gradient: [string, string, string] | null;
  thickness: number;
  length: number;
  smoke: string | null;
}

export interface StudioGun {
  weapon: string;
  channel: string;
  chance: number;
}

export interface StudioSettings {
  guns: StudioGun[];
  channels: StudioChannelTweak[];
}

export const CODEC_TAG = 'MGTS1';

export function emptyTweak(channel: string): StudioChannelTweak {
  return { channel, gradient: null, thickness: 1, length: 1, smoke: null };
}

export function tweakIsEmpty(t: StudioChannelTweak): boolean {
  return t.gradient === null && t.smoke === null
    && Math.abs(t.thickness - 1) < 0.001 && Math.abs(t.length - 1) < 0.001;
}

export function encodeStudio(s: StudioSettings): string {
  const parts: string[] = [CODEC_TAG];
  const guns = s.guns.filter(g => g.weapon && g.channel);
  if (guns.length > 0) {
    parts.push('W:' + guns
      .map(g => `${g.weapon}=${g.channel}:${fmt(g.chance)}:${fmt(g.chance)}`)
      .join(';'));
  }
  for (const c of s.channels) {
    if (tweakIsEmpty(c)) continue;
    const seg: string[] = [];
    if (c.gradient) seg.push('g:' + c.gradient.map(clean6).join(','));
    if (Math.abs(c.thickness - 1) >= 0.001) seg.push('t:' + fmt(c.thickness));
    if (Math.abs(c.length - 1) >= 0.001) seg.push('l:' + fmt(c.length));
    if (c.smoke) seg.push('s:' + clean6(c.smoke));
    parts.push(`C:${c.channel}=${seg.join(';')}`);
  }
  return parts.length > 1 ? parts.join('|') : '';
}

export function decodeStudio(packed: string | null | undefined): StudioSettings {
  const s: StudioSettings = { guns: [], channels: [] };
  if (!packed) return s;
  const parts = packed.split('|');
  if (parts[0] !== CODEC_TAG) return s;
  for (const part of parts.slice(1)) {
    if (part.startsWith('W:')) {
      for (const chunk of part.slice(2).split(';')) {
        const eq = chunk.indexOf('=');
        if (eq <= 0) continue;
        const weapon = chunk.slice(0, eq).trim();
        const rest = chunk.slice(eq + 1).split(':');
        const channel = (rest[0] ?? '').trim();
        const sp = parseFloat(rest[1] ?? '1');
        if (!weapon || !channel) continue;
        s.guns.push({ weapon, channel, chance: Number.isFinite(sp) ? clamp01(sp) : 1 });
      }
    } else if (part.startsWith('C:')) {
      const eq = part.indexOf('=');
      if (eq <= 2) continue;
      const t = emptyTweak(part.slice(2, eq).trim());
      if (!t.channel) continue;
      for (const seg of part.slice(eq + 1).split(';')) {
        if (seg.startsWith('g:')) {
          const cols = seg.slice(2).split(',').map(clean6).filter(c => c.length === 6);
          if (cols.length === 3) t.gradient = [cols[0], cols[1], cols[2]];
        } else if (seg.startsWith('t:')) {
          const v = parseFloat(seg.slice(2));
          if (Number.isFinite(v)) t.thickness = v;
        } else if (seg.startsWith('l:')) {
          const v = parseFloat(seg.slice(2));
          if (Number.isFinite(v)) t.length = v;
        } else if (seg.startsWith('s:')) {
          const c = clean6(seg.slice(2));
          if (c.length === 6) t.smoke = c;
        }
      }
      s.channels.push(t);
    }
  }
  return s;
}

function fmt(v: number): string {
  return (Math.round(v * 1000) / 1000).toString();
}

function clean6(hex: string): string {
  const h = hex.trim().replace(/^#/, '').toUpperCase();
  return /^[0-9A-F]{6}$/.test(h) ? h : '';
}

function clamp01(v: number): number {
  return Math.max(0, Math.min(1, v));
}

export interface StudioChannelInfo {
  id: string;
  labelKey: string;
  labelDefault: string;
  hasOwnSmoke: boolean;
  warnKey: string;
  warnDefault: string;
}

export const STUDIO_CHANNELS: StudioChannelInfo[] = [
  {
    id: 'bullet_tracer', labelKey: 'customize.tracerStudio.ch.normal', labelDefault: 'Обычный',
    hasOwnSmoke: true,
    warnKey: 'customize.tracerStudio.chWarn.normal',
    warnDefault: 'Канал почти всего оружия: перекраска коснётся всех стволов без своего канала.',
  },
  {
    id: 'bullet_tracer_mg', labelKey: 'customize.tracerStudio.ch.mg', labelDefault: 'Пулемётный',
    hasOwnSmoke: false,
    warnKey: 'customize.tracerStudio.chWarn.mg',
    warnDefault: 'Его же использует боевой пулемёт и часть турелей машин.',
  },
  {
    id: 'bullet_tracer_railgun', labelKey: 'customize.tracerStudio.ch.railgun', labelDefault: 'Рельса',
    hasOwnSmoke: true,
    warnKey: 'customize.tracerStudio.chWarn.railgun',
    warnDefault: 'Толстый яркий жгут. Кроме рельсы, его никто не использует.',
  },
  {
    id: 'bullet_tracer_jet', labelKey: 'customize.tracerStudio.ch.jet', labelDefault: 'Реактивный',
    hasOwnSmoke: true,
    warnKey: 'customize.tracerStudio.chWarn.jet',
    warnDefault: 'Его же используют пушки самолётов, Делюксо и вертолёт Акула.',
  },
];

export interface GunLook {
  gradient: [string, string, string];
  thickness: number;
  length: number;
  smoke: string | null;
}

export interface StudioGunConfig {
  weapon: string;
  chance: number;
  look: GunLook;
}

export function defaultLook(): GunLook {
  return { gradient: ['FFFFFF', 'FFFFFF', 'FFFFFF'], thickness: 1, length: 1, smoke: null };
}

export function lookIsVanilla(l: GunLook): boolean {
  return l.gradient.every(c => c === 'FFFFFF')
    && Math.abs(l.thickness - 1) < 0.001 && Math.abs(l.length - 1) < 0.001
    && l.smoke === null;
}

export function lookKey(l: GunLook): string {
  return `${l.gradient.join(',')}|${l.thickness}|${l.length}|${l.smoke ?? '-'}`;
}

export const ALLOC_ORDER = ['bullet_tracer_jet', 'bullet_tracer_mg', 'bullet_tracer_railgun', 'bullet_tracer'];

export interface Allocation {
  gunChannel: Map<string, string>;
  channelLook: Map<string, GunLook>;
  overflow: string[];
}

export function allocateChannels(guns: StudioGunConfig[]): Allocation {
  const byKey = new Map<string, string>();
  const res: Allocation = { gunChannel: new Map(), channelLook: new Map(), overflow: [] };
  let next = 0;
  for (const g of guns) {
    if (lookIsVanilla(g.look)) { res.gunChannel.set(g.weapon, ''); continue; }
    const key = lookKey(g.look);
    let chan = byKey.get(key);
    if (!chan) {
      if (next >= ALLOC_ORDER.length) { res.overflow.push(g.weapon); continue; }
      chan = ALLOC_ORDER[next++];
      byKey.set(key, chan);
      res.channelLook.set(chan, g.look);
    }
    res.gunChannel.set(g.weapon, chan);
  }
  return res;
}

export function encodeGunConfigs(guns: StudioGunConfig[]): string {
  const alloc = allocateChannels(guns);
  const settings: StudioSettings = { guns: [], channels: [] };
  for (const g of guns) {
    const chan = alloc.gunChannel.get(g.weapon);
    if (chan === undefined) continue;
    settings.guns.push({ weapon: g.weapon, channel: chan, chance: g.chance });
  }
  for (const [chan, look] of alloc.channelLook) {
    const t = emptyTweak(chan);
    if (!look.gradient.every(c => c === 'FFFFFF')) t.gradient = look.gradient;
    t.thickness = look.thickness;
    t.length = look.length;
    t.smoke = look.smoke;
    settings.channels.push(t);
  }
  return encodeStudio(settings);
}

export function decodeGunConfigs(packed: string | null | undefined): StudioGunConfig[] {
  const s = decodeStudio(packed);
  const tweakOf = new Map(s.channels.map(c => [c.channel, c]));
  return s.guns.map(g => {
    const t = g.channel ? tweakOf.get(g.channel) : undefined;
    const look = defaultLook();
    if (t) {
      if (t.gradient) look.gradient = t.gradient;
      look.thickness = t.thickness;
      look.length = t.length;
      look.smoke = t.smoke;
    }
    return { weapon: g.weapon, chance: g.chance, look };
  });
}

export const CHANNEL_SIDE_EFFECTS: Record<string, string> = {
  bullet_tracer_railgun: 'Побочек нет: кроме рельсы, канал никто не использует.',
  bullet_tracer_jet: 'Этот же вид получат пушки самолётов, Делюксо и Акулы.',
  bullet_tracer_mg: 'Этот же вид получат боевой пулемёт и часть турелей машин.',
  bullet_tracer: 'ВНИМАНИЕ: этот вид получат ВСЕ стволы, у которых здесь нет своей настройки.',
};

export const STUDIO_GUNS: { id: string; label: string }[] = [
  { id: 'WEAPON_PISTOL', label: 'Пистолет' },
  { id: 'WEAPON_COMBATPISTOL', label: 'Боевой пистолет' },
  { id: 'WEAPON_APPISTOL', label: 'AP-пистолет' },
  { id: 'WEAPON_PISTOL50', label: 'Пистолет .50' },
  { id: 'WEAPON_HEAVYPISTOL', label: 'Тяжёлый пистолет' },
  { id: 'WEAPON_REVOLVER', label: 'Револьвер' },
  { id: 'WEAPON_REVOLVER_MK2', label: 'Револьвер Mk II' },
  { id: 'WEAPON_MICROSMG', label: 'Микро-СМГ' },
  { id: 'WEAPON_SMG', label: 'СМГ' },
  { id: 'WEAPON_SMG_MK2', label: 'СМГ Mk II' },
  { id: 'WEAPON_ASSAULTSMG', label: 'Штурмовой СМГ' },
  { id: 'WEAPON_MINISMG', label: 'Мини-СМГ' },
  { id: 'WEAPON_MACHINEPISTOL', label: 'Автопистолет' },
  { id: 'WEAPON_ASSAULTRIFLE', label: 'Автомат' },
  { id: 'WEAPON_ASSAULTRIFLE_MK2', label: 'Автомат Mk II' },
  { id: 'WEAPON_CARBINERIFLE', label: 'Карабиновая винтовка' },
  { id: 'WEAPON_CARBINERIFLE_MK2', label: 'Карабин Mk II' },
  { id: 'WEAPON_SPECIALCARBINE', label: 'Спецкарабин' },
  { id: 'WEAPON_SPECIALCARBINE_MK2', label: 'Спешик Mk II' },
  { id: 'WEAPON_BULLPUPRIFLE', label: 'Буллпап-винтовка' },
  { id: 'WEAPON_BULLPUPRIFLE_MK2', label: 'Буллпап Mk II' },
  { id: 'WEAPON_ADVANCEDRIFLE', label: 'Продвинутая винтовка' },
  { id: 'WEAPON_COMPACTRIFLE', label: 'Компактная винтовка' },
  { id: 'WEAPON_MILITARYRIFLE', label: 'Военная винтовка' },
  { id: 'WEAPON_HEAVYRIFLE', label: 'Тяжёлая винтовка' },
  { id: 'WEAPON_TACTICALRIFLE', label: 'Тактическая винтовка' },
  { id: 'WEAPON_MG', label: 'Пулемёт' },
  { id: 'WEAPON_COMBATMG', label: 'Боевой пулемёт' },
  { id: 'WEAPON_COMBATMG_MK2', label: 'Боевой пулемёт Mk II' },
  { id: 'WEAPON_GUSENBERG', label: 'Гузенберг' },
  { id: 'WEAPON_SNIPERRIFLE', label: 'Снайперская винтовка' },
  { id: 'WEAPON_HEAVYSNIPER', label: 'Тяжёлый снайпер' },
  { id: 'WEAPON_HEAVYSNIPER_MK2', label: 'Хевик Mk II' },
  { id: 'WEAPON_MARKSMANRIFLE', label: 'Марксман' },
  { id: 'WEAPON_MARKSMANRIFLE_MK2', label: 'Марксман Mk II' },
  { id: 'WEAPON_PRECISIONRIFLE', label: 'Precision' },
  { id: 'WEAPON_MINIGUN', label: 'Миниган' },
];
