import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Search, Edit2, Eye, EyeOff, Trash2, X, Copy, Upload, Check, Layers, Crosshair, Users, Settings as SettingsIcon, Shield, RefreshCw, Loader2, Image as ImageIcon, Plus, Lock } from 'lucide-react';
import { useAdminStore } from '@/store/adminStore';
import { useReduxVersionsStore } from '@/store/reduxVersionsStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { useGtaSettingsStore } from '@/store/gtaSettingsStore';
import type { ReduxItem, ReduxVersion, Gunpack, ArmorLibraryItem, GtaPreset } from '@/bridge/types';
import type { UserBuild } from '@/store/userBuildsStore';
import { bridge } from '@/bridge';
import { Toast } from '@/components/Toast';
import { WipeAllButton } from '../WipeAllButton';

type DbTab = 'redux' | 'guns' | 'players' | 'settings' | 'armor';

export function DatabaseSection() {
  const { t } = useTranslation();
  const catalog = useAdminStore(s => s.catalog);
  const loadCatalog = useAdminStore(s => s.loadCatalog);
  const updateCatalog = useAdminStore(s => s.updateCatalog);
  const deleteCatalog = useAdminStore(s => s.deleteCatalog);

  const gunpacks      = useGunpackStore(s => s.packs);
  const loadGunpacks  = useGunpackStore(s => s.loadPacks);
  const deleteGunpack = useGunpackStore(s => s.deletePack);
  const patchGunpack  = useGunpackStore(s => s.patchPack);

  const builds        = useUserBuildsStore(s => s.builds);
  const loadBuilds    = useUserBuildsStore(s => s.load);
  const removeBuild   = useUserBuildsStore(s => s.remove);

  const presets       = useGtaSettingsStore(s => s.adminPresets);
  const loadPresets   = useGtaSettingsStore(s => s.loadAdminPresets);
  const deletePreset  = useGtaSettingsStore(s => s.deletePreset);

  const [armorRows, setArmorRows]       = useState<ArmorLibraryItem[]>([]);
  const [armorLoading, setArmorLoading] = useState(false);

  const [tab, setTab] = useState<DbTab>('redux');
  const [search, setSearch] = useState('');
  const [server, setServer] = useState('');
  const [status, setStatus] = useState('');
  const [editTarget, setEditTarget] = useState<ReduxItem | null>(null);
  const [editGunTarget, setEditGunTarget] = useState<Gunpack | null>(null);
  const [confirmDelete, setConfirmDelete] = useState<ReduxItem | null>(null);
  const [confirmDeleteGun, setConfirmDeleteGun] = useState<Gunpack | null>(null);

  const [confirmDeleteOther, setConfirmDeleteOther] = useState<
    | { kind: 'build';  row: UserBuild }
    | { kind: 'preset'; row: GtaPreset }
    | { kind: 'armor';  row: ArmorLibraryItem }
    | null>(null);
  const [toast, setToast] = useState<{ tone: 'success' | 'error'; message: string } | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [rebuildingComponents, setRebuildingComponents] = useState(false);
  const [recalculatingPatchSizes, setRecalculatingPatchSizes] = useState(false);

  useEffect(() => {
    if (tab !== 'redux') return;
    const id = window.setTimeout(() => {
      void loadCatalog(search || undefined, server || undefined, status || undefined);
    }, 300);
    return () => window.clearTimeout(id);
  }, [tab, search, server, status, loadCatalog]);

  useEffect(() => {
    if (tab === 'guns')     void loadGunpacks();
    if (tab === 'players')  void loadBuilds();
    if (tab === 'settings') void loadPresets();
    if (tab === 'armor') {
      setArmorLoading(true);
      bridge.armorLibraryListAll()
        .then(rows => setArmorRows(rows))
        .catch(e => setToast({ tone: 'error', message: (e as Error).message }))
        .finally(() => setArmorLoading(false));
    }
  }, [tab, loadGunpacks, loadBuilds, loadPresets]);

  const filteredGuns = (() => {
    if (!search.trim()) return gunpacks;
    const q = search.trim().toLowerCase();
    return gunpacks.filter(g =>
      g.name.toLowerCase().includes(q)
      || (g.author ?? '').toLowerCase().includes(q)
      || g.id.toLowerCase().includes(q));
  })();

  const filteredBuilds = useMemo(() => {
    if (!search.trim()) return builds;
    const q = search.trim().toLowerCase();
    return builds.filter(b =>
      b.name.toLowerCase().includes(q)
      || b.author.toLowerCase().includes(q)
      || b.hntCode.toLowerCase().includes(q)
      || b.id.toLowerCase().includes(q));
  }, [builds, search]);

  const filteredPresets = useMemo(() => {
    if (!search.trim()) return presets;
    const q = search.trim().toLowerCase();
    return presets.filter(p =>
      p.name.toLowerCase().includes(q)
      || p.author.toLowerCase().includes(q)
      || p.id.toLowerCase().includes(q));
  }, [presets, search]);

  const filteredArmor = useMemo(() => {
    if (!search.trim()) return armorRows;
    const q = search.trim().toLowerCase();
    return armorRows.filter(a =>
      a.name.toLowerCase().includes(q)
      || a.author.toLowerCase().includes(q)
      || a.id.toLowerCase().includes(q));
  }, [armorRows, search]);

  const count = tab === 'redux'    ? catalog.length
              : tab === 'guns'     ? filteredGuns.length
              : tab === 'players'  ? filteredBuilds.length
              : tab === 'settings' ? filteredPresets.length
              : tab === 'armor'    ? filteredArmor.length
              : 0;
  const countLabel = tab === 'redux'    ? 'редуксов'
                   : tab === 'guns'     ? 'паков'
                   : tab === 'players'  ? 'сборок'
                   : tab === 'settings' ? 'пресетов'
                   : tab === 'armor'    ? 'броников'
                   : '';

  const onConfirmDeleteOther = async () => {
    if (!confirmDeleteOther) return;
    setDeleting(true);
    try {
      switch (confirmDeleteOther.kind) {
        case 'build':
          await removeBuild(confirmDeleteOther.row.id);
          setToast({ tone: 'success', message: `Сборка «${confirmDeleteOther.row.name}» удалена.` });
          break;
        case 'preset':
          await deletePreset(confirmDeleteOther.row.id);
          setToast({ tone: 'success', message: `Пресет «${confirmDeleteOther.row.name}» удалён.` });
          break;
        case 'armor': {
          const ok = await bridge.armorLibraryDelete(confirmDeleteOther.row.id);
          if (ok) {
            setArmorRows(prev => prev.filter(a => a.id !== confirmDeleteOther.row.id));
            setToast({ tone: 'success', message: `Броник «${confirmDeleteOther.row.name}» удалён.` });
          } else {
            setToast({ tone: 'error', message: 'Не удалось удалить броник.' });
          }
          break;
        }
      }
      setConfirmDeleteOther(null);
    } catch (e) {
      setToast({ tone: 'error', message: (e as Error).message });
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="h-full flex flex-col">
      <header className="h-14 px-6 flex items-center gap-3 border-b border-border-subtle shrink-0">
        <h1 className="text-base font-bold text-text-primary shrink-0 whitespace-nowrap">{t('admin.database.title')}</h1>
        <span className="text-xs text-text-muted tabular-nums shrink-0 whitespace-nowrap">{count} {countLabel}</span>
        <div className="flex-1" />
        {}
        <WipeAllButton
          category={
            tab === 'redux'    ? 'redux'
            : tab === 'guns'     ? 'gunpacks'
            : tab === 'players'  ? 'userBuilds'
            : tab === 'settings' ? 'gtaPresets'
            :                      'armorLibrary'
          }
          categoryLabel={
            tab === 'redux'    ? 'редуксов'
            : tab === 'guns'     ? 'ган-паков'
            : tab === 'players'  ? 'сборок'
            : tab === 'settings' ? 'пресетов настроек'
            :                      'броников из library'
          }
          count={count}
          onAfterWipe={async () => {
            if (tab === 'redux')    await loadCatalog();
            if (tab === 'guns')     await loadGunpacks();
            if (tab === 'players')  await loadBuilds();
            if (tab === 'settings') await loadPresets();
            if (tab === 'armor') {
              try { setArmorRows(await bridge.armorLibraryListAll()); }
              catch {  }
            }
          }}
          onToast={(tone, message) => setToast({ tone, message })}
        />
        <div className="relative">
          <Search size={14} className="absolute left-3 top-1/2 -translate-y-1/2 text-text-muted" />
          <input
            type="text"
            value={search}
            onChange={e => setSearch(e.target.value)}
            placeholder={t('admin.database.searchPlaceholder')}
            className="pl-9 pr-3 py-1.5 w-48 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors"
          />
        </div>
        {}
        {tab === 'redux' && (
          <>
            <select
              value={server}
              onChange={e => setServer(e.target.value)}
              className="px-3 py-1.5 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors"
            >
              <option value="">{t('admin.database.allServers')}</option>
              <option value="majestic">Majestic</option>
              <option value="gta5rp">GTA5RP</option>
            </select>
            <select
              value={status}
              onChange={e => setStatus(e.target.value)}
              className="px-3 py-1.5 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent transition-colors"
            >
              <option value="">{t('admin.database.allStatuses')}</option>
              <option value="published">Published</option>
              <option value="hidden">Hidden</option>
            </select>
            {}
            <button
              type="button"
              disabled={rebuildingComponents}
              onClick={async () => {
                if (rebuildingComponents) return;
                setRebuildingComponents(true);
                try {
                  const updated = await bridge.adminRebuildReduxComponents();
                  setToast({
                    tone: 'success',
                    message: updated > 0
                      ? `Components пересчитаны для ${updated} редуксов`
                      : 'Все редуксы уже актуальны - ничего не изменилось',
                  });
                  void loadCatalog(search || undefined, server || undefined, status || undefined);
                } catch (e) {
                  setToast({ tone: 'error', message: (e as Error).message });
                } finally {
                  setRebuildingComponents(false);
                }
              }}
              title={rebuildingComponents
                ? 'Идёт пересчёт components...'
                : 'Пересчитать components. Пересобрать redux_items.components из redux_versions у всех записей. Полезно если у legacy-редуксов поле пустое и пользовательский фильтр их не находит.'}
              aria-label="Пересчитать components"
              className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-lg border border-border-strong text-text-secondary hover:bg-bg-elevated hover:text-text-primary transition-colors disabled:opacity-50"
            >
              {rebuildingComponents
                ? <Loader2 size={14} className="animate-spin" />
                : <Layers size={14} />}
            </button>
            <button
              type="button"
              disabled={recalculatingPatchSizes}
              onClick={async () => {
                if (recalculatingPatchSizes) return;
                setRecalculatingPatchSizes(true);
                try {
                  const updated = await bridge.adminRecalculateReduxPatchSizes();
                  setToast({
                    tone: 'success',
                    message: updated > 0
                      ? `Размер и SHA пересчитаны для ${updated} строк`
                      : 'Размер и SHA уже актуальны',
                  });
                  void loadCatalog(search || undefined, server || undefined, status || undefined);
                } catch (e) {
                  setToast({ tone: 'error', message: (e as Error).message });
                } finally {
                  setRecalculatingPatchSizes(false);
                }
              }}
              title={recalculatingPatchSizes
                ? 'Идёт пересчёт размеров и SHA...'
                : 'Пересчитать размер и SHA-256 всех версий по опубликованным R2 patch.zip (скачивает и хеширует каждый). Чинит рассинхрон после перезаливки. Без перезаливки объектов.'}
              aria-label="Пересчитать размер и SHA"
              className="shrink-0 inline-flex items-center justify-center w-9 h-9 rounded-lg border border-border-strong text-text-secondary hover:bg-bg-elevated hover:text-text-primary transition-colors disabled:opacity-50"
            >
              {recalculatingPatchSizes
                ? <Loader2 size={14} className="animate-spin" />
                : <RefreshCw size={14} />}
            </button>
          </>
        )}
      </header>

      {}
      <div className="px-6 pt-3 flex items-center gap-2 border-b border-border-subtle">
        <TabButton
          active={tab === 'redux'} onClick={() => setTab('redux')}
          icon={<Layers size={13} />} label="Редуксы" />
        <TabButton
          active={tab === 'guns'} onClick={() => setTab('guns')}
          icon={<Crosshair size={13} />} label="Оружия" />
        <TabButton
          active={tab === 'players'} onClick={() => setTab('players')}
          icon={<Users size={13} />} label="Сборки" />
        <TabButton
          active={tab === 'settings'} onClick={() => setTab('settings')}
          icon={<SettingsIcon size={13} />} label="Сеттингс" />
        <TabButton
          active={tab === 'armor'} onClick={() => setTab('armor')}
          icon={<Shield size={13} />} label="Броники" />
      </div>

      <div className="flex-1 overflow-auto px-6 py-4">
        {tab === 'redux' && (
          <table className="w-full border-separate border-spacing-y-1 text-sm">
            <thead>
              <tr className="text-text-muted text-[10px] uppercase tracking-wider">
                <th className="text-left px-3 py-2 font-medium w-12"></th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.database.colName')}</th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.database.colAuthor')}</th>
                <th className="text-right px-3 py-2 font-medium">{t('admin.database.colSize')}</th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.database.colServer')}</th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.database.colStatus')}</th>
                <th className="text-left px-3 py-2 font-medium">{t('admin.database.colDate')}</th>
                <th className="text-right px-3 py-2 font-medium w-32">{t('admin.database.colActions')}</th>
              </tr>
            </thead>
            <tbody>
              {catalog.length === 0 && (
                <tr><td colSpan={8} className="text-center text-text-muted py-12">{t('admin.database.empty')}</td></tr>
              )}
              {catalog.map(it => (
                <tr key={it.id} className={'bg-bg-surface border border-border-subtle ' + (it.status === 'hidden' ? 'opacity-50' : '')}>
                  <td className="px-3 py-2 rounded-l-lg">
                    <div className="w-9 h-9 rounded-md bg-bg-elevated overflow-hidden flex items-center justify-center text-text-muted">
                      {it.previewUrl
                        ? <img src={it.previewUrl} alt="" className="w-full h-full object-cover"
                               onError={e => (e.currentTarget.style.display = 'none')} />
                        : <Layers size={14} />}
                    </div>
                  </td>
                  <td className="px-3 py-2">
                    <div className="font-medium text-text-primary">{it.name}</div>
                    <div className="text-xs text-text-muted font-mono">{it.id}</div>
                  </td>
                  <td className="px-3 py-2 text-text-secondary">{it.author || '-'}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-text-secondary">
                    {(it.patchSizeBytes / (1024 * 1024)).toFixed(0)} MB
                  </td>
                  <td className="px-3 py-2 text-text-secondary">{it.supportedServers.join(', ')}</td>
                  <td className="px-3 py-2">
                    <span className={
                      'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded ' +
                      (it.status === 'published' ? 'bg-accent-soft text-accent' : 'bg-bg-elevated text-text-muted')
                    }>
                      {it.status}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-text-muted text-xs">{new Date(it.uploadedAt).toLocaleDateString()}</td>
                  <td className="px-3 py-2 rounded-r-lg">
                    <div className="flex items-center justify-end gap-1">
                      <IconBtn icon={<Edit2 size={14} />} onClick={() => setEditTarget(it)} title={t('admin.database.edit')} />
                      <IconBtn
                        icon={it.status === 'published' ? <EyeOff size={14} /> : <Eye size={14} />}
                        onClick={() => void updateCatalog({ ...it, status: it.status === 'published' ? 'hidden' : 'published' })}
                        title={it.status === 'published' ? t('admin.database.hide') : t('admin.database.show')}
                      />
                      <IconBtn
                        icon={<Trash2 size={14} />}
                        onClick={() => setConfirmDelete(it)}
                        title={t('admin.database.delete')}
                        danger
                      />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tab === 'guns' && (
          <table className="w-full border-separate border-spacing-y-1 text-sm">
            <thead>
              <tr className="text-text-muted text-[10px] uppercase tracking-wider">
                <th className="text-left px-3 py-2 font-medium w-12"></th>
                <th className="text-left px-3 py-2 font-medium">{t('common.name')}</th>
                <th className="text-left px-3 py-2 font-medium">Автор</th>
                <th className="text-right px-3 py-2 font-medium">Размер</th>
                <th className="text-left px-3 py-2 font-medium">Cover</th>
                <th className="text-left px-3 py-2 font-medium">Статус</th>
                <th className="text-right px-3 py-2 font-medium">Установок</th>
                <th className="text-left px-3 py-2 font-medium">Загружен</th>
                <th className="text-right px-3 py-2 font-medium w-12">{t('admin.database.colActions')}</th>
              </tr>
            </thead>
            <tbody>
              {filteredGuns.length === 0 && (
                <tr><td colSpan={9} className="text-center text-text-muted py-12">Пусто. Загрузи первый ганпак через Admin → Guns.</td></tr>
              )}
              {filteredGuns.map(g => (
                <tr key={g.id} className={'bg-bg-surface border border-border-subtle ' + (g.status === 'hidden' ? 'opacity-50' : '')}>
                  <td className="px-3 py-2 rounded-l-lg">
                    <div className="w-9 h-9 rounded-md bg-bg-elevated overflow-hidden flex items-center justify-center text-text-muted">
                      {g.coverKind === 'image' && g.coverUrl
                        ? <img src={g.coverUrl} alt="" className="w-full h-full object-cover"
                               onError={e => (e.currentTarget.style.display = 'none')} />
                        : <Crosshair size={14} />}
                    </div>
                  </td>
                  <td className="px-3 py-2">
                    <div className="font-medium text-text-primary">{g.name}</div>
                    <div className="text-xs text-text-muted font-mono truncate max-w-[200px]" title={g.id}>{g.id.slice(0, 8)}</div>
                  </td>
                  <td className="px-3 py-2 text-text-secondary">{g.author || '-'}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-text-secondary">
                    {(g.weaponsRpfSize / (1024 * 1024)).toFixed(1)} MB
                  </td>
                  <td className="px-3 py-2 text-text-secondary text-xs">{g.coverKind === 'youtube' ? 'YouTube' : (g.coverUrl ? 'Image' : '-')}</td>
                  <td className="px-3 py-2">
                    <span className={
                      'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded ' +
                      (g.status === 'published' ? 'bg-accent-soft text-accent' : 'bg-bg-elevated text-text-muted')
                    }>
                      {g.status}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-text-secondary">{g.downloadCount}</td>
                  <td className="px-3 py-2 text-text-muted text-xs">{new Date(g.uploadedAt).toLocaleDateString()}</td>
                  <td className="px-3 py-2 rounded-r-lg">
                    <div className="flex items-center justify-end gap-1">
                      <IconBtn
                        icon={<Edit2 size={14} />}
                        onClick={() => setEditGunTarget(g)}
                        title={t('admin.database.edit')}
                      />
                      <IconBtn
                        icon={g.status === 'published' ? <EyeOff size={14} /> : <Eye size={14} />}
                        onClick={() => void patchGunpack(g.id, { status: g.status === 'published' ? 'hidden' : 'published' })}
                        title={g.status === 'published' ? t('admin.database.hide') : t('admin.database.show')}
                      />
                      <IconBtn
                        icon={<Trash2 size={14} />}
                        onClick={() => setConfirmDeleteGun(g)}
                        title={t('admin.database.delete')}
                        danger
                      />
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tab === 'players' && (
          <table className="w-full border-separate border-spacing-y-1 text-sm">
            <thead>
              <tr className="text-text-muted text-[10px] uppercase tracking-wider">
                <th className="text-left px-3 py-2 font-medium">{t('common.name')}</th>
                <th className="text-left px-3 py-2 font-medium">Автор</th>
                <th className="text-left px-3 py-2 font-medium">HNT</th>
                <th className="text-left px-3 py-2 font-medium">Статус</th>
                <th className="text-right px-3 py-2 font-medium tabular-nums w-20">Загрузки</th>
                <th className="text-right px-3 py-2 font-medium w-20">Действия</th>
              </tr>
            </thead>
            <tbody>
              {filteredBuilds.length === 0 && (
                <tr><td colSpan={6} className="text-center text-text-muted py-12">
                  {builds.length === 0 ? 'Сборок пока нет.' : 'По запросу ничего не нашли.'}
                </td></tr>
              )}
              {filteredBuilds.map(b => (
                <tr key={b.id} className="bg-bg-surface border border-border-subtle">
                  <td className="px-3 py-2 rounded-l-lg">
                    <div className="font-medium text-text-primary">{b.name}</div>
                    <div className="text-xs text-text-muted font-mono">{b.id}</div>
                  </td>
                  <td className="px-3 py-2 text-text-secondary">{b.author || '-'}</td>
                  <td className="px-3 py-2 font-mono text-xs text-text-muted">{b.hntCode}</td>
                  <td className="px-3 py-2">
                    <span className={
                      'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded ' +
                      (b.status === 'approved' ? 'bg-status-success/15 text-status-success'
                       : b.status === 'pending' ? 'bg-status-warning-soft text-status-warning'
                       : 'bg-status-error/15 text-status-error')
                    }>
                      {b.status}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-text-secondary">
                    {b.downloadCount}
                  </td>
                  <td className="px-3 py-2 rounded-r-lg text-right">
                    <button
                      type="button"
                      onClick={() => setConfirmDeleteOther({ kind: 'build', row: b })}
                      title="Удалить сборку"
                      className="inline-flex items-center justify-center w-7 h-7 rounded-md
                                 text-text-muted hover:text-status-error hover:bg-status-error/10
                                 transition-colors"
                    >
                      <Trash2 size={12} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tab === 'settings' && (
          <table className="w-full border-separate border-spacing-y-1 text-sm">
            <thead>
              <tr className="text-text-muted text-[10px] uppercase tracking-wider">
                <th className="text-left px-3 py-2 font-medium">{t('common.name')}</th>
                <th className="text-left px-3 py-2 font-medium">Автор</th>
                <th className="text-right px-3 py-2 font-medium tabular-nums w-16">Прирост</th>
                <th className="text-left px-3 py-2 font-medium w-24">Bias</th>
                <th className="text-left px-3 py-2 font-medium w-24">Статус</th>
                <th className="text-right px-3 py-2 font-medium tabular-nums w-20">Загрузки</th>
                <th className="text-right px-3 py-2 font-medium w-20">Действия</th>
              </tr>
            </thead>
            <tbody>
              {filteredPresets.length === 0 && (
                <tr><td colSpan={7} className="text-center text-text-muted py-12">
                  {presets.length === 0 ? 'Пресетов пока нет.' : 'По запросу ничего не нашли.'}
                </td></tr>
              )}
              {filteredPresets.map(p => (
                <tr key={p.id} className={'bg-bg-surface border border-border-subtle ' + (p.status === 'hidden' ? 'opacity-50' : '')}>
                  <td className="px-3 py-2 rounded-l-lg">
                    <div className="font-medium text-text-primary">{p.name}</div>
                    <div className="text-xs text-text-muted font-mono">{p.id}</div>
                  </td>
                  <td className="px-3 py-2 text-text-secondary">{p.author || '-'}</td>
                  <td className="px-3 py-2 text-right tabular-nums text-accent font-semibold">
                    +{p.computedGainPercent}%
                  </td>
                  <td className="px-3 py-2 text-text-secondary text-xs uppercase">{p.cpuBias}</td>
                  <td className="px-3 py-2">
                    <span className={
                      'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded ' +
                      (p.status === 'published' ? 'bg-accent-soft text-accent' : 'bg-bg-elevated text-text-muted')
                    }>
                      {p.status}
                    </span>
                  </td>
                  <td className="px-3 py-2 text-right tabular-nums text-text-secondary">
                    {p.downloadCount}
                  </td>
                  <td className="px-3 py-2 rounded-r-lg text-right">
                    <button
                      type="button"
                      onClick={() => setConfirmDeleteOther({ kind: 'preset', row: p })}
                      title="Удалить пресет"
                      className="inline-flex items-center justify-center w-7 h-7 rounded-md
                                 text-text-muted hover:text-status-error hover:bg-status-error/10
                                 transition-colors"
                    >
                      <Trash2 size={12} />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {tab === 'armor' && (
          <>
            {armorLoading && armorRows.length === 0 ? (
              <div className="py-12 flex items-center justify-center text-text-muted gap-2">
                <Loader2 size={14} className="animate-spin" />
                <span className="text-[12px]">Загружаем броники…</span>
              </div>
            ) : (
              <table className="w-full border-separate border-spacing-y-1 text-sm">
                <thead>
                  <tr className="text-text-muted text-[10px] uppercase tracking-wider">
                    <th className="text-left px-3 py-2 font-medium">{t('common.name')}</th>
                    <th className="text-left px-3 py-2 font-medium">Автор</th>
                    <th className="text-left px-3 py-2 font-medium w-24">Сервера</th>
                    <th className="text-left px-3 py-2 font-medium w-24">Статус</th>
                    <th className="text-right px-3 py-2 font-medium tabular-nums w-20">Загрузки</th>
                    <th className="text-right px-3 py-2 font-medium w-20">Действия</th>
                  </tr>
                </thead>
                <tbody>
                  {filteredArmor.length === 0 && (
                    <tr><td colSpan={6} className="text-center text-text-muted py-12">
                      {armorRows.length === 0 ? 'Броников пока нет.' : 'По запросу ничего не нашли.'}
                    </td></tr>
                  )}
                  {filteredArmor.map(a => (
                    <tr key={a.id} className={'bg-bg-surface border border-border-subtle ' + (a.status === 'hidden' ? 'opacity-50' : '')}>
                      <td className="px-3 py-2 rounded-l-lg">
                        <div className="font-medium text-text-primary">{a.name}</div>
                        <div className="text-xs text-text-muted font-mono">{a.id}</div>
                      </td>
                      <td className="px-3 py-2 text-text-secondary">{a.author || '-'}</td>
                      <td className="px-3 py-2 text-text-secondary text-xs">
                        {a.supportedServers?.length ? a.supportedServers.join(', ') : '-'}
                      </td>
                      <td className="px-3 py-2">
                        <span className={
                          'text-[10px] uppercase tracking-wider px-2 py-0.5 rounded ' +
                          (a.status === 'visible' || a.status === 'published'
                            ? 'bg-accent-soft text-accent' : 'bg-bg-elevated text-text-muted')
                        }>
                          {a.status}
                        </span>
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums text-text-secondary">
                        {a.downloadCount}
                      </td>
                      <td className="px-3 py-2 rounded-r-lg text-right">
                        <button
                          type="button"
                          onClick={() => setConfirmDeleteOther({ kind: 'armor', row: a })}
                          title="Удалить броник"
                          className="inline-flex items-center justify-center w-7 h-7 rounded-md
                                     text-text-muted hover:text-status-error hover:bg-status-error/10
                                     transition-colors"
                        >
                          <Trash2 size={12} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </>
        )}
      </div>

      {editTarget && (
        <EditModal
          item={editTarget}
          onClose={() => setEditTarget(null)}
          onSave={async (next) => { await updateCatalog(next); setEditTarget(null); }}
          onRewriteUpdate={(seed) => {

            useAdminStore.getState().setRewriteSeed(seed);
            useAdminStore.getState().requestSectionChange('redux');
            setEditTarget(null);
          }}
        />
      )}

      {editGunTarget && (
        <GunpackEditModal
          item={editGunTarget}
          onClose={() => setEditGunTarget(null)}
          onSave={async (patch) => {
            await patchGunpack(editGunTarget.id, patch);
            setEditGunTarget(null);
            setToast({ tone: 'success', message: `Пак «${patch.name ?? editGunTarget.name}» обновлён.` });
          }}
        />
      )}

      {confirmDelete && (
        <ConfirmDialog
          title={t('admin.database.deleteConfirmTitle')}
          message={t('admin.database.deleteConfirmMessage', { name: confirmDelete.name })}
          busy={deleting}
          onCancel={() => setConfirmDelete(null)}
          onConfirm={async () => {
            const target = confirmDelete;
            setDeleting(true);
            try {
              await deleteCatalog(target.id);
              setConfirmDelete(null);
              setToast({ tone: 'success', message: t('admin.database.deleted', { name: target.name }) });
            } catch (e) {
              setToast({ tone: 'error', message: (e as Error).message });
            } finally {
              setDeleting(false);
            }
          }}
        />
      )}

      {confirmDeleteGun && (
        <ConfirmDialog
          title="Удалить ганпак?"
          message={`«${confirmDeleteGun.name}» - пак будет удалён из БД, дочерние пушки тоже снимутся (cascade). R2-объекты остаются (best-effort cleanup делает Admin → Guns).`}
          busy={deleting}
          onCancel={() => setConfirmDeleteGun(null)}
          onConfirm={async () => {
            const target = confirmDeleteGun;
            setDeleting(true);
            try {
              await deleteGunpack(target.id);
              setConfirmDeleteGun(null);
              setToast({ tone: 'success', message: `Пак «${target.name}» удалён.` });
            } catch (e) {
              setToast({ tone: 'error', message: (e as Error).message });
            } finally {
              setDeleting(false);
            }
          }}
        />
      )}

      {}
      {confirmDeleteOther && (
        <ConfirmDialog
          title={
            confirmDeleteOther.kind === 'build'  ? 'Удалить сборку?'  :
            confirmDeleteOther.kind === 'preset' ? 'Удалить пресет?'  :
            'Удалить броник?'
          }
          message={
            confirmDeleteOther.kind === 'build'
              ? `«${confirmDeleteOther.row.name}» от ${confirmDeleteOther.row.author} (${confirmDeleteOther.row.hntCode}) - будет удалена из user_builds. Если она была одобрена, исчезнет из публичного каталога Players.`
              : confirmDeleteOther.kind === 'preset'
                ? `«${confirmDeleteOther.row.name}» - будет удалён из gta_presets. R2 settings.xml тоже стирается (best-effort).`
                : `«${confirmDeleteOther.row.name}» от ${confirmDeleteOther.row.author} - броник удалится из armor_library. Установленные сборки игроков, ссылающиеся на него, перестанут показывать броник.`
          }
          busy={deleting}
          onCancel={() => setConfirmDeleteOther(null)}
          onConfirm={() => onConfirmDeleteOther()}
        />
      )}

      <Toast
        open={!!toast}
        tone={toast?.tone ?? 'success'}
        message={toast?.message ?? ''}
        onClose={() => setToast(null)}
        autoCloseMs={toast?.tone === 'error' ? 8000 : 4000}
      />
    </div>
  );
}

function TabButton({
  active, onClick, icon, label,
}: {
  active: boolean;
  onClick: () => void;
  icon: React.ReactNode;
  label: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={
        'inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-t-md border-b-2 transition-colors ' +
        (active
          ? 'text-accent border-accent bg-accent-soft/40'
          : 'text-text-muted border-transparent hover:text-text-primary')
      }
    >
      {icon}
      <span>{label}</span>
    </button>
  );
}

function IconBtn({ icon, onClick, title, danger }: { icon: React.ReactNode; onClick: () => void; title: string; danger?: boolean }) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-label={title}
      className={
        'w-7 h-7 rounded-md flex items-center justify-center transition-colors ' +
        (danger ? 'text-text-muted hover:text-status-error hover:bg-bg-elevated' : 'text-text-muted hover:text-text-primary hover:bg-bg-elevated')
      }
    >
      {icon}
    </button>
  );
}

function EditModal({ item, onClose, onSave, onRewriteUpdate }: {
  item: ReduxItem;
  onClose: () => void;
  onSave: (i: ReduxItem) => Promise<void>;
  onRewriteUpdate: (seed: ReduxItem) => void;
}) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState<ReduxItem>(item);
  const [saving, setSaving] = useState(false);

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={onClose}>
      <div
        className="w-full max-w-[720px] max-h-[90vh] overflow-y-auto rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-text-primary">{t('admin.database.editTitle')}</h2>
          <button type="button" onClick={onClose} className="text-text-muted hover:text-text-primary"><X size={16} /></button>
        </div>
        <FieldRow label="Name"        value={draft.name}        onChange={v => setDraft({ ...draft, name: v })} />
        <FieldRow label="Author"      value={draft.author}      onChange={v => setDraft({ ...draft, author: v })} />
        <FieldRow label="Author link" value={draft.authorLink}  onChange={v => setDraft({ ...draft, authorLink: v })} />
        <FieldRow label="Description" value={draft.description} onChange={v => setDraft({ ...draft, description: v })} textarea />

        <div className="flex flex-col gap-3 border-t border-border-subtle pt-3">
          <div className="flex items-center gap-2">
            <ImageIcon size={14} className="text-text-muted" />
            <span className="text-xs uppercase tracking-wider text-text-muted">Обложка и видео</span>
          </div>

          <EditModalCoverRow
            reduxId={draft.id}
            url={draft.previewUrl ?? ''}
            onChange={next => setDraft({ ...draft, previewUrl: next })}
          />

          <label className="flex flex-col gap-1">
            <span className="text-xs uppercase tracking-wider text-text-muted">Видео (YouTube URL)</span>
            <div className="flex gap-2">
              <input
                type="text"
                value={draft.videoUrl ?? ''}
                onChange={e => setDraft({ ...draft, videoUrl: e.target.value })}
                placeholder="https://youtube.com/watch?v=..."
                className="flex-1 px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
              />
              <button
                type="button"
                onClick={() => {
                  const yid = extractYouTubeId(draft.videoUrl ?? '');
                  if (yid) setDraft({ ...draft, previewUrl: `https://i.ytimg.com/vi/${yid}/maxresdefault.jpg` });
                }}
                disabled={!extractYouTubeId(draft.videoUrl ?? '')}
                title="Поставить обложку = превью этого YouTube-видео"
                className="shrink-0 inline-flex items-center gap-1.5 px-3 py-2 rounded-lg border border-border-strong text-xs text-text-primary hover:bg-bg-elevated transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <ImageIcon size={12} />
                <span>Превью с видео</span>
              </button>
            </div>
          </label>
        </div>

        {}
        <label className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">
            {t('admin.database.viewerPriority')}
          </span>
          <input
            type="number"
            value={draft.viewerPriority || 0}
            onChange={e => setDraft({ ...draft, viewerPriority: parseInt(e.target.value, 10) || 0 })}
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent w-32"
          />
          <span className="text-xs text-text-muted">{t('admin.database.viewerPriorityHint')}</span>
        </label>

        {}
        <div className="flex flex-col gap-2">
          <span className="text-xs uppercase tracking-wider text-text-muted">
            {t('admin.database.tagsLabel')}
          </span>
          <div className="flex flex-wrap items-center gap-3">
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={!!draft.tagNew}
                onChange={e => setDraft({ ...draft, tagNew: e.target.checked })}
                className="w-4 h-4 rounded cursor-pointer"
                style={{ accentColor: 'var(--accent)' }}
              />
              <span className="text-sm text-text-primary">{t('redux.tag.new')}</span>
            </label>
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={!!draft.tagBest}
                onChange={e => setDraft({ ...draft, tagBest: e.target.checked })}
                className="w-4 h-4 rounded cursor-pointer"
                style={{ accentColor: 'var(--accent)' }}
              />
              <span className="text-sm text-text-primary">{t('redux.tag.best')}</span>
            </label>
            <label className="inline-flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={!!draft.isVerified}
                onChange={e => setDraft({ ...draft, isVerified: e.target.checked })}
                className="w-4 h-4 rounded cursor-pointer"
                style={{ accentColor: 'var(--status-success)' }}
              />
              <span className="text-sm text-text-primary inline-flex items-center gap-1.5">
                <Shield size={13} className="text-status-success" />
                Проверенный
              </span>
            </label>
          </div>
          <span className="text-xs text-text-muted">{t('admin.database.tagsHint')}</span>
          <span className="text-xs text-text-muted">
            «Проверенный» = плашка в каталоге + зелёный вердикт в «Безопасности» (если автопроверка не найдёт опасных значений).
          </span>
        </div>

        <div className="flex flex-col gap-2">
          <span className="text-xs uppercase tracking-wider text-text-muted">
            {t('admin.database.serverLabel')}
          </span>
          <div className="flex gap-2">
            {(['majestic', 'gta5rp', 'both'] as const).map(s => {
              const cur = serverFromList(draft.supportedServers);
              const active = cur === s;
              const extras = draft.supportedServers.filter(x => x === 'kapt' || x === 'rp');
              return (
                <button
                  key={s}
                  type="button"
                  onClick={() => setDraft({ ...draft, supportedServers: [...listFromServer(s), ...extras] })}
                  className={
                    'px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                    (active
                      ? 'bg-accent-soft text-accent border-accent'
                      : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                  }
                >
                  {s === 'both' ? t('admin.database.serverBoth') : s}
                </button>
              );
            })}
          </div>
          <div className="flex gap-2">
            {([['kapt', 'КАПТ'], ['rp', 'РП']] as const).map(([key, label]) => {
              const on = draft.supportedServers.includes(key);
              return (
                <button
                  key={key}
                  type="button"
                  onClick={() => setDraft({
                    ...draft,
                    supportedServers: on
                      ? draft.supportedServers.filter(x => x !== key)
                      : [...draft.supportedServers, key],
                  })}
                  className={
                    'px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                    (on
                      ? 'bg-accent-soft text-accent border-accent'
                      : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                  }
                >
                  {label}
                </button>
              );
            })}
          </div>
          <span className="text-xs text-text-muted">{t('admin.database.serverHint')}</span>
        </div>

        {}
        <div className="flex flex-col gap-2">
          <span className="text-xs uppercase tracking-wider text-text-muted">
            {t('admin.database.armorVisibilityLabel')}
          </span>
          <label className="inline-flex items-center gap-2 cursor-pointer">
            <input
              type="checkbox"
              checked={!!draft.armorStandaloneInstallHidden}
              onChange={e => setDraft({ ...draft, armorStandaloneInstallHidden: e.target.checked })}
              className="w-4 h-4 rounded cursor-pointer"
              style={{ accentColor: 'var(--accent)' }}
            />
            <span className="text-sm text-text-primary">
              {t('admin.database.armorVisibilityToggle')}
            </span>
          </label>
          <span className="text-xs text-text-muted">{t('admin.database.armorVisibilityHint')}</span>
        </div>

        <div className="flex flex-col gap-2 border-t border-border-subtle pt-3">
          <span className="text-xs uppercase tracking-wider text-text-muted">
            Экспорт компонентов (в персонализацию других)
          </span>
          <span className="text-xs text-text-muted -mt-1">
            Зелёный - компонент можно взять как донор в персонализации любого редукса.
            Клик = запретить: тогда этот компонент данного редукса исчезнет из выбора у всех.
          </span>
          <div className="flex flex-wrap items-center gap-2 mt-1">
            {EXPORT_COMPONENT_SLOTS
              .filter(slot => draft.components?.[slot.key]?.isFound)
              .map(slot => {
                const blocked = (draft.components?.[slot.key]?.flags ?? []).includes('exportBlocked');
                return (
                  <button
                    key={slot.key}
                    type="button"
                    onClick={() => {
                      const comps = { ...(draft.components ?? {}) };
                      const cur = comps[slot.key];
                      if (!cur) return;
                      const flags = (cur.flags ?? []).filter(f => f !== 'exportBlocked');
                      if (!blocked) flags.push('exportBlocked');
                      comps[slot.key] = { ...cur, flags };
                      setDraft({ ...draft, components: comps });
                    }}
                    title={blocked
                      ? `«${slot.label}» этого редукса СКРЫТ из доноров. Клик - разрешить экспорт.`
                      : `«${slot.label}» можно брать как донор. Клик - запретить экспорт.`}
                    className={
                      'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                      (blocked
                        ? 'bg-[color-mix(in_srgb,var(--status-error)_14%,transparent)] text-[color-mix(in_srgb,var(--status-error)_92%,white)] border-[color-mix(in_srgb,var(--status-error)_38%,transparent)]'
                        : 'bg-accent-soft text-accent border-[color-mix(in_srgb,var(--accent)_45%,transparent)] hover:bg-[color-mix(in_srgb,var(--accent)_16%,transparent)]')
                    }
                  >
                    {blocked ? <Lock size={12} /> : <Check size={12} />}
                    <span>{slot.label}</span>
                  </button>
                );
              })}
          </div>
        </div>

        <div className="flex flex-col gap-2 border-t border-border-subtle pt-3">
          <span className="text-xs uppercase tracking-wider text-text-muted">
            Персонализация компонентов (в этом редуксе)
          </span>
          <span className="text-xs text-text-muted -mt-1">
            Зелёный - элемент можно персонализировать в этом редуксе.
            Клик = запретить: тогда в окне персонализации этого редукса кнопка станет неактивной.
          </span>
          <div className="flex flex-wrap items-center gap-2 mt-1">
            {EXPORT_COMPONENT_SLOTS
              .filter(slot => draft.components?.[slot.key]?.isFound)
              .map(slot => {
                const blocked = (draft.components?.[slot.key]?.flags ?? []).includes('personalizeBlocked');
                return (
                  <button
                    key={slot.key}
                    type="button"
                    onClick={() => {
                      const comps = { ...(draft.components ?? {}) };
                      const cur = comps[slot.key];
                      if (!cur) return;
                      const flags = (cur.flags ?? []).filter(f => f !== 'personalizeBlocked');
                      if (!blocked) flags.push('personalizeBlocked');
                      comps[slot.key] = { ...cur, flags };
                      setDraft({ ...draft, components: comps });
                    }}
                    title={blocked
                      ? `«${slot.label}» нельзя персонализировать в этом редуксе. Клик - разрешить.`
                      : `«${slot.label}» можно персонализировать. Клик - запретить (кнопка станет неактивной).`}
                    className={
                      'inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                      (blocked
                        ? 'bg-[color-mix(in_srgb,var(--status-error)_14%,transparent)] text-[color-mix(in_srgb,var(--status-error)_92%,white)] border-[color-mix(in_srgb,var(--status-error)_38%,transparent)]'
                        : 'bg-accent-soft text-accent border-[color-mix(in_srgb,var(--accent)_45%,transparent)] hover:bg-[color-mix(in_srgb,var(--accent)_16%,transparent)]')
                    }
                  >
                    {blocked ? <Lock size={12} /> : <Check size={12} />}
                    <span>{slot.label}</span>
                  </button>
                );
              })}
          </div>
        </div>

        {}
        <div className="rounded-lg bg-bg-elevated border border-border-subtle p-3 flex flex-col gap-2">
          <div className="flex items-center justify-between gap-3">
            <div className="flex flex-col gap-0.5">
              <span className="text-sm font-medium text-text-primary">{t('admin.database.rewriteUpdateTitle')}</span>
              <span className="text-xs text-text-muted">{t('admin.database.rewriteUpdateHint')}</span>
            </div>
            <button
              type="button"
              onClick={() => onRewriteUpdate(item)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-base transition-colors shrink-0"
            >
              <Upload size={13} />
              <span>{t('admin.database.rewriteUpdateAction')}</span>
            </button>
          </div>
        </div>

        {}
        <div className="flex flex-col gap-2 border-t border-border-subtle pt-3">
          <div className="flex items-center gap-2">
            <ImageIcon size={14} className="text-text-muted" />
            <span className="text-xs uppercase tracking-wider text-text-muted">
              Скриншоты компонентов
            </span>
          </div>
          <span className="text-xs text-text-muted -mt-1">
            Покажутся юзерам в окне «Компоненты» на этой странице редукса.
            Армор не показывается - у него 3D-модель.
          </span>
          <div className="flex flex-col gap-2 mt-1">
            {EDIT_MODAL_SCREENSHOT_SLOTS.map(slot => {
              const url = (draft.componentScreenshots ?? {})[slot.key] ?? '';
              return (
                <EditModalScreenshotRow
                  key={slot.key}
                  reduxId={draft.id}
                  componentKey={slot.key}
                  label={slot.label}
                  url={url}
                  onChange={(next) => {
                    const map = { ...(draft.componentScreenshots ?? {}) };
                    if (next) map[slot.key] = next;
                    else delete map[slot.key];
                    setDraft({ ...draft, componentScreenshots: map });
                  }}
                />
              );
            })}
          </div>
        </div>

        <VersionsSection reduxId={item.id} parentItem={item} onAfterAddSeed={onClose} />
        <R2UrlsSection item={draft} />
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" onClick={onClose} className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated">
            {t('admin.database.cancel')}
          </button>
          <button
            type="button"
            disabled={saving}
            onClick={async () => {
              setSaving(true);
              try {
                const mirrored = await mirrorExternalUrlsForRedux(draft);
                await onSave(mirrored);
              } finally {
                setSaving(false);
              }
            }}
            className="px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-60 disabled:cursor-not-allowed inline-flex items-center gap-2"
          >
            {saving && <Loader2 size={14} className="animate-spin" />}
            <span>{saving ? t('admin.database.savingMirror') : t('admin.database.save')}</span>
          </button>
        </div>
      </div>
    </div>
  );
}

function isWhitelistedHost(host: string): boolean {
  const h = host.toLowerCase();
  return h.endsWith('miamigraphicsstorage.uk')
      || h.endsWith('.r2.dev')
      || h.endsWith('.r2.cloudflarestorage.com');
}

async function mirrorOne(reduxId: string, url: string, slot: string): Promise<string> {
  if (!url) return url;
  try {
    const u = new URL(url);
    if (u.protocol !== 'https:' && u.protocol !== 'http:') return url;
    if (isWhitelistedHost(u.host)) return url;
  } catch { return url; }
  try {
    return await bridge.adminMirrorImageToR2(reduxId, url, slot);
  } catch (e) {
    console.warn(`[admin.mirror] ${url} -> R2 failed:`, e);
    return url;
  }
}

async function mirrorExternalUrlsForRedux(item: ReduxItem): Promise<ReduxItem> {
  if (!item.id) return item;
  const next: ReduxItem = { ...item };
  next.previewUrl = await mirrorOne(item.id, item.previewUrl ?? '', 'preview');
  const gallerySrc = item.galleryUrls ?? [];
  const gallery: string[] = [];
  for (let i = 0; i < gallerySrc.length; i++) {
    gallery.push(await mirrorOne(item.id, gallerySrc[i], `gallery-${i + 1}`));
  }
  next.galleryUrls = gallery;
  const scrSrc = item.componentScreenshots ?? {};
  const scr: Record<string, string> = {};
  for (const [k, v] of Object.entries(scrSrc)) {
    scr[k] = await mirrorOne(item.id, v, `screenshot-${k}`);
  }
  next.componentScreenshots = scr;
  return next;
}

function serverFromList(list: readonly string[] | undefined): 'majestic' | 'gta5rp' | 'both' {
  const s = new Set(list ?? []);
  if (s.has('majestic') && s.has('gta5rp')) return 'both';
  if (s.has('gta5rp')) return 'gta5rp';
  return 'majestic';
}

function listFromServer(v: 'majestic' | 'gta5rp' | 'both'): string[] {
  return v === 'both' ? ['majestic', 'gta5rp'] : [v];
}

function FieldRow({ label, value, onChange, textarea }: { label: string; value: string; onChange: (v: string) => void; textarea?: boolean }) {
  return (
    <label className="flex flex-col gap-1">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      {textarea ? (
        <textarea rows={3} value={value} onChange={e => onChange(e.target.value)} className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent resize-none" />
      ) : (
        <input type="text" value={value} onChange={e => onChange(e.target.value)} className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent" />
      )}
    </label>
  );
}

const EDIT_MODAL_SCREENSHOT_SLOTS: ReadonlyArray<{ key: string; label: string }> = [
  { key: 'arena',     label: 'Арена'    },
  { key: 'minimap',   label: 'Минимапа' },
  { key: 'crosshair', label: 'Прицел'   },
  { key: 'tracers',   label: 'Трейсера' },
  { key: 'bloodfx',   label: 'Кровь'    },
  { key: 'timecycle', label: 'Таймцикл' },
];

const EXPORT_COMPONENT_SLOTS: ReadonlyArray<{ key: string; label: string }> = [
  { key: 'tracers',   label: 'Трейсера'    },
  { key: 'minimap',   label: 'Миникарта'   },
  { key: 'crosshair', label: 'Прицел'      },
  { key: 'armor',     label: 'Бронежилет'  },
  { key: 'arena',     label: 'Арена'       },
  { key: 'bloodfx',   label: 'Кровь'       },
  { key: 'timecycle', label: 'Таймцикл'    },
];

function EditModalScreenshotRow({
  reduxId, componentKey, label, url, onChange,
}: {
  reduxId: string;
  componentKey: string;
  label: string;
  url: string;
  onChange: (next: string) => void;
}) {
  const [busy, setBusy]     = useState(false);
  const [error, setError]   = useState<string | null>(null);

  const onPick = async () => {
    if (!reduxId) {
      setError('Нет id редукса.');
      return;
    }
    setError(null);
    let localPath: string | null = null;
    try {
      localPath = await bridge.openFileDialog(
        `Скриншот компонента "${label}"`,
        '*.png;*.jpg;*.jpeg;*.webp');
    } catch (e) {
      setError((e as Error).message || 'Ошибка выбора файла.');
      return;
    }
    if (!localPath) return;
    if (typeof bridge.adminUploadComponentScreenshot !== 'function') {
      setError('Перезапусти лаунчер - UI bridge устарел.');
      return;
    }
    setBusy(true);
    try {
      const publicUrl = await bridge.adminUploadComponentScreenshot(
        reduxId, componentKey, localPath);
      onChange(publicUrl);
    } catch (e) {
      console.error('[admin.editModal.screenshot.upload] failed', e);
      setError((e as Error).message || 'Не удалось загрузить - см. консоль.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex items-start gap-3 px-3 py-2.5 rounded-xl bg-bg-elevated border border-border-subtle">
      <div className="shrink-0 w-[80px] h-[52px] rounded-lg overflow-hidden bg-bg-base border border-border-subtle flex items-center justify-center">
        {url ? (
          <img
            src={url}
            alt=""
            onError={(e) => (e.currentTarget.style.display = 'none')}
            className="w-full h-full object-cover"
          />
        ) : (
          <span className="text-[9px] uppercase tracking-wider text-text-muted">no img</span>
        )}
      </div>
      <div className="flex-1 min-w-0 flex flex-col gap-1.5">
        <div className="flex items-center gap-2">
          <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
          <span className="font-mono text-[10px] text-text-muted/70">{componentKey}</span>
        </div>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onPick}
            disabled={busy || !reduxId}
            className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md
                       border border-border-strong bg-bg-base text-xs text-text-primary
                       hover:bg-bg-elevated transition-colors
                       disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy
              ? <Loader2 size={11} className="animate-spin" />
              : <Upload size={11} />}
            <span>{busy ? 'Загрузка…' : (url ? 'Заменить' : 'Загрузить файл')}</span>
          </button>
          {url && !busy && (
            <button
              type="button"
              onClick={() => onChange('')}
              className="inline-flex items-center gap-1 px-2 py-1.5 rounded-md
                         text-[11px] text-text-muted hover:text-status-error transition-colors"
              title="Убрать ссылку из этого слота"
            >
              <X size={11} />
              <span>Очистить</span>
            </button>
          )}
          {url && (
            <span className="font-mono text-[10px] text-text-muted/70 truncate flex-1" title={url}>
              {url.replace(/^https?:\/\//, '')}
            </span>
          )}
        </div>
        {error && (
          <span className="text-[10.5px] text-status-error truncate" title={error}>{error}</span>
        )}
      </div>
    </div>
  );
}

function extractYouTubeId(url: string): string | null {
  const m = url.match(/(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^"&?\/\s]{11})/);
  return m ? m[1] : null;
}

function EditModalCoverRow({ reduxId, url, onChange }: {
  reduxId: string;
  url: string;
  onChange: (next: string) => void;
}) {
  const [busy, setBusy]   = useState(false);
  const [error, setError] = useState<string | null>(null);

  const onPick = async () => {
    if (!reduxId) { setError('Нет id редукса.'); return; }
    setError(null);
    let localPath: string | null = null;
    try {
      localPath = await bridge.openFileDialog('Обложка редукса', '*.png;*.jpg;*.jpeg;*.webp');
    } catch (e) {
      setError((e as Error).message || 'Ошибка выбора файла.');
      return;
    }
    if (!localPath) return;
    if (typeof bridge.adminUploadComponentScreenshot !== 'function') {
      setError('Перезапусти лаунчер - UI bridge устарел.');
      return;
    }
    setBusy(true);
    try {
      const publicUrl = await bridge.adminUploadComponentScreenshot(reduxId, 'preview', localPath);
      onChange(publicUrl);
    } catch (e) {
      console.error('[admin.editModal.cover.upload] failed', e);
      setError((e as Error).message || 'Не удалось загрузить - см. консоль.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="flex items-start gap-3 px-3 py-2.5 rounded-xl bg-bg-elevated border border-border-subtle">
      <div className="shrink-0 w-[120px] h-[68px] rounded-lg overflow-hidden bg-bg-base border border-border-subtle flex items-center justify-center">
        {url ? (
          <img src={url} alt="" onError={(e) => (e.currentTarget.style.display = 'none')} className="w-full h-full object-cover" />
        ) : (
          <span className="text-[9px] uppercase tracking-wider text-text-muted">no img</span>
        )}
      </div>
      <div className="flex-1 min-w-0 flex flex-col gap-1.5">
        <span className="text-xs uppercase tracking-wider text-text-muted">Обложка (превью)</span>
        <input
          type="text"
          value={url}
          onChange={e => onChange(e.target.value)}
          placeholder="https://... или загрузи файл"
          className="px-2.5 py-1.5 bg-bg-base border border-border-subtle rounded-md text-sm text-text-primary outline-none focus:border-accent"
        />
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onPick}
            disabled={busy || !reduxId}
            className="inline-flex items-center gap-1.5 px-2.5 py-1.5 rounded-md border border-border-strong bg-bg-base text-xs text-text-primary hover:bg-bg-elevated transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {busy ? <Loader2 size={11} className="animate-spin" /> : <Upload size={11} />}
            <span>{busy ? 'Загрузка…' : (url ? 'Заменить файлом' : 'Загрузить файл')}</span>
          </button>
          {url && !busy && (
            <button
              type="button"
              onClick={() => onChange('')}
              className="inline-flex items-center gap-1 px-2 py-1.5 rounded-md text-[11px] text-text-muted hover:text-status-error transition-colors"
              title="Убрать обложку"
            >
              <X size={11} /><span>Очистить</span>
            </button>
          )}
        </div>
        {error && <span className="text-[10.5px] text-status-error truncate" title={error}>{error}</span>}
      </div>
    </div>
  );
}

function R2UrlsSection({ item }: { item: ReduxItem }) {
  const urls = item.r2Urls;
  const componentCount = urls ? Object.keys(urls.components).length : 0;
  const anyUrl = urls && (urls.patch || urls.manifest || urls.componentMap || urls.contentInfo || componentCount > 0);
  if (!anyUrl) return null;

  return (
    <details className="rounded-lg bg-bg-elevated p-3">
      <summary className="text-xs uppercase tracking-wider text-text-muted cursor-pointer select-none">
        R2 URLs ({componentCount} components)
      </summary>
      <div className="mt-3 flex flex-col gap-1 text-xs font-mono">
        <UrlRow label="patch.zip"          url={urls!.patch} />
        {Object.entries(urls!.components).map(([n, u]) => (
          <UrlRow key={n} label={`${n}.zip`} url={u} />
        ))}
        <UrlRow label="manifest.json"      url={urls!.manifest} />
        <UrlRow label="component_map.json" url={urls!.componentMap} />
        <UrlRow label="content_info.json"  url={urls!.contentInfo} />
      </div>
    </details>
  );
}

function UrlRow({ label, url }: { label: string; url: string | null }) {
  if (!url) return null;
  return (
    <div className="flex items-center gap-2 group/url">
      <span className="text-text-muted w-32 shrink-0">{label}</span>
      <a
        href={url}
        target="_blank"
        rel="noreferrer noopener"
        className="flex-1 truncate text-text-secondary hover:text-accent transition-colors"
        title={url}
      >
        {url}
      </a>
      <button
        type="button"
        onClick={() => navigator.clipboard.writeText(url)}
        className="text-text-muted hover:text-text-primary opacity-0 group-hover/url:opacity-100 transition-all"
        title="Copy URL"
      >
        <Copy size={12} />
      </button>
    </div>
  );
}

function VersionsSection({ reduxId, parentItem, onAfterAddSeed }: {
  reduxId: string;
  parentItem: ReduxItem;
  onAfterAddSeed: () => void;
}) {
  const { t } = useTranslation();
  const versions = useReduxVersionsStore(s => s.byRedux[reduxId]);
  const loading  = useReduxVersionsStore(s => s.loading[reduxId] ?? false);
  const error    = useReduxVersionsStore(s => s.error[reduxId] ?? null);
  const load     = useReduxVersionsStore(s => s.load);
  const upsert   = useReduxVersionsStore(s => s.upsert);
  const remove   = useReduxVersionsStore(s => s.remove);

  const [confirmDel, setConfirmDel] = useState<ReduxVersion | null>(null);

  useEffect(() => { void load(reduxId); }, [reduxId, load]);

  const list = versions ?? [];
  const canAdd = list.length < 5;

  const onAddVersion = () => {
    if (!canAdd) return;
    const used = new Set(list.map(v => v.slot));
    let nextSlot = 1;
    while (used.has(nextSlot) && nextSlot < 6) nextSlot++;
    if (nextSlot > 5) return;
    useAdminStore.getState().setAppendVersionSeed({ parent: parentItem, nextSlot });
    useAdminStore.getState().requestSectionChange('redux');
    onAfterAddSeed();
  };

  return (
    <div className="rounded-lg bg-bg-elevated border border-border-subtle p-3 flex flex-col gap-3">
      <div className="flex items-start justify-between gap-3">
        <div className="flex flex-col gap-0.5">
          <span className="text-sm font-medium text-text-primary">
            {t('admin.database.versions.title')} <span className="text-text-muted">({list.length}/5)</span>
          </span>
          <span className="text-xs text-text-muted">{t('admin.database.versions.hint')}</span>
        </div>
        <button
          type="button"
          onClick={onAddVersion}
          disabled={!canAdd}
          title={canAdd ? t('admin.database.versions.addButton') : t('admin.database.versions.addLimit')}
          className="shrink-0 w-8 h-8 rounded-md flex items-center justify-center text-accent bg-accent-soft/40 border border-accent/40 hover:bg-accent-soft hover:border-accent disabled:opacity-30 disabled:cursor-not-allowed transition-colors"
        >
          <Plus size={16} />
        </button>
      </div>

      {loading && list.length === 0 && (
        <div className="text-xs text-text-muted">{t('admin.database.versions.loading')}</div>
      )}
      {error && (
        <div className="text-xs text-status-error">{error}</div>
      )}
      {!loading && list.length === 0 && (
        <div className="text-xs text-text-muted italic">{t('admin.database.versions.empty')}</div>
      )}

      {list.length > 0 && (
        <ul className="flex flex-col gap-1.5">
          {list.map(v => (
            <VersionRow
              key={v.id}
              version={v}
              onSave={async (patch) => { await upsert({ ...v, ...patch }); }}
              onDelete={() => setConfirmDel(v)}
            />
          ))}
        </ul>
      )}

      {confirmDel && (
        <ConfirmDialog
          title={t('admin.database.versions.deleteConfirmTitle')}
          message={t('admin.database.versions.deleteConfirmMessage', { slot: confirmDel.slot, label: confirmDel.label })}
          onCancel={() => setConfirmDel(null)}
          onConfirm={async () => {
            await remove(confirmDel.id, reduxId);
            setConfirmDel(null);
          }}
        />
      )}
    </div>
  );
}

function VersionRow({
  version, onSave, onDelete,
}: {
  version: ReduxVersion;
  onSave: (patch: Partial<ReduxVersion>) => Promise<void>;
  onDelete: () => void;
}) {
  const { t } = useTranslation();
  const [label, setLabel] = useState(version.label);
  const [saving, setSaving] = useState(false);
  const dirty = label !== version.label;

  const sizeMb = version.patchSizeBytes > 0 ? `${(version.patchSizeBytes / (1024 * 1024)).toFixed(0)} MB` : '-';
  const shaShort = version.patchSha256 ? version.patchSha256.slice(0, 8) : '-';

  return (
    <li className="flex items-center gap-2 px-2 py-1.5 rounded-md bg-bg-base border border-border-subtle">
      <span className="inline-flex items-center justify-center w-7 h-7 rounded bg-accent-soft text-accent text-xs font-bold tabular-nums shrink-0">
        {version.slot}
      </span>
      <input
        type="text"
        value={label}
        onChange={e => setLabel(e.target.value)}
        className="flex-1 px-2 py-1 bg-bg-elevated border border-border-subtle rounded text-sm text-text-primary outline-none focus:border-accent min-w-0"
      />
      <span className="text-xs text-text-muted tabular-nums shrink-0 w-14 text-right">{sizeMb}</span>
      <span className="text-[10px] text-text-muted font-mono shrink-0" title={version.patchSha256 ?? ''}>{shaShort}</span>
      {dirty && (
        <button
          type="button"
          disabled={saving}
          onClick={async () => {
            setSaving(true);
            try { await onSave({ label }); } finally { setSaving(false); }
          }}
          className="w-7 h-7 rounded-md flex items-center justify-center text-accent hover:bg-bg-elevated disabled:opacity-50"
          title={t('admin.database.versions.saveLabel')}
        >
          <Check size={14} />
        </button>
      )}
      <button
        type="button"
        onClick={onDelete}
        className="w-7 h-7 rounded-md flex items-center justify-center text-text-muted hover:text-status-error hover:bg-bg-elevated"
        title={t('admin.database.versions.delete')}
      >
        <Trash2 size={13} />
      </button>
    </li>
  );
}

function GunpackEditModal({ item, onClose, onSave }: {
  item: Gunpack;
  onClose: () => void;
  onSave: (patch: import('@/bridge/types').GunpackPatch) => Promise<void>;
}) {
  const [name,        setName]        = useState(item.name);
  const [author,      setAuthor]      = useState(item.author ?? '');
  const [authorLink,  setAuthorLink]  = useState(item.authorLink ?? '');
  const [description, setDescription] = useState(item.description ?? '');
  const [viewerPriority, setViewerPriority] = useState(item.viewerPriority ?? 0);
  const [coverKind,   setCoverKind]   = useState<'image' | 'youtube'>(item.coverKind);
  const [coverUrl,    setCoverUrl]    = useState(item.coverUrl ?? '');
  const [isVerified,  setIsVerified]  = useState(item.isVerified);

  const [coverUploading, setCoverUploading] = useState(false);
  const [coverError, setCoverError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError]   = useState<string | null>(null);

  const onUploadCover = async () => {
    setCoverError(null);
    if (typeof bridge.adminUploadGunpackCover !== 'function') {
      setCoverError('Перезапусти лаунчер - UI bridge устарел.');
      return;
    }
    let path: string | null = null;
    try {
      path = await bridge.openFileDialog('PNG / JPG / WEBP / GIF', '*.png;*.jpg;*.jpeg;*.gif;*.webp');
    } catch (e) { console.warn('[gunpack.cover.dialog]', e); return; }
    if (!path) return;
    setCoverUploading(true);
    try {
      const url = await bridge.adminUploadGunpackCover(path);
      setCoverUrl(url);
      setCoverKind('image');
    } catch (e) {
      setCoverError((e as Error).message || 'Не удалось загрузить обложку.');
    } finally {
      setCoverUploading(false);
    }
  };

  const handleSave = async () => {
    const patch: import('@/bridge/types').GunpackPatch = {};
    if (name.trim() !== item.name)                            patch.name = name.trim();
    if ((author.trim() || null) !== (item.author ?? null))    patch.author = author.trim();
    if ((authorLink.trim() || null) !== (item.authorLink ?? null)) patch.authorLink = authorLink.trim();
    if ((description.trim() || null) !== (item.description ?? null)) patch.description = description.trim();
    if (viewerPriority !== (item.viewerPriority ?? 0))        patch.viewerPriority = viewerPriority;
    if (coverKind !== item.coverKind)                         patch.coverKind = coverKind;
    if ((coverUrl.trim() || null) !== (item.coverUrl ?? null)) patch.coverUrl = coverUrl.trim();
    if (isVerified !== item.isVerified)                       patch.isVerified = isVerified;

    if (Object.keys(patch).length === 0) { onClose(); return; }

    if (!name.trim()) { setError('Имя не может быть пустым'); return; }

    setSaving(true);
    setError(null);
    try {
      await onSave(patch);
    } catch (e) {
      setError((e as Error).message || 'Не удалось сохранить');
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={saving ? undefined : onClose}>
      <div
        className="w-full max-w-[720px] max-h-[90vh] overflow-y-auto rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4"
        onClick={e => e.stopPropagation()}
      >
        <div className="flex items-center justify-between">
          <h2 className="text-base font-bold text-text-primary">Редактировать ганпак</h2>
          <button type="button" onClick={onClose} disabled={saving} className="text-text-muted hover:text-text-primary disabled:opacity-50">
            <X size={16} />
          </button>
        </div>

        <FieldRow label="Name"        value={name}        onChange={setName} />
        <FieldRow label="Author"      value={author}      onChange={setAuthor} />
        <FieldRow label="Author link" value={authorLink}  onChange={setAuthorLink} />
        <FieldRow label="Description" value={description} onChange={setDescription} textarea />

        <label className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wider text-text-muted">Viewer priority</span>
          <input
            type="number"
            value={viewerPriority || 0}
            onChange={e => setViewerPriority(parseInt(e.target.value, 10) || 0)}
            className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent w-32"
          />
          <span className="text-xs text-text-muted">Выше число - выше в каталоге.</span>
        </label>

        <div className="flex flex-col gap-2">
          <span className="text-xs uppercase tracking-wider text-text-muted">Обложка</span>
          <div className="flex gap-2">
            {(['image', 'youtube'] as const).map(k => (
              <button
                key={k}
                type="button"
                onClick={() => setCoverKind(k)}
                className={
                  'px-3 py-1.5 rounded-lg text-xs uppercase tracking-wider border transition-colors ' +
                  (coverKind === k
                    ? 'bg-accent-soft text-accent border-accent'
                    : 'bg-bg-base text-text-secondary border-border-subtle hover:border-border-strong')
                }
              >
                {k}
              </button>
            ))}
          </div>

          <div className="flex items-stretch gap-3">
            <div className="w-24 h-24 rounded-lg bg-bg-elevated overflow-hidden flex items-center justify-center text-text-muted shrink-0">
              {coverKind === 'image' && coverUrl
                ? <img src={coverUrl} alt="" className="w-full h-full object-cover" onError={e => (e.currentTarget.style.display = 'none')} />
                : <Crosshair size={28} />}
            </div>
            <div className="flex-1 flex flex-col gap-2">
              <input
                type="text"
                value={coverUrl}
                onChange={e => setCoverUrl(e.target.value)}
                placeholder={coverKind === 'image' ? 'https://...png' : 'https://youtu.be/...'}
                className="px-3 py-2 bg-bg-elevated border border-border-subtle rounded-lg text-sm text-text-primary outline-none focus:border-accent"
              />
              {coverKind === 'image' && (
                <button
                  type="button"
                  onClick={() => void onUploadCover()}
                  disabled={coverUploading}
                  className="inline-flex items-center gap-2 self-start px-3 py-1.5 rounded-lg
                             bg-white/[0.04] border border-white/[0.08] text-text-secondary text-xs
                             hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                             transition-colors disabled:opacity-60"
                >
                  {coverUploading ? <Loader2 size={12} className="animate-spin" /> : <Upload size={12} />}
                  <span>{coverUploading ? 'Загружаем…' : 'Загрузить файл'}</span>
                </button>
              )}
              {coverError && <span className="text-xs text-status-error">{coverError}</span>}
            </div>
          </div>
        </div>

        <label className="inline-flex items-center gap-2 cursor-pointer">
          <input
            type="checkbox"
            checked={isVerified}
            onChange={e => setIsVerified(e.target.checked)}
            className="w-4 h-4 rounded cursor-pointer"
            style={{ accentColor: 'var(--accent)' }}
          />
          <span className="text-sm text-text-primary">Verified автор</span>
        </label>

        {error && <div className="text-xs text-status-error">{error}</div>}

        <div className="flex justify-end gap-2 pt-2">
          <button
            type="button"
            disabled={saving}
            onClick={onClose}
            className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-50"
          >
            Отмена
          </button>
          <button
            type="button"
            disabled={saving}
            onClick={() => void handleSave()}
            className="inline-flex items-center gap-2 px-4 py-2 rounded-lg bg-accent text-text-on-accent text-sm font-medium hover:bg-accent-hover disabled:opacity-50"
          >
            {saving && <Loader2 size={12} className="animate-spin" />}
            <span>Сохранить</span>
          </button>
        </div>
      </div>
    </div>
  );
}

function ConfirmDialog({ title, message, busy, onCancel, onConfirm }: { title: string; message: string; busy?: boolean; onCancel: () => void; onConfirm: () => Promise<void> | void }) {
  const { t } = useTranslation();
  return (
    <div className="fixed inset-0 z-50 bg-black/60 flex items-center justify-center p-6" onClick={busy ? undefined : onCancel}>
      <div className="w-full max-w-[440px] rounded-2xl bg-bg-surface border border-border-subtle p-6 flex flex-col gap-4" onClick={e => e.stopPropagation()}>
        <h2 className="text-base font-bold text-text-primary">{title}</h2>
        <p className="text-sm text-text-secondary">{message}</p>
        <div className="flex justify-end gap-2 pt-2">
          <button type="button" disabled={busy} onClick={onCancel} className="px-4 py-2 rounded-lg border border-border-strong text-sm text-text-primary hover:bg-bg-elevated disabled:opacity-50">
            {t('admin.database.cancel')}
          </button>
          <button type="button" disabled={busy} onClick={() => void onConfirm()} className="px-4 py-2 rounded-lg bg-status-error text-white text-sm font-medium hover:opacity-90 disabled:opacity-50">
            {busy ? '...' : t('admin.database.deleteConfirm')}
          </button>
        </div>
      </div>
    </div>
  );
}
