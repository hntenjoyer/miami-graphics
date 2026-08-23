import { lazy, Suspense, type ComponentProps } from 'react';

if (typeof window !== 'undefined') {
  const prefetch = () => {
    void import('./AmbientScene');
  };
  if ('requestIdleCallback' in window) {
    (window as Window & { requestIdleCallback: (cb: IdleRequestCallback) => number })
      .requestIdleCallback(prefetch, { timeout: 2500 });
  } else {
    setTimeout(prefetch, 1200);
  }
}

const Crystal3DInner      = lazy(() => import('./Crystal3D').then(m => ({ default: m.Crystal3D })));
const Loader3DInner       = lazy(() => import('./Loader3D').then(m => ({ default: m.Loader3D })));
const AmbientSceneInner   = lazy(() => import('./AmbientScene').then(m => ({ default: m.AmbientScene })));

function AmbientFallback() {
  return (
    <div
      aria-hidden
      style={{
        position: 'fixed', inset: 0, pointerEvents: 'none', zIndex: 0,
        background:
          'radial-gradient(circle at 50% 50%, color-mix(in srgb, var(--accent-soft) 80%, transparent), transparent 60%), '
          + 'radial-gradient(circle at 30% 80%, color-mix(in srgb, var(--accent) 12%, transparent), transparent 50%)',
      }}
    />
  );
}

export function LazyCrystal3D(props: ComponentProps<typeof Crystal3DInner>) {
  return (
    <Suspense fallback={null}>
      <Crystal3DInner {...props} />
    </Suspense>
  );
}

export function LazyLoader3D(props: ComponentProps<typeof Loader3DInner>) {
  return (
    <Suspense fallback={null}>
      <Loader3DInner {...props} />
    </Suspense>
  );
}

export function LazyAmbientScene(props: ComponentProps<typeof AmbientSceneInner>) {
  return (
    <Suspense fallback={<AmbientFallback />}>
      <AmbientSceneInner {...props} />
    </Suspense>
  );
}
