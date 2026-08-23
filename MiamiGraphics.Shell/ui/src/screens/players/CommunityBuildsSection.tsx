import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, type Variants } from 'framer-motion';
import { Loader2 } from 'lucide-react';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { PlayerCard, buildToProPlayer } from './PlayersScreen';
import { PlayerDetailScreen } from './PlayerDetailScreen';
import type { ProPlayer } from './playersApi';

const cardV: Variants = {
  hidden:  { opacity: 0, y: 12 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.4, ease: [0.22, 1, 0.36, 1] } },
};

const VIEWED_LOCAL_KEY = 'miamiGraphics.players.viewed';
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

interface Props {
  search: string;
}

export function CommunityBuildsSection({ search }: Props) {
  const { t, i18n } = useTranslation();
  const isRu = (i18n.language || 'ru').startsWith('ru');

  const builds     = useUserBuildsStore(s => s.builds);
  const loading    = useUserBuildsStore(s => s.loading);
  const loadBuilds = useUserBuildsStore(s => s.load);

  useEffect(() => { void loadBuilds(); }, [loadBuilds]);

  const visible = useMemo<ProPlayer[]>(() => {
    const q = search.trim().toLowerCase();
    const list = q
      ? builds.filter(b =>
          b.name.toLowerCase().includes(q)
       || b.author.toLowerCase().includes(q)
       || b.hntCode.toLowerCase().includes(q))
      : builds;
    const sorted = [...list].sort((a, b) => {
      const ta = a.tier ?? 99, tb = b.tier ?? 99;
      if (ta !== tb) return ta - tb;
      return b.createdAt - a.createdAt;
    });
    return sorted.map(buildToProPlayer);
  }, [builds, search]);

  const [viewed, setViewed] = useState<Set<string>>(() => loadViewed());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const selectedPlayer = useMemo(
    () => visible.find(p => p.id === selectedId) ?? null,
    [visible, selectedId]);

  if (loading && builds.length === 0) {
    return (
      <section className="py-6 flex items-center justify-center text-text-muted gap-2">
        <Loader2 size={14} className="animate-spin" />
        <span className="text-[12px]">{t('players.communityLoading', 'Загружаем сборки сообщества…')}</span>
      </section>
    );
  }

  if (selectedPlayer) {
    return (
      <PlayerDetailScreen
        player={selectedPlayer}
        isRu={isRu}
        onBack={() => setSelectedId(null)}
      />
    );
  }

  if (visible.length === 0) {
    return (
      <div className="py-20 text-center text-text-muted text-sm">
        {search
          ? t('players.searchEmpty')
          : t('players.communityEmpty', 'Пока нет ни одной сборки.')}
      </div>
    );
  }

  const onCardClick = (p: ProPlayer) => {
    setViewed(prev => {
      if (prev.has(p.id)) return prev;
      const next = new Set(prev);
      next.add(p.id);
      persistViewed(next);
      return next;
    });
    setSelectedId(p.id);
  };

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-4">
      {visible.map(p => (
        <motion.div key={p.id} variants={cardV} initial="hidden" animate="visible">
          <PlayerCard
            player={p}
            isRu={isRu}
            isViewed={viewed.has(p.id)}
            viewedLabel={t('players.viewed')}
            onClick={() => onCardClick(p)}
          />
        </motion.div>
      ))}
    </div>
  );
}
