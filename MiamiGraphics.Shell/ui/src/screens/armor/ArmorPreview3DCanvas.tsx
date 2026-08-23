import { Component, Suspense, useEffect, type ReactNode } from 'react';
import { Canvas } from '@react-three/fiber';
import { OrbitControls, Center, Bounds, useGLTF } from '@react-three/drei';
import { AlertTriangle } from 'lucide-react';
import * as THREE from 'three';
import { CarbonSurface } from '@/design';
import i18n from '@/i18n';

export function PreviewCanvas({ url, withBackground }: { url: string; withBackground: boolean }) {
  return (
    <PreviewErrorBoundary>
      <div className="relative w-full h-full overflow-hidden">
        {withBackground && (
          <CarbonSurface weaveOpacity={0.22} glowIntensity={0.7} vignetteIntensity={0.35} />
        )}
        <Canvas
          dpr={[1, 1.5]}
          camera={{ position: [0, 0, 2.4], fov: 35 }}
          gl={{ antialias: true, alpha: true }}
          frameloop="demand"
          className="!absolute !inset-0 w-full h-full"
        >
          <ambientLight intensity={0.95} />
          <hemisphereLight args={['#ffffff', '#5b6170', 0.85]} />
          <directionalLight position={[4, 5, 4]} intensity={1.4} color="#ffffff" />
          <directionalLight position={[-3, 2, 2]} intensity={0.85} color="#ffffff" />
          <directionalLight position={[0, 3, -5]} intensity={0.6} color="#ffffff" />
          <Suspense fallback={null}>
            <Bounds fit clip margin={1.25} interpolateFunc={() => 1} maxDuration={0.001}>
              <Center>
                <ArmorModel url={url} />
              </Center>
            </Bounds>
          </Suspense>
          <OrbitControls
            enableZoom={false}
            enablePan={false}
            minPolarAngle={Math.PI / 3}
            maxPolarAngle={Math.PI / 1.7}
          />
        </Canvas>
      </div>
    </PreviewErrorBoundary>
  );
}

function ArmorModel({ url }: { url: string }) {
  const { scene } = useGLTF(url);

  useEffect(() => {
    return () => {
      scene.traverse((obj) => {
        const mesh = obj as THREE.Mesh;
        if (mesh.geometry) mesh.geometry.dispose?.();
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        for (const m of mats) {
          if (!m) continue;
          const mat = m as THREE.Material & Record<string, unknown>;
          for (const key of Object.keys(mat)) {
            const val = (mat as Record<string, unknown>)[key];
            if (val && typeof val === 'object' && (val as { isTexture?: boolean }).isTexture) {
              (val as THREE.Texture).dispose?.();
            }
          }
          mat.dispose?.();
        }
      });
      try { useGLTF.clear(url); } catch {  }
    };
  }, [scene, url]);

  return <primitive object={scene} />;
}

class PreviewErrorBoundary extends Component<{ children: ReactNode }, { hasError: boolean }> {
  state = { hasError: false };
  static getDerivedStateFromError() { return { hasError: true }; }
  render() {
    if (this.state.hasError) {
      return (
        <div className="w-full h-full flex flex-col items-center justify-center gap-1
                        bg-gradient-to-b from-glass-strong to-bg-surface text-text-muted">
          <AlertTriangle size={28} className="text-status-warning" />
          <span className="text-[10px] uppercase tracking-wider">{i18n.t('armor.preview3dFailed', '3D failed')}</span>
        </div>
      );
    }
    return this.props.children;
  }
}
