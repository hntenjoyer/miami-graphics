import { create } from 'zustand';
import { bridge } from '@/bridge';
import i18n from '@/i18n';
import type { UserBuildDto, BuildDevices } from '@/bridge/types';

export type UserBuildStatus = 'pending' | 'approved' | 'rejected';

export type ArmorSelection =
  | { kind: 'none' }
  | { kind: 'override'; reduxId: string }
  | { kind: 'library';  armorLibraryId: string };

export type ArenaSelection =
  | { kind: 'override'; reduxId: string };

export type MinimapSelection =
  | { kind: 'override'; reduxId: string }
  | { kind: 'library';  minimapLibraryId: string };

export type ReticleSelection =
  | { kind: 'override'; reduxId: string }
  | { kind: 'library';  reticleLibraryId: string };

export type SoundsSelection =
  | { kind: 'library'; soundsLibraryId: string };

export type GunSlotState =
  | { kind: 'vanilla' }
  | { kind: 'override'; gunpackId: string; gunId: string };

export interface UserBuild {
  id: string;
  hntCode: string;
  name: string;
  author: string;
  authorUserId: string | null;
  reduxId: string;
  gunpackId: string;
  reduxNameSnapshot: string;
  gunpackNameSnapshot: string;
  gunSlots: Record<string, GunSlotState>;
  armor: ArmorSelection | null;
  arena: ArenaSelection | null;
  minimap: MinimapSelection | null;
  reticle: ReticleSelection | null;
  sounds: SoundsSelection | null;
  downloadCount: number;
  viewCount: number;
  createdAt: number;

  devices:        BuildDevices;
  sensitivity:    number | null;
  dpi:            number | null;
  resolution:     string | null;
  videoUrl:       string | null;
  settingsXmlUrl: string | null;
  description:    string;
  tier:           number | null;

  status:               UserBuildStatus;
  submittedForReview:   boolean;
  reviewedBy:           string | null;
  reviewedAt:           number | null;
  rejectReason:         string | null;

  coverUrl:             string | null;

  family:                    string | null;
  categoryLabel:             string | null;
  fpsAvg:                    number | null;
  monitorHz:                 number | null;
  adminNotes:                string | null;
}

export function gunInternalName(g: { weaponPrefix: string; baseName: string }): string {
  return `${g.weaponPrefix}${g.baseName}`;
}

function fromDto(d: UserBuildDto): UserBuild {
  let gunSlots: Record<string, GunSlotState> = {};
  try {
    if (d.gunSlotsJson) gunSlots = JSON.parse(d.gunSlotsJson);
  } catch {  }
  let armor: ArmorSelection | null = null;
  if (d.armorJson) {
    try { armor = JSON.parse(d.armorJson); } catch { armor = null; }
  }
  let arena: ArenaSelection | null = null;
  if (d.arenaJson) {
    try { arena = JSON.parse(d.arenaJson); } catch { arena = null; }
  }
  let minimap: MinimapSelection | null = null;
  if (d.minimapJson) {
    try { minimap = JSON.parse(d.minimapJson); } catch { minimap = null; }
  }
  let reticle: ReticleSelection | null = null;
  if (d.reticleJson) {
    try { reticle = JSON.parse(d.reticleJson); } catch { reticle = null; }
  }
  let sounds: SoundsSelection | null = null;
  if (d.soundsJson) {
    try { sounds = JSON.parse(d.soundsJson); } catch { sounds = null; }
  }

  let devices: BuildDevices = {};
  if (d.devicesJson) {
    try {
      const parsed = JSON.parse(d.devicesJson);
      if (parsed && typeof parsed === 'object') devices = parsed as BuildDevices;
    } catch { devices = {}; }
  }
  return {
    id:                  d.id,
    hntCode:             d.hntCode,
    name:                d.name,
    author:              d.authorUsername,
    authorUserId:        d.authorUserId,
    reduxId:             d.reduxId,
    gunpackId:           d.gunpackId,
    reduxNameSnapshot:   d.reduxNameSnapshot,
    gunpackNameSnapshot: d.gunpackNameSnapshot,
    gunSlots,
    armor,
    arena,
    minimap,
    reticle,
    sounds,
    downloadCount:       d.downloadCount,
    viewCount:           d.viewCount ?? 0,
    createdAt:           d.createdAt ? Date.parse(d.createdAt) : Date.now(),

    devices,
    sensitivity:    d.sensitivity,
    dpi:            d.dpi,
    resolution:     d.resolution,
    videoUrl:       d.videoUrl,
    settingsXmlUrl: d.settingsXmlUrl,
    description:    d.description ?? '',
    tier:           d.tier,

    status:               d.status,
    submittedForReview:   d.submittedForReview,
    reviewedBy:           d.reviewedBy,
    reviewedAt:           d.reviewedAt ? Date.parse(d.reviewedAt) : null,
    rejectReason:         d.rejectReason,

    coverUrl:             d.coverUrl ?? null,

    family:                    d.family ?? null,
    categoryLabel:             d.categoryLabel ?? null,
    fpsAvg:                    d.fpsAvg ?? null,
    monitorHz:                 d.monitorHz ?? null,
    adminNotes:                d.adminNotes ?? null,
  };
}

export interface BuildProfilePayload {
  devices?:        BuildDevices;
  sensitivity?:    number | null;
  dpi?:            number | null;
  resolution?:     string | null;
  videoUrl?:       string | null;
  settingsXmlUrl?: string | null;
  description?:    string;
  coverUrl?:       string | null;
}

interface UserBuildsState {
  builds: UserBuild[];
  loading: boolean;
  error: string | null;

  pending:          UserBuild[];
  myPending:        UserBuild[];
  loadingPending:   boolean;
  loadingMyPending: boolean;

  load: (search?: string | null) => Promise<void>;

  add: (input: {
    name: string;
    author: string;
    authorUserId: string | null;
    hntCode: string;
    reduxId: string;
    gunpackId: string;
    reduxNameSnapshot: string;
    gunpackNameSnapshot: string;
    gunSlots: Record<string, GunSlotState>;
    armor: ArmorSelection | null;
    arena: ArenaSelection | null;
    minimap: MinimapSelection | null;
    reticle: ReticleSelection | null;
    sounds: SoundsSelection | null;
  } & BuildProfilePayload) => Promise<UserBuild>;

  submit: (input: {
    name: string;
    author: string;
    authorUserId: string | null;
    hntCode: string;
    reduxId: string;
    gunpackId: string;
    reduxNameSnapshot: string;
    gunpackNameSnapshot: string;
    gunSlots: Record<string, GunSlotState>;
    armor: ArmorSelection | null;
    arena: ArenaSelection | null;
    minimap: MinimapSelection | null;
    reticle: ReticleSelection | null;
    sounds: SoundsSelection | null;
  } & BuildProfilePayload) => Promise<UserBuild>;

  update: (id: string, patch: Partial<UserBuildDto>) => Promise<UserBuild>;

  uploadSettingsXml: (buildId: string, sourceXmlPath: string) => Promise<string>;

  remove: (id: string) => Promise<void>;
  incrementDownloads: (id: string) => Promise<void>;
  incrementViews: (id: string) => Promise<void>;

  loadPending: () => Promise<void>;

  loadMyPending: (authorUserId: string) => Promise<void>;

  approve: (id: string, reviewerUserId: string, tier: number | null) => Promise<UserBuild>;

  reject: (id: string, reviewerUserId: string, reason: string) => Promise<UserBuild>;

  resubmit: (id: string) => Promise<UserBuild>;

  fetchByHntCode: (code: string) => Promise<UserBuild | null>;

  pendingDetailId: string | null;
  openByHntCode: (code: string) => Promise<UserBuild | null>;
  clearPendingDetail: () => void;
}

function buildDto(input: {
  hntCode: string;
  name: string;
  author: string;
  authorUserId: string | null;
  reduxId: string;
  gunpackId: string;
  reduxNameSnapshot: string;
  gunpackNameSnapshot: string;
  gunSlots: Record<string, GunSlotState>;
  armor: ArmorSelection | null;
  arena: ArenaSelection | null;
  minimap: MinimapSelection | null;
  reticle: ReticleSelection | null;
  sounds: SoundsSelection | null;
} & BuildProfilePayload): UserBuildDto {
  return {
    id: '',
    hntCode: input.hntCode,
    name: input.name,
    authorUserId: input.authorUserId,
    authorUsername: input.author,
    reduxId: input.reduxId,
    gunpackId: input.gunpackId,
    reduxNameSnapshot: input.reduxNameSnapshot,
    gunpackNameSnapshot: input.gunpackNameSnapshot,
    gunSlotsJson: JSON.stringify(input.gunSlots ?? {}),
    armorJson: input.armor ? JSON.stringify(input.armor) : null,
    arenaJson: input.arena ? JSON.stringify(input.arena) : null,
    minimapJson: input.minimap ? JSON.stringify(input.minimap) : null,
    reticleJson: input.reticle ? JSON.stringify(input.reticle) : null,
    soundsJson: input.sounds ? JSON.stringify(input.sounds) : null,
    downloadCount: 0,
    viewCount: 0,

    createdAt: null,
    updatedAt: null,

    devicesJson:    input.devices && Object.keys(input.devices).length > 0
                       ? JSON.stringify(input.devices) : null,
    sensitivity:    input.sensitivity    ?? null,
    dpi:            input.dpi            ?? null,
    resolution:     input.resolution     ?? null,
    videoUrl:       input.videoUrl       ?? null,
    settingsXmlUrl: input.settingsXmlUrl ?? null,
    description:    input.description    ?? '',
    tier:           null,

    status:               'approved',
    submittedForReview:   false,
    reviewedBy:           null,
    reviewedAt:           null,
    rejectReason:         null,

    coverUrl:             input.coverUrl ?? null,

    family:                    null,
    categoryLabel:             null,
    fpsAvg:                    null,
    adminNotes:                null,
  };
}

export const useUserBuildsStore = create<UserBuildsState>((set, get) => ({
  builds: [],
  loading: false,
  pendingDetailId: null,
  error: null,
  pending:          [],
  myPending:        [],
  loadingPending:   false,
  loadingMyPending: false,

  load: async (search = null) => {
    set({ loading: true, error: null });
    try {
      const dtos = await bridge.userBuildsList(search, null);
      set({ builds: dtos.map(fromDto), loading: false });
    } catch (e) {
      set({
        loading: false,
        error: e instanceof Error
          ? e.message
          : i18n.t('userBuilds.loadFail', 'Не удалось загрузить сборки'),
      });
    }
  },

  add: async (input) => {
    const saved = await bridge.userBuildCreate(buildDto(input));
    const hydrated = fromDto(saved);
    set(s => ({ builds: [hydrated, ...s.builds.filter(b => b.id !== hydrated.id)] }));
    return hydrated;
  },

  submit: async (input) => {

    const saved = await bridge.userBuildSubmit(buildDto(input));
    const hydrated = fromDto(saved);
    set(s => ({
      myPending: [hydrated, ...s.myPending.filter(b => b.id !== hydrated.id)],
    }));
    return hydrated;
  },

  update: async (id, patch) => {
    const saved = await bridge.userBuildUpdate(id, patch);
    const hydrated = fromDto(saved);

    set(s => ({
      builds:    s.builds.map(b => b.id === id ? hydrated : b),
      pending:   s.pending.map(b => b.id === id ? hydrated : b),
      myPending: s.myPending.map(b => b.id === id ? hydrated : b),
    }));
    return hydrated;
  },

  uploadSettingsXml: (buildId, sourceXmlPath) =>
    bridge.userBuildUploadSettingsXml(buildId, sourceXmlPath),

  remove: async (id) => {
    await bridge.userBuildDelete(id);
    set(s => ({
      builds:    s.builds.filter(b => b.id !== id),
      pending:   s.pending.filter(b => b.id !== id),
      myPending: s.myPending.filter(b => b.id !== id),
    }));
  },

  incrementDownloads: async (id) => {
    try {
      const newTotal = await bridge.userBuildIncrementDownloads(id);
      set(s => ({
        builds: s.builds.map(b => b.id === id ? { ...b, downloadCount: newTotal } : b),
      }));
    } catch {

      set(s => ({
        builds: s.builds.map(b => b.id === id ? { ...b, downloadCount: b.downloadCount + 1 } : b),
      }));
    }
  },

  incrementViews: async (id) => {
    try {
      const newTotal = await bridge.userBuildIncrementViews(id);
      set(s => ({
        builds: s.builds.map(b => b.id === id ? { ...b, viewCount: newTotal } : b),
      }));
    } catch {
      set(s => ({
        builds: s.builds.map(b => b.id === id ? { ...b, viewCount: b.viewCount + 1 } : b),
      }));
    }
  },

  loadPending: async () => {
    set({ loadingPending: true });
    try {
      const dtos = await bridge.userBuildListPending();
      set({ pending: dtos.map(fromDto), loadingPending: false });
    } catch (e) {
      console.warn('[userBuildsStore] loadPending failed', e);
      set({ loadingPending: false });
    }
  },

  loadMyPending: async (authorUserId) => {
    set({ loadingMyPending: true });
    try {
      const dtos = await bridge.userBuildListMyPending(authorUserId);
      set({ myPending: dtos.map(fromDto), loadingMyPending: false });
    } catch (e) {
      console.warn('[userBuildsStore] loadMyPending failed', e);
      set({ loadingMyPending: false });
    }
  },

  approve: async (id, reviewerUserId, tier) => {
    const saved = await bridge.userBuildApprove(id, reviewerUserId, tier);
    const hydrated = fromDto(saved);

    set(s => ({
      pending:   s.pending.filter(b => b.id !== id),
      builds:    [hydrated, ...s.builds.filter(b => b.id !== id)],
      myPending: s.myPending.filter(b => b.id !== id),
    }));
    return hydrated;
  },

  reject: async (id, reviewerUserId, reason) => {
    const saved = await bridge.userBuildReject(id, reviewerUserId, reason);
    const hydrated = fromDto(saved);

    set(s => ({
      pending:   s.pending.filter(b => b.id !== id),
      myPending: [hydrated, ...s.myPending.filter(b => b.id !== id)],
    }));
    return hydrated;
  },

  resubmit: async (id) => {
    const saved = await bridge.userBuildResubmit(id);
    const hydrated = fromDto(saved);

    set(s => ({
      myPending: s.myPending.map(b => b.id === id ? hydrated : b),
    }));
    return hydrated;
  },

  fetchByHntCode: async (code) => {
    const dto = await bridge.userBuildGetByHntCode(code.trim());
    if (!dto) return null;
    return fromDto(dto);
  },

  openByHntCode: async (code) => {
    const b = await get().fetchByHntCode(code);
    if (b) {
      set(s => ({
        builds: [b, ...s.builds.filter(x => x.id !== b.id)],
        pendingDetailId: b.id,
      }));
    }
    return b;
  },

  clearPendingDetail: () => set({ pendingDetailId: null }),
}));
