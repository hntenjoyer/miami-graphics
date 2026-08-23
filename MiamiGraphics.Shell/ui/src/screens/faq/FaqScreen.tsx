import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import {
  HelpCircle, ChevronDown, Sparkles, Shield, Layers, Crosshair, Users,
  Trophy, Settings as SettingsIcon, Eye, Download, Zap, Heart, Info,
  type LucideIcon,
} from 'lucide-react';
import { GlassPanel } from '@/design';
import { bridge } from '@/bridge';
import logoSrc from '@/assets/logo/favicon.png';

const container: Variants = {
  hidden: { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.05, staggerChildren: 0.07 } },
};
const item: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] } },
};

interface FaqEntry {
  q: string;
  a: string;
}
interface FaqGroup {
  id: string;
  title: string;
  icon: LucideIcon;
  entries: FaqEntry[];
}

interface Feature {
  id: string;
  icon: LucideIcon;
  titleKey: string;
  descKey:  string;
}

const FEATURES: Feature[] = [
  { id: 'redux',   icon: Layers,    titleKey: 'faq.features.redux.title',   descKey: 'faq.features.redux.desc' },
  { id: 'guns',    icon: Crosshair, titleKey: 'faq.features.guns.title',    descKey: 'faq.features.guns.desc' },
  { id: 'players', icon: Users,     titleKey: 'faq.features.players.title', descKey: 'faq.features.players.desc' },
  { id: 'install', icon: Download,  titleKey: 'faq.features.install.title', descKey: 'faq.features.install.desc' },
  { id: 'settings',icon: SettingsIcon, titleKey: 'faq.features.settings.title', descKey: 'faq.features.settings.desc' },
  { id: 'safety',  icon: Shield,    titleKey: 'faq.features.safety.title',  descKey: 'faq.features.safety.desc' },
];

export function FaqScreen() {
  const { t } = useTranslation();

  const [appVersion, setAppVersion] = useState('');
  useEffect(() => {
    let alive = true;
    bridge.getAppVersion()
      .then(v => { if (alive && v) setAppVersion(v); })
      .catch(() => {  });
    return () => { alive = false; };
  }, []);

  const groups: FaqGroup[] = [
    {
      id: 'getting-started',
      title: t('faq.groups.gettingStarted.title'),
      icon: Sparkles,
      entries: [
        { q: t('faq.groups.gettingStarted.q1'), a: t('faq.groups.gettingStarted.a1') },
        { q: t('faq.groups.gettingStarted.q2'), a: t('faq.groups.gettingStarted.a2') },
        { q: t('faq.groups.gettingStarted.q3'), a: t('faq.groups.gettingStarted.a3') },
      ],
    },
    {
      id: 'mods',
      title: t('faq.groups.mods.title'),
      icon: Layers,
      entries: [
        { q: t('faq.groups.mods.q1'), a: t('faq.groups.mods.a1') },
        { q: t('faq.groups.mods.q2'), a: t('faq.groups.mods.a2') },
        { q: t('faq.groups.mods.q3'), a: t('faq.groups.mods.a3') },
        { q: t('faq.groups.mods.q4'), a: t('faq.groups.mods.a4') },
      ],
    },
    {
      id: 'install',
      title: t('faq.groups.install.title'),
      icon: Download,
      entries: [
        { q: t('faq.groups.install.q1'), a: t('faq.groups.install.a1') },
        { q: t('faq.groups.install.q2'), a: t('faq.groups.install.a2') },
        { q: t('faq.groups.install.q3'), a: t('faq.groups.install.a3') },
      ],
    },
    {
      id: 'safety',
      title: t('faq.groups.safety.title'),
      icon: Shield,
      entries: [
        { q: t('faq.groups.safety.q1'), a: t('faq.groups.safety.a1') },
        { q: t('faq.groups.safety.q2'), a: t('faq.groups.safety.a2') },
        { q: t('faq.groups.safety.q3'), a: t('faq.groups.safety.a3') },
      ],
    },
  ];

  return (
    <div className="h-full overflow-y-auto">
      <motion.div
        className="max-w-[1280px] mx-auto px-8 py-8 flex flex-col gap-10"
        variants={container}
        initial="hidden"
        animate="visible"
      >
        {}
        <motion.section
          variants={item}
          className="grid grid-cols-1 md:grid-cols-[1fr_280px] gap-8 items-center"
        >
          <div className="flex flex-col gap-4">
            <span className="inline-flex items-center gap-2 self-start px-3 py-1.5 rounded-full
                             bg-glass-strong border border-glass-border
                             text-[10px] uppercase tracking-[0.22em] text-text-muted">
              <HelpCircle size={12} className="text-accent" />
              {t('faq.heroBadge')}
            </span>
            <h1 className="font-display font-extrabold text-4xl md:text-5xl text-text-primary uppercase tracking-tight leading-[1.05]">
              {t('faq.heroTitleA')}{' '}
              <span className="text-accent">{t('faq.heroTitleB')}</span>
            </h1>
            <p className="text-sm md:text-base text-text-secondary leading-relaxed max-w-[640px]">
              {t('faq.heroBody')}
            </p>
          </div>

          {}
          <div className="hidden md:flex justify-center relative">
            <div className="absolute inset-0 -z-0 blur-3xl opacity-40 pointer-events-none"
                 style={{ background: 'radial-gradient(circle at 50% 50%, var(--accent) 0%, transparent 70%)' }} />
            <motion.img
              src={logoSrc}
              alt=""
              draggable={false}
              animate={{ y: [-6, 6, -6] }}
              transition={{ duration: 5, repeat: Infinity, ease: 'easeInOut' }}
              className="relative w-[220px] h-[220px] object-contain
                         drop-shadow-[0_8px_30px_color-mix(in srgb, var(--accent) 45%, transparent)]
                         select-none"
            />
          </div>
        </motion.section>

        {}
        <motion.section variants={item} className="flex flex-col gap-4">
          <header className="flex items-baseline gap-3">
            <h2 className="font-display font-bold text-2xl text-text-primary uppercase tracking-tight">
              {t('faq.about.title')}
            </h2>
            <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted">
              {t('faq.about.subtitle')}
            </span>
          </header>

          <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-6">
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              {}
              <div className="flex flex-col gap-2">
                <div className="inline-flex items-center gap-2 text-[10px] uppercase tracking-[0.22em] text-text-muted">
                  <Info size={12} className="text-accent" />
                  {t('faq.about.whatLabel')}
                </div>
                <p className="text-sm text-text-primary font-semibold">
                  {t('faq.about.whatTitle')}
                </p>
                <p className="text-xs text-text-secondary leading-relaxed">
                  {t('faq.about.whatBody')}
                </p>
              </div>

              {}
              <div className="flex flex-col gap-2 md:px-6 md:border-l md:border-r md:border-glass-border">
                <div className="inline-flex items-center gap-2 text-[10px] uppercase tracking-[0.22em] text-text-muted">
                  <Sparkles size={12} className="text-accent" />
                  {t('faq.about.versionLabel')}
                </div>
                <p className="text-sm text-text-primary font-semibold tabular-nums">
                  {t('faq.about.versionValue', { version: appVersion || '…' })}
                </p>
                <p className="text-xs text-text-secondary leading-relaxed">
                  {t('faq.about.versionBody')}
                </p>
              </div>

              {}
              <div className="flex flex-col gap-2">
                <div className="inline-flex items-center gap-2 text-[10px] uppercase tracking-[0.22em] text-text-muted">
                  <Heart size={12} className="text-accent" />
                  {t('faq.about.helpLabel')}
                </div>
                <p className="text-sm text-text-primary font-semibold">
                  {t('faq.about.helpTitle')}
                </p>
                <p className="text-xs text-text-secondary leading-relaxed">
                  {t('faq.about.helpBody')}
                </p>
              </div>
            </div>
          </GlassPanel>
        </motion.section>

        {}
        <motion.section variants={item} className="flex flex-col gap-4">
          <header className="flex items-baseline gap-3">
            <h2 className="font-display font-bold text-2xl text-text-primary uppercase tracking-tight">
              {t('faq.featuresTitle')}
            </h2>
            <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted">
              {t('faq.featuresSubtitle')}
            </span>
          </header>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 2xl:grid-cols-4 gap-4">
            {FEATURES.map((f, idx) => (
              <FeatureCard
                key={f.id}
                feature={f}
                index={idx}
                title={t(f.titleKey)}
                desc={t(f.descKey)}
              />
            ))}
          </div>
        </motion.section>

        {}
        <motion.section variants={item} className="flex flex-col gap-6">
          <header className="flex items-baseline gap-3">
            <h2 className="font-display font-bold text-2xl text-text-primary uppercase tracking-tight">
              {t('faq.faqTitle')}
            </h2>
            <span className="text-[11px] uppercase tracking-[0.22em] text-text-muted">
              {t('faq.faqSubtitle')}
            </span>
          </header>

          <div className="flex flex-col gap-5">
            {groups.map(g => (
              <FaqGroupView key={g.id} group={g} />
            ))}
          </div>
        </motion.section>

        {}
        <motion.section variants={item}>
          <GlassPanel depth="z2" tint="strong" rounded="3xl" className="p-7 flex flex-col md:flex-row items-start md:items-center gap-5">
            <div className="w-14 h-14 rounded-2xl bg-accent-soft text-accent flex items-center justify-center shrink-0 shadow-z1">
              <Heart size={24} />
            </div>
            <div className="flex-1 min-w-0">
              <h3 className="font-display font-bold text-xl text-text-primary uppercase tracking-wide">
                {t('faq.ctaTitle')}
              </h3>
              <p className="text-sm text-text-secondary leading-relaxed mt-1">
                {t('faq.ctaBody')}
              </p>
            </div>
          </GlassPanel>
        </motion.section>
      </motion.div>
    </div>
  );
}

function FeatureCard({
  feature, title, desc, index,
}: {
  feature: Feature;
  title: string;
  desc: string;
  index: number;
}) {
  const Icon = feature.icon;
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.45, delay: 0.05 + index * 0.06, ease: [0.22, 1, 0.36, 1] }}
      whileHover={{ y: -4 }}
      className="group relative rounded-2xl bg-bg-surface border border-glass-border
                 p-5 flex flex-col gap-3 cursor-default
                 transition-[border-color,box-shadow] duration-300 ease-out
                 hover:border-accent/40 hover:shadow-glow-accent"
    >
      {}
      <div
        aria-hidden="true"
        className="absolute inset-0 rounded-2xl pointer-events-none opacity-0 group-hover:opacity-100 transition-opacity duration-500"
        style={{ background: 'radial-gradient(circle at 30% 0%, var(--accent-soft) 0%, transparent 60%)' }}
      />
      <div className="relative w-12 h-12 rounded-xl bg-accent-soft text-accent flex items-center justify-center shadow-z1">
        <Icon size={22} />
      </div>
      <h3 className="relative font-display font-bold text-base text-text-primary uppercase tracking-wide">
        {title}
      </h3>
      <p className="relative text-sm text-text-secondary leading-relaxed">
        {desc}
      </p>
    </motion.div>
  );
}

function FaqGroupView({ group }: { group: FaqGroup }) {
  const Icon = group.icon;
  const [openIdx, setOpenIdx] = useState<number | null>(null);

  return (
    <div className="rounded-2xl bg-bg-surface border border-glass-border overflow-hidden">
      <header className="flex items-center gap-3 px-5 py-4 border-b border-glass-border">
        <div className="w-8 h-8 rounded-lg bg-accent-soft text-accent flex items-center justify-center shrink-0">
          <Icon size={15} />
        </div>
        <h3 className="font-display font-bold text-sm text-text-primary uppercase tracking-[0.18em]">
          {group.title}
        </h3>
        <span className="ml-auto text-[10px] uppercase tracking-[0.22em] text-text-muted tabular-nums">
          {group.entries.length}
        </span>
      </header>

      <ul className="divide-y divide-glass-border">
        {group.entries.map((entry, idx) => {
          const isOpen = openIdx === idx;
          return (
            <li key={idx}>
              <button
                type="button"
                onClick={() => setOpenIdx(isOpen ? null : idx)}
                aria-expanded={isOpen}
                style={{ outline: 'none' }}
                className="w-full px-5 py-4 flex items-center gap-3 text-left
                           hover:bg-glass transition-colors"
              >
                <span className="flex-1 text-sm font-medium text-text-primary leading-snug">
                  {entry.q}
                </span>
                <motion.span
                  animate={{ rotate: isOpen ? 180 : 0 }}
                  transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
                  className={
                    'shrink-0 w-7 h-7 rounded-md flex items-center justify-center transition-colors ' +
                    (isOpen
                      ? 'bg-accent text-text-on-accent'
                      : 'bg-bg-elevated text-text-muted')
                  }
                >
                  <ChevronDown size={14} />
                </motion.span>
              </button>

              {}
              <AnimatePresence initial={false}>
                {isOpen && (
                  <motion.div
                    key="answer"
                    initial={{ opacity: 0, height: 0 }}
                    animate={{ opacity: 1, height: 'auto' }}
                    exit={{ opacity: 0, height: 0 }}
                    transition={{ duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
                    className="overflow-hidden"
                  >
                    <div className="px-5 pb-5 -mt-1 text-sm text-text-secondary leading-relaxed whitespace-pre-line">
                      {entry.a}
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </li>
          );
        })}
      </ul>
    </div>
  );
}

export const _FAQ_FEATURES = FEATURES;

export const _ICONS = { Trophy, Eye, Zap };
