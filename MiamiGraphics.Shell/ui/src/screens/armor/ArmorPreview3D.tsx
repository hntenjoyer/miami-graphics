import { Suspense, lazy, useEffect, useRef, useState } from 'react';
import { CarbonSurface } from '@/design';

interface Props {
  glbUrl: string | null;
  withBackground?: boolean;
}

const READY = new Set<string>();
const SUBS = new Set<() => void>();
function notifyReady() { for (const s of SUBS) s(); }

function useInView(rootMargin = '200px'): [React.RefObject<HTMLDivElement>, boolean] {
  const ref = useRef<HTMLDivElement>(null);
  const [inView, setInView] = useState(false);
  useEffect(() => {
    if (inView) return;
    const el = ref.current;
    if (!el) return;
    const io = new IntersectionObserver(
      ([entry]) => { if (entry.isIntersecting) setInView(true); },
      { rootMargin });
    io.observe(el);
    return () => io.disconnect();
  }, [inView, rootMargin]);
  return [ref, inView];
}

const PreviewCanvas = lazy(() =>
  import('./ArmorPreview3DCanvas').then(m => ({ default: m.PreviewCanvas }))
);

export function ArmorPreview3D({ glbUrl, withBackground = true }: Props) {
  const [ref, inView] = useInView('200px');

  const placeholder = (
    <div ref={ref} className="relative w-full h-full overflow-hidden">
      {withBackground && (
        <CarbonSurface weaveOpacity={0.22} glowIntensity={0.7} vignetteIntensity={0.35} />
      )}
    </div>
  );

  if (!glbUrl || !inView) return placeholder;
  return (
    <div ref={ref} className="relative w-full h-full overflow-hidden">
      <PreparedPreview url={glbUrl} withBackground={withBackground} />
    </div>
  );
}

function PreparedPreview({ url, withBackground }: { url: string; withBackground: boolean }) {
  const [ready, setReady] = useState(() => READY.has(url));

  useEffect(() => {
    if (READY.has(url)) {
      setReady(true);
      return;
    }
    let cancelled = false;
    const finish = () => {
      if (cancelled) return;
      READY.add(url);
      setReady(true);
      notifyReady();
    };
    fetch(url, { cache: 'force-cache' })
      .then(() => requestAnimationFrame(finish))
      .catch(finish);

    const sub = () => { if (READY.has(url)) setReady(true); };
    SUBS.add(sub);
    return () => {
      cancelled = true;
      SUBS.delete(sub);
    };
  }, [url]);

  if (!ready) {
    return (
      <div className="relative w-full h-full overflow-hidden" aria-hidden>
        {withBackground && (
          <CarbonSurface weaveOpacity={0.22} glowIntensity={0.7} vignetteIntensity={0.35} />
        )}
      </div>
    );
  }

  const fallback = (
    <div className="relative w-full h-full overflow-hidden" aria-hidden>
      {withBackground && (
        <CarbonSurface weaveOpacity={0.22} glowIntensity={0.7} vignetteIntensity={0.35} />
      )}
    </div>
  );

  return (
    <Suspense fallback={fallback}>
      <PreviewCanvas url={url} withBackground={withBackground} />
    </Suspense>
  );
}
