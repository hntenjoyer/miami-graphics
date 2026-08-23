import { useEffect, useState } from 'react';
import { motion } from 'framer-motion';
import { useTranslation } from 'react-i18next';
import type { AuthResult, UserProfile } from '@/bridge/types';
import { EASE_DEPTH } from '@/design';

interface WelcomeOverlayProps {
  auth:    AuthResult;
  profile: UserProfile | null;
  onDone:  () => void;
}

export function WelcomeOverlay({ auth, profile, onDone }: WelcomeOverlayProps) {
  const { t } = useTranslation();

  const REVEAL_MS = 2400;
  const HOLD_MS   = 1700;

  const [stage, setStage] = useState<'enter' | 'exit'>('enter');

  useEffect(() => {
    const t = window.setTimeout(() => setStage('exit'), REVEAL_MS + HOLD_MS);
    return () => window.clearTimeout(t);
  }, []);

  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.code === 'Space' || e.key === ' ') && stage === 'enter') {
        e.preventDefault();
        setStage('exit');
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [stage]);

  const username  = profile?.username ?? auth.username ?? 'user';
  const avatarUrl = profile?.avatarUrl ?? null;
  const initial   = username.charAt(0).toUpperCase();

  const helloWords = t('welcomeOverlay.hello', 'Добро пожаловать').split(' ');

  return (
    <motion.div
      initial={{ opacity: 0 }}
      animate={{ opacity: stage === 'enter' ? 1 : 0 }}

      transition={{ duration: stage === 'enter' ? 0.75 : 0.7, ease: EASE_DEPTH }}
      onAnimationComplete={() => { if (stage === 'exit') onDone(); }}
      className="fixed inset-0 z-[100] flex items-center justify-center
                 bg-black/65 backdrop-blur-xl pointer-events-none"
    >
      {}
      <motion.div
        aria-hidden
        initial={{ opacity: 0, scale: 0.6 }}
        animate={stage === 'enter' ? { opacity: 1, scale: 1 } : { opacity: 0, scale: 1.1 }}
        transition={{ duration: 0.9, ease: EASE_DEPTH, delay: stage === 'enter' ? 0.1 : 0 }}
        className="absolute w-[640px] h-[640px] rounded-full
                   bg-gradient-radial from-accent/35 via-accent/10 to-transparent blur-3xl"
        style={{
          background:
            'radial-gradient(circle, color-mix(in srgb, var(--accent) 35%, transparent) 0%, color-mix(in srgb, var(--accent) 10%, transparent) 30%, transparent 70%)',
        }}
      />

      <div className="relative flex flex-col items-center text-center">
        {}
        <motion.div
          initial={{ opacity: 0, scale: 0.88, y: 8 }}
          animate={
            stage === 'enter'
              ? { opacity: 1, scale: 1,    y: 0 }
              : { opacity: 0, scale: 1.02, y: -4 }
          }
          transition={{
            duration: stage === 'enter' ? 0.9 : 0.5,
            ease: EASE_DEPTH,
          }}
          className="relative"
        >
          {}
          <motion.div
            aria-hidden
            initial={{ opacity: 0 }}
            animate={{ opacity: [0, 0.32, 0.42, 0.28, 0.42] }}
            transition={{ duration: 3.6, repeat: Infinity, ease: 'easeInOut', delay: 0.5 }}
            className="absolute inset-[-24px] rounded-full bg-accent/30 blur-3xl"
          />

          {}
          <div className="relative w-36 h-36 rounded-full p-1
                          bg-gradient-to-br from-accent via-accent-hover to-accent/30
                          shadow-glow-accent">
            <div className="w-full h-full rounded-full overflow-hidden bg-glass-strong border border-glass-border
                            flex items-center justify-center">
              {avatarUrl ? (
                <img
                  src={avatarUrl}
                  alt={username}
                  draggable={false}
                  className="w-full h-full object-cover select-none"
                  onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
                />
              ) : (
                <span className="font-bold text-5xl text-text-primary">{initial}</span>
              )}
            </div>
          </div>
        </motion.div>

        {}
        <div className="mt-10 flex justify-center gap-x-3 flex-wrap">
          {helloWords.map((w, i) => (
            <motion.span
              key={`${w}-${i}`}

              initial={{ opacity: 0, y: 14, filter: 'blur(3px)' }}
              animate={
                stage === 'enter'
                  ? { opacity: 1, y: 0,  filter: 'blur(0px)' }
                  : { opacity: 0, y: -6, filter: 'blur(2px)' }
              }
              transition={{
                duration: 0.7,
                ease: EASE_DEPTH,
                delay: 0.55 + i * 0.18,
              }}
              className="font-display text-5xl font-bold text-text-primary tracking-tight"
            >
              {w}
            </motion.span>
          ))}
        </div>

        {}
        <motion.div
          initial={{ opacity: 0, y: 10 }}
          animate={
            stage === 'enter'
              ? { opacity: 1, y: 0 }
              : { opacity: 0, y: -4 }
          }
          transition={{ duration: 0.85, ease: EASE_DEPTH, delay: 1.15 }}
          className="mt-3 font-display text-2xl font-bold uppercase tracking-[0.32em]
                     text-accent"
        >
          {t('welcomeOverlay.toBrand', 'В MIAMI GRAPHICS')}
        </motion.div>

        {}
        <motion.div
          aria-hidden
          initial={{ scaleX: 0, opacity: 0 }}
          animate={
            stage === 'enter'
              ? { scaleX: 1, opacity: 0.55 }
              : { scaleX: 1, opacity: 0 }
          }
          transition={{ duration: 1.1, ease: EASE_DEPTH, delay: 1.35 }}
          style={{ transformOrigin: 'left center' }}
          className="mt-3 h-px w-48 bg-gradient-to-r from-transparent via-accent to-transparent"
        />

        {}
        <motion.p
          initial={{ opacity: 0, y: 6 }}
          animate={
            stage === 'enter'
              ? { opacity: 1, y: 0 }
              : { opacity: 0 }
          }
          transition={{ duration: 0.7, ease: EASE_DEPTH, delay: 1.55 }}
          className="mt-7 text-lg text-text-secondary"
        >
          {username}
        </motion.p>
      </div>
    </motion.div>
  );
}
