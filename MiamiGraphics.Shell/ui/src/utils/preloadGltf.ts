type DreiUseGltf = { preload: (url: string) => void };

let cached: ((url: string) => void) | null = null;
let inFlight: Promise<void> | null = null;

export function preloadGltf(url: string): void {
  if (!url) return;
  if (cached) {
    try { cached(url); } catch {  }
    return;
  }
  if (inFlight) {

    inFlight.then(() => { try { cached?.(url); } catch {  } });
    return;
  }
  inFlight = import('@react-three/drei')
    .then((mod) => {
      const useGltf = (mod as unknown as { useGLTF: DreiUseGltf }).useGLTF;
      cached = useGltf.preload.bind(useGltf);
      try { cached(url); } catch {  }
    })
    .catch(() => {  })
    .finally(() => { inFlight = null; });
}
