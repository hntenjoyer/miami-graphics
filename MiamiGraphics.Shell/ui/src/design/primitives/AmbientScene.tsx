import { Canvas, useFrame, useThree } from '@react-three/fiber';
import { Float, Stars } from '@react-three/drei';
import { useEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import { ACCENT, R3F_DPR, CAMERA_AMBIENT } from '../tokens';
import type { Background } from '@/bridge/types';

interface AmbientSceneProps {
  tone?: 'default' | 'login' | 'home';
  background?: Background;
  className?: string;
}

export function AmbientScene({ tone = 'default', background = 'cubes', className }: AmbientSceneProps) {
  return (
    <div
      aria-hidden
      className={className}
      style={{
        position: 'fixed', inset: 0, pointerEvents: 'none', zIndex: 0,
      }}
    >
      <Canvas
        dpr={R3F_DPR}
        camera={CAMERA_AMBIENT}

        gl={{ alpha: true, antialias: false, powerPreference: 'default' }}

        frameloop="demand"
      >
        <FpsCapper fps={30} />
        <SceneContents tone={tone} background={background} />
      </Canvas>
    </div>
  );
}

function FpsCapper({ fps }: { fps: number }) {
  const invalidate = useThree(s => s.invalidate);
  useEffect(() => {
    const minGap = Math.max(8, Math.round(1000 / fps));
    let raf = 0, last = 0;
    const tick = (now: number) => {
      raf = requestAnimationFrame(tick);
      if (document.hidden) return;
      if (now - last < minGap) return;
      last = now;
      invalidate();
    };
    raf = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(raf);
  }, [fps, invalidate]);
  return null;
}

function SceneContents({ tone, background }: { tone: NonNullable<AmbientSceneProps['tone']>; background: Background }) {
  const { camera } = useThree();

  const target = useRef({ x: 0, y: 0 });

  useEffect(() => {
    camera.position.x = 0;
    camera.position.y = 0;
    camera.lookAt(0, 0, 0);
  }, [camera]);

  return (
    <>
      <ambientLight intensity={0.4} />
      {background === 'cubes'
        ? <FloatingCubes tone={tone} cursor={target} />
        : <LuxuryDust    tone={tone} cursor={target} />}
    </>
  );
}

type CursorRef = React.MutableRefObject<{ x: number; y: number }>;

interface DustParticle {
  x:  number; y:  number; z:  number;
  size:    number;
  opacity: number;
  speed:   number;
  amp:     number;
  drift:   number;
  phase:   number;
}

function buildDust(count: number): DustParticle[] {

  const out: DustParticle[] = [];
  for (let i = 0; i < count; i++) {

    const r = (n: number) => {
      const v = Math.sin((i + 1) * (n + 1) * 12.9898) * 43758.5453;
      return v - Math.floor(v);
    };
    out.push({
      x:       (r(1) - 0.5) * 14,
      y:       (r(2) - 0.5) * 9,
      z:       -1 - r(3) * 4,
      size:    0.05 + r(4) * 0.16,
      opacity: 0.22 + r(5) * 0.42,
      speed:   0.15 + r(6) * 0.35,
      amp:     0.20 + r(7) * 0.45,
      drift:   0.04 + r(8) * 0.06,
      phase:   r(9) * Math.PI * 2,
    });
  }
  return out;
}

function buildHaloOrbs(): DustParticle[] {

  return [
    { x: -3.8, y:  1.6, z: -3.2, size: 2.6, opacity: 0.18, speed: 0.10, amp: 0.6, drift: 0, phase: 0.0 },
    { x:  3.2, y:  1.9, z: -3.6, size: 3.0, opacity: 0.14, speed: 0.08, amp: 0.7, drift: 0, phase: 1.5 },
    { x:  0.0, y:  2.5, z: -4.5, size: 3.4, opacity: 0.10, speed: 0.06, amp: 0.5, drift: 0, phase: 2.7 },
    { x: -2.2, y: -1.8, z: -3.0, size: 2.2, opacity: 0.16, speed: 0.12, amp: 0.4, drift: 0, phase: 3.8 },
    { x:  2.6, y: -2.0, z: -3.4, size: 2.5, opacity: 0.13, speed: 0.09, amp: 0.5, drift: 0, phase: 4.9 },
    { x:  0.0, y: -2.6, z: -4.2, size: 3.0, opacity: 0.09, speed: 0.07, amp: 0.6, drift: 0, phase: 5.6 },
  ];
}

function LuxuryDust({ tone, cursor }: { tone: NonNullable<AmbientSceneProps['tone']>; cursor: CursorRef }) {
  const dustGroupRef = useRef<THREE.Group>(null);
  const haloGroupRef = useRef<THREE.Group>(null);
  const sprite = useMemo(() => createGlowTexture(), []);

  const count = tone === 'login' ? 36 : tone === 'home' ? 64 : 48;
  const dust  = useMemo(() => buildDust(count), [count]);
  const halos = useMemo(() => buildHaloOrbs(), []);

  useFrame((state) => {
    const t = state.clock.elapsedTime;

    const cx =  cursor.current.x *  3.5;
    const cy = -cursor.current.y * 2.5;

    if (dustGroupRef.current) {
      dustGroupRef.current.children.forEach((child, i) => {
        const p = dust[i];

        const baseX = p.x + Math.sin(t * p.speed + p.phase) * p.amp;

        const yWrap = ((p.y - t * p.drift + 5) % 10) - 5;
        const baseY = yWrap + Math.cos(t * p.speed * 0.8 + p.phase) * p.amp * 0.4;

        const depthBias = Math.max(0, 1 - Math.abs(p.z) / 5);
        child.position.x = baseX + (cx - baseX) * 0.10 * depthBias;
        child.position.y = baseY + (cy - baseY) * 0.10 * depthBias;
      });
    }

    if (haloGroupRef.current) {
      haloGroupRef.current.children.forEach((child, i) => {
        const o = halos[i];

        child.position.x = o.x + Math.sin(t * o.speed + o.phase) * o.amp;
        child.position.y = o.y + Math.cos(t * o.speed * 0.9 + o.phase) * o.amp * 0.7;
      });
    }
  });

  return (
    <>
      {}
      <group ref={haloGroupRef}>
        {halos.map((h, i) => (
          <sprite key={'h' + i} position={[h.x, h.y, h.z]} scale={[h.size, h.size, 1]}>
            <spriteMaterial
              map={sprite}
              color="#ffffff"
              transparent
              opacity={h.opacity}
              depthWrite={false}
              blending={THREE.AdditiveBlending}
            />
          </sprite>
        ))}
      </group>
      {}
      <group ref={dustGroupRef}>
        {dust.map((p, i) => (
          <sprite key={'d' + i} position={[p.x, p.y, p.z]} scale={[p.size, p.size, 1]}>
            <spriteMaterial
              map={sprite}
              color="#ffffff"
              transparent
              opacity={p.opacity}
              depthWrite={false}
              blending={THREE.AdditiveBlending}
            />
          </sprite>
        ))}
      </group>
    </>
  );
}

function Orbs({ tone, cursor }: { tone: NonNullable<AmbientSceneProps['tone']>; cursor: CursorRef }) {
  const groupRef = useRef<THREE.Group>(null);

  const sprite = useMemo(() => createGlowTexture(), []);

  const orbs = useMemo(() => buildOrbs(tone), [tone]);

  useFrame((state) => {
    if (!groupRef.current) return;
    const t = state.clock.elapsedTime;

    const cx = cursor.current.x *  6;
    const cy = -cursor.current.y * 4;
    groupRef.current.children.forEach((child, i) => {
      const o = orbs[i];

      const baseX = o.x + Math.sin(t * o.speed + o.phase) * o.amp;
      const baseY = o.y + Math.cos(t * o.speed * 0.8 + o.phase) * o.amp;

      const depthBias = Math.max(0, 1 - Math.abs(o.z) / 3);
      child.position.x = baseX + (cx - baseX) * 0.18 * depthBias;
      child.position.y = baseY + (cy - baseY) * 0.18 * depthBias;
    });
  });

  return (
    <group ref={groupRef}>
      {orbs.map((o, i) => (
        <sprite key={i} position={[o.x, o.y, o.z]} scale={[o.size, o.size, 1]}>
          <spriteMaterial
            map={sprite}
            color={o.color}
            transparent
            opacity={o.opacity}
            depthWrite={false}
            blending={THREE.AdditiveBlending}
          />
        </sprite>
      ))}
    </group>
  );
}

interface Orb {
  x: number; y: number; z: number;
  size: number; opacity: number;
  speed: number; amp: number; phase: number;
  color: string;
}

function buildOrbs(tone: 'default' | 'login' | 'home'): Orb[] {
  const accent = ACCENT.base;
  const glow   = ACCENT.glow;

  const palette = tone === 'login'
    ? [accent, accent, glow, '#5eead4']
    : tone === 'home'
      ? [accent, glow, '#22d3ee', '#f0abfc']
      : [accent, glow, '#a5b4fc', '#fbcfe8'];

  return [
    { x: -3.5, y:  1.5, z: -1, size: 5.5, opacity: 0.55, speed: 0.30, amp: 0.4, phase: 0.0, color: palette[0] },
    { x:  3.0, y:  2.0, z: -2, size: 6.5, opacity: 0.45, speed: 0.22, amp: 0.5, phase: 1.2, color: palette[1] },
    { x: -2.5, y: -2.0, z:  0, size: 4.5, opacity: 0.40, speed: 0.35, amp: 0.3, phase: 2.3, color: palette[2] },
    { x:  2.5, y: -1.5, z: -1, size: 5.0, opacity: 0.50, speed: 0.28, amp: 0.4, phase: 3.4, color: palette[3] },
    { x:  0.0, y:  3.0, z: -3, size: 7.5, opacity: 0.30, speed: 0.18, amp: 0.6, phase: 4.5, color: palette[0] },
    { x:  0.0, y: -3.0, z: -2, size: 6.0, opacity: 0.35, speed: 0.24, amp: 0.5, phase: 5.6, color: palette[1] },
  ];
}

function createGlowTexture(): THREE.Texture {
  const size = 256;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d')!;
  const grad = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  grad.addColorStop(0.00, 'rgba(255,255,255,1.0)');
  grad.addColorStop(0.25, 'rgba(255,255,255,0.6)');
  grad.addColorStop(0.55, 'rgba(255,255,255,0.15)');
  grad.addColorStop(1.00, 'rgba(255,255,255,0.0)');
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, size, size);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.minFilter  = THREE.LinearMipmapLinearFilter;
  tex.magFilter  = THREE.LinearFilter;
  tex.generateMipmaps = true;
  return tex;
}

function createStarTexture(): THREE.Texture {
  const size = 128;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d')!;
  const grad = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  grad.addColorStop(0.00, 'rgba(255,255,255,1.0)');
  grad.addColorStop(0.20, 'rgba(255,255,255,0.85)');
  grad.addColorStop(0.45, 'rgba(255,255,255,0.10)');
  grad.addColorStop(1.00, 'rgba(255,255,255,0.0)');
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, size, size);
  const tex = new THREE.CanvasTexture(canvas);
  tex.colorSpace = THREE.SRGBColorSpace;
  tex.minFilter  = THREE.LinearMipmapLinearFilter;
  tex.magFilter  = THREE.LinearFilter;
  tex.generateMipmaps = true;
  return tex;
}

function rand(seed: number) {
  let s = seed >>> 0;
  return () => {
    s = (s + 0x6D2B79F5) >>> 0;
    let t = s;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

interface NebulaCloud {
  x: number; y: number; z: number;
  size: number; opacity: number;
  speed: number; amp: number; phase: number;
  color: string;
}
interface Star {
  x: number; y: number; z: number;
  size: number; baseOpacity: number;
  twinkleSpeed: number; twinklePhase: number;
  color: string;
}

function Nebula({ tone, cursor }: { tone: NonNullable<AmbientSceneProps['tone']>; cursor: CursorRef }) {
  const cloudGroupRef = useRef<THREE.Group>(null);
  const starsGroupRef = useRef<THREE.Group>(null);
  const cloudTex = useMemo(() => createGlowTexture(), []);
  const starTex  = useMemo(() => createStarTexture(), []);

  const palette = useMemo(() => {
    if (tone === 'login') return [ACCENT.base, ACCENT.base, '#5b21b6', '#22d3ee'];
    if (tone === 'home')  return [ACCENT.glow, '#22d3ee', '#f472b6', '#a78bfa'];
    return [ACCENT.base, ACCENT.glow, '#a5b4fc', '#22d3ee'];
  }, [tone]);

  const clouds = useMemo<NebulaCloud[]>(() => buildNebula(palette, tone), [palette, tone]);
  const stars  = useMemo<Star[]>(() => buildStars(palette), [palette]);

  useFrame((state) => {
    const t = state.clock.elapsedTime;

    if (cloudGroupRef.current) {
      cloudGroupRef.current.children.forEach((child, i) => {
        const c = clouds[i];
        child.position.x = c.x + Math.sin(t * c.speed + c.phase) * c.amp;
        child.position.y = c.y + Math.cos(t * c.speed * 0.7 + c.phase * 1.3) * c.amp * 0.8;
      });

      cloudGroupRef.current.rotation.z = t * 0.035 + cursor.current.x * 0.18;

      cloudGroupRef.current.position.x = cursor.current.x *  0.6;
      cloudGroupRef.current.position.y = -cursor.current.y * 0.4;
    }

    if (starsGroupRef.current) {
      starsGroupRef.current.children.forEach((child, i) => {
        const s = stars[i];
        const mat = (child as THREE.Sprite).material as THREE.SpriteMaterial;
        mat.opacity = s.baseOpacity * (0.6 + 0.4 * Math.sin(t * s.twinkleSpeed + s.twinklePhase));
      });
    }
  });

  return (
    <>
      <group ref={cloudGroupRef}>
        {clouds.map((c, i) => (
          <sprite key={i} position={[c.x, c.y, c.z]} scale={[c.size, c.size, 1]}>
            <spriteMaterial
              map={cloudTex}
              color={c.color}
              transparent
              opacity={c.opacity}
              depthWrite={false}
              blending={THREE.AdditiveBlending}
            />
          </sprite>
        ))}
      </group>

      <group ref={starsGroupRef}>
        {stars.map((s, i) => (
          <sprite key={i} position={[s.x, s.y, s.z]} scale={[s.size, s.size, 1]}>
            <spriteMaterial
              map={starTex}
              color={s.color}
              transparent
              opacity={s.baseOpacity}
              depthWrite={false}
              blending={THREE.AdditiveBlending}
            />
          </sprite>
        ))}
      </group>
    </>
  );
}

function buildNebula(palette: string[], tone: 'default' | 'login' | 'home'): NebulaCloud[] {

  const intensity = tone === 'login' ? 0.85 : 1.0;
  return [

    { x: -4.5, y:  2.8, z: -5.0, size: 12.0, opacity: 0.18 * intensity, speed: 0.10, amp: 0.5, phase: 0.0, color: palette[0] },
    { x:  4.5, y: -2.5, z: -5.5, size: 13.0, opacity: 0.16 * intensity, speed: 0.09, amp: 0.6, phase: 1.4, color: palette[1] },
    { x:  0.0, y:  3.5, z: -6.0, size: 14.0, opacity: 0.14 * intensity, speed: 0.07, amp: 0.7, phase: 2.7, color: palette[2] },
    { x:  0.0, y: -3.5, z: -5.5, size: 12.5, opacity: 0.15 * intensity, speed: 0.08, amp: 0.6, phase: 4.0, color: palette[3] },

    { x: -3.5, y:  1.0, z: -3.0, size:  7.5, opacity: 0.32 * intensity, speed: 0.18, amp: 0.4, phase: 0.7, color: palette[1] },
    { x:  3.5, y:  1.5, z: -3.5, size:  8.0, opacity: 0.28 * intensity, speed: 0.16, amp: 0.5, phase: 2.0, color: palette[0] },
    { x: -3.0, y: -1.8, z: -3.2, size:  6.5, opacity: 0.30 * intensity, speed: 0.20, amp: 0.4, phase: 3.3, color: palette[2] },
    { x:  3.0, y: -1.0, z: -3.5, size:  7.0, opacity: 0.34 * intensity, speed: 0.17, amp: 0.4, phase: 4.6, color: palette[3] },

    { x: -1.8, y:  0.5, z: -1.5, size:  4.0, opacity: 0.45 * intensity, speed: 0.28, amp: 0.3, phase: 5.0, color: palette[0] },
    { x:  2.0, y: -0.5, z: -1.0, size:  4.5, opacity: 0.42 * intensity, speed: 0.26, amp: 0.3, phase: 0.5, color: palette[1] },
    { x:  0.5, y:  2.2, z: -1.8, size:  3.5, opacity: 0.50 * intensity, speed: 0.32, amp: 0.25, phase: 2.5, color: palette[3] },
    { x: -0.8, y: -2.2, z: -1.6, size:  3.8, opacity: 0.48 * intensity, speed: 0.30, amp: 0.25, phase: 3.8, color: palette[2] },
  ];
}

function buildStars(palette: string[]): Star[] {

  const r = rand(0xC4FBA571);
  const out: Star[] = [];
  for (let i = 0; i < 35; i++) {
    out.push({
      x: (r() - 0.5) * 12,
      y: (r() - 0.5) * 8,
      z: -1.5 + r() * 1.2,
      size: 0.10 + r() * 0.18,
      baseOpacity: 0.35 + r() * 0.50,
      twinkleSpeed: 0.6 + r() * 1.4,
      twinklePhase: r() * Math.PI * 2,

      color: r() < 0.35 ? palette[i % palette.length] : '#ffffff',
    });
  }
  return out;
}

const cubeGeometry = new THREE.BoxGeometry(1, 1, 1);

interface CubeVariantPalette {
  white:  THREE.MeshBasicMaterial;
  accent: THREE.MeshBasicMaterial;
  cool:   THREE.MeshBasicMaterial;
}

let cubeMatCache: CubeVariantPalette | null = null;
function getCubeMaterials(): CubeVariantPalette {
  if (cubeMatCache) return cubeMatCache;
  cubeMatCache = {
    white:  new THREE.MeshBasicMaterial({ color: '#ffffff',     transparent: true, opacity: 0.08, wireframe: true }),
    accent: new THREE.MeshBasicMaterial({ color: ACCENT.base,   transparent: true, opacity: 0.14, wireframe: true }),
    cool:   new THREE.MeshBasicMaterial({ color: '#3b82f6',     transparent: true, opacity: 0.12, wireframe: true }),
  };
  return cubeMatCache;
}

type CubeVariant = keyof CubeVariantPalette;

interface CubeSpec {
  position: [number, number, number];
  scale:    number;
  variant:  CubeVariant;
}

function FloatingCube({ position, scale, variant }: CubeSpec) {
  const mats = getCubeMaterials();
  const material = mats[variant];

  const initialRotation = useMemo(() => new THREE.Euler(
    Math.random() * Math.PI,
    Math.random() * Math.PI,
    Math.random() * Math.PI,
  ), []);

  return (

    <Float speed={1.2} rotationIntensity={0.25} floatIntensity={0.55}>
      <mesh
        position={position}
        scale={scale}
        rotation={initialRotation}
        geometry={cubeGeometry}
        material={material}
      />
    </Float>
  );
}

function CubeDust({ cursor: _cursor }: { cursor: CursorRef }) {
  const ref = useRef<THREE.Points>(null);
  const COUNT = 32;

  const geometry = useMemo(() => {
    const pos = new Float32Array(COUNT * 3);
    for (let i = 0; i < COUNT; i++) {
      pos[i * 3]     = (Math.random() - 0.5) * 50;
      pos[i * 3 + 1] = (Math.random() - 0.5) * 50;
      pos[i * 3 + 2] = (Math.random() - 0.5) * 30;
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    return g;
  }, []);

  const material = useMemo(() => new THREE.PointsMaterial({
    size: 0.05,
    color: '#ffffff',
    transparent: true,
    opacity: 0.30,
    sizeAttenuation: true,
    depthWrite: false,
  }), []);

  useFrame((_, dt) => {
    if (!ref.current) return;
    ref.current.rotation.y += dt * 0.02;
    ref.current.rotation.x += dt * 0.01;

  });

  return <points ref={ref} geometry={geometry} material={material} />;
}

function FloatingCubes({ tone, cursor }: { tone: NonNullable<AmbientSceneProps['tone']>; cursor: CursorRef }) {
  const groupRef = useRef<THREE.Group>(null);

  const cubes = useMemo<CubeSpec[]>(() => {
    const base: CubeSpec[] = [
      { position: [-6,  2,  -5],  scale: 2.5, variant: 'accent' },
      { position: [-9, -4,  -8],  scale: 1.5, variant: 'cool'   },
      { position: [ 7, -2,  -6],  scale: 3.0, variant: 'accent' },
      { position: [ 9,  5, -10],  scale: 1.8, variant: 'cool'   },
      { position: [ 0,  8, -15],  scale: 4.0, variant: 'white'  },
    ];
    if (tone === 'login') {

      return base.map(c => ({ ...c, variant: c.variant === 'cool' ? 'accent' : c.variant }));
    }
    return base;
  }, [tone]);

  useFrame((_, _dt) => {
    if (!groupRef.current) return;

  });

  return (
    <>
      {}
      <Stars
        radius={80}
        depth={40}
        count={140}
        factor={3}
        saturation={0}
        fade
        speed={0.18}
      />

      {}
      <CubeDust cursor={cursor} />

      {}
      <group ref={groupRef}>
        {cubes.map((c, i) => (
          <FloatingCube key={i} {...c} />
        ))}
      </group>
    </>
  );
}

void Orbs;
void Nebula;
