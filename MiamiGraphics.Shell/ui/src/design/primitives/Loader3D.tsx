import { Canvas, useFrame } from '@react-three/fiber';
import { Float, Sparkles } from '@react-three/drei';
import { useRef } from 'react';
import * as THREE from 'three';
import { ACCENT } from '../tokens';

interface Loader3DProps {
  size?:    number;
  accent?:  string;
  progress?: number;
  className?: string;
}

export function Loader3D({
  size = 96, accent = ACCENT.base, progress, className,
}: Loader3DProps) {
  return (
    <div style={{ width: size, height: size }} className={className}>
      <Canvas
        dpr={[1, 1.2]}
        camera={{ position: [0, 0, 4], fov: 38 }}
        gl={{ alpha: true, antialias: false, powerPreference: 'default' }}
        style={{ background: 'transparent' }}
      >
        {}
        <ambientLight intensity={0.25} />
        <pointLight position={[3, 2.5, 3]}  intensity={2.6} color="#ffffff" />
        <pointLight position={[-3, -1.5, 2]} intensity={3.5} color={accent} />
        <pointLight position={[0, 0, -3]}    intensity={1.5} color={ACCENT.glow} />

        <Float
          speed={1.6}
          rotationIntensity={0.35}
          floatIntensity={0.45}
        >
          <SpinningKnot accent={accent} />
        </Float>

        {typeof progress === 'number' && (
          <ProgressRing progress={progress} accent={accent} />
        )}

        {}
        {size >= 120 && (
          <Sparkles
            count={12}
            scale={3.2}
            size={2.5}
            speed={0.4}
            opacity={0.7}
            color={accent}
          />
        )}

      </Canvas>
    </div>
  );
}

function SpinningKnot({ accent }: { accent: string }) {
  const ref = useRef<THREE.Mesh>(null);

  useFrame((_, dt) => {
    if (!ref.current) return;
    ref.current.rotation.x += dt * 0.35;
    ref.current.rotation.y += dt * 0.55;
  });

  return (
    <mesh ref={ref}>
      <torusKnotGeometry args={[0.7, 0.22, 80, 14]} />
      <meshPhysicalMaterial
        color={accent}
        metalness={0.85}
        roughness={0.18}
        clearcoat={1}
        clearcoatRoughness={0.08}
        envMapIntensity={1.6}
        emissive={accent}
        emissiveIntensity={0.18}
        sheen={0.4}
        sheenColor={ACCENT.glow}
      />
    </mesh>
  );
}

function ProgressRing({ progress, accent }: { progress: number; accent: string }) {
  const ref = useRef<THREE.Mesh>(null);
  const clamped = Math.max(0, Math.min(100, progress));
  const arc = (clamped / 100) * Math.PI * 2;

  useFrame((_, dt) => {
    if (!ref.current) return;
    ref.current.rotation.z -= dt * 0.5;
  });

  return (
    <mesh ref={ref}>
      {}
      <ringGeometry args={[1.40, 1.58, 96, 1, 0, arc]} />
      {}
      <meshBasicMaterial
        color={accent}
        side={THREE.DoubleSide}
        toneMapped={false}
      />
    </mesh>
  );
}
