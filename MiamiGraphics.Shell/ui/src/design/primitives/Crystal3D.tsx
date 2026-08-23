import { Canvas, useFrame } from '@react-three/fiber';
import { Float, Sparkles } from '@react-three/drei';
import { useRef } from 'react';
import * as THREE from 'three';
import { ACCENT, R3F_DPR, CAMERA_OBJECT } from '../tokens';

interface Crystal3DProps {
  size?:  number;
  accent?: string;
  intensity?: 'compact' | 'hero';
  className?: string;
}

export function Crystal3D({
  size = 240, accent = ACCENT.base, intensity = 'compact', className,
}: Crystal3DProps) {
  const dpr: [number, number] = intensity === 'hero' ? R3F_DPR : [1, 1.2];
  const isHero = intensity === 'hero';

  return (
    <div style={{ width: size, height: size }} className={className}>
      <Canvas
        dpr={dpr}
        camera={CAMERA_OBJECT}
        gl={{ alpha: true, antialias: false, powerPreference: 'default' }}
        style={{ background: 'transparent' }}
      >
        <ambientLight intensity={0.35} />
        <pointLight position={[3, 3, 3]}    intensity={3}  color="#ffffff" />
        <pointLight position={[-3, -2, 2]}  intensity={5}  color={accent} />
        <pointLight position={[0, 0, -3]}   intensity={3}  color={ACCENT.glow} />
        <pointLight position={[0, 4, 0]}    intensity={2}  color="#ffffff" />

        <Float speed={1.4} rotationIntensity={0.6} floatIntensity={0.5}>
          <SpinningCrystal accent={accent} />
        </Float>

        {}
        {isHero && size >= 140 && (
          <Sparkles
            count={18}
            scale={3.6}
            size={2.8}
            speed={0.3}
            opacity={0.7}
            color={accent}
          />
        )}

      </Canvas>
    </div>
  );
}

function SpinningCrystal({ accent }: { accent: string }) {
  const shellRef = useRef<THREE.Mesh>(null);
  const coreRef  = useRef<THREE.Mesh>(null);

  useFrame((state, dt) => {
    if (shellRef.current) {
      shellRef.current.rotation.y += dt * 0.20;
      shellRef.current.rotation.x += dt * 0.08;
    }

    if (coreRef.current) {
      coreRef.current.rotation.y -= dt * 0.45;
      coreRef.current.rotation.z += dt * 0.30;

      const t = state.clock.elapsedTime;
      const pulse = 0.85 + Math.sin(t * 1.6) * 0.15;
      const mat = coreRef.current.material as THREE.MeshBasicMaterial;
      mat.opacity = pulse;
    }
  });

  return (
    <group>
      {}
      <mesh ref={shellRef}>
        {}
        <octahedronGeometry args={[1.2, 2]} />
        <meshPhysicalMaterial
          color={accent}
          transparent
          opacity={0.34}
          metalness={0.18}
          roughness={0.16}
          transmission={0.25}
          clearcoat={1}
          clearcoatRoughness={0.08}
          emissive={accent}
          emissiveIntensity={0.08}
          side={THREE.DoubleSide}
        />
      </mesh>

      {}
      <mesh ref={coreRef}>
        <icosahedronGeometry args={[0.42, 0]} />
        <meshBasicMaterial
          color={accent}
          transparent
          opacity={0.85}
          toneMapped={false}
        />
      </mesh>
    </group>
  );
}
