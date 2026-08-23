import { useState } from 'react';
import { motion, type Variants } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { Boxes, Layers, Share2, Star, Search, Loader2 } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { useUserBuildsStore } from '@/store/userBuildsStore';

interface Props {
  onNavigate: (id: string) => void;
}

export function BuildsHubScreen({ onNavigate }: Props) {
  const { t } = useTranslation();
  const openByHntCode = useUserBuildsStore(s => s.openByHntCode);

  const [code, setCode] = useState('');
  const [finding, setFinding] = useState(false);
  const [findError, setFindError] = useState<string | null>(null);
  const onFind = async () => {
    const c = code.trim();
    if (!c || finding) return;
    setFinding(true);
    setFindError(null);
    try {
      const build = await openByHntCode(c);
      if (build) onNavigate('user-builds');
      else setFindError(t('buildsHub.tryNotFound', 'Сборка с таким кодом не найдена.'));
    } catch (e) {
      setFindError(e instanceof Error ? e.message : t('buildsHub.tryFailed', 'Не удалось найти сборку.'));
    } finally {
      setFinding(false);
    }
  };

  const pageV: Variants = {
    hidden:  { opacity: 0, y: 14 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.34, ease: EASE_DEPTH, staggerChildren: 0.08 } },
  };
  const itemV: Variants = {
    hidden:  { opacity: 0, y: 14 },
    visible: { opacity: 1, y: 0, transition: { duration: 0.4, ease: EASE_DEPTH } },
  };

  const features: Array<{ icon: React.ReactNode; title: string; body: string; action?: React.ReactNode }> = [
    {
      icon: <Layers size={18} strokeWidth={2.2} />,
      title: t('buildsHub.feature1Title', 'Всё в одной сборке'),
      body:  t('buildsHub.feature1Body', 'Редукс, ганпак с заменой пушек, броня, арена, миникарта, прицел и звуки - одним пакетом.'),
    },
    {
      icon: <Share2 size={18} strokeWidth={2.2} />,
      title: t('buildsHub.feature2Title', 'Делись по HNT-коду'),
      body:  t('buildsHub.feature2Body', 'У каждой сборки есть код. Скинул другу - он ставит твою сборку в один клик.'),
      action: (
        <form
          onSubmit={(e) => { e.preventDefault(); void onFind(); }}
          className="mt-auto pt-1 flex flex-col gap-2"
        >
          <span className="text-[10px] uppercase tracking-[0.18em] text-accent font-bold">
            {t('buildsHub.tryEyebrow', 'Попробуй')}
          </span>
          <div className="flex items-center gap-2">
            <div className="relative flex-1 min-w-0 overflow-hidden rounded-lg border border-white/[0.10]
                            bg-bg-elevated/55 backdrop-blur-xl focus-within:border-accent transition-colors">
              <span
                aria-hidden
                className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                           bg-gradient-to-r from-transparent via-white/40 to-transparent"
              />
              <input
                value={code}
                onChange={(e) => { setCode(e.target.value); setFindError(null); }}
                placeholder={t('buildsHub.tryPlaceholder', 'HNT-XXXXXXXX')}
                spellCheck={false}
                style={{ outline: 'none' }}
                className="relative z-10 w-full h-9 px-3 bg-transparent text-[12px] font-mono tracking-wide
                           text-text-primary placeholder:text-text-muted"
              />
            </div>
            <button
              type="submit"
              disabled={finding || code.trim().length < 3}
              style={{ outline: 'none' }}
              className="shrink-0 inline-flex items-center gap-1.5 h-9 px-3.5 rounded-lg
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/80 hover:border-white/[0.20]
                         disabled:opacity-40 disabled:cursor-not-allowed
                         transition-colors text-[11px] font-bold uppercase tracking-wider"
            >
              {finding ? <Loader2 size={13} className="animate-spin" /> : <Search size={13} />}
              {t('buildsHub.tryFind', 'Найти')}
            </button>
          </div>
          {findError && <span className="text-[11px] text-status-error leading-snug">{findError}</span>}
        </form>
      ),
    },
    {
      icon: <Star size={18} strokeWidth={2.2} />,
      title: t('buildsHub.feature3Title', 'Оценки и отзывы'),
      body:  t('buildsHub.feature3Body', 'Ставь рейтинг, оставляй отзывы, смотри просмотры и установки лучших сборок.'),
    },
  ];

  const ctaBtn = (
    <button
      type="button"
      onClick={() => onNavigate('user-builds')}
      style={{ outline: 'none' }}
      className="inline-flex items-center gap-2 h-12 px-6 rounded-xl
                 bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                 hover:bg-bg-elevated/80 hover:border-white/[0.20]
                 transition-colors text-sm font-bold uppercase tracking-wider"
    >
      <span>{t('buildsHub.cta', 'Открыть сборки')}</span>
    </button>
  );

  return (
    <motion.div
      variants={pageV}
      initial="hidden"
      animate="visible"
      className="h-full overflow-y-auto"
    >
      <div className="w-full px-8 py-8 min-h-full flex flex-col gap-6">
        <motion.div variants={itemV}>
          <Island className="p-8 md:p-10">
            <div className="flex flex-col gap-4 max-w-2xl">
              <span
                className="inline-flex items-center justify-center w-14 h-14 rounded-2xl text-accent"
                style={{
                  background: 'color-mix(in srgb, var(--accent) 14%, transparent)',
                  boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 32%, transparent)',
                }}
              >
                <Boxes size={28} strokeWidth={2} />
              </span>
              <div className="flex flex-col gap-1.5">
                <span className="text-[10px] uppercase tracking-[0.28em] text-accent font-bold">
                  {t('buildsHub.eyebrow', 'Пользовательские сборки')}
                </span>
                <h1 className="text-[26px] md:text-[32px] font-bold tracking-tight text-text-primary leading-[1.1]">
                  {t('buildsHub.title', 'Собирай и делись своими сборками')}
                </h1>
                <p className="text-sm md:text-[15px] text-text-secondary leading-relaxed mt-1">
                  {t('buildsHub.description',
                     'Здесь живут пользовательские сборки: готовые наборы из редукса, ганпака и настроек, которые можно установить в один клик, оценить и расшарить по коду.')}
                </p>
              </div>
              <div className="mt-2">{ctaBtn}</div>
            </div>
          </Island>
        </motion.div>

        <div className="flex-1 grid grid-cols-1 md:grid-cols-3 gap-4">
          {features.map((f, i) => (
            <motion.div key={i} variants={itemV} className="h-full">
              <Island className="p-5 h-full flex flex-col gap-3">
                <span
                  className="inline-flex items-center justify-center w-10 h-10 rounded-xl text-accent"
                  style={{
                    background: 'color-mix(in srgb, var(--accent) 12%, transparent)',
                    boxShadow: 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 30%, transparent)',
                  }}
                >
                  {f.icon}
                </span>
                <h3 className="text-sm font-bold text-text-primary tracking-tight">{f.title}</h3>
                <p className="text-[12.5px] text-text-secondary leading-relaxed">{f.body}</p>
                {f.action}
              </Island>
            </motion.div>
          ))}
        </div>

        <motion.div variants={itemV}>
          <Island className="p-5">
            <div className="flex items-center gap-4 flex-wrap">
              <div className="flex-1 min-w-0">
                <h3 className="text-sm font-bold text-text-primary">
                  {t('buildsHub.ctaStripTitle', 'Готов посмотреть сборки?')}
                </h3>
                <p className="text-[12.5px] text-text-muted mt-0.5">
                  {t('buildsHub.ctaStripBody', 'Перейди в каталог пользовательских сборок - установи готовую или собери свою.')}
                </p>
              </div>
              <div className="shrink-0">{ctaBtn}</div>
            </div>
          </Island>
        </motion.div>
      </div>
    </motion.div>
  );
}

function Island({ className = '', children }: { className?: string; children: React.ReactNode }) {
  return (
    <GlassPanel
      depth="z3" tint="ultra" rounded="3xl" highlight edge
      className="relative overflow-hidden border border-white/[0.08] h-full"
    >
      <span aria-hidden className="absolute inset-0 pointer-events-none bg-bg-elevated/55" />
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-20 -right-14 w-56 h-56 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
      />
      <div className={'relative ' + className}>{children}</div>
    </GlassPanel>
  );
}
