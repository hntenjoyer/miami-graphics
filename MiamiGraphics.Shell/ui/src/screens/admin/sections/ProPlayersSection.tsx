import { useEffect, useMemo, useState } from 'react';
import { motion } from 'framer-motion';
import { Trophy, Plus, Loader2, Search, Trash2, Edit3, Eye } from 'lucide-react';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { useSessionStore } from '@/store/sessionStore';
import { useAdminStore } from '@/store/adminStore';
import { useSubmitDraftStore } from '@/store/submitDraftStore';
import { ConfirmModal } from '@/components/ConfirmModal';
import type { UserBuild } from '@/store/userBuildsStore';
import type { UserBuildDto } from '@/bridge/types';
import { ProPlayerEditor } from './ProPlayerEditor';

export function ProPlayersSection() {
  const auth = useSessionStore(s => s.auth);
  const reviewerUserId = auth?.token?.startsWith('local-')
    ? auth.token.slice('local-'.length)
    : null;

  const builds      = useUserBuildsStore(s => s.builds);
  const loading     = useUserBuildsStore(s => s.loading);
  const loadBuilds  = useUserBuildsStore(s => s.load);

  const addBuild = useUserBuildsStore(s => s.add);
  const updateBuild = useUserBuildsStore(s => s.update);
  const removeBuild = useUserBuildsStore(s => s.remove);

  const reduxItems = useReduxStore(s => s.items);
  const reduxLoad  = useReduxStore(s => s.load);
  const publicPacks    = useGunpackStore(s => s.publicPacks);
  const loadPublicPacks = useGunpackStore(s => s.loadPublicPacks);

  useEffect(() => {
    if (builds.length === 0) void loadBuilds();
    if (reduxItems.length === 0) void reduxLoad();
    if (publicPacks.length === 0) void loadPublicPacks();
  }, []);

  const [search, setSearch] = useState('');
  const [deleteCandidate, setDeleteCandidate] = useState<UserBuild | null>(null);
  const [deleting, setDeleting] = useState(false);

  const proDraft       = useAdminStore(s => s.proPlayerDraft);
  const openEditor     = useAdminStore(s => s.openProPlayerEditor);
  const patchDraft     = useAdminStore(s => s.patchProPlayerDraft);
  const closeEditor    = useAdminStore(s => s.closeProPlayerEditor);
  const editorOpen     = proDraft.editingTarget !== null;

  const submitDraft     = useSubmitDraftStore();
  const finalizedRedux   = submitDraft.returnTo === 'admin' ? submitDraft.reduxId   : null;
  const finalizedGunpack = submitDraft.returnTo === 'admin' ? submitDraft.gunpackId : null;
  useEffect(() => {
    if (!editorOpen) return;
    if (submitDraft.returnTo !== 'admin') return;
    if (submitDraft.reduxId && submitDraft.reduxId !== proDraft.reduxId) {
      patchDraft({
        reduxId: submitDraft.reduxId,
        reduxNameSnapshot: submitDraft.reduxName ?? '',
      });
    }
    if (submitDraft.gunpackId && submitDraft.gunpackId !== proDraft.gunpackId) {
      patchDraft({
        gunpackId: submitDraft.gunpackId,
        gunpackNameSnapshot: submitDraft.gunpackName ?? '',
        gunSlots: {},
      });
    }

    if (submitDraft.reduxId || submitDraft.gunpackId) {
      submitDraft.reset();
    }

  }, [editorOpen, finalizedRedux, finalizedGunpack]);

  const proRows = useMemo(() => {
    const q = search.trim().toLowerCase();
    return builds
      .filter(b => b.tier !== null)
      .filter(b => {
        if (!q) return true;
        return b.name.toLowerCase().includes(q)
          || b.author.toLowerCase().includes(q)
          || (b.family ?? '').toLowerCase().includes(q);
      })
      .sort((a, b) => {

        const ta = a.tier ?? 99;
        const tb = b.tier ?? 99;
        if (ta !== tb) return ta - tb;
        return b.createdAt - a.createdAt;
      });
  }, [builds, search]);

  const onConfirmDelete = async () => {
    if (!deleteCandidate || deleting) return;
    setDeleting(true);
    try {
      await removeBuild(deleteCandidate.id);
      setDeleteCandidate(null);
    } catch (err) {
      console.warn('[admin.proPlayers] delete failed', err);
    } finally {
      setDeleting(false);
    }
  };

  const onSave = async (
    payload: Partial<UserBuildDto>,
  ) => {
    const target = proDraft.editingTarget;
    if (target === null) return;
    if (target === 'new') {

      const input = {
        name:                payload.name ?? '',
        author:              payload.authorUsername ?? 'admin',
        authorUserId:        reviewerUserId,
        hntCode:             generateHntCodeStub(),
        reduxId:             payload.reduxId ?? '',
        gunpackId:           payload.gunpackId ?? '',
        reduxNameSnapshot:   payload.reduxNameSnapshot ?? '',
        gunpackNameSnapshot: payload.gunpackNameSnapshot ?? '',
        gunSlots:            payload.gunSlotsJson ? JSON.parse(payload.gunSlotsJson) : {},
        armor:               null,
        arena:               null,
        minimap:             null,
        reticle:             null,
        sounds:              null,
        devices:             payload.devicesJson ? JSON.parse(payload.devicesJson) : {},
        sensitivity:         payload.sensitivity ?? null,
        dpi:                 payload.dpi ?? null,
        resolution:          payload.resolution ?? null,
        videoUrl:            payload.videoUrl ?? null,
        settingsXmlUrl:      payload.settingsXmlUrl ?? null,
        description:         payload.description ?? '',
        coverUrl:            payload.coverUrl ?? null,
      };
      const saved = await addBuild(input);

      const proPatch: Partial<UserBuildDto> = {
        tier:                     payload.tier ?? 1,
        family:                   payload.family ?? null,
        categoryLabel:            payload.categoryLabel ?? null,
        fpsAvg:                   payload.fpsAvg ?? null,
        monitorHz:                payload.monitorHz ?? null,
        adminNotes:               payload.adminNotes ?? null,
        status:                   'approved',
      };
      await updateBuild(saved.id, proPatch);
    } else {

      await updateBuild(target.id, payload);
    }
    closeEditor();
  };

  if (editorOpen) {
    return (
      <ProPlayerEditor
        onCancel={() => closeEditor()}
        onSave={onSave}
      />
    );
  }

  return (
    <motion.div
      className="h-full overflow-y-auto"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.32, ease: EASE_DEPTH }}
    >
      <div className="max-w-[1280px] mx-auto px-8 py-6 flex flex-col gap-5">
        <header className="flex items-center justify-between gap-4 flex-wrap">
          <div className="flex items-center gap-3">
            <span className="inline-flex items-center justify-center w-10 h-10 rounded-xl
                             bg-accent-soft text-accent">
              <Trophy size={20} />
            </span>
            <div>
              <h1 className="font-display font-bold text-2xl text-text-primary uppercase tracking-tight">
                PRO Players
              </h1>
              <p className="text-sm text-text-muted">
                Курируемые сборки топ-игроков. Заносит админ; tier 1-3.
              </p>
            </div>
          </div>
          <button
            type="button"
            onClick={() => openEditor('new')}
            className="inline-flex items-center gap-2 px-4 h-10 rounded-xl
                       bg-accent text-text-on-accent font-semibold text-sm
                       hover:bg-accent-hover shadow-glow-accent transition-colors"
            style={{ outline: 'none' }}
          >
            <Plus size={14} />
            <span>Добавить игрока</span>
          </button>
        </header>

        {}
        <div className="flex items-center gap-3 flex-wrap">
          <label className="relative flex items-center w-[320px] max-w-full h-10 rounded-xl
                            bg-white/[0.04] border border-white/[0.06]
                            focus-within:border-accent/40 transition-colors">
            <Search size={14} className="absolute left-3.5 text-text-muted pointer-events-none" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Поиск по нику, автору, семье"
              className="w-full h-full pl-10 pr-3 bg-transparent rounded-xl
                         text-[13px] text-text-primary placeholder:text-text-muted outline-none"
            />
          </label>
          <span className="ml-auto inline-flex items-center gap-1.5 px-3 h-9 rounded-xl
                           bg-white/[0.04] border border-white/[0.06]
                           text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted">
            <span className="tabular-nums text-text-primary">{proRows.length}</span>
            <span>игроков</span>
          </span>
        </div>

        {}
        {loading && proRows.length === 0 ? (
          <div className="py-20 flex flex-col items-center gap-3 text-text-muted">
            <Loader2 size={20} className="animate-spin" />
            <span className="text-sm">Загружаем сборки…</span>
          </div>
        ) : proRows.length === 0 ? (
          <GlassPanel depth="z1" tint="soft" rounded="3xl" className="p-10 text-center">
            <Trophy size={40} className="mx-auto mb-3 text-text-muted opacity-40" />
            <h2 className="text-base font-semibold text-text-primary mb-1">
              Пока нет PRO-игроков
            </h2>
            <p className="text-sm text-text-secondary">
              Нажмите «Добавить игрока» сверху чтобы завести первого.
            </p>
          </GlassPanel>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {proRows.map(b => (
              <ProRow
                key={b.id}
                build={b}
                onEdit={() => openEditor(b)}
                onDelete={() => setDeleteCandidate(b)}
              />
            ))}
          </div>
        )}

      </div>

      <ConfirmModal
        open={deleteCandidate !== null}
        title="Удалить PRO-игрока?"
        message={`«${deleteCandidate?.name ?? ''}»\n\nЭто действие нельзя отменить - карточка пропадёт у всех, кто видит её через PRO-чип.`}
        confirmLabel={deleting ? 'Удаляем…' : 'Удалить'}
        cancelLabel="Отмена"
        destructive
        onConfirm={() => { void onConfirmDelete(); }}
        onCancel={() => { if (!deleting) setDeleteCandidate(null); }}
      />
    </motion.div>
  );
}

function generateHntCodeStub(): string {
  const a = Math.random().toString(36).slice(2, 6).toUpperCase();
  const b = Math.random().toString(36).slice(2, 6).toUpperCase();
  return `HNT-${a}-${b}`;
}

function ProRow({
  build, onEdit, onDelete,
}: {
  build: UserBuild;
  onEdit: () => void;
  onDelete: () => void;
}) {
  return (
    <GlassPanel depth="z2" tint="strong" rounded="2xl" className="p-4 flex gap-3">
      <div className="shrink-0 w-20 h-20 rounded-xl overflow-hidden
                      bg-bg-elevated/60 border border-white/[0.06]
                      flex items-center justify-center">
        {build.coverUrl ? (
          <img
            src={build.coverUrl}
            alt=""
            draggable={false}
            className="w-full h-full object-cover"
            onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
          />
        ) : (
          <Trophy size={22} className="text-accent opacity-60" />
        )}
      </div>
      <div className="flex-1 min-w-0 flex flex-col gap-0.5">
        <div className="flex items-center gap-2">
          <span className="text-[10px] font-bold uppercase tracking-[0.2em] text-accent">
            TIER {build.tier}
          </span>
          {build.categoryLabel && (
            <span className="text-[10px] font-bold uppercase tracking-[0.18em] text-text-muted">
              · {build.categoryLabel}
            </span>
          )}
        </div>
        <h3 className="font-display font-bold text-base text-text-primary truncate">
          {build.name}
        </h3>
        {build.family && (
          <p className="text-xs text-text-muted truncate">{build.family}</p>
        )}
        <p className="text-[11px] text-text-muted truncate mt-0.5">
          {build.reduxNameSnapshot} · {build.gunpackNameSnapshot}
        </p>
        <div className="flex items-center gap-1 mt-2">
          <button
            type="button"
            onClick={onEdit}
            className="inline-flex items-center gap-1.5 px-2.5 h-7 rounded-md
                       bg-glass border border-glass-border text-[11px] font-semibold
                       text-text-secondary hover:text-text-primary hover:bg-glass-strong
                       transition-colors"
            style={{ outline: 'none' }}
          >
            <Edit3 size={11} /> <span>Редактировать</span>
          </button>
          <button
            type="button"
            onClick={onDelete}
            className="inline-flex items-center justify-center w-7 h-7 rounded-md
                       bg-red-500/10 border border-red-500/20 text-red-300
                       hover:bg-red-500/20 hover:border-red-500/40 transition-colors"
            style={{ outline: 'none' }}
            title="Удалить"
          >
            <Trash2 size={11} />
          </button>
          <span className="ml-auto inline-flex items-center gap-1 text-[10px] text-text-muted">
            <Eye size={10} /> {build.downloadCount}
          </span>
        </div>
      </div>
    </GlassPanel>
  );
}
