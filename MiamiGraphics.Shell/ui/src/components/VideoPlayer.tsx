import { useEffect, useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion, AnimatePresence } from 'framer-motion';
import {
  Volume2, VolumeX, X, Play, Pause, Maximize2, Minimize2,
} from 'lucide-react';
import { EASE_DEPTH } from '@/design';

export function VideoPlayer({
  url, poster, title, onClose,
}: {
  url: string;
  poster?: string;
  title?: string;
  onClose?: () => void;
}) {
  const { t } = useTranslation();
  const videoRef     = useRef<HTMLVideoElement | null>(null);
  const containerRef = useRef<HTMLDivElement   | null>(null);
  const scrubId = `hg-video-scrub-${useId().replace(/:/g, '')}`;
  const [muted, setMuted]           = useState(false);
  const [playing, setPlaying]       = useState(true);
  const [currentTime, setCurrentTime] = useState(0);
  const [duration, setDuration]     = useState(0);
  const [volume, setVolume]         = useState(1);
  const [bufferedEnd, setBufferedEnd] = useState(0);
  const [showControls, setShowControls] = useState(true);
  const [scrubbing, setScrubbing]   = useState(false);
  const [scrubFrac, setScrubFrac]   = useState<number | null>(null);
  const [fullscreen, setFullscreen] = useState(false);
  const hideTimerRef = useRef<number | null>(null);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && onClose) {
        if (!document.fullscreenElement) onClose();
      }
      if (e.key === ' ' && containerRef.current?.contains(document.activeElement as Node | null)) {
        e.preventDefault();
        togglePlay();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);

  }, [onClose]);

  useEffect(() => {
    if (!videoRef.current) return;
    videoRef.current.muted = false;
    videoRef.current.volume = 1;
    void videoRef.current.play().catch(() => {
      if (videoRef.current) {
        videoRef.current.muted = true;
        setMuted(true);
        void videoRef.current.play().catch(() => {  });
      }
    });
  }, [url]);

  useEffect(() => { if (videoRef.current) videoRef.current.muted = muted; }, [muted]);
  useEffect(() => { if (videoRef.current) videoRef.current.volume = volume; }, [volume]);

  const armHideTimer = () => {
    if (hideTimerRef.current !== null) window.clearTimeout(hideTimerRef.current);
    hideTimerRef.current = window.setTimeout(() => {
      if (videoRef.current && !videoRef.current.paused && !scrubbing) {
        setShowControls(false);
      }
    }, 2500);
  };
  const onPointerMove = () => { setShowControls(true); armHideTimer(); };
  useEffect(() => {
    armHideTimer();
    return () => { if (hideTimerRef.current !== null) window.clearTimeout(hideTimerRef.current); };

  }, [playing, scrubbing]);

  useEffect(() => {
    const onFsChange = () => setFullscreen(!!document.fullscreenElement);
    document.addEventListener('fullscreenchange', onFsChange);
    return () => document.removeEventListener('fullscreenchange', onFsChange);
  }, []);

  const togglePlay = () => {
    const v = videoRef.current;
    if (!v) return;
    if (v.paused) void v.play().catch(() => {  });
    else v.pause();
  };
  const toggleFullscreen = () => {
    if (!containerRef.current) return;
    if (document.fullscreenElement) void document.exitFullscreen().catch(() => {});
    else void containerRef.current.requestFullscreen().catch(() => {});
  };
  const seekToFraction = (frac: number) => {
    const v = videoRef.current;
    if (!v || !isFinite(duration) || duration <= 0) return;
    v.currentTime = Math.max(0, Math.min(1, frac)) * duration;
  };
  const formatTime = (s: number) => {
    if (!isFinite(s) || s < 0) return '0:00';
    const m  = Math.floor(s / 60);
    const ss = Math.floor(s % 60).toString().padStart(2, '0');
    return `${m}:${ss}`;
  };

  useEffect(() => {
    if (!scrubbing) return;
    const onMove = (e: PointerEvent) => {
      const bar = document.getElementById(scrubId);
      if (!bar) return;
      const rect = bar.getBoundingClientRect();
      const frac = (e.clientX - rect.left) / Math.max(1, rect.width);
      setScrubFrac(Math.max(0, Math.min(1, frac)));
    };
    const onUp = () => {
      if (scrubFrac !== null) seekToFraction(scrubFrac);
      setScrubbing(false);
      setScrubFrac(null);
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup',   onUp);
    return () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup',   onUp);
    };

  }, [scrubbing, scrubFrac, duration, scrubId]);

  const liveFrac = duration > 0
    ? (scrubFrac !== null ? scrubFrac : currentTime / duration)
    : 0;
  const bufferedFrac = duration > 0 ? Math.min(1, bufferedEnd / duration) : 0;

  return (
    <div
      ref={containerRef}
      className="absolute inset-0 bg-black focus:outline-none"
      onPointerMove={onPointerMove}
      tabIndex={-1}
    >
      <video
        ref={videoRef}
        src={url}
        poster={poster}
        autoPlay
        loop
        playsInline
        onClick={togglePlay}
        onPlay={() => setPlaying(true)}
        onPause={() => setPlaying(false)}
        onTimeUpdate={() => {
          const v = videoRef.current;
          if (!v) return;
          if (!scrubbing) setCurrentTime(v.currentTime);
          try {
            if (v.buffered.length > 0) {
              setBufferedEnd(v.buffered.end(v.buffered.length - 1));
            }
          } catch {  }
        }}
        onLoadedMetadata={() => {
          const v = videoRef.current;
          if (v) setDuration(isFinite(v.duration) ? v.duration : 0);
        }}
        onVolumeChange={() => {
          const v = videoRef.current;
          if (!v) return;
          setMuted(v.muted);
          setVolume(v.volume);
        }}
        className="absolute inset-0 w-full h-full object-contain bg-black cursor-pointer"
      />

      <AnimatePresence>
        {!playing && (
          <motion.div
            key="paused-pulse"
            initial={{ opacity: 0, scale: 0.7 }}
            animate={{ opacity: 1, scale: 1 }}
            exit   ={{ opacity: 0, scale: 1.2 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            className="pointer-events-none absolute inset-0 z-10 flex items-center justify-center"
          >
            <span
              className="w-20 h-20 rounded-full flex items-center justify-center
                         bg-black/60 backdrop-blur-md text-white border border-white/[0.20]
                         shadow-[0_12px_32px_rgba(0,0,0,0.55)]"
            >
              <Play size={28} strokeWidth={2.4} className="ml-1" />
            </span>
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {showControls && (title || onClose) && (
          <motion.div
            key="top-bar"
            initial={{ opacity: 0, y: -6 }}
            animate={{ opacity: 1, y: 0 }}
            exit   ={{ opacity: 0, y: -4 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            className="absolute top-0 inset-x-0 z-30 px-4 py-3 flex items-center gap-2
                       bg-gradient-to-b from-black/70 via-black/30 to-transparent"
            onClick={(e) => e.stopPropagation()}
          >
            <h2 className="text-sm font-semibold text-white truncate flex-1
                           drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">
              {title}
            </h2>
            {onClose && (
              <button
                type="button"
                onClick={onClose}
                aria-label={t('videoModal.closeEsc', 'Закрыть (Esc)')}
                title={t('videoModal.closeEsc', 'Закрыть (Esc)')}
                className="w-9 h-9 rounded-lg flex items-center justify-center
                           text-white/85 bg-black/45 hover:bg-black/70 hover:text-white
                           border border-white/[0.12] transition-colors"
              >
                <X size={16} />
              </button>
            )}
          </motion.div>
        )}
      </AnimatePresence>

      <AnimatePresence>
        {showControls && (
          <motion.div
            key="bottom-bar"
            initial={{ opacity: 0, y: 12 }}
            animate={{ opacity: 1, y: 0 }}
            exit   ={{ opacity: 0, y: 8 }}
            transition={{ duration: 0.22, ease: EASE_DEPTH }}
            className="absolute bottom-0 inset-x-0 z-30 px-4 pb-3 pt-12
                       bg-gradient-to-t from-black/85 via-black/50 to-transparent"
            onClick={(e) => e.stopPropagation()}
          >
            <div
              id={scrubId}
              onPointerDown={(e) => {
                (e.currentTarget as HTMLDivElement).setPointerCapture?.(e.pointerId);
                const rect = (e.currentTarget as HTMLDivElement).getBoundingClientRect();
                const frac = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
                setScrubFrac(frac);
                setScrubbing(true);
              }}
              className="group/scrub relative h-2 rounded-full bg-white/[0.16] cursor-pointer
                         transition-[height] duration-150 hover:h-2.5 mb-2.5"
            >
              <div
                className="absolute top-0 bottom-0 left-0 rounded-full bg-white/30 pointer-events-none"
                style={{ width: `${bufferedFrac * 100}%` }}
              />
              <div
                className="absolute top-0 bottom-0 left-0 rounded-full bg-white pointer-events-none"
                style={{ width: `${liveFrac * 100}%` }}
              />
              <span
                className="absolute top-1/2 -translate-y-1/2 -translate-x-1/2
                           w-3 h-3 rounded-full bg-white pointer-events-none
                           shadow-[0_0_0_4px_rgba(255,255,255,0.18),0_2px_8px_rgba(0,0,0,0.55)]
                           opacity-0 group-hover/scrub:opacity-100
                           transition-opacity duration-150"
                style={{ left: `${liveFrac * 100}%`, opacity: scrubbing ? 1 : undefined }}
              />
            </div>

            <div className="flex items-center gap-3">
              <ControlButton
                onClick={togglePlay}
                ariaLabel={playing
                  ? t('videoModal.pause', 'Пауза')
                  : t('videoModal.play', 'Воспроизвести')}
                title={playing
                  ? t('videoModal.pauseSpace', 'Пауза (Space)')
                  : t('videoModal.playSpace', 'Воспроизвести (Space)')}
              >
                {playing
                  ? <Pause size={16} strokeWidth={2.4} />
                  : <Play  size={16} strokeWidth={2.4} className="ml-0.5" />}
              </ControlButton>

              <span className="text-[11px] text-white/85 font-mono tabular-nums select-none">
                {formatTime(scrubbing && scrubFrac !== null ? scrubFrac * duration : currentTime)}
                <span className="text-white/40 mx-1">/</span>
                {formatTime(duration)}
              </span>

              <div className="flex-1" />

              <VolumePill
                muted={muted}
                volume={volume}
                onToggleMute={() => {
                  if (muted || volume === 0) {
                    setMuted(false);
                    if (volume === 0) setVolume(1);
                  } else {
                    setMuted(true);
                  }
                }}
                onVolume={(v) => {
                  setVolume(v);
                  if (v > 0 && muted) setMuted(false);
                }}
              />

              <ControlButton
                onClick={toggleFullscreen}
                ariaLabel={fullscreen
                  ? t('videoModal.exitFullscreen', 'Выйти из полноэкранного')
                  : t('videoModal.fullscreen', 'Полноэкранный')}
                title={fullscreen
                  ? t('videoModal.exitFullscreenKey', 'Выйти (F)')
                  : t('videoModal.fullscreenKey', 'Полный экран (F)')}
              >
                {fullscreen
                  ? <Minimize2 size={16} strokeWidth={2.4} />
                  : <Maximize2 size={16} strokeWidth={2.4} />}
              </ControlButton>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

function ControlButton({
  children, onClick, ariaLabel, title,
}: {
  children: React.ReactNode;
  onClick: () => void;
  ariaLabel: string;
  title?: string;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={ariaLabel}
      title={title ?? ariaLabel}
      className="w-9 h-9 rounded-lg flex items-center justify-center
                 text-white/90 bg-white/[0.08] hover:bg-white/[0.18] hover:text-white
                 border border-white/[0.10] hover:border-white/[0.22]
                 transition-[background-color,border-color,color] duration-150 ease-depth"
    >
      {children}
    </button>
  );
}

function VolumePill({
  muted, volume, onToggleMute, onVolume,
}: {
  muted:   boolean;
  volume:  number;
  onToggleMute: () => void;
  onVolume:     (v: number) => void;
}) {
  const { t } = useTranslation();
  const effective = muted ? 0 : volume;
  return (
    <div
      className="group/vol flex items-center gap-1 rounded-lg
                 bg-white/[0.06] border border-white/[0.10]
                 hover:bg-white/[0.10] hover:border-white/[0.18]
                 transition-[background-color,border-color] duration-150"
    >
      <button
        type="button"
        onClick={onToggleMute}
        aria-label={muted || volume === 0
          ? t('videoModal.unmute', 'Включить звук')
          : t('videoModal.mute', 'Выключить звук')}
        title={muted || volume === 0
          ? t('videoModal.unmuteKey', 'Звук вкл (M)')
          : t('videoModal.muteKey', 'Звук выкл (M)')}
        className="w-9 h-9 rounded-lg flex items-center justify-center text-white/90
                   hover:text-white transition-colors"
      >
        {muted || volume === 0
          ? <VolumeX size={16} strokeWidth={2.4} />
          : <Volume2 size={16} strokeWidth={2.4} />}
      </button>
      <input
        type="range"
        min={0}
        max={1}
        step={0.01}
        value={effective}
        onChange={(e) => onVolume(parseFloat(e.target.value))}
        className="hg-volume-slider h-1 w-0 group-hover/vol:w-20 group-focus-within/vol:w-20
                   accent-white opacity-0 group-hover/vol:opacity-100 group-focus-within/vol:opacity-100
                   transition-[width,opacity] duration-200 ease-out
                   cursor-pointer mr-2 outline-none"
        aria-label={t('videoModal.volume', 'Громкость')}
      />
    </div>
  );
}
