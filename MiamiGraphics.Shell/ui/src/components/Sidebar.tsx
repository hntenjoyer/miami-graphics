import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ConfirmModal } from '@/components/ConfirmModal';
import { AnimatePresence, motion } from 'framer-motion';
import { LogOut, LogIn, Settings, User as UserIcon, Download } from 'lucide-react';
import {
  MAIN_NAV, REDUX_CUSTOMIZE_CHILD, MODIFICATION_IDS, SECURITY_NAV_ITEM,
  type NavItem, type UserRole, type NavItemId,
} from '@/data/navigation';
import { useSessionStore, useCanSeeTesterFeature } from '@/store/sessionStore';
import { useAppScreenStore } from '@/store/appScreenStore';
import { useCustomizeStore } from '@/store/customizeStore';
import { useInstallProgressStore } from '@/store/installProgressStore';
import { EASE_DEPTH } from '@/design';

interface SidebarProps {
  activeId: NavItemId;
  onNavigate: (id: NavItemId) => void;
}

export function Sidebar({ activeId, onNavigate }: SidebarProps) {
  const { t } = useTranslation();

  const collapsed = true;

  const auth = useSessionStore(s => s.auth);
  const profile = useSessionStore(s => s.profile);
  const logout = useSessionStore(s => s.logout);

  const [logoutAsk, setLogoutAsk] = useState(false);

  const role: UserRole = (auth?.role as UserRole) ?? 'Guest';
  const username = auth?.username ?? t('sidebar.guest');
  const isGuest = role === 'Guest';

  const customizeActive = useCustomizeStore(s => s.activeReduxId !== null);
  const canSeeTesterFeature = useCanSeeTesterFeature();
  const SecurityIcon = SECURITY_NAV_ITEM.icon;

  const activeInstallCount = useInstallProgressStore(s =>
    Object.values(s.byId).filter(e => e.phase !== 'done' && e.phase !== 'error').length,
  );
  const visibleNav = MAIN_NAV
    .filter(item => !item.testerOnly || canSeeTesterFeature)
    .filter(item => !item.rolesVisible || item.rolesVisible.includes(role))
    .map(item => {

      if (item.id === 'redux' && customizeActive) {
        return { ...item, children: [REDUX_CUSTOMIZE_CHILD] };
      }
      return item;
    });

  return (
    <>
    <aside className="shrink-0 h-full pl-4 pr-1 py-2 w-[90px]">
      <div
        className="relative h-full flex flex-col rounded-3xl overflow-hidden
                   bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid
                   border border-white/[0.07]
                   shadow-[0_12px_40px_-18px_rgba(0,0,0,0.6)]"
      >
        <span
          aria-hidden
          className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                     bg-gradient-to-r from-transparent via-white/35 to-transparent"
        />
        <span
          aria-hidden
          className="absolute -top-16 -left-10 w-44 h-44 pointer-events-none blur-3xl z-0"
          style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }}
        />
        {}
        {}
        <nav className={
          'relative z-[1] ' +
          'flex-1 overflow-y-auto pt-2 pb-2 flex flex-col ' +
          (collapsed ? 'gap-2' : 'gap-1.5')
        }>
          {visibleNav.map(item => {

            if (!item.children) {

              const isActive = item.id === 'modifications'
                ? MODIFICATION_IDS.includes(activeId)
                : item.id === activeId;
              return (
                <NavButton
                  key={item.id}
                  item={item}
                  active={isActive}
                  collapsed={collapsed}
                  onClick={() => onNavigate(item.id)}
                  label={t(item.labelKey)}
                />
              );
            }

            const containsActive = item.id === activeId || item.children.some(c =>
              c.id === activeId || c.children?.some(gc => gc.id === activeId),
            );
            return (
              <div key={item.id} className="flex flex-col gap-1.5">
                <NavButton
                  item={item}
                  active={containsActive && item.id === activeId}
                  collapsed={collapsed}
                  onClick={() => onNavigate(item.id)}
                  label={t(item.labelKey)}
                />
                {}
                <AnimatePresence initial={false}>
                  {item.children.map(child => (
                    <motion.div
                      key={child.id}
                      initial={{ opacity: 0, scale: 0.85, y: -6 }}
                      animate={{ opacity: 1, scale: 1, y: 0 }}
                      exit   ={{ opacity: 0, scale: 0.85, y: -6 }}
                      transition={{ duration: 0.28, ease: EASE_DEPTH }}
                      className="flex justify-center"
                    >
                      <SatelliteTile
                        item={child}
                        active={child.id === activeId}
                        label={t(child.labelKey)}
                        onClick={() => onNavigate(child.id)}
                      />
                    </motion.div>
                  ))}
                </AnimatePresence>
              </div>
            );
          })}
        </nav>

        {}

        {}

        {}
        <div className={
          (collapsed ? 'mx-4 ' : 'mx-3 ') +
          'h-px bg-gradient-to-r from-transparent via-white/15 to-transparent'
        } />

        {}
        <div className={
          'py-2 flex flex-col ' +
          (collapsed ? 'gap-2' : 'gap-1.5')
        }>
          {}
          <UtilityButton
            icon={<Download size={22} strokeWidth={1.7} />}
            label={t('nav.downloads')}
            collapsed={collapsed}
            active={activeId === 'downloads'}
            onClick={() => onNavigate('downloads' as NavItemId)}
            badgeCount={activeInstallCount}
            trailing={
              activeInstallCount > 0
                ? <DownloadActivityDot count={activeInstallCount} />
                : null
            }
          />
          {(
            <UtilityButton
              icon={<SecurityIcon size={22} strokeWidth={1.7} />}
              label={t(SECURITY_NAV_ITEM.labelKey)}
              collapsed={collapsed}
              active={activeId === SECURITY_NAV_ITEM.id}
              onClick={() => onNavigate(SECURITY_NAV_ITEM.id as NavItemId)}
            />
          )}
          <UtilityButton
            icon={<Settings size={22} strokeWidth={1.7} />}
            label={t('sidebar.appSettings')}
            collapsed={collapsed}
            active={activeId === 'app-settings'}
            onClick={() => onNavigate('app-settings' as NavItemId)}
          />
          {}
          <div data-launcher-demo-hide="exit">
            <UtilityButton
              icon={isGuest
                ? <LogIn size={22} strokeWidth={1.7} />
                : <LogOut size={22} strokeWidth={1.7} />}
              label={isGuest ? t('sidebar.login') : t('sidebar.logout')}
              collapsed={collapsed}
              onClick={() => {
                if (isGuest) {
                  logout();
                  useAppScreenStore.getState().request('welcome');
                  return;
                }
                setLogoutAsk(true);
              }}
              danger={!isGuest}
            />
          </div>
        </div>

        {}
        <div data-launcher-demo-hide="profile-divider" className={
          (collapsed ? 'mx-4 ' : 'mx-3 ') +
          'h-px bg-gradient-to-r from-transparent via-white/15 to-transparent'
        } />
        <button
          data-launcher-demo-hide="profile"
          type="button"
          onClick={() => onNavigate('profile' as NavItemId)}
          className={
            'group relative flex items-center transition-transform duration-200 ease-depth ' +
            (collapsed
              ? 'mx-auto my-1.5 justify-center hover:scale-[1.05]'
              : 'w-full py-3 gap-3 px-3 hover:bg-white/[0.06] rounded-2xl')
          }
          title={username}
          aria-label={username}
          style={{ outline: 'none' }}
        >
          {profile?.avatarUrl
            ? <img
                src={profile.avatarUrl}
                alt={username}
                className={(collapsed ? 'w-10 h-10' : 'w-9 h-9') +
                  ' rounded-full object-cover shrink-0 shadow-z1' +
                  ' border border-white/10 bg-glass-strong'}
                onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
              />
            : <div className={(collapsed ? 'w-10 h-10' : 'w-9 h-9') +
                ' rounded-full bg-glass-strong border border-white/10' +
                ' flex items-center justify-center shrink-0 shadow-z1'}>
                <UserIcon size={collapsed ? 18 : 16} className="text-text-secondary" />
              </div>
          }
          {}
          {!collapsed && (
            <div className="flex flex-col items-start min-w-0 flex-1">
              <span className="text-sm font-medium text-text-primary truncate w-full text-left">{username}</span>
              {isGuest && (
                <span className="text-[10px] text-text-muted uppercase tracking-wider">
                  {t('sidebar.guest')}
                </span>
              )}
            </div>
          )}
        </button>
      </div>
    </aside>

    <ConfirmModal
      open={logoutAsk}
      title={t('sidebar.logoutConfirmTitle', 'Выйти из аккаунта?')}
      message={t('sidebar.logoutConfirmBody', 'Моды и настройки останутся на месте - выход касается только аккаунта. Войти обратно можно тем же логином.')}
      confirmLabel={t('sidebar.logoutConfirmYes', 'Выйти')}
      cancelLabel={t('sidebar.logoutConfirmNo', 'Отмена')}
      destructive
      onConfirm={() => {
        setLogoutAsk(false);
        logout();
        useAppScreenStore.getState().request('welcome');
      }}
      onCancel={() => setLogoutAsk(false)}
    />
    </>
  );
}

interface NavButtonProps {
  item: NavItem;
  active: boolean;
  collapsed: boolean;
  onClick: () => void;
  label: string;
  trailing?: React.ReactNode;
  indent?: 0 | 1 | 2;
}

function NavButton({ item, active, collapsed, onClick, label, trailing, indent = 0 }: NavButtonProps) {
  const Icon = item.icon;

  if (collapsed) {
    return (
      <motion.button
        type="button"
        onClick={onClick}
        title={label}
        aria-label={label}
        whileHover={{ y: -3 }}
        whileTap={{ scale: 0.88 }}

        transition={{
          y:     { duration: 0.22, ease: EASE_DEPTH },
          scale: { type: 'spring', stiffness: 480, damping: 14, mass: 0.7 },
        }}
        className={
          'relative mx-auto w-[56px] h-[56px] rounded-[16px] flex items-center justify-center ' +
          'transition-[background-color,color] duration-300 ease-smooth ' +
          'focus-visible:outline-none ' +
          (active

            ? 'bg-white text-black ' +
              'shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_8px_-2px_rgba(0,0,0,0.35)]'

            : 'bg-transparent text-text-secondary hover:text-text-primary')
        }
      >
        <Icon size={22} strokeWidth={1.7} className="relative" />
      </motion.button>
    );
  }

  const indentClass = indent === 0 ? 'mx-2' : indent === 1 ? 'ml-6 mr-2' : 'ml-10 mr-2';
  const heightClass = indent === 0 ? 'h-11' : 'h-9';
  const iconSize    = indent === 0 ? 20 : 16;
  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileHover={!active ? { y: -1 } : undefined}
      whileTap={{ scale: 0.97 }}
      transition={{ duration: 0.15, ease: EASE_DEPTH }}
      className={
        indentClass + ' ' + heightClass + ' rounded-2xl flex items-center text-sm font-semibold tracking-tight ' +
        'transition-[background-color,color,box-shadow,border-color] duration-300 ease-depth ' +
        'border justify-start px-3 gap-3 text-left ' +
        (active
          ? 'bg-white text-black border-white ' +
            'shadow-pill-active'
          : 'border-transparent text-text-secondary hover:text-text-primary ' +
            'hover:bg-white/[0.06] hover:border-white/[0.10]')
      }
    >
      <Icon size={iconSize} className="shrink-0" />
      <span className="truncate flex-1">{label}</span>
      {trailing}
    </motion.button>
  );
}

interface UtilityButtonProps {
  icon: React.ReactNode;
  label: string;
  collapsed: boolean;
  active?: boolean;
  onClick: () => void;
  danger?: boolean;
  trailing?: React.ReactNode;
  badgeCount?: number;
}

function UtilityButton({ icon, label, collapsed, active, onClick, danger, trailing, badgeCount }: UtilityButtonProps) {

  if (collapsed) {
    return (
      <motion.button
        type="button"
        onClick={onClick}
        title={label}
        aria-label={label}
        whileHover={{ y: -3 }}
        whileTap={{ scale: 0.88 }}
        transition={{
          y:     { duration: 0.22, ease: EASE_DEPTH },
          scale: { type: 'spring', stiffness: 480, damping: 14, mass: 0.7 },
        }}
        className={
          'relative mx-auto w-[56px] h-[56px] rounded-[16px] flex items-center justify-center ' +
          'transition-[background-color,color] duration-300 ease-smooth ' +
          'focus-visible:outline-none ' +
          (active
            ? 'bg-white text-black ' +
              'shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_8px_-2px_rgba(0,0,0,0.35)]'
            : danger
              ? 'bg-transparent text-text-secondary hover:text-status-error'
              : 'bg-transparent text-text-secondary hover:text-text-primary')
        }
      >
        <span className="relative shrink-0 flex items-center justify-center w-[22px] h-[22px]">
          {icon}
        </span>
        {badgeCount != null && badgeCount > 0 && (
          <motion.span
            key={badgeCount}
            initial={{ scale: 0.6, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ type: 'spring', stiffness: 480, damping: 20 }}
            className="absolute -top-0.5 -right-0.5 min-w-[18px] h-[18px] px-1 rounded-full
                       bg-accent text-text-on-accent text-[10px] font-bold tabular-nums leading-none
                       flex items-center justify-center border border-bg-base
                       shadow-[0_0_10px_var(--accent)]"
          >
            {badgeCount}
          </motion.span>
        )}
      </motion.button>
    );
  }

  return (
    <motion.button
      type="button"
      onClick={onClick}
      whileHover={!active ? { y: -1 } : undefined}
      whileTap={{ scale: 0.97 }}
      transition={{ duration: 0.15, ease: EASE_DEPTH }}
      className={
        'mx-2 h-11 rounded-2xl flex items-center text-sm font-medium ' +
        'transition-[background-color,color,box-shadow,border-color] duration-300 ease-depth ' +
        'border justify-start px-3 gap-3 text-left ' +
        (active
          ? 'bg-white text-black border-white ' +
            'shadow-pill-active'
          : danger
            ? 'border-transparent text-text-secondary hover:text-status-error hover:bg-white/[0.06] ' +
              'hover:border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]'
            : 'border-transparent text-text-secondary hover:text-text-primary hover:bg-white/[0.06] ' +
              'hover:border-white/[0.10]')
      }
    >
      <span className="shrink-0 flex items-center justify-center w-5 h-5">{icon}</span>
      <span className="truncate flex-1">{label}</span>
      {trailing}
    </motion.button>
  );
}

function SatelliteTile({
  item, label, active, onClick,
}: {
  item: NavItem;
  label: string;
  active: boolean;
  onClick: () => void;
}) {
  const Icon = item.icon;
  return (
    <motion.button
      type="button"
      onClick={onClick}
      title={label}
      aria-label={label}
      whileHover={{ y: -2 }}
      whileTap={{ scale: 0.94 }}
      transition={{ duration: 0.18, ease: EASE_DEPTH }}
      className={
        'relative w-9 h-9 rounded-xl flex items-center justify-center ' +
        'transition-[background-color,color] duration-300 ease-smooth ' +
        'focus-visible:outline-none ' +
        (active
          ? 'bg-white text-black shadow-[inset_0_1px_0_rgba(255,255,255,0.85),0_2px_6px_-2px_rgba(0,0,0,0.35)]'
          : 'bg-transparent text-text-secondary hover:text-text-primary')
      }
    >
      <Icon size={15} strokeWidth={1.7} className="relative" />
    </motion.button>
  );
}

function DownloadActivityDot({ count }: { count: number }) {
  return (
    <span className="inline-flex items-center gap-1.5 ml-auto">
      <motion.span
        aria-hidden="true"
        animate={{ scale: [1, 1.25, 1], opacity: [0.85, 1, 0.85] }}
        transition={{ duration: 1.4, repeat: Infinity, ease: 'easeInOut' }}
        className="block w-2 h-2 rounded-full bg-accent shadow-[0_0_10px_var(--accent)]"
      />
      {count > 1 && (
        <span className="text-[10px] font-bold tabular-nums text-accent">
          {count}
        </span>
      )}
    </span>
  );
}
