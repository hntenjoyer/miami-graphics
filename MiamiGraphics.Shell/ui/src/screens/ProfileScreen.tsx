import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import {
  User as UserIcon, Mail, Calendar, Shield, Crown, Pencil, X, Check,
  Download, Upload, LogIn, Star, Hash,
  type LucideIcon,
} from 'lucide-react';
import { useSessionStore } from '@/store/sessionStore';
import { useAppScreenStore } from '@/store/appScreenStore';
import { useReduxStore } from '@/store/reduxStore';
import { useGunpackStore } from '@/store/gunpackStore';
import { bridge } from '@/bridge';
import type { UserProfile, AccountStats } from '@/bridge/types';
import { GlassPanel, Glow3DButton, AccentLoader } from '@/design';
import { SOCIAL_LINKS } from '@/data/socials';
import { DiscordIcon, TelegramIcon } from '@/components/icons/BrandIcons';
import { AccountSecuritySection } from '@/components/settings/AccountSecuritySection';

const profileContainer: Variants = {
  hidden: { opacity: 1 },
  visible: { opacity: 1, transition: { delayChildren: 0.05, staggerChildren: 0.07 } },
};
const profileItem: Variants = {
  hidden: { opacity: 0, y: 14 },
  visible: { opacity: 1, y: 0, transition: { duration: 0.45, ease: [0.22, 1, 0.36, 1] } },
};

function IslandCard({ className = '', children }: { className?: string; children: React.ReactNode }) {
  return (
    <GlassPanel
      depth="z3" tint="ultra" rounded="3xl" highlight edge
      className={'relative overflow-hidden border border-white/[0.08] ' + className}
    >
      <span
        aria-hidden
        className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                   bg-gradient-to-r from-transparent via-white/40 to-transparent"
      />
      <span
        aria-hidden
        className="absolute -top-24 -right-16 w-56 h-56 pointer-events-none blur-3xl"
        style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 18%, transparent) 0%, transparent 70%)' }}
      />
      {children}
    </GlassPanel>
  );
}

interface Props {
  onOpenMod?: (modId: string) => void;
  onOpenGunpack?: (packId: string) => void;
}

export function ProfileScreen(_props: Props) {
  const { t, i18n } = useTranslation();
  const auth = useSessionStore(s => s.auth);
  const logout = useSessionStore(s => s.logout);
  const userId = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;

  const [profile, setProfile] = useState<UserProfile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError]     = useState<string | null>(null);

  const [editing, setEditing]     = useState(false);
  const [usernameDraft, setUsernameDraft] = useState('');
  const [avatarDraft, setAvatarDraft]     = useState('');
  const [saving, setSaving]   = useState(false);
  const [saveErr, setSaveErr] = useState<string | null>(null);
  const [uploadingAvatar, setUploadingAvatar] = useState(false);

  useEffect(() => {
    if (!userId) { setLoading(false); return; }
    let cancelled = false;
    setLoading(true);
    setError(null);
    (async () => {
      const t0 = Date.now();
      try {
        const timeout = new Promise<never>((_, rej) =>
          setTimeout(() => rej(new Error(t('profile.loadTimeout', 'Сервер долго не отвечает. Проверь интернет и попробуй ещё раз.'))), 10_000));
        const p = await Promise.race([bridge.getUserProfile(userId), timeout]);
        console.log('[profile] getUserProfile ok in', Date.now() - t0, 'ms ->', p);
        if (!cancelled) {
          setProfile(p);
          if (!p) setError(t('profile.notFound', 'Профиль не найден.'));
        }
      } catch (e) {
        console.warn('[profile] getUserProfile FAIL in', Date.now() - t0, 'ms', e);
        if (!cancelled) setError(e instanceof Error ? e.message : t('profile.loadFailed', 'Не удалось загрузить профиль.'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [userId, t]);

  const [stats, setStats] = useState<AccountStats | null>(null);
  useEffect(() => {
    if (!userId) { setStats(null); return; }
    let alive = true;
    bridge.accountStats()
      .then(s => { if (alive) setStats(s); })
      .catch(() => {  });
    return () => { alive = false; };
  }, [userId]);

  const reduxFavCount   = useReduxStore(s => s.favorites.size);
  const gunpackFavCount = useGunpackStore(s => s.gunpackFavorites.size);
  const [itemFavCount, setItemFavCount] = useState(0);
  useEffect(() => {
    if (!userId) { setItemFavCount(0); return; }
    let alive = true;
    Promise.all(
      (['minimap', 'reticle', 'sounds'] as const).map(t =>
        bridge.itemFavoritesList(userId, t).catch(() => [] as string[])),
    ).then(lists => {
      if (alive) setItemFavCount(lists.reduce((n, l) => n + l.length, 0));
    });
    return () => { alive = false; };
  }, [userId]);
  const favCount = reduxFavCount + gunpackFavCount + itemFavCount;

  const beginEdit = () => {
    if (!profile) return;
    setUsernameDraft(profile.username);
    setAvatarDraft(profile.avatarUrl ?? '');
    setSaveErr(null);
    setEditing(true);
  };

  const pickAndUploadAvatar = async () => {
    if (!userId || uploadingAvatar) return;
    setSaveErr(null);
    try {
      const path = await bridge.openFileDialog(t('userBuilds.fileDialogImage', 'Изображение'), '*.png;*.jpg;*.jpeg;*.webp;*.gif');
      if (!path) return;
      setUploadingAvatar(true);
      const url = await bridge.uploadAvatar(userId, path);
      setAvatarDraft(url);
    } catch (e) {
      setSaveErr(e instanceof Error ? e.message : t('profile.avatarUploadFailed'));
    } finally {
      setUploadingAvatar(false);
    }
  };

  const saveEdit = async () => {
    if (!userId || !profile) return;
    if (!/^[A-Za-z0-9_]{3,32}$/.test(usernameDraft)) {
      setSaveErr(t('profile.usernameInvalid'));
      return;
    }
    setSaving(true);
    setSaveErr(null);
    try {
      const updated = await bridge.updateUserProfile(
        userId,
        usernameDraft,
        avatarDraft.trim() || null,
      );
      setProfile(updated);
      setEditing(false);

      useSessionStore.setState((prev) => ({
        auth:    prev.auth ? { ...prev.auth, username: updated.username } : prev.auth,
        profile: updated,
      }));
    } catch (e) {
      setSaveErr(e instanceof Error ? e.message : t('profile.saveFailed'));
    } finally {
      setSaving(false);
    }
  };

  if (!userId) {

    return (
      <motion.div
        className="h-full flex items-center justify-center px-8"
        initial={{ opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.42, ease: [0.22, 1, 0.36, 1] }}
      >
        <GlassPanel
          depth="z3" tint="ultra" rounded="3xl" highlight edge
          className="relative w-[420px] max-w-[92vw] overflow-hidden border border-white/[0.08] px-9 py-10"
        >
          <span
            aria-hidden="true"
            className="absolute top-0 inset-x-0 h-px pointer-events-none
                       bg-gradient-to-r from-transparent via-white/45 to-transparent"
          />
          <span
            aria-hidden="true"
            className="absolute -top-24 -right-16 w-64 h-64 pointer-events-none blur-3xl"
            style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 22%, transparent) 0%, transparent 70%)' }}
          />

          <div className="relative flex flex-col items-center text-center">
            <div className="relative mb-5 w-14 h-14">
              <motion.span
                aria-hidden="true"
                className="absolute inset-0 rounded-2xl blur-md"
                style={{ background: 'radial-gradient(circle, var(--accent) 0%, transparent 70%)' }}
                initial={{ opacity: 0.3, scale: 0.85 }}
                animate={{ opacity: [0.3, 0.55, 0.3], scale: [0.85, 1.1, 0.85] }}
                transition={{ duration: 2.8, ease: 'easeInOut', repeat: Infinity }}
              />
              <div className="relative w-14 h-14 rounded-2xl bg-accent-soft border border-white/[0.08]
                              flex items-center justify-center">
                <UserIcon size={26} className="text-accent" strokeWidth={2} />
              </div>
            </div>

            <h1 className="text-[22px] font-bold text-text-primary tracking-tight">
              {t('sidebar.profile', 'Профиль')}
            </h1>
            <p className="mt-2 text-[13.5px] leading-relaxed text-text-secondary max-w-[300px]">
              {t('profile.guestPrompt')}
            </p>

            <button
              type="button"
              onClick={() => {
                logout();
                useAppScreenStore.getState().request('welcome');
              }}
              style={{ outline: 'none' }}
              className="mt-6 w-full inline-flex items-center justify-center gap-2 h-12 px-4 rounded-xl
                         bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                         hover:bg-bg-elevated/75 hover:border-white/[0.18]
                         transition-colors text-sm font-bold uppercase tracking-wider"
            >
              <LogIn size={16} strokeWidth={2.5} />
              <span>{t('sidebar.login', 'Войти')}</span>
            </button>
          </div>
        </GlassPanel>
      </motion.div>
    );
  }

  return (
    <div className="h-full overflow-y-auto">
      <AnimatePresence mode="wait" initial={false}>
        {loading ? (
          <motion.div
            key="profile-loader"
            className="h-full flex items-center justify-center"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.18, ease: [0.22, 1, 0.36, 1] }}
          >
            <div className="flex flex-col items-center gap-3 text-text-muted">
              <AccentLoader size={36} />
              <span className="text-xs uppercase tracking-[0.22em]">
                {t('profile.loading')}
              </span>
            </div>
          </motion.div>
        ) : (
          <motion.div
            key="profile-content"
            className="max-w-6xl mx-auto px-8 py-6 flex flex-col gap-4"
            variants={profileContainer}
            initial="hidden"
            animate="visible"
            exit={{ opacity: 0 }}
          >

        {}
        <motion.div variants={profileItem}>
        <IslandCard className="p-6">
          {loading ? (
            <p className="text-sm text-text-muted">{t('profile.loading')}</p>
          ) : error ? (
            <p className="text-sm text-status-error">{error}</p>
          ) : !profile ? (
            <p className="text-sm text-text-muted">{t('profile.notFound')}</p>
          ) : (
            <ProfileHero profile={profile} onEdit={beginEdit} />
          )}
        </IslandCard>
        </motion.div>

        {}
        {profile && (
          <motion.div
            variants={profileItem}
            initial={{ opacity: 0, y: 14 }}
            animate={{ opacity: 1, y: 0 }}
            exit={{ opacity: 0, y: -8 }}
            transition={{ duration: 0.35, ease: [0.22, 1, 0.36, 1] }}
            layout
          >
            <IslandCard className="p-5">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-x-6 gap-y-5">
                <div className="flex flex-col gap-3.5">
                  <h2 className="text-xs uppercase tracking-[0.2em] text-text-muted">
                    {t('profile.infoTitle')}
                  </h2>
                  <div className="grid grid-cols-1 gap-2.5">
                    <InfoRow icon={Mail}     label={t('profile.email')}     value={maskEmail(profile.email)} mono />
                    <InfoRow icon={Calendar} label={t('profile.createdAt')} value={formatDate(profile.createdAt, i18n.language)} />
                    <InfoRow icon={iconForRole(profile.role)} label={t('profile.role')} value={profile.role} accent />
                  </div>
                </div>

                <div className="flex flex-col gap-3.5">
                  <h2 className="text-xs uppercase tracking-[0.2em] text-text-muted">
                    {t('profile.statsTitle', 'Статистика')}
                  </h2>
                  <div className="grid grid-cols-3 gap-2.5 flex-1 content-stretch">
                    <StatTile
                      icon={Download}
                      label={t('profile.statDownloads', 'Загрузки')}
                      value={stats ? formatStat(stats.downloads) : '-'}
                    />
                    <StatTile
                      icon={Star}
                      label={t('profile.statFavorites', 'Избранное')}
                      value={formatStat(favCount)}
                    />
                    <StatTile
                      icon={Hash}
                      label={t('profile.statAccount', 'Аккаунт')}
                      value={stats && stats.accountNo > 0 ? `#${stats.accountNo}` : '-'}
                    />
                  </div>
                </div>
              </div>
            </IslandCard>
          </motion.div>
        )}

        {}
        {profile && (
          <motion.div variants={profileItem}>
            <AccountSecuritySection />
          </motion.div>
        )}

        {}
        {profile && (
          <motion.div
            className="grid grid-cols-1 sm:grid-cols-2 gap-4"
            variants={profileItem}
          >
            {SOCIAL_LINKS.map(link => (
              <button
                key={link.id}
                type="button"
                onClick={() => window.open(link.url, '_blank', 'noopener,noreferrer')}
                className="group relative h-24 rounded-3xl overflow-hidden text-left
                           bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid
                           border border-white/[0.08] shadow-z3
                           focus-visible:outline-none focus-visible:border-white/[0.3]"
              >
                <span
                  aria-hidden
                  className="absolute top-0 inset-x-0 h-px pointer-events-none z-10
                             bg-gradient-to-r from-transparent via-white/40 to-transparent"
                />
                <span
                  aria-hidden
                  className="absolute -top-20 -right-12 w-48 h-48 pointer-events-none blur-3xl"
                  style={{ background: 'radial-gradient(circle, color-mix(in srgb, var(--accent) 16%, transparent) 0%, transparent 70%)' }}
                />
                <span
                  aria-hidden
                  className="absolute inset-0 pointer-events-none opacity-70"
                  style={{
                    background:
                      'radial-gradient(ellipse at 0% 0%, rgba(255,255,255,0.08), transparent 60%)',
                  }}
                />
                <div className="relative h-full flex items-center gap-4 px-6">
                  <div className="w-14 h-14 rounded-2xl shrink-0 flex items-center justify-center
                                  bg-white/[0.06] border border-white/[0.06]
                                  text-text-primary">
                    {link.iconKind === 'discord'
                      ? <DiscordIcon className="w-7 h-7" />
                      : <TelegramIcon className="w-7 h-7" />}
                  </div>
                  <div className="min-w-0 flex-1">
                    <div className="text-[10px] font-bold uppercase tracking-[0.22em] text-text-muted mb-0.5">
                      {link.iconKind === 'discord' ? 'Discord' : 'Telegram'}
                    </div>
                    <div className="text-base font-semibold text-text-primary truncate">
                      {t(link.labelKey)}
                    </div>
                  </div>
                  <span
                    aria-hidden
                    className="shrink-0 w-9 h-9 rounded-full flex items-center justify-center
                               bg-white/[0.04] text-text-secondary"
                  >
                    <Upload size={14} className="rotate-45" />
                  </span>
                </div>
              </button>
            ))}
          </motion.div>
        )}

        {}
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {editing && profile && (
          <ProfileEditModal onClose={() => { if (!saving && !uploadingAvatar) setEditing(false); }}>
            <ProfileEditForm
              profile={profile}
              usernameDraft={usernameDraft}
              avatarDraft={avatarDraft}
              saving={saving}
              uploadingAvatar={uploadingAvatar}
              error={saveErr}
              onChangeUsername={setUsernameDraft}
              onChangeAvatar={setAvatarDraft}
              onPickAvatar={pickAndUploadAvatar}
              onCancel={() => setEditing(false)}
              onSave={saveEdit}
            />
          </ProfileEditModal>
        )}
      </AnimatePresence>
    </div>
  );
}

function ProfileEditModal({ children, onClose }: { children: React.ReactNode; onClose: () => void }) {
  const { t } = useTranslation();
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);
  return (
    <motion.div
      key="profile-edit-modal"
      className="fixed inset-0 z-[100] flex items-center justify-center p-6"
      initial={{ opacity: 0, pointerEvents: 'none' as const }}
      animate={{ opacity: 1, pointerEvents: 'auto' as const }}
      exit   ={{ opacity: 0, pointerEvents: 'none' as const }}
      transition={{ duration: 0.24, ease: [0.22, 1, 0.36, 1] }}
      onClick={onClose}
      style={{
        background:
          'radial-gradient(ellipse at center, rgba(20,20,28,0.78) 0%, rgba(0,0,0,0.86) 75%)',
        backdropFilter: 'blur(14px) saturate(140%)',
        WebkitBackdropFilter: 'blur(14px) saturate(140%)',
      }}
    >
      <motion.div
        initial={{ opacity: 0, scale: 0.94, y: 14 }}
        animate={{ opacity: 1, scale: 1,    y: 0 }}
        exit   ={{ opacity: 0, scale: 0.96, y: 8 }}
        transition={{ duration: 0.34, ease: [0.22, 1, 0.36, 1] }}
        onClick={(e) => e.stopPropagation()}
        className="w-full max-w-[760px]"
        style={{
          filter:
            'drop-shadow(0 26px 60px rgba(255,255,255,0.10)) drop-shadow(0 6px 18px rgba(0,0,0,0.62))',
        }}
      >
        <div
          className="relative overflow-hidden rounded-3xl p-8 flex flex-col gap-6
                     border border-white/[0.08]"
          style={{
            background:
              'linear-gradient(155deg, rgba(40,40,52,0.75), rgba(28,28,38,0.85))',
            boxShadow:
              'inset 0 1px 0 rgba(255,255,255,0.10), ' +
              '0 24px 56px rgba(0,0,0,0.55)',
            backdropFilter: 'blur(22px) saturate(140%)',
            WebkitBackdropFilter: 'blur(22px) saturate(140%)',
          }}
        >
          <span
            aria-hidden
            className="absolute top-0 inset-x-0 h-px pointer-events-none
                       bg-gradient-to-r from-transparent via-white/40 to-transparent"
          />
          <span
            aria-hidden
            className="pointer-events-none absolute -top-24 -left-24 w-72 h-72 rounded-full opacity-30 blur-3xl"
            style={{ background: 'radial-gradient(circle, rgba(255,255,255,0.5), transparent 70%)' }}
          />

          <button
            type="button"
            onClick={onClose}
            aria-label={t('common.close', 'Закрыть')}
            className="absolute top-4 right-4 z-10 w-9 h-9 rounded-xl flex items-center justify-center
                       bg-white/[0.04] border border-white/[0.06] text-text-secondary
                       hover:bg-white/[0.10] hover:text-text-primary hover:border-white/[0.18]
                       transition-colors"
          >
            <X size={14} />
          </button>

          <header className="flex flex-col gap-1.5">
            <span className="text-[11px] uppercase tracking-[0.28em] text-text-muted font-semibold">
              {t('sidebar.profile', 'Профиль')}
            </span>
            <h2 className="font-display text-2xl font-bold uppercase tracking-wide text-text-primary leading-tight">
              {t('profile.editButton', 'Редактировать')}
            </h2>
          </header>

          {children}
        </div>
      </motion.div>
    </motion.div>
  );
}

function maskEmail(email: string | null | undefined): string {
  if (!email) return '-';
  const at = email.indexOf('@');
  if (at < 1) return '••••@•••';
  const head = email[0];
  const tail = email.slice(at);
  return head + '••••••' + tail;
}

function ProfileHero({ profile, onEdit }: { profile: UserProfile; onEdit: () => void }) {
  const { t } = useTranslation();
  const initial = (profile.username || 'u').charAt(0).toUpperCase();
  return (
    <div className="flex items-center gap-5 flex-wrap">
      <Avatar src={profile.avatarUrl} initial={initial} size={88} />
      <div className="min-w-0 flex-1">
        <h1 className="font-display text-3xl font-bold text-text-primary tracking-tight truncate">
          {profile.username}
        </h1>
        <div className="mt-2 inline-flex items-center gap-2">
          <RoleBadge role={profile.role} />
        </div>
      </div>
      <Glow3DButton variant="secondary" leading={<Pencil size={14} />} onClick={onEdit}>
        {t('profile.editButton')}
      </Glow3DButton>
    </div>
  );
}

interface EditFormProps {
  profile:         UserProfile;
  usernameDraft:   string;
  avatarDraft:     string;
  saving:          boolean;
  uploadingAvatar: boolean;
  error:           string | null;
  onChangeUsername: (v: string) => void;
  onChangeAvatar:   (v: string) => void;
  onPickAvatar:     () => void;
  onCancel:       () => void;
  onSave:         () => void;
}

function ProfileEditForm({
  profile, usernameDraft, avatarDraft, saving, uploadingAvatar, error,
  onChangeUsername, onChangeAvatar, onPickAvatar, onCancel, onSave,
}: EditFormProps) {
  const { t } = useTranslation();
  const initial = (usernameDraft || profile.username || 'u').charAt(0).toUpperCase();
  return (
    <div className="flex flex-col gap-5">
      <div className="flex items-center gap-5 flex-wrap">
        <div className="relative shrink-0">
          <Avatar src={avatarDraft || profile.avatarUrl} initial={initial} size={88} />
          {uploadingAvatar && (
            <div className="absolute inset-0 rounded-full flex items-center justify-center
                            bg-black/55 backdrop-blur-sm">
              <AccentLoader size={28} color="#ffffff" />
            </div>
          )}
        </div>
        <div className="flex-1 min-w-[260px]">
          <Field label={t('profile.usernameLabel')}>
            <input
              value={usernameDraft}
              onChange={e => onChangeUsername(e.target.value)}
              className={inputCls}
              placeholder="username"
              autoComplete="off"
            />
          </Field>
        </div>
      </div>

      {}
      <Field label={t('profile.avatarLabel')} hint={t('profile.avatarHint')}>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={onPickAvatar}
            disabled={uploadingAvatar}
            className="inline-flex items-center gap-2 px-5 h-11 rounded-2xl
                       bg-glass-strong border border-glass-border
                       text-sm font-medium text-text-primary
                       hover:bg-glass transition-colors duration-200 ease-depth
                       disabled:opacity-60 disabled:cursor-not-allowed"
            title={t('profile.avatarUploadHint')}
          >
            {uploadingAvatar
              ? <AccentLoader size={16} color="currentColor" />
              : <Upload size={14} />
            }
            {avatarDraft ? t('profile.avatarReplace') : t('profile.avatarUpload')}
          </button>

          {avatarDraft && !uploadingAvatar && (
            <button
              type="button"
              onClick={() => onChangeAvatar('')}
              className="inline-flex items-center gap-1.5 px-3 h-11 rounded-2xl
                         text-sm text-text-muted hover:text-status-error
                         transition-colors duration-200"
              title={t('profile.avatarClearHint')}
            >
              <X size={14} />
              {t('profile.avatarClear')}
            </button>
          )}
        </div>
      </Field>

      {error && (
        <div className="px-3.5 py-2.5 rounded-xl
                        bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                        border border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]
                        text-sm text-text-primary">
          {error}
        </div>
      )}

      <div className="flex justify-end gap-2">
        <button
          type="button"
          onClick={onCancel}
          className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl
                     text-sm text-text-secondary hover:text-text-primary transition-colors"
        >
          <X size={14} />
          {t('profile.cancel')}
        </button>
        <button
          type="button"
          onClick={onSave}
          disabled={saving}
          style={{ outline: 'none' }}
          className="inline-flex items-center justify-center gap-2 h-10 px-5 rounded-xl
                     text-[12px] font-bold uppercase tracking-[0.08em] transition-colors
                     disabled:opacity-55 disabled:cursor-not-allowed
                     bg-bg-elevated/55 text-text-primary
                     border border-white/[0.08]
                     hover:bg-bg-elevated/75 hover:border-white/[0.18]"
        >
          {saving
            ? <AccentLoader size={13} color="currentColor" />
            : <Check size={13} strokeWidth={2.4} />}
          <span>{t('profile.save')}</span>
        </button>
      </div>
    </div>
  );
}

const inputCls =
  'w-full px-4 py-3 ' +
  'bg-glass-strong backdrop-blur-glass ' +
  'border border-glass-border rounded-2xl ' +
  'text-text-primary placeholder:text-text-muted ' +
  'outline-none transition-all duration-200 ease-depth ' +
  'focus:border-accent focus:shadow-[0_0_0_4px_var(--accent-soft)]';

function Avatar({ src, initial, size = 64 }: { src: string | null | undefined; initial: string; size?: number }) {
  const dim = { width: size, height: size, fontSize: size * 0.4 } as const;
  const [broken, setBroken] = useState(false);
  useEffect(() => { setBroken(false); }, [src]);
  if (src && !broken) {
    return (
      <img
        src={src}
        alt={initial}
        className="rounded-full object-cover shrink-0 shadow-z2 border border-glass-border bg-glass-strong"
        style={dim}
        onError={() => setBroken(true)}
      />
    );
  }
  return (
    <div
      className="rounded-full shrink-0 flex items-center justify-center
                 bg-glass-strong border border-glass-border text-text-primary font-bold shadow-z2"
      style={dim}
    >
      {initial}
    </div>
  );
}

function Field({ label, hint, children }: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs uppercase tracking-wider text-text-muted">{label}</span>
      {children}
      {hint && <span className="text-xs text-text-muted">{hint}</span>}
    </label>
  );
}

function InfoRow({
  icon: Icon, label, value, mono, accent,
}: {
  icon: LucideIcon;
  label: string;
  value: string;
  mono?: boolean;
  accent?: boolean;
}) {
  return (
    <div className="flex items-center gap-3 px-4 py-3 rounded-2xl bg-glass border border-glass-border">
      <div className="shrink-0 w-9 h-9 rounded-xl flex items-center justify-center bg-glass-strong text-text-secondary">
        <Icon size={16} />
      </div>
      <div className="min-w-0 flex-1">
        <div className="text-[10px] uppercase tracking-wider text-text-muted">{label}</div>
        <div className={
          'truncate ' +
          (mono ? 'font-mono text-xs ' : 'text-sm ') +
          (accent ? 'text-accent font-bold' : 'text-text-primary font-medium')
        }>
          {value}
        </div>
      </div>
    </div>
  );
}

function StatTile({ icon: Icon, label, value }: { icon: LucideIcon; label: string; value: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-1.5 text-center
                    px-2 py-3.5 rounded-2xl bg-glass border border-glass-border">
      <div className="w-8 h-8 rounded-xl flex items-center justify-center bg-glass-strong text-text-secondary">
        <Icon size={15} />
      </div>
      <div className="text-lg font-bold text-text-primary tabular-nums leading-none truncate max-w-full">
        {value}
      </div>
      <div className="text-[10px] uppercase tracking-wider text-text-muted">{label}</div>
    </div>
  );
}

function formatStat(n: number): string {
  if (n >= 1_000_000) return (n / 1_000_000).toFixed(1) + 'M';
  if (n >= 10_000)    return (n / 1_000).toFixed(1) + 'k';
  return String(n);
}

function RoleBadge({ role }: { role: string }) {
  const { t } = useTranslation();
  const isAdmin = role === 'AdminL1' || role === 'AdminL2';
  const isMod   = role === 'Moderator';
  if (isAdmin) {
    return (
      <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md
                       text-xs font-bold tracking-wider uppercase
                       bg-accent text-text-on-accent">
        <Crown size={12} />
        {role === 'AdminL2' ? t('profile.roleAdminL2', 'Admin L2') : t('profile.roleAdminL1', 'Admin L1')}
      </span>
    );
  }
  if (isMod) {
    return (
      <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md
                       text-xs font-bold tracking-wider uppercase
                       bg-status-info text-white">
        <Shield size={12} />
        {t('profile.roleModerator', 'Moderator')}
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-md
                     text-xs font-bold tracking-wider uppercase
                     bg-glass-strong text-text-secondary border border-glass-border">
      <UserIcon size={12} />
      {role || t('profile.roleUser', 'User')}
    </span>
  );
}

function iconForRole(role: string) {
  if (role === 'AdminL1' || role === 'AdminL2') return Crown;
  if (role === 'Moderator') return Shield;
  return UserIcon;
}

function formatDate(iso: string, locale: string): string {
  try {
    return new Date(iso).toLocaleDateString(locale, {
      day: '2-digit', month: 'long', year: 'numeric',
    });
  } catch {
    return iso;
  }
}
