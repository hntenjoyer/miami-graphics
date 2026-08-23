import { useState, useEffect, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Star, MessageSquare, LogIn, Pencil, X, CheckCircle2, Trash2, Loader2,
} from 'lucide-react';
import { useSessionStore } from '@/store/sessionStore';
import { useReduxStore } from '@/store/reduxStore';
import { bridge } from '@/bridge';
import type { ReduxReview } from '@/bridge/types';
import { GlassPanel, EASE_DEPTH } from '@/design';
import { ConfirmModal } from '@/components/ConfirmModal';

interface Props {
  reduxId: string;
}

export function ReviewsSection({ reduxId }: Props) {
  const { t } = useTranslation();
  const auth = useSessionStore(s => s.auth);

  const userId   = auth?.token?.startsWith('local-') ? auth.token.slice('local-'.length) : null;
  const canPost  = !!auth && auth.role !== 'Guest' && !!userId;
  const username = auth?.username ?? '';
  const role     = auth?.role ?? 'Guest';

  const profile  = useSessionStore(s => s.profile);
  const myAvatar = profile?.avatarUrl ?? null;

  const [reviews, setReviews]       = useState<ReduxReview[]>([]);
  const [loading, setLoading]       = useState(true);
  const [error, setError]           = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [editing, setEditing]       = useState(false);

  const [pendingDelete, setPendingDelete] = useState<ReduxReview | null>(null);

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setError(null);
    setEditing(false);
    (async () => {
      try {
        const list = await bridge.reduxReviewsList(reduxId);
        if (!cancelled) setReviews(list);
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : t('reviews.loadFailed'));
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [reduxId, t]);

  const stats = useMemo(() => {
    const total = reviews.length;
    const avg = total === 0 ? 0 : reviews.reduce((s, r) => s + r.rating, 0) / total;
    const distribution: Record<1 | 2 | 3 | 4 | 5, number> = { 1: 0, 2: 0, 3: 0, 4: 0, 5: 0 };
    for (const r of reviews) {
      const k = Math.max(1, Math.min(5, Math.round(r.rating))) as 1 | 2 | 3 | 4 | 5;
      distribution[k]++;
    }
    return { total, avg, distribution };
  }, [reviews]);

  const ownReview = useMemo(
    () => userId ? reviews.find(r => r.userId === userId) ?? null : null,
    [reviews, userId],
  );

  const isAdmin = role === 'Moderator' || role === 'AdminL1' || role === 'AdminL2';
  const canDelete = (review: ReduxReview): boolean => {
    if (isAdmin) return true;
    return !!userId && review.userId === userId;
  };

  const deleteReview = async (reviewId: string) => {
    if (!userId) return;
    setError(null);
    try {
      const ok = await bridge.reduxReviewDelete(reviewId, userId, role);
      if (!ok) {
        setError(t('reviews.deleteFailed'));
        return;
      }
      setReviews(prev => prev.filter(r => r.id !== reviewId));
      void refreshRatings();
    } catch (e) {
      setError(e instanceof Error ? e.message : t('reviews.deleteFailed'));
    }
  };

  const refreshRatings = useReduxStore(s => s.loadRatings);

  const submit = async (rating: number, body: string) => {
    if (!canPost || !userId) return;
    setSubmitting(true);
    setError(null);
    try {
      const fresh = await bridge.reduxReviewSubmit(reduxId, userId, username, role, myAvatar, rating, body);
      setReviews(prev => [fresh, ...prev.filter(r => r.userId !== fresh.userId)]);
      setEditing(false);

      void refreshRatings();
    } catch (e) {
      setError(e instanceof Error ? e.message : t('reviews.submitFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const writeArea =
    !canPost
      ? <SignInPrompt />
      : ownReview && !editing
        ? <OwnReviewSuccess onEdit={() => setEditing(true)} />
        : <ReviewForm
            initial={ownReview}
            isEdit={!!ownReview}
            busy={submitting}
            onSubmit={submit}
            onCancel={ownReview ? () => setEditing(false) : undefined}
          />;

  return (
    <div className="flex flex-col gap-5">
      {}
      <header className="flex items-center gap-4">
        <div className="w-14 h-14 rounded-2xl bg-glass-strong border border-glass-border
                        flex items-center justify-center shadow-z1">
          <MessageSquare size={26} className="text-accent" />
        </div>
        <div>
          <h2 className="text-2xl font-bold tracking-wide text-text-primary uppercase leading-none">
            {t('reviews.title')}
          </h2>
          <p className="text-[10px] uppercase tracking-[0.28em] text-text-muted mt-1.5">
            {t('reviews.subtitle')}
          </p>
        </div>
      </header>

      {}
      <GlassPanel depth="z2" tint="ultra" highlight rounded="3xl" className="p-7">
        <div className="grid grid-cols-1 md:grid-cols-[auto_1fr] gap-8 items-center">
          {}
          <div className="text-center md:text-left md:pr-8 md:border-r md:border-glass-border">
            <div className="text-6xl font-bold text-text-primary tabular-nums leading-none">
              {stats.total === 0 ? '-' : stats.avg.toFixed(1)}
            </div>
            <div className="flex items-center justify-center md:justify-start gap-1 mt-3">
              {[1, 2, 3, 4, 5].map(n => (
                <Star
                  key={n}
                  size={20}
                  className={
                    n <= Math.round(stats.avg)
                      ? 'text-yellow-400 fill-current'
                      : 'text-text-muted/30'
                  }
                />
              ))}
            </div>
            <div className="text-[10px] uppercase tracking-[0.2em] text-text-muted mt-3">
              {t('reviews.totalCount', { count: stats.total })}
            </div>
          </div>

          {}
          <div className="flex flex-col gap-2.5">
            {([5, 4, 3, 2, 1] as const).map(rating => {
              const count = stats.distribution[rating];
              const pct   = stats.total === 0 ? 0 : (count / stats.total) * 100;
              return (
                <div key={rating} className="flex items-center gap-3 text-sm">
                  <span className="w-3 text-right text-text-muted tabular-nums">{rating}</span>
                  <Star size={12} className="text-text-muted/60 fill-current" />
                  <div className="flex-1 h-2 rounded-full bg-glass-strong border border-glass-border overflow-hidden">
                    <motion.div
                      initial={false}
                      animate={{ width: `${pct}%` }}
                      transition={{ duration: 0.5, ease: EASE_DEPTH }}
                      className="h-full bg-yellow-400 rounded-full"
                    />
                  </div>
                  <span className="w-6 text-right text-text-muted tabular-nums">{count}</span>
                </div>
              );
            })}
          </div>
        </div>
      </GlassPanel>

      {}
      {error && (
        <div className="px-3.5 py-2.5 rounded-xl
                        bg-[color-mix(in_srgb,var(--status-error)_10%,transparent)]
                        border border-[color-mix(in_srgb,var(--status-error)_30%,transparent)]
                        text-sm text-text-primary">
          {error}
        </div>
      )}

      {}
      <GlassPanel depth="z2" tint="ultra" highlight rounded="3xl" className="p-7">
        {writeArea}
      </GlassPanel>

      {}
      {loading ? (
        <GlassPanel depth="z1" tint="ultra" highlight rounded="3xl" className="p-7">
          <p className="text-sm text-text-muted text-center">{t('reviews.loading')}</p>
        </GlassPanel>
      ) : reviews.length === 0 ? (
        <GlassPanel depth="z1" tint="ultra" highlight rounded="3xl" className="p-7">
          <p className="text-sm text-text-muted text-center">{t('reviews.empty')}</p>
        </GlassPanel>
      ) : (
        <div className="flex flex-col gap-3">
          <AnimatePresence initial={false}>
            {reviews.map(r => (
              <motion.div
                key={r.id}
                layout

                initial={{ opacity: 0, y: -18, scale: 0.94 }}
                animate={{ opacity: 1, y: 0,   scale: 1 }}
                exit   ={{ opacity: 0, x: 12, height: 0, marginTop: 0, scale: 0.96 }}
                transition={{
                  layout:  { duration: 0.32, ease: EASE_DEPTH },
                  opacity: { duration: 0.32, ease: EASE_DEPTH },
                  y:       { type: 'spring', stiffness: 360, damping: 24, mass: 0.7 },
                  scale:   { type: 'spring', stiffness: 360, damping: 24, mass: 0.7 },
                  height:  { duration: 0.28, ease: EASE_DEPTH },
                  x:       { duration: 0.22, ease: EASE_DEPTH },
                }}
              >
                <ReviewItem
                  review={r}
                  isOwn={r.userId === userId}
                  canDelete={canDelete(r)}
                  onDelete={() => setPendingDelete(r)}
                />
              </motion.div>
            ))}
          </AnimatePresence>
        </div>
      )}

      {}
      <ConfirmModal
        open={pendingDelete !== null}
        title={t('reviews.deleteConfirmTitle')}
        message={
          pendingDelete
            ? `${pendingDelete.username}\n\n${t('reviews.deleteConfirmBody')}`
            : ''
        }
        confirmLabel={t('reviews.delete')}
        cancelLabel={t('reviews.cancel')}
        destructive
        onConfirm={() => {
          if (pendingDelete) void deleteReview(pendingDelete.id);
          setPendingDelete(null);
        }}
        onCancel={() => setPendingDelete(null)}
      />
    </div>
  );
}

function OwnReviewSuccess({ onEdit }: { onEdit: () => void }) {
  const { t } = useTranslation();
  return (
    <motion.div
      initial={{ opacity: 0, y: -4 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.3, ease: EASE_DEPTH }}
      className="flex flex-col items-center justify-center py-8 text-center"
    >
      <div className="w-14 h-14 rounded-2xl
                      bg-[color-mix(in_srgb,var(--status-success)_18%,transparent)]
                      border border-[color-mix(in_srgb,var(--status-success)_40%,transparent)]
                      flex items-center justify-center mb-4">
        <CheckCircle2 size={28} style={{ color: 'var(--status-success)' }} />
      </div>
      <h3 className="font-bold text-text-primary text-lg mb-1">{t('reviews.posted')}</h3>
      <p className="text-sm text-text-muted mb-5">{t('reviews.thanks')}</p>
      <button
        type="button"
        onClick={onEdit}
        className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl
                   bg-glass-strong border border-glass-border
                   text-sm text-text-primary
                   hover:bg-glass transition-colors duration-200"
      >
        <Pencil size={13} />
        {t('reviews.edit')}
      </button>
    </motion.div>
  );
}

interface FormProps {
  initial:   ReduxReview | null;
  isEdit:    boolean;
  busy:      boolean;
  onSubmit:  (rating: number, body: string) => void;
  onCancel?: () => void;
}

function ReviewForm({ initial, isEdit, busy, onSubmit, onCancel }: FormProps) {
  const { t } = useTranslation();
  const [rating, setRating] = useState(initial?.rating ?? 0);
  const [hover,  setHover]  = useState<number | null>(null);
  const [body,   setBody]   = useState(initial?.body ?? '');

  useEffect(() => {
    setRating(initial?.rating ?? 0);
    setBody(initial?.body ?? '');
  }, [initial?.id, initial?.rating, initial?.body]);

  const canSubmit = !busy && rating > 0 && body.trim().length >= 4;

  const handle = (e: React.FormEvent) => {
    e.preventDefault();
    if (!canSubmit) return;
    onSubmit(rating, body);
  };

  return (
    <form onSubmit={handle} className="flex flex-col gap-3">
      <div className="flex items-center gap-2">
        <span className="text-sm text-text-secondary mr-1">
          {isEdit ? t('reviews.yourRatingEdit') : t('reviews.yourRating')}:
        </span>
        {[1, 2, 3, 4, 5].map(n => {
          const filled = (hover ?? rating) >= n;
          return (
            <button
              key={n}
              type="button"
              onMouseEnter={() => setHover(n)}
              onMouseLeave={() => setHover(null)}
              onClick={() => setRating(n)}
              className="p-1 rounded-md transition-colors"
              aria-label={`${n} star${n > 1 ? 's' : ''}`}
            >
              <Star
                size={22}
                className={filled ? 'text-yellow-400 fill-current' : 'text-text-muted/40'}
              />
            </button>
          );
        })}
      </div>

      <textarea
        value={body}
        onChange={e => setBody(e.target.value)}
        placeholder={t('reviews.placeholder')}
        rows={3}
        className="w-full px-4 py-3 resize-none
                   bg-glass-strong backdrop-blur-glass
                   border border-glass-border rounded-2xl
                   text-text-primary placeholder:text-text-muted
                   outline-none transition-all duration-200 ease-depth
                   focus:border-accent focus:shadow-[0_0_0_4px_var(--accent-soft)]"
      />
      {body.trim().length > 0 && body.trim().length < 4 && (
        <span className="text-[11px] text-text-muted -mt-1">
          {t('reviews.tooShort', 'Минимум 4 символа')}
        </span>
      )}

      <div className="flex justify-end gap-2">
        {onCancel && (
          <button
            type="button"
            onClick={onCancel}
            className="inline-flex items-center gap-1.5 px-4 py-2 rounded-xl
                       text-sm text-text-secondary hover:text-text-primary transition-colors"
          >
            <X size={14} />
            {t('reviews.cancel')}
          </button>
        )}
        <button
          type="submit"
          disabled={!canSubmit || busy}
          style={{ outline: 'none' }}
          className="inline-flex items-center justify-center gap-2 h-11 px-5 rounded-xl
                     bg-bg-elevated/55 text-text-primary border border-white/[0.08]
                     hover:bg-bg-elevated/75 hover:border-white/[0.18]
                     transition-colors text-sm font-bold uppercase tracking-wider
                     disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {busy && <Loader2 size={14} className="animate-spin" />}
          <span>{isEdit ? t('reviews.update') : t('reviews.submit')}</span>
        </button>
      </div>
    </form>
  );
}

function SignInPrompt() {
  const { t } = useTranslation();
  return (
    <div className="flex items-center gap-3 px-4 py-3 rounded-2xl
                    bg-glass-strong border border-glass-border">
      <LogIn size={16} className="text-text-muted shrink-0" />
      <span className="text-sm text-text-secondary">
        {t('reviews.guestPrompt')}
      </span>
    </div>
  );
}

interface ItemProps {
  review: ReduxReview;
  isOwn:  boolean;
  canDelete?: boolean;
  onDelete?:  () => void;
}

function ReviewItem({ review, canDelete, onDelete }: ItemProps) {
  const { t } = useTranslation();

  const onTrash = () => onDelete?.();
  const initial = (review.username || 'u').charAt(0).toUpperCase();
  const date = new Date(review.createdAt);
  const dateLabel = date.toLocaleDateString('ru-RU', {
    day: '2-digit', month: '2-digit', year: 'numeric',
  });

  return (
    <GlassPanel depth="z2" tint="ultra" highlight rounded="3xl" className="p-5">
      <div className="flex gap-4">
        {}
        {review.avatarUrl ? (
          <img
            src={review.avatarUrl}
            alt={review.username}
            className="shrink-0 w-11 h-11 rounded-full object-cover
                       border border-glass-border shadow-z1 bg-glass-strong"
            onError={(e) => {

              const el = e.currentTarget;
              const fb = document.createElement('div');
              fb.className = 'shrink-0 w-11 h-11 rounded-full ' +
                'bg-glass-strong border border-glass-border ' +
                'flex items-center justify-center font-bold text-sm text-text-primary shadow-z1';
              fb.textContent = initial;
              el.replaceWith(fb);
            }}
          />
        ) : (
          <div className="shrink-0 w-11 h-11 rounded-full
                          bg-glass-strong border border-glass-border
                          flex items-center justify-center font-bold text-sm text-text-primary
                          shadow-z1">
            {initial}
          </div>
        )}

        <div className="min-w-0 flex-1">
          {}
          <div className="flex items-center gap-3 flex-wrap">
            <span className="text-sm font-bold text-text-primary truncate tracking-tight">
              {review.username}
            </span>
            <span className="text-[11px] text-text-muted tabular-nums ml-auto">
              {dateLabel}
            </span>
            {canDelete && (
              <button
                type="button"
                onClick={onTrash}
                title={t('reviews.delete')}
                aria-label={t('reviews.delete')}
                style={{ outline: 'none' }}
                className="inline-flex items-center justify-center w-7 h-7 rounded-md border
                           bg-white/[0.04] border-white/[0.06] text-text-muted
                           hover:bg-status-error/15 hover:text-status-error hover:border-status-error/40
                           transition-[background-color,color,border-color] duration-200 ease-smooth"
              >
                <Trash2 size={12} strokeWidth={2} />
              </button>
            )}
          </div>

          {}
          <div className="inline-flex items-center gap-0.5 mt-1.5">
            {[1, 2, 3, 4, 5].map(n => (
              <Star
                key={n}
                size={13}
                className={n <= review.rating ? 'text-yellow-400 fill-current' : 'text-text-muted/30'}
              />
            ))}
          </div>

          {}
          <p className="text-sm text-text-secondary leading-relaxed mt-2.5 whitespace-pre-line break-words">
            {review.body}
          </p>
        </div>
      </div>
    </GlassPanel>
  );
}
