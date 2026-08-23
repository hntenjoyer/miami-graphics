import { useState } from 'react';
import { Play } from 'lucide-react';

export type VideoSlot =
  | { kind: 'embed'; url: string; provider: 'youtube' | 'vimeo'; id: string | null }
  | { kind: 'video'; url: string };

export function videoSlotForUrl(raw: string): VideoSlot {
  const embed = embedUrlForVideo(raw);
  return embed ?? { kind: 'video', url: raw };
}

export function embedUrlForVideo(raw: string): Extract<VideoSlot, { kind: 'embed' }> | null {
  let u: URL;
  try { u = new URL(raw); } catch { return null; }
  const host = u.hostname.toLowerCase();

  if (host === 'youtu.be') {
    const id = cleanYouTubeId(u.pathname.replace(/^\//, ''));
    return id ? youtubeSlot(id) : null;
  }

  if (host.endsWith('youtube.com') || host.endsWith('youtube-nocookie.com')) {
    const v = cleanYouTubeId(u.searchParams.get('v') ?? '');
    if (v) return youtubeSlot(v);
    const m = u.pathname.match(/^\/(?:shorts|embed|v)\/([^/?#]+)/);
    const id = cleanYouTubeId(m?.[1] ?? '');
    return id ? youtubeSlot(id) : null;
  }

  if (host === 'vimeo.com' || host === 'www.vimeo.com') {
    const id = u.pathname.split('/').filter(Boolean).pop() ?? '';
    if (/^\d+$/.test(id)) {
      return {
        kind: 'embed',
        provider: 'vimeo',
        id,
        url: `https://player.vimeo.com/video/${id}`,
      };
    }
  }

  return null;
}

export function LazyEmbedVideo({
  slot,
  title,
  className = 'w-full h-full',
  autoplay = true,
}: {
  slot: Extract<VideoSlot, { kind: 'embed' }>;
  title: string;
  className?: string;
  autoplay?: boolean;
}) {
  const [playing, setPlaying] = useState(false);
  const src = autoplay && playing ? withQuery(slot.url, { autoplay: '1' }) : slot.url;

  if (!playing && slot.provider === 'youtube' && slot.id) {
    return (
      <button
        type="button"
        onClick={() => setPlaying(true)}
        className={`${className} relative block overflow-hidden bg-black group`}
        title={title}
        aria-label={title}
      >
        <img
          src={`https://i.ytimg.com/vi/${slot.id}/hqdefault.jpg`}
          alt=""
          draggable={false}
          loading="lazy"
          className="absolute inset-0 w-full h-full object-cover transition-transform duration-700 group-hover:scale-105"
        />
        <span className="absolute inset-0 bg-gradient-to-t from-black/80 via-black/25 to-transparent" />
        <span className="absolute inset-0 flex items-center justify-center">
          <span className="w-16 h-16 rounded-full bg-white/12 backdrop-blur-md border border-white/20 flex items-center justify-center shadow-2xl transition-colors group-hover:bg-accent group-hover:border-accent">
            <Play size={22} className="text-white ml-1 fill-current group-hover:text-text-on-accent" />
          </span>
        </span>
      </button>
    );
  }

  return (
    <iframe
      src={src}
      title={title}
      className={className}
      frameBorder={0}
      loading="lazy"
      allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture; web-share"
      allowFullScreen
    />
  );
}

function youtubeSlot(id: string): Extract<VideoSlot, { kind: 'embed' }> {
  return {
    kind: 'embed',
    provider: 'youtube',
    id,
    url: `https://www.youtube-nocookie.com/embed/${encodeURIComponent(id)}?rel=0&modestbranding=1`,
  };
}

function cleanYouTubeId(id: string): string | null {
  const clean = id.trim().slice(0, 11);
  return /^[A-Za-z0-9_-]{11}$/.test(clean) ? clean : null;
}

function withQuery(raw: string, params: Record<string, string>): string {
  const u = new URL(raw);
  for (const [key, value] of Object.entries(params)) u.searchParams.set(key, value);
  return u.toString();
}
