import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Plus, Loader2, Palette, Users, User, Crosshair, Lock, X, Download, Clock } from 'lucide-react';
import { motion, AnimatePresence } from 'framer-motion';
import { useCustomGunStore } from '@/store/customGunStore';
import type { CustomGun } from '@/bridge/types';
import { CustomGunCard, CATEGORY_LABEL } from './CustomGunCard';
import { MyGunEditModal } from './MyGunEditModal';
import { Modal, GlassDropdown, type GlassDropdownOption } from '@/design';

export const PILL_CTA =
  'focus-glow inline-flex items-center justify-center gap-2 px-4 h-11 rounded-2xl border ' +
  'text-[12px] font-bold uppercase tracking-[0.12em] ' +
  'transition-[background-color,color,border-color,box-shadow,filter] duration-300 ease-smooth ' +
  'bg-bg-elevated text-text-primary border-white/[0.16] ' +
  'shadow-[inset_0_1px_0_rgba(255,255,255,0.08),0_10px_26px_-12px_rgba(0,0,0,0.65)] ' +
  'hover:border-white/[0.26] hover:brightness-[1.12] ' +
  'disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:brightness-100';

type CustomSort = 'new' | 'downloads';

function sortOptions(t: (k: string, d: string) => string): GlassDropdownOption<CustomSort>[] {
  return [
    { value: 'new',       label: t('guns.custom.sortNew', 'Новые'),               icon: Clock },
    { value: 'downloads', label: t('guns.custom.sortDownloads', 'По скачиваниям'), icon: Download },
  ];
}

function weaponOptions(t: (k: string, d: string) => string): GlassDropdownOption<string>[] {
  return [
    { value: 'all', label: t('guns.custom.weaponAll', 'Любое оружие'), icon: Crosshair },
    ...Object.entries(CATEGORY_LABEL).map(([value, ru]) => ({
      value,
      label: t(`guns.category.${value}`, ru),
    })),
  ];
}

interface Props { search: string; }

export function CustomBrowse({ search }: Props) {
  const { t } = useTranslation();
  const weaponOpts = useMemo(() => weaponOptions(t), [t]);
  const sortOpts   = useMemo(() => sortOptions(t), [t]);
  const view    = useCustomGunStore(s => s.view);
  const setView = useCustomGunStore(s => s.setView);
  const all     = useCustomGunStore(s => s.all);
  const mine    = useCustomGunStore(s => s.mine);
  const limits  = useCustomGunStore(s => s.limits);
  const loading = useCustomGunStore(s => s.loading);
  const load    = useCustomGunStore(s => s.load);
  const patch   = useCustomGunStore(s => s.patch);
  const remove  = useCustomGunStore(s => s.remove);
  const install = useCustomGunStore(s => s.install);
  const openWorkshop = useCustomGunStore(s => s.openWorkshop);

  const [editing, setEditing]   = useState<CustomGun | null>(null);
  const [deleting, setDeleting] = useState<CustomGun | null>(null);
  const [installing, setInstalling] = useState<Set<string>>(new Set());
  const [installed, setInstalled]   = useState<Set<string>>(new Set());

  useEffect(() => { void load(); }, [load]);

  const create = () => openWorkshop({});

  const [sort, setSort]   = useState<'new' | 'downloads'>('new');
  const [cat, setCat]     = useState('all');
  const [byAuthor, setByAuthor] = useState(false);
  const [author, setAuthor]     = useState('');

  const source = view === 'mine' ? mine : all;
  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    const a = byAuthor ? author.trim().toLowerCase() : '';
    let out = source;

    if (q) out = out.filter(g =>
      g.displayName.toLowerCase().includes(q) || g.ownerName.toLowerCase().includes(q));
    if (cat !== 'all') out = out.filter(g => g.category === cat);
    if (a)   out = out.filter(g => g.ownerName.toLowerCase().includes(a));

    if (sort === 'downloads') out = [...out].sort((x, y) => y.downloadCount - x.downloadCount);

    return out;
  }, [source, search, cat, byAuthor, author, sort]);

  const PAGE = 24;
  const [shown, setShown] = useState(PAGE);
  useEffect(() => { setShown(PAGE); }, [view, search, cat, author, byAuthor, sort]);

  const visible  = useMemo(() => filtered.slice(0, shown), [filtered, shown]);
  const hasMore  = shown < filtered.length;

  const sentinel = useRef<HTMLDivElement | null>(null);
  useEffect(() => {
    const el = sentinel.current;
    if (!el || !hasMore) return;
    const io = new IntersectionObserver(
      entries => { if (entries[0]?.isIntersecting) setShown(s => s + PAGE); },
      { rootMargin: '600px 0px' },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [hasMore]);

  const atLimit = false;
  const slotsFull = limits.used >= limits.max;

  const onInstall = async (g: CustomGun) => {
    setInstalling(s => new Set(s).add(g.id));
    try { await install(g.id); setInstalled(s => new Set(s).add(g.id)); }
    finally { setInstalling(s => { const n = new Set(s); n.delete(g.id); return n; }); }
  };

  return (
    <div className="max-w-[1500px] 2xl:max-w-[1920px] mx-auto">
      <div className="flex flex-wrap items-center gap-2.5 mb-5">
        <div className="inline-flex rounded-xl border border-white/[0.07] bg-white/[0.03] p-1">
          <ViewTab active={view === 'all'}  onClick={() => setView('all')}  icon={Users} label={t('guns.custom.viewAll', 'Все')} />
          <ViewTab active={view === 'mine'} onClick={() => setView('mine')} icon={User}  label={t('guns.custom.viewMine', 'Мои ганы')} count={mine.length} />
        </div>

        <div className="ml-auto flex items-center gap-2.5">
          <span className="inline-flex items-center gap-1.5 px-3 h-9 rounded-xl bg-white/[0.03] border border-white/[0.07]
                           text-[11px] font-bold uppercase tracking-[0.1em] text-text-muted">
            <span className={'tabular-nums ' + (slotsFull ? 'text-amber-400' : 'text-text-primary')}>
              {limits.used}/{limits.max}
            </span>
            {t('guns.custom.slotsUnit', { count: limits.max, defaultValue: 'слотов' })}
          </span>
          <button
            type="button"
            onClick={() => !atLimit && create()}
            disabled={atLimit}
            title={atLimit
              ? t('guns.custom.limitReachedTitle', { max: limits.max, defaultValue: 'Достигнут лимит {{max}} скинов' })
              : t('guns.custom.createNewTitle', 'Создать новый скин')}
            className={PILL_CTA}
          >
            {atLimit ? <Lock size={14} /> : <Plus size={14} />} {t('guns.custom.create', 'Создать скин')}
          </button>
        </div>
      </div>

      <div className="flex flex-wrap items-center gap-2.5 mb-4">
        <GlassDropdown<string>
          value={cat}
          options={weaponOpts}
          onChange={setCat}
          ariaLabel={t('guns.custom.filterWeaponAria', 'Фильтр по оружию')}
          title={t('guns.custom.filterWeaponTitle', 'Оружие')}
          width={190}
        />

        <button
          type="button"
          onClick={() => { setByAuthor(v => !v); if (byAuthor) setAuthor(''); }}
          className={
            'inline-flex items-center gap-1.5 h-9 px-3 rounded-xl border text-[12px] transition-colors ' +
            (byAuthor
              ? 'border-white/[0.2] bg-white/[0.08] text-text-primary'
              : 'border-white/[0.07] bg-white/[0.03] text-text-secondary hover:text-text-primary hover:border-white/[0.14]')
          }
          style={{ outline: 'none' }}
        >
          <User size={13} /> {t('guns.custom.byAuthor', 'От автора')}
        </button>

        {byAuthor && (
          <label className="relative inline-flex items-center">
            <input
              value={author}
              onChange={(e) => setAuthor(e.target.value)}
              placeholder={t('guns.custom.authorPlaceholder', 'Ник автора')}
              autoFocus
              className="h-9 w-[180px] pl-3 pr-8 rounded-xl border border-white/[0.07] bg-white/[0.03]
                         text-[12px] text-text-primary placeholder:text-text-muted
                         focus:outline-none focus:border-white/[0.2] transition-colors"
            />
            {author && (
              <button
                type="button"
                onClick={() => setAuthor('')}
                aria-label={t('common.clear', 'Очистить')}
                className="absolute right-1.5 w-6 h-6 rounded-md flex items-center justify-center
                           text-text-muted hover:text-text-primary hover:bg-white/[0.06]"
                style={{ outline: 'none' }}
              >
                <X size={12} />
              </button>
            )}
          </label>
        )}

        <div className="ml-auto">
          <GlassDropdown<CustomSort>
            value={sort}
            options={sortOpts}
            onChange={setSort}
            ariaLabel={t('guns.custom.sortAria', 'Сортировка')}
            title={t('guns.custom.sortTitle', 'Сортировка')}
            width={210}
          />
        </div>
      </div>

      {slotsFull && view === 'mine' && (
        <div className="mb-4 flex items-center gap-2 text-[12px] text-amber-400/90
                        bg-amber-400/10 border border-amber-400/20 rounded-lg px-3 py-2">
          <Lock size={13} /> {t('guns.custom.slotLimitBanner', { count: limits.max, defaultValue: 'Лимит {{count}} скинов на аккаунт. Удали один или оформи премиум для новых слотов.' })}
        </div>
      )}

      {loading && source.length === 0 ? (
        <div className="py-16 flex items-center justify-center text-text-muted gap-2">
          <Loader2 size={16} className="animate-spin" /> <span className="text-sm">{t('guns.custom.loading', 'Загружаем каталог…')}</span>
        </div>
      ) : filtered.length === 0 ? (
        <EmptyState view={view} onCreate={() => !atLimit && create()} atLimit={atLimit}
                    hasSearch={search.trim().length > 0} />
      ) : (
        <>
        <motion.div layout className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 2xl:grid-cols-5 gap-4">
          <AnimatePresence mode="popLayout">
            {visible.map((g, i) => (
              <motion.div key={g.id} layout
                          initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }}
                          transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1], delay: Math.min(i % PAGE, 12) * 0.03 }}>
                <CustomGunCard
                  gun={g}
                  installing={installing.has(g.id)}
                  installed={installed.has(g.id)}
                  onEditSkin={() => openWorkshop({ customGunId: g.id })}
                  onEditMeta={() => setEditing(g)}
                  onDelete={() => setDeleting(g)}
                  onInstall={() => onInstall(g)}
                />
              </motion.div>
            ))}
          </AnimatePresence>
        </motion.div>

        {hasMore && (
          <div ref={sentinel} className="py-8 flex items-center justify-center gap-2 text-text-muted">
            <Loader2 size={14} className="animate-spin" />
            <span className="text-[12px]">
              {t('guns.custom.shownOf', {
                count: visible.length, total: filtered.length,
                defaultValue: 'Показано {{count}} из {{total}}',
              })}
            </span>
          </div>
        )}
        </>
      )}

      {editing && (
        <MyGunEditModal
          gun={editing}
          onClose={() => setEditing(null)}
          onSave={async (p) => { await patch(editing.id, p); }}
        />
      )}

      {deleting && (
        <Modal.Root onClose={() => setDeleting(null)} maxWidthClassName="max-w-[400px]">
          <Modal.Header icon={Crosshair}>
            <Modal.Title>{t('guns.custom.deleteTitle', 'Удалить скин?')}</Modal.Title>
            <Modal.Subtitle>{t('guns.custom.deleteSubtitle', { name: deleting.displayName, defaultValue: '«{{name}}» исчезнет из каталога у всех. Это действие необратимо.' })}</Modal.Subtitle>
          </Modal.Header>
          <Modal.Actions>
            <button onClick={() => setDeleting(null)} className="btn-glow btn-glow--ghost">{t('common.cancel', 'Отмена')}</button>
            <button
              onClick={async () => { const id = deleting.id; setDeleting(null); await remove(id); }}
              className="btn-glow btn-glow--filled !bg-status-error !border-status-error"
            >
              {t('common.delete', 'Удалить')}
            </button>
          </Modal.Actions>
        </Modal.Root>
      )}

    </div>
  );
}

function ViewTab({
  active, onClick, icon: Icon, label, count,
}: {
  active: boolean; onClick: () => void; icon: typeof Users; label: string; count?: number;
}) {
  return (
    <button type="button" onClick={onClick}
            className={
              'focus-glow inline-flex items-center gap-1.5 px-3.5 h-9 rounded-lg text-[12px] font-bold uppercase tracking-[0.1em] transition-colors ' +
              (active ? 'bg-bg-elevated text-text-primary' : 'text-text-secondary hover:text-text-primary')
            }>
      <Icon size={13} /> {label}
      {count !== undefined && (
        <span className="text-[10px] tabular-nums px-1.5 py-0.5 rounded bg-white/[0.08] text-text-muted">{count}</span>
      )}
    </button>
  );
}

function EmptyState({ view, onCreate, atLimit, hasSearch }: {
  view: 'all' | 'mine'; onCreate: () => void; atLimit: boolean; hasSearch: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div className="py-20 flex flex-col items-center justify-center text-text-muted gap-3">
      <Palette size={48} className="opacity-25" />
      <p className="text-sm text-center max-w-sm">
        {hasSearch
          ? t('guns.emptySearch', 'Ничего не нашлось по запросу.')
          : view === 'mine'
            ? t('guns.custom.emptyMine', 'У тебя пока нет своих скинов. Создай первый - покрась любое оружие в мастерской.')
            : t('guns.custom.emptyAll', 'Пока никто не опубликовал кастомных скинов. Стань первым!')}
      </p>
      {!atLimit && (
        <button onClick={onCreate} className={PILL_CTA}>
          <Plus size={14} /> {t('guns.custom.create', 'Создать скин')}
        </button>
      )}
    </div>
  );
}
