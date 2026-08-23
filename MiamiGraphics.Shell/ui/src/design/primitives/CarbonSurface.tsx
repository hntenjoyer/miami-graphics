import { useMemo } from 'react';

export interface CarbonSurfaceProps {
  weaveOpacity?: number;
  glowIntensity?: number;
  vignetteIntensity?: number;
  baseColor?: string;
}

export function CarbonSurface({
  weaveOpacity = 0.28,
  glowIntensity = 0.55,
  vignetteIntensity = 0.4,
  baseColor,
}: CarbonSurfaceProps) {
  const weaveBg = useMemo(() => {
    const svg = `
<svg xmlns='http://www.w3.org/2000/svg' width='8' height='8' viewBox='0 0 8 8'>
  <rect width='4' height='4' x='0' y='0' fill='#2a2f38'/>
  <rect width='4' height='4' x='4' y='4' fill='#2a2f38'/>
  <rect width='4' height='4' x='4' y='0' fill='#1a1f28'/>
  <rect width='4' height='4' x='0' y='4' fill='#1a1f28'/>
  <line x1='0' y1='3.7' x2='4' y2='3.7' stroke='#0e1218' stroke-width='0.4'/>
  <line x1='4' y1='7.7' x2='8' y2='7.7' stroke='#0e1218' stroke-width='0.4'/>
  <line x1='3.7' y1='0' x2='3.7' y2='4' stroke='#3a4250' stroke-width='0.4' opacity='0.55'/>
  <line x1='7.7' y1='4' x2='7.7' y2='8' stroke='#3a4250' stroke-width='0.4' opacity='0.55'/>
</svg>`.trim();
    return `url("data:image/svg+xml;utf8,${encodeURIComponent(svg)}")`;
  }, []);

  const defaultBase =
    'radial-gradient(ellipse at 50% 0%, #2a2e36 0%, #1a1d24 40%, #11141b 100%)';

  return (
    <>
      <div
        aria-hidden
        className="absolute inset-0 pointer-events-none"
        style={{ background: baseColor ?? defaultBase }}
      />
      <div
        aria-hidden
        className="absolute inset-0 pointer-events-none"
        style={{
          backgroundImage: weaveBg,
          backgroundRepeat: 'repeat',
          backgroundSize: '12px 12px',
          opacity: weaveOpacity,
          mixBlendMode: 'overlay',
        }}
      />
      {}
      <div
        aria-hidden
        className="absolute inset-0 pointer-events-none"
        style={{
          background:
            `radial-gradient(ellipse at 50% 0%, ` +
            `rgba(255,255,255,${0.18 * glowIntensity}) 0%, ` +
            `transparent 55%)`,
        }}
      />
      {}
      <div
        aria-hidden
        className="absolute inset-0 pointer-events-none"
        style={{
          background:
            `radial-gradient(ellipse at 50% 60%, ` +
            `rgba(255,255,255,${0.14 * glowIntensity}) 0%, ` +
            `transparent 60%)`,
        }}
      />
      {}
      <div
        aria-hidden
        className="absolute inset-0 pointer-events-none"
        style={{
          background:
            `radial-gradient(ellipse at 50% 50%, transparent 45%, ` +
            `rgba(0,0,0,${vignetteIntensity}) 100%)`,
        }}
      />
      {}
      <div
        aria-hidden
        className="absolute inset-x-0 top-0 h-px pointer-events-none"
        style={{
          background:
            'linear-gradient(90deg, transparent, rgba(255,255,255,0.32), transparent)',
        }}
      />
    </>
  );
}
