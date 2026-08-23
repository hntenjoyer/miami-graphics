import { Settings, Layers, Crosshair, Syringe, Database, TrendingUp, Gamepad2, FolderOpen, Sliders, Shield, FileBox, Inbox, Trophy, Map, Palette, type LucideIcon } from 'lucide-react';
import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import { useSessionStore } from '@/store/sessionStore';
import { useUserBuildsStore } from '@/store/userBuildsStore';
import { useCustomGunsStore } from '@/store/customGunsStore';
import { EASE_DEPTH } from '@/design';

export type AdminSectionId = 'settings' | 'redux' | 'guns' | 'armor' | 'armorImport' | 'injector' | 'database' | 'popularity' | 'gtaVersions' | 'library' | 'gtaPresets' | 'pendingBuilds' | 'customGunsReview' | 'customGunsManage' | 'proPlayers' | 'bigmap';

interface AdminSection {
  id: AdminSectionId;
  icon: LucideIcon;
  labelKey: string;
  label?: string;
  rolesVisible?: string[];
}

interface AdminGroup {
  title: string;
  items: AdminSection[];
}

export const ADMIN_GROUPS: AdminGroup[] = [
  {
    title: '',
    items: [
      { id: 'settings', icon: Settings, labelKey: 'admin.subnav.settings' },
    ],
  },
  {
    title: 'Модерация',
    items: [
      { id: 'armor',            icon: Shield,   labelKey: 'admin.subnav.armor',            label: 'Manage Armor' },
      { id: 'pendingBuilds',    icon: Inbox,    labelKey: 'admin.subnav.pendingBuilds',    label: 'Players Request' },
      { id: 'customGunsReview', icon: Palette,  labelKey: 'admin.subnav.customGunsReview', label: 'Skins Request' },
      { id: 'customGunsManage', icon: Palette,  labelKey: 'admin.subnav.customGunsManage', label: 'Manage Skins' },
    ],
  },
  {
    title: 'ВАЖНОЕ',
    items: [
      { id: 'gtaVersions', icon: Gamepad2,   labelKey: 'admin.subnav.gtaVersions', label: 'Game Versions', rolesVisible: ['AdminL2'] },
    ],
  },
  {
    title: 'Инструменты',
    items: [
      { id: 'database', icon: Database, labelKey: 'admin.subnav.database', label: 'All Mods' },
    ],
  },
  {
    title: 'Маркетинг',
    items: [
      { id: 'proPlayers', icon: Trophy,     labelKey: 'admin.subnav.proPlayers', label: 'Players Build', rolesVisible: ['AdminL1', 'AdminL2'] },
      { id: 'popularity', icon: TrendingUp, labelKey: 'admin.subnav.popularity', label: 'Main Screen' },
    ],
  },
  {
    title: 'Загрузка',
    items: [
      { id: 'redux',       icon: Layers,     labelKey: 'admin.subnav.redux',       label: 'Redux Upload' },
      { id: 'guns',        icon: Crosshair,  labelKey: 'admin.subnav.guns',        label: 'Guns Upload' },
      { id: 'armorImport', icon: FileBox,    labelKey: 'admin.subnav.armorImport', label: 'Armor Upload',    rolesVisible: ['AdminL2'] },
      { id: 'gtaPresets',  icon: Sliders,    labelKey: 'admin.subnav.gtaPresets',  label: 'Settings Upload', rolesVisible: ['AdminL2'] },
      { id: 'library',     icon: FolderOpen, labelKey: 'admin.subnav.library',     label: 'Other Upload',    rolesVisible: ['AdminL2'] },
      { id: 'bigmap',      icon: Map,        labelKey: 'admin.subnav.bigmap',      label: 'Big Map Upload',  rolesVisible: ['AdminL2'] },
    ],
  },
  {
    title: 'Другое',
    items: [
      { id: 'injector', icon: Syringe, labelKey: 'admin.subnav.injector' },
    ],
  },
];

export const ADMIN_SECTIONS: AdminSection[] = ADMIN_GROUPS.flatMap(g => g.items);

interface Props {
  active: AdminSectionId;
  onChange: (id: AdminSectionId) => void;
}

export function AdminSidebar({ active, onChange }: Props) {
  const { t } = useTranslation();
  const role = useSessionStore(s => s.auth?.role) ?? '';
  const pendingCount = useUserBuildsStore(s => s.pending.length);
  const customPendingCount = useCustomGunsStore(s => s.pending.length);

  let flatIdx = 0;

  return (
    <aside className="w-[208px] shrink-0 h-full p-3">
      <div className="h-full rounded-3xl bg-glass backdrop-blur-glass backdrop-saturate-150
                      border border-glass-border shadow-z2 flex flex-col overflow-hidden">
        <nav className="flex-1 overflow-y-auto pt-3 pb-3 flex flex-col gap-0.5">
          {ADMIN_GROUPS.map((group, groupIdx) => {
            const visibleItems = group.items.filter(s => !s.rolesVisible || s.rolesVisible.includes(role));
            if (visibleItems.length === 0) return null;

            return (
              <div key={group.title || `group-${groupIdx}`} className="flex flex-col gap-1.5">
                {group.title && (
                  <div className="mx-3 mt-3 mb-1 text-[10px] uppercase tracking-[0.18em] font-bold text-text-muted/70">
                    {group.title}
                  </div>
                )}
                {visibleItems.map(s => {
                  const Icon = s.icon;
                  const isActive = s.id === active;
                  const badgeCount = s.id === 'pendingBuilds' ? pendingCount
                    : s.id === 'customGunsReview' ? customPendingCount : 0;
                  const showBadge = badgeCount > 0;
                  const idx = flatIdx++;
                  return (
                    <motion.button
                      key={s.id}
                      type="button"
                      onClick={() => onChange(s.id)}

                      initial={{ opacity: 0, x: -6 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{
                        duration: 0.28,
                        delay: idx * 0.025,
                        ease: EASE_DEPTH,
                      }}
                      whileHover={{ x: isActive ? 0 : 2 }}
                      whileTap={{ scale: 0.97 }}
                      className={
                        'mx-2 h-10 rounded-2xl flex items-center gap-3 px-3 text-sm font-semibold tracking-tight ' +
                        'transition-[background-color,color,box-shadow,border-color] duration-300 ease-depth text-left ' +
                        'border ' +
                        (isActive

                          ? 'bg-accent-soft text-accent border-[color-mix(in_srgb,var(--accent)_55%,transparent)] ' +
                            'shadow-[0_0_0_1px_color-mix(in_srgb,var(--accent)_28%,transparent),0_8px_24px_-10px_color-mix(in_srgb,var(--accent)_55%,transparent)]'
                          : 'border-transparent text-text-secondary hover:text-text-primary hover:bg-glass-strong ' +
                            'hover:border-[color-mix(in_srgb,var(--accent)_22%,transparent)]')
                      }
                    >
                      <Icon size={16} className="shrink-0" />
                      <span className="truncate flex-1">{s.label ?? t(s.labelKey)}</span>
                      {showBadge && (
                        <motion.span
                          initial={{ scale: 0.6, opacity: 0 }}
                          animate={{ scale: 1, opacity: 1 }}
                          transition={{ type: 'spring', stiffness: 420, damping: 24 }}
                          className="shrink-0 px-1.5 h-4 inline-flex items-center justify-center rounded
                                         bg-status-warning/20 text-status-warning
                                         text-[10px] font-bold tabular-nums"
                        >
                          {badgeCount}
                        </motion.span>
                      )}
                    </motion.button>
                  );
                })}
              </div>
            );
          })}
        </nav>
      </div>
    </aside>
  );
}
