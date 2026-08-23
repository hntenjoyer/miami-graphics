import { createPortal } from 'react-dom';
import { motion } from 'framer-motion';
import { EASE_DEPTH } from '@/design';
import { VideoPlayer } from '@/components/VideoPlayer';

export function VideoModal({
  url, title, poster, onClose,
}: {
  url: string;
  title: string;
  poster?: string;
  onClose: () => void;
}) {
  return createPortal((
    <motion.div
      key="hg-video-modal"
      className="fixed inset-0 z-[100] bg-black/80 backdrop-blur-glass-ultra
                 backdrop-saturate-liquid flex items-center justify-center p-6"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.24, ease: EASE_DEPTH }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-[min(92vw,1280px)] aspect-[16/9] max-h-[85vh] rounded-3xl overflow-hidden
                   bg-black shadow-[0_30px_60px_rgba(0,0,0,0.6)]"
        initial={{ opacity: 0, scale: 0.92, y: 16 }}
        animate={{ opacity: 1, scale: 1,    y: 0 }}
        exit   ={{ opacity: 0, scale: 0.96, y: 8 }}
        transition={{ duration: 0.34, ease: EASE_DEPTH }}
        onClick={(e) => e.stopPropagation()}
      >
        <VideoPlayer url={url} poster={poster} title={title} onClose={onClose} />
      </motion.div>
    </motion.div>
  ), document.body);
}
