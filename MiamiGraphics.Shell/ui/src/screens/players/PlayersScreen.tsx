import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { Eye, AlertTriangle, Trophy, Search, CheckCircle2, Plus, Users } from 'lucide-react';
import { AccentLoader, EASE_DEPTH } from '@/design';
import { type ProPlayer } from './playersApi';
import { useUserBuildsStore, type UserBuild } from '@/store/userBuildsStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { PlayerDetailScreen } from './PlayerDetailScreen';
import { SubmitProfileScreen } from './SubmitProfileScreen';
import { MyBuildsPanel } from './MyBuildsPanel';
import { CommunityBuildsSection } from './CommunityBuildsSection';
import { useSessionStore } from '@/store/sessionStore';

const container: Variants = {
  hidden: { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.05, staggerChildren: 0.05 } },
};
const item: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] } },
};

const VIEWED_LOCAL_KEY  = 'miamiGraphics.players.viewed';
const SCROLL_SESSION_KEY = 'miamiGraphics.players.scroll';

function loadViewed(): Set<string> {
  try {
    const raw = window.localStorage.getItem(VIEWED_LOCAL_KEY);
    if (!raw) return new Set();
    const arr = JSON.parse(raw);
    return Array.isArray(arr) ? new Set(arr.filter((x: unknown) => typeof x === 'string')) : new Set();
  } catch { return new Set(); }
}
function persistViewed(s: Set<string>): void {
  try { window.localStorage.setItem(VIEWED_LOCAL_KEY, JSON.stringify([...s])); } catch {  }
}

export function buildToProPlayer(b: UserBuild): ProPlayer {
  const youtubeId = extractYoutubeId(b.videoUrl);
  return {
    id:            'pro:' + b.id,
    name:          b.name || b.author || 'PRO',
    roleEn:        b.categoryLabel,
    roleRu:        b.categoryLabel,
    descriptionEn: null,
    descriptionRu: null,
    image:         b.coverUrl,
    tier:          b.tier ? `tier-${b.tier}` : null,
    youtube:       b.videoUrl,
    twitch:        null,
    discord:       null,
    videoIds:      youtubeId ? [youtubeId] : [],

    reduxLink:     null,
    reduxId:       b.reduxId || null,
    reduxName:     b.reduxNameSnapshot || null,
    gunpackId:     b.gunpackId || null,
    gunpackName:   b.gunpackNameSnapshot || null,
    settingsLink:  b.settingsXmlUrl,
    specs: {
      dpi:         b.dpi,
      sensitivity: b.sensitivity,
      resolution:  b.resolution,
      hz:          b.fpsAvg,
      family:      b.family,
    },
    devices:       b.devices as unknown as Record<string, unknown>,
    createdAt:     new Date(b.createdAt).toISOString(),
    views:         b.downloadCount,
    lastUpdated:   null,
  };
}

function extractYoutubeId(url: string | null): string | null {
  if (!url) return null;

  const m = url.match(/(?:youtu\.be\/|v=|embed\/|shorts\/)([A-Za-z0-9_-]{11})/);
  return m ? m[1] : null;
}

export function PlayersScreen() {
  const { t, i18n } = useTranslation();
  const lang = i18n.language || 'ru';
  const isRu = lang.startsWith('ru');

  const [players] = useState<ProPlayer[]>([]);
  const [loading] = useState(false);
  const [error]   = useState<string | null>(null);
  const [search, setSearch]   = useState('');
  const [viewed, setViewed]   = useState<Set<string>>(() => loadViewed());

  const [category, setCategory] = useState<'pro' | 'community'>('pro');

  const [selectedId, setSelectedId] = useState<string | null>(null);

  const [submitOpen, setSubmitOpen] = useState(false);

  const draftReduxId   = useSubmitDraftStore(s => s.reduxId);
  const draftGunpackId = useSubmitDraftStore(s => s.gunpackId);
  const draftReturnTo  = useSubmitDraftStore(s => s.returnTo);
  useEffect(() => {

    if (draftReturnTo !== 'players') return;
    if (draftReduxId || draftGunpackId) {
      setSubmitOpen(true);
    }
  }, [draftReduxId, draftGunpackId, draftReturnTo]);

  const listScrollSnapshot = useRef<number>(0);

  const auth = useSessionStore(s => s.auth);
  const isGuest = !(auth?.token?.startsWith('local-'));
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const scrollRef = useRef<HTMLDivElement | null>(null);

  const internalBuilds      = useUserBuildsStore(s => s.builds);
  const internalLoadBuilds  = useUserBuildsStore(s => s.load);
  useEffect(() => {
    if (internalBuilds.length === 0) void internalLoadBuilds();
  }, [internalBuilds.length, internalLoadBuilds]);

  const mergedPro = useMemo<ProPlayer[]>(() => {
    const internal = internalBuilds
      .filter(b => b.tier !== null)
      .map(buildToProPlayer);
    return [...internal, ...players];
  }, [internalBuilds, players]);

  useEffect(() => {
    const el = scrollRef.current;
    if (!el) return;
    const saved = window.sessionStorage.getItem(SCROLL_SESSION_KEY);
    if (saved) {

      const y = parseFloat(saved);
      if (!Number.isNaN(y)) requestAnimationFrame(() => { el.scrollTop = y; });
    }
    return () => {
      try { window.sessionStorage.setItem(SCROLL_SESSION_KEY, String(el.scrollTop)); } catch {  }
    };
  }, []);

  useEffect(() => {
    if (loading || players.length === 0) return;
    const el = scrollRef.current;
    if (!el) return;
    const saved = window.sessionStorage.getItem(SCROLL_SESSION_KEY);
    if (!saved) return;
    const y = parseFloat(saved);
    if (Number.isNaN(y)) return;
    requestAnimationFrame(() => { el.scrollTop = y; });
  }, [loading, players.length]);

  const visible = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return mergedPro;
    return mergedPro.filter(p =>
      p.name.toLowerCase().includes(q)
      || (p.roleEn ?? '').toLowerCase().includes(q)
      || (p.roleRu ?? '').toLowerCase().includes(q));
  }, [mergedPro, search]);

  const onCardClick = (p: ProPlayer) => {

    setViewed(prev => {
      if (prev.has(p.id)) return prev;
      const next = new Set(prev);
      next.add(p.id);
      persistViewed(next);
      return next;
    });

    if (scrollRef.current) {
      listScrollSnapshot.current = scrollRef.current.scrollTop;

      scrollRef.current.scrollTo({ top: 0, behavior: 'smooth' });
    }
    setSelectedId(p.id);
  };

  const [pendingScrollRestore, setPendingScrollRestore] = useState(false);

  useEffect(() => {
    if (selectedId !== null) return;
    const el = scrollRef.current;
    if (!el) return;
    const y = listScrollSnapshot.current;
    if (!y) return;
    requestAnimationFrame(() => requestAnimationFrame(() => {
      if (scrollRef.current) scrollRef.current.scrollTop = y;
    }));
  }, [selectedId]);

  const selectedPlayer = useMemo(
    () => mergedPro.find(p => p.id === selectedId) ?? null,
    [mergedPro, selectedId],
  );

  return (
    <div ref={scrollRef} className="h-full overflow-y-auto">
      <AnimatePresence mode="wait" initial={false}>
        {submitOpen ? (
          <motion.div
            key="submit"
            initial={{ opacity: 0, y: 10 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -10 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
            className="h-full"
          >
            <SubmitProfileScreen
              onClose={() => setSubmitOpen(false)}
              onSubmitted={() => {}}
            />
          </motion.div>
        ) : selectedPlayer ? (
          <motion.div
            key={`detail:${selectedPlayer.id}`}
            initial={{ opacity: 0, scale: 0.99 }}
            animate={{ opacity: 1, scale: 1 }}
            exit={{ opacity: 0, scale: 0.99 }}
            transition={{ duration: 0.28, ease: EASE_DEPTH }}
          >
            <PlayerDetailScreen
              player={selectedPlayer}
              isRu={isRu}
              onBack={() => { setPendingScrollRestore(true); setSelectedId(null); }}
            />
          </motion.div>
        ) : (
          <motion.div
            key="list"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            onAnimationStart={() => {

              if (!pendingScrollRestore) return;
              const el = scrollRef.current;
              const y = listScrollSnapshot.current;
              if (el && y) el.scrollTop = y;
              setPendingScrollRestore(false);
            }}
          >
      <motion.div
        className="max-w-[1500px] mx-auto px-8 py-8 flex flex-col gap-8"
        variants={container}
        initial="hidden"
        animate="visible"
      >
        {}
        <motion.div variants={item} className="flex items-center gap-3 flex-wrap">
          <CategoryChip
            active={category === 'pro'}
            label="PRO Players"
            icon={<Trophy size={14} strokeWidth={2} />}
            onClick={() => setCategory('pro')}
          />
          <CategoryChip
            active={category === 'community'}
            label="All Players"
            icon={<Users size={14} strokeWidth={2} />}
            onClick={() => setCategory('community')}
          />
          <div className="flex-1 min-w-[120px]" />
          {}
          {category === 'community' && (
            <button
              type="button"
              onClick={() => { if (!isGuest) setSubmitOpen(true); }}
              disabled={isGuest}
              title={isGuest ? 'Войди в аккаунт, чтобы отправить свою сборку' : 'Отправить свою сборку на одобрение'}
              className="inline-flex items-center gap-2 px-4 h-10 rounded-xl text-sm font-medium
                         bg-accent text-text-on-accent
                         hover:bg-accent-hover shadow-glow-accent
                         disabled:opacity-40 disabled:cursor-not-allowed disabled:shadow-none
                         transition-colors"
            >
              <Plus size={14} />
              <span>Отправить свою сборку</span>
            </button>
          )}
          <div className="relative w-full sm:w-[320px]">
            <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted pointer-events-none" />
            <input
              type="text"
              value={search}
              onChange={e => setSearch(e.target.value)}
              placeholder={t('players.searchPlaceholder')}
              className="w-full pl-9 pr-3 h-10 rounded-xl
                         bg-glass-strong border border-transparent
                         text-sm text-text-primary placeholder:text-text-muted
                         outline-none focus:border-accent transition-colors"
            />
          </div>
        </motion.div>

        {}
        {userId && (
          <motion.div variants={item}>
            <MyBuildsPanel userId={userId} />
          </motion.div>
        )}

        {}
        {category === 'pro' ? (
          loading ? (
            <div className="py-20 flex flex-col items-center justify-center text-text-muted gap-3">
              <AccentLoader size={36} />
              <span className="text-xs uppercase tracking-[0.22em]">{t('players.loading')}</span>
            </div>
          ) : error ? (
            <div className="py-12 px-5 rounded-2xl border border-red-500/40 bg-red-500/10
                            flex items-start gap-3">
              <AlertTriangle size={18} className="text-red-300 shrink-0 mt-0.5" />
              <div className="flex flex-col gap-1 text-sm">
                <span className="font-semibold text-text-primary">{t('players.errorTitle')}</span>
                <code className="text-xs text-text-muted font-mono break-all">{error}</code>
              </div>
            </div>
          ) : visible.length === 0 ? (
            <div className="py-20 text-center text-text-muted text-sm">
              {search ? t('players.searchEmpty') : t('players.empty')}
            </div>
          ) : (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
              {visible.map(p => (
                <PlayerCard
                  key={p.id}
                  player={p}
                  isRu={isRu}
                  isViewed={viewed.has(p.id)}
                  viewedLabel={t('players.viewed')}
                  onClick={() => onCardClick(p)}
                />
              ))}
            </div>
          )
        ) : (

          <motion.div variants={item}>
            <CommunityBuildsSection search={search} />
          </motion.div>
        )}
      </motion.div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function CategoryChip({
  active, label, icon, onClick,
}: {
  active: boolean;
  label:  string;
  icon:   React.ReactNode;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={label}
      style={{
        boxShadow: active
          ? '0 0 0 1px rgba(255,255,255,0.30), 0 4px 12px -4px rgba(0,0,0,0.35), inset 0 1px 0 rgba(255,255,255,0.85)'
          : undefined,
      }}
      className={
        'group relative inline-flex items-center gap-2 h-[42px] px-[18px] rounded-2xl ' +
        'overflow-hidden text-[10.5px] font-bold uppercase tracking-[0.16em] ' +
        'transition-[background-color,color,border-color,box-shadow] duration-300 ease-smooth ' +
        'border focus-visible:outline-none ' +
        (active
          ? 'bg-white text-black border-white'
          : 'bg-white/[0.04] text-text-secondary border-white/[0.06] ' +
            'hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]')
      }
    >
      <span className="relative shrink-0">{icon}</span>
      <span className="relative leading-none">{label}</span>
    </button>
  );
}

interface PlayerCardProps {
  player:      ProPlayer;
  isRu:        boolean;
  isViewed:    boolean;
  viewedLabel: string;
  onClick:     () => void;
}

export function PlayerCard({ player, isRu, isViewed, viewedLabel, onClick }: PlayerCardProps) {
  const role = (isRu ? player.roleRu : player.roleEn) ?? player.roleEn ?? player.roleRu ?? '';
  const tierLabel = role.toUpperCase();
  const [imgFailed, setImgFailed] = useState(false);
  const showImage = !!player.image && !imgFailed;

  return (
    <article
      role="button"
      tabIndex={0}
      onClick={onClick}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick(); } }}
      className="group relative flex flex-col rounded-2xl overflow-hidden cursor-pointer text-left
                 bg-bg-surface border border-transparent
                 hover:border-accent/50 hover:shadow-glow-accent
                 focus-visible:outline-none focus-visible:border-accent/60 focus-visible:shadow-glow-accent
                 transition-[box-shadow,border-color] duration-300 ease-smooth"
    >
      {}
      {}
      <div className="relative w-full aspect-[15/17] overflow-hidden bg-bg-surface">
        {showImage ? (
          <img
            src={player.image!}
            alt={player.name}
            draggable={false}
            loading="lazy"
            className="absolute inset-0 w-full h-full object-cover select-none
                       transform-gpu transition-transform duration-[1100ms] ease-smooth
                       group-hover:scale-[1.04]"
            onError={() => setImgFailed(true)}
          />
        ) : (
          <div className="absolute inset-0 flex flex-col items-center justify-center gap-2
                          text-text-muted bg-gradient-to-br from-bg-elevated via-bg-base to-bg-elevated">
            <Trophy size={56} className="opacity-25" />
            <span className="text-[10px] uppercase tracking-[0.22em] opacity-60">
              {player.name}
            </span>
          </div>
        )}

        {}
        {tierLabel && (
          <div className="absolute top-3 right-3 z-10 inline-flex items-center gap-1.5
                          px-2.5 py-1 rounded-md bg-black/60 backdrop-blur-md
                          border border-transparent
                          text-[10px] font-bold uppercase tracking-wider text-white">
            <span className="w-1.5 h-1.5 rounded-full bg-accent" />
            <span>{tierLabel}</span>
          </div>
        )}

        {}
        {isViewed && (
          <div
            className="absolute top-3 left-3 z-10 w-6 h-6 rounded-full
                       bg-green-500/85 text-white flex items-center justify-center
                       shadow-[0_2px_6px_rgba(0,0,0,0.4)] border border-green-400/40"
            title={viewedLabel}
          >
            <CheckCircle2 size={13} strokeWidth={2.5} />
          </div>
        )}

        {}
        <div className="absolute inset-x-0 bottom-0 h-32 pointer-events-none
                        bg-gradient-to-t from-bg-surface via-black/40 to-transparent" />
      </div>

      {}
      <div className="px-5 pt-4 pb-4 flex flex-col gap-3 bg-bg-surface">
        <h3 className="font-display font-extrabold italic text-2xl text-text-primary uppercase
                       tracking-wide truncate">
          {player.name}
        </h3>
        <div className="h-px bg-glass-border" />
        <div className="flex items-center gap-2 text-sm text-text-muted">
          <Eye size={14} />
          <span className="tabular-nums">{formatViews(player.views)}</span>
          {isViewed && (
            <span className="ml-auto inline-flex items-center gap-1 text-[10px] uppercase
                             tracking-wider text-green-300 font-semibold">
              <CheckCircle2 size={11} />
              <span>{viewedLabel}</span>
            </span>
          )}
        </div>
      </div>
    </article>
  );
}

function formatViews(n: number): string {
  if (!n) return '0';
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 10_000)    return Math.round(n / 1_000) + 'k';
  return n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
}
