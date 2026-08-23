import { useEffect, useMemo, useState, type CSSProperties } from 'react';
import { create } from 'zustand';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Search, ArrowLeft, Bell, BellRing, ExternalLink, Package, Download,
  Layers, Crosshair, Boxes, Pencil,
} from 'lucide-react';
import { bridge } from '@/bridge';
import type { ModmakerCard, ModmakerDetail, ModmakerMod } from '@/bridge/types';
import { useSessionStore } from '@/store/sessionStore';
import { useNavStore } from '@/store/navStore';
import { EASE_DEPTH } from '@/design/tokens';
import { readCache, writeCache } from '@/store/catalogCache';
import i18n from '@/i18n';

interface AuthorsNavState {
  openPromo: string | null;
  requestOpen: (promo: string) => void;
  consume: () => void;
}
export const useAuthorsStore = create<AuthorsNavState>((set) => ({
  openPromo: null,
  requestOpen: (promo) => set({ openPromo: promo }),
  consume: () => set({ openPromo: null }),
}));

const MEDIA = 'https://media.miamigraphicsstorage.uk/tpl';
const SITE = 'https://miami-graphics.com';
const num = (v: number | null | undefined) => (v ?? 0).toLocaleString(i18n.language);

const SOC_LINK: Record<string, (v: string) => string> = {
  twitch:   v => v.startsWith('http') ? v : `https://twitch.tv/${v}`,
  youtube:  v => v.startsWith('http') ? v : `https://youtube.com/@${v.replace(/^@/, '')}`,
  telegram: v => v.startsWith('http') ? v : `https://t.me/${v.replace(/^@/, '')}`,
  discord:  v => v.startsWith('http') ? v : `https://discord.gg/${v}`,
  vk:       v => v.startsWith('http') ? v : `https://vk.com/${v}`,
  tiktok:   v => v.startsWith('http') ? v : `https://tiktok.com/@${v.replace(/^@/, '')}`,
};
function cardCrop(src: { cardx?: string; cardy?: string; cards?: string }): CSSProperties {
  const x = +(src.cardx ?? 0) || 0, y = +(src.cardy ?? 0) || 0, s = +(src.cards ?? 100) || 100;
  if (!x && !y && s === 100) return {};
  return { transform: `translate(${x / 2}%, ${y / 2}%) scale(${s / 100})` };
}

const SOC_LABEL: Record<string, string> = {
  twitch: 'Twitch', youtube: 'YouTube', telegram: 'Telegram',
  discord: 'Discord', vk: 'VK', tiktok: 'TikTok',
};

interface AuthorsScreenProps {
  onOpenMod?: (reduxId: string) => void;
  onOpenGunpack?: (packId: string) => void;
}

export function AuthorsScreen({ onOpenMod, onOpenGunpack }: AuthorsScreenProps = {}) {
  const { t } = useTranslation();
  const [makers, setMakers] = useState<ModmakerCard[]>(
    () => readCache<ModmakerCard[]>('authors') ?? []);
  const [loading, setLoading] = useState(makers.length === 0);
  const [q, setQ] = useState('');
  const [open, setOpen] = useState<string | null>(null);
  const [fresh, setFresh] = useState<Record<string, number>>({});
  const isGuest = useSessionStore(s => !s.auth || s.auth.token === 'guest');

  const requested = useAuthorsStore(s => s.openPromo);
  const consume = useAuthorsStore(s => s.consume);
  useEffect(() => {
    if (requested) { setOpen(requested); consume(); }
  }, [requested, consume]);

  useEffect(() => {
    let alive = true;
    bridge.modmakersList()
      .then(r => {
        if (!alive || !r?.ok) return;
        setMakers(r.makers); writeCache('authors', r.makers);
      })
      .finally(() => { if (alive) setLoading(false); });
    if (!isGuest) {
      bridge.modmakerFeed(true).then(r => {
        if (!alive || !r?.ok || !r.follows) return;
        const map: Record<string, number> = {};
        for (const f of r.follows) if (f.due && f.fresh > 0) map[f.promo] = f.fresh;
        setFresh(map);
      }).catch(() => {});
    }
    return () => { alive = false; };
  }, [isGuest]);

  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase();
    if (!s) return makers;
    return makers.filter(m =>
      m.display.toLowerCase().includes(s) || m.promo.toLowerCase().includes(s));
  }, [makers, q]);

  const freshTotal = Object.values(fresh).reduce((a, b) => a + b, 0);

  return (
    <div className="h-full overflow-y-auto px-8 py-6">
      <AnimatePresence mode="wait" initial={false}>
        {open ? (
          <motion.div key={'d:' + open}
            initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }} transition={{ duration: 0.35, ease: EASE_DEPTH }}>
            <AuthorDetail promo={open} isGuest={isGuest} onBack={() => setOpen(null)}
              onOpenMod={onOpenMod} onOpenGunpack={onOpenGunpack} />
          </motion.div>
        ) : (
          <motion.div key="grid"
            initial={{ opacity: 0, y: 14 }} animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }} transition={{ duration: 0.35, ease: EASE_DEPTH }}>

            <div className="flex items-center justify-between gap-4 mb-5 flex-wrap">
              <div>
                <h1 className="text-2xl font-bold text-text-primary tracking-tight">
                  {t('authors.title', 'Авторы')}
                </h1>
                <p className="text-sm text-text-secondary mt-1">
                  {t('authors.subtitle', 'Люди, которые делают моды для Miami Graphics.')}
                </p>
              </div>
              <div className="relative w-72 max-w-full">
                <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-text-muted" />
                <input
                  value={q}
                  onChange={e => setQ(e.target.value)}
                  placeholder={t('authors.searchPlaceholder', 'Поиск автора')}
                  className="w-full h-[42px] pl-10 pr-3 rounded-xl bg-glass-strong backdrop-blur-glass
                             border border-glass-border text-[14px] text-text-primary
                             placeholder:text-text-muted outline-none transition-all
                             focus:border-accent focus:shadow-[0_0_0_3px_var(--accent-soft)]"
                />
              </div>
            </div>

            {freshTotal > 0 && (
              <div className="mb-4 px-4 py-3 rounded-xl border border-accent/40 bg-accent-soft
                              text-[13.5px] text-text-primary flex items-center gap-2.5">
                <BellRing size={16} className="text-accent shrink-0" />
                <span>
                  {t('authors.freshBanner', 'Новые моды у твоих авторов:')}{' '}
                  {Object.entries(fresh).map(([p, n], i) => (
                    <span key={p}>
                      {i > 0 && ', '}
                      <button className="font-semibold text-accent hover:underline"
                              onClick={() => setOpen(p)}>
                        {makers.find(m => m.promo === p)?.display ?? p}
                      </button>{' '}({n})
                    </span>
                  ))}
                </span>
              </div>
            )}

            {loading ? (
              <div className="text-text-muted text-sm py-16 text-center">
                {t('authors.loading', 'Загружаю авторов…')}
              </div>
            ) : filtered.length === 0 ? (
              <div className="text-text-muted text-sm py-16 text-center">
                {makers.length === 0
                  ? t('authors.emptyNone', 'Авторов пока нет.')
                  : t('authors.emptySearch', 'Никого не нашлось.')}
              </div>
            ) : (
              <div className="grid gap-4"
                   style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))' }}>
                {filtered.map(m => (
                  <button key={m.promo} onClick={() => setOpen(m.promo)}
                    className="group text-left rounded-2xl overflow-hidden border border-glass-border
                               bg-glass-strong backdrop-blur-glass transition-all duration-200
                               hover:border-accent/60 hover:-translate-y-0.5 relative">
                    {fresh[m.promo] ? (
                      <span className="absolute top-2.5 right-2.5 z-10 min-w-[22px] h-[22px] px-1.5
                                       rounded-full bg-accent text-[11px] font-bold text-white
                                       flex items-center justify-center">
                        {fresh[m.promo]}
                      </span>
                    ) : null}
                    <div className="aspect-square w-full overflow-hidden bg-white/[0.03]">
                      {m.card ? (
                        <img src={`${MEDIA}/${m.card}`} alt="" loading="lazy"
                             style={cardCrop(m)}
                             className="w-full h-full object-cover" />
                      ) : (
                        <div className="w-full h-full flex items-center justify-center">
                          <Package size={40} className="text-text-muted opacity-40" />
                        </div>
                      )}
                    </div>
                    <div className="p-3.5">
                      <div className="font-bold text-[15px] text-text-primary truncate">{m.display}</div>
                      <div className="mt-1.5 flex items-center gap-3 text-[12px] text-text-secondary">
                        <span className="inline-flex items-center gap-1">
                          <Boxes size={13} className="text-accent" />
                          {t('authors.modsCount', {
                            count: m.mods ?? 0, formatted: num(m.mods),
                            defaultValue: '{{formatted}} модов',
                          })}
                        </span>
                        <span className="inline-flex items-center gap-1">
                          <Download size={13} className="text-accent" />{num(m.downloads)}
                        </span>
                      </div>
                    </div>
                  </button>
                ))}
              </div>
            )}
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function AuthorDetail({ promo, isGuest, onBack, onOpenMod, onOpenGunpack }:
  { promo: string; isGuest: boolean; onBack: () => void;
    onOpenMod?: (reduxId: string) => void; onOpenGunpack?: (packId: string) => void }) {
  const { t } = useTranslation();
  const [d, setD] = useState<ModmakerDetail | null>(null);
  const [following, setFollowing] = useState(false);
  const [followers, setFollowers] = useState<number | null>(null);
  const [canEdit, setCanEdit] = useState(false);
  const [kind, setKind] = useState<'all' | 'redux' | 'gunpack'>('all');
  const [q, setQ] = useState('');
  const requestNavigate = useNavStore(s => s.requestNavigate);

  useEffect(() => {
    let alive = true;
    bridge.modmakerDetail(promo).then(r => {
      if (!alive || !r?.ok) return;
      setD(r); setFollowers(r.followers ?? 0);
    });
    if (!isGuest) {
      bridge.modmakerFeed(false).then(r => {
        if (alive && r?.ok) setFollowing(!!r.follows?.some(f => f.promo === promo));
      }).catch(() => {});
      bridge.modmakerCanEdit(promo).then(r => {
        if (alive && r?.ok) setCanEdit(!!r.can_edit);
      }).catch(() => {});
    }
    return () => { alive = false; };
  }, [promo, isGuest]);

  const toggleFollow = async () => {
    if (isGuest) return;
    const next = !following;
    setFollowing(next);
    try {
      const r = await bridge.modmakerFollow(promo, next);
      if (r?.ok) setFollowers(r.followers ?? null);
      else setFollowing(!next);
    } catch { setFollowing(!next); }
  };

  const mods = useMemo(() => {
    let list: ModmakerMod[] = d?.mods ?? [];
    if (kind !== 'all') list = list.filter(m => m.kind === kind);
    const s = q.trim().toLowerCase();
    if (s) list = list.filter(m => m.name.toLowerCase().includes(s));
    return list;
  }, [d, kind, q]);

  const page = d?.page ?? {};
  const socials = Object.keys(SOC_LINK).filter(k => page[k]);
  const openMod = (m: ModmakerMod) => {
    if (m.kind === 'redux' && onOpenMod) { onOpenMod(m.id); return; }
    if (m.kind === 'gunpack' && onOpenGunpack) { onOpenGunpack(m.id); return; }
    requestNavigate(m.kind === 'redux' ? 'redux' : 'guns');
  };

  return (
    <div>
      <button onClick={onBack}
        className="inline-flex items-center gap-2 text-[13.5px] font-semibold text-text-secondary
                   hover:text-text-primary transition-colors mb-5">
        <ArrowLeft size={15} /> {t('authors.back', 'Авторы')}
      </button>

      <div className="flex items-center gap-5 flex-wrap mb-6 p-5 rounded-2xl border
                      border-glass-border bg-glass-strong backdrop-blur-glass">
        <div className="w-20 h-20 rounded-2xl overflow-hidden bg-white/[0.04] shrink-0
                        border border-glass-border">
          {d?.card ? (
            <img src={`${MEDIA}/${d.card}`} alt="" className="w-full h-full object-cover"
                 style={cardCrop(d.page ?? {})} />
          ) : (
            <div className="w-full h-full flex items-center justify-center">
              <Package size={30} className="text-text-muted opacity-40" />
            </div>
          )}
        </div>
        <div className="min-w-0 flex-1">
          <h1 className="text-[22px] font-bold text-text-primary tracking-tight truncate">
            {d?.display ?? promo}
          </h1>
          <div className="mt-1.5 flex items-center gap-3 flex-wrap text-[12.5px] text-text-secondary">
            <span>{t('authors.modsCount', {
              count: d?.mods?.length ?? 0, formatted: num(d?.mods?.length),
              defaultValue: '{{formatted}} модов',
            })}</span>
            {followers != null && <span>{t('authors.followersCount', {
              count: followers, formatted: num(followers),
              defaultValue: '{{formatted}} подписчиков',
            })}</span>}
            {socials.map(k => (
              <a key={k} href={SOC_LINK[k](page[k])} target="_blank" rel="noopener noreferrer"
                 className="inline-flex items-center gap-1.5 text-accent hover:underline">
                {SOC_LABEL[k]}
              </a>
            ))}
          </div>
        </div>
        <div className="flex items-center gap-2.5">
          <button onClick={toggleFollow} disabled={isGuest}
            title={isGuest ? t('authors.followGuestHint', 'Войди в аккаунт, чтобы подписаться') : undefined}
            className={
              'inline-flex items-center gap-2 h-[40px] px-4 rounded-xl text-[13.5px] font-bold ' +
              'transition-all border ' +
              (following
                ? 'bg-accent-soft border-accent/50 text-accent'
                : 'bg-glass-strong border-glass-border text-text-primary hover:border-accent/50') +
              (isGuest ? ' opacity-45 cursor-not-allowed' : '')
            }>
            {following ? <BellRing size={15} /> : <Bell size={15} />}
            {following
              ? t('authors.following', 'Подписан')
              : t('authors.follow', 'Подписаться')}
          </button>
          <a href={`${SITE}/mod/${promo}`} target="_blank" rel="noopener noreferrer"
             className="inline-flex items-center gap-2 h-[40px] px-4 rounded-xl text-[13.5px]
                        font-bold border border-glass-border bg-glass-strong text-text-primary
                        hover:border-accent/50 transition-all">
            <ExternalLink size={15} /> {t('authors.page', 'Страница')}
          </a>
          {canEdit && (
            <a href="https://media.miamigraphicsstorage.uk/#mods" target="_blank"
               rel="noopener noreferrer"
               title={t('authors.editHint', 'Открыть партнёрскую панель: карточка, моды, страница')}
               className="inline-flex items-center gap-2 h-[40px] px-4 rounded-xl text-[13.5px]
                          font-bold border border-accent/50 bg-accent-soft text-accent
                          hover:border-accent transition-all">
              <Pencil size={15} /> {t('authors.edit', 'Редактировать')}
            </a>
          )}
        </div>
      </div>

      <div className="flex items-center gap-2.5 mb-4 flex-wrap">
        {([
          ['all', 'authors.filterAll', 'Все'],
          ['redux', 'authors.filterRedux', 'Редуксы'],
          ['gunpack', 'authors.filterGunpack', 'Ганпаки'],
        ] as const).map(([k, labelKey, labelDef]) => (
          <button key={k} onClick={() => setKind(k)}
            className={
              'h-[34px] px-3.5 rounded-lg text-[12.5px] font-bold border transition-all ' +
              (kind === k
                ? 'bg-accent-soft border-accent/50 text-accent'
                : 'bg-glass-strong border-glass-border text-text-secondary hover:text-text-primary')
            }>
            {t(labelKey, labelDef)}
          </button>
        ))}
        <div className="relative ml-auto w-60 max-w-full">
          <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted" />
          <input value={q} onChange={e => setQ(e.target.value)} placeholder={t('authors.searchModsPlaceholder', 'Поиск по модам')}
            className="w-full h-[34px] pl-9 pr-3 rounded-lg bg-glass-strong border border-glass-border
                       text-[13px] text-text-primary placeholder:text-text-muted outline-none
                       focus:border-accent transition-all" />
        </div>
      </div>

      {!d ? (
        <div className="text-text-muted text-sm py-12 text-center">{t('authors.detailLoading', 'Загружаю…')}</div>
      ) : mods.length === 0 ? (
        <div className="text-text-muted text-sm py-12 text-center">{t('authors.noMods', 'Модов не нашлось.')}</div>
      ) : (
        <div className="grid gap-4"
             style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(250px, 1fr))' }}>
          {mods.map(m => (
            <button key={m.kind + ':' + m.id} onClick={() => openMod(m)}
              className="group text-left rounded-2xl overflow-hidden border border-glass-border
                         bg-glass-strong backdrop-blur-glass transition-all duration-200
                         hover:border-accent/60 hover:-translate-y-0.5">
              <div className="aspect-video w-full overflow-hidden bg-white/[0.03]">
                {m.cover ? (
                  <img src={m.cover} alt="" loading="lazy"
                       className="w-full h-full object-cover transition-transform duration-300
                                  group-hover:scale-[1.04]" />
                ) : (
                  <div className="w-full h-full flex items-center justify-center">
                    {m.kind === 'redux'
                      ? <Layers size={30} className="text-text-muted opacity-40" />
                      : <Crosshair size={30} className="text-text-muted opacity-40" />}
                  </div>
                )}
              </div>
              <div className="p-3.5">
                <div className="text-[10.5px] font-bold tracking-widest uppercase text-accent mb-1">
                  {m.kind === 'redux'
                    ? t('authors.kindRedux', 'Редукс')
                    : t('authors.kindGunpack', 'Ганпак')}
                </div>
                <div className="font-bold text-[14.5px] text-text-primary truncate">{m.name}</div>
                <div className="mt-1.5 flex items-center gap-3 text-[12px] text-text-secondary">
                  <span className="inline-flex items-center gap-1">
                    <Download size={12} className="text-accent" />{num(m.downloads)}
                  </span>
                  {m.month != null && <span>{t('authors.perMonth', {
                    count: m.month, formatted: num(m.month),
                    defaultValue: '{{formatted}} за месяц',
                  })}</span>}
                </div>
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
