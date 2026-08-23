import { Component, Suspense, useEffect, useState, useMemo, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Canvas, useThree, useFrame } from '@react-three/fiber';
import { OrbitControls, Center, useGLTF, useProgress, Bounds } from '@react-three/drei';
import { motion } from 'framer-motion';
import { X, Loader2, AlertTriangle, Sun, Info, RefreshCw } from 'lucide-react';
import * as THREE from 'three';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { CarbonSurface } from '@/design';

interface Props {
  glbUrl: string | null;
  title:  string;
  onClose: () => void;
  subjectKind?: 'gun' | 'armor';
}

export function GlbViewerModal({ glbUrl, title, onClose, subjectKind = 'gun' }: Props) {
  const { t } = useTranslation();
  const [lightIntensity, setLightIntensity] = useState(1.0);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <motion.div
      key="backdrop"

      className="fixed inset-0 z-[100] bg-black/65 backdrop-blur-glass-ultra backdrop-saturate-liquid flex items-center justify-center p-6"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.28, ease: [0.22, 1, 0.36, 1] }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-[min(90vw,1400px)] h-[min(90vh,900px)] rounded-3xl
                   bg-glass-ultra backdrop-blur-glass-ultra backdrop-saturate-liquid
                   shadow-z3 shadow-glass-inner overflow-hidden"

        initial={{ opacity: 0, scale: 0.97, y: 12 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.97, y: 8 }}
        transition={{ duration: 0.32, ease: [0.22, 1, 0.36, 1] }}
        onClick={e => e.stopPropagation()}
      >
        {}
        <div className="absolute top-0 inset-x-0 z-20 px-4 py-3 flex items-center gap-3
                        bg-gradient-to-b from-black/50 to-transparent pointer-events-none">
          <h2 className="text-sm font-semibold text-white truncate flex-1
                         drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">
            {title}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label={t('catalog.closeEsc', 'Закрыть (Esc)')}
            className="pointer-events-auto w-9 h-9 rounded-lg flex items-center justify-center
                       text-white/85 bg-black/50 hover:bg-black/80 hover:text-white
                       transition-colors"
          >
            <X size={16} />
          </button>
        </div>

        <HelpHint />

        {}
        {glbUrl && subjectKind === 'gun' && <TintNotice />}

        {}
        <BrightnessChip value={lightIntensity} onChange={setLightIntensity} />

        {}
        <div className="absolute inset-0 overflow-hidden">
          <CarbonSurface weaveOpacity={0.45} glowIntensity={0.85} vignetteIntensity={0.4} />
          {glbUrl ? (
            <ViewerErrorBoundary url={glbUrl}>
              <Canvas
                dpr={[1, 2]}
                camera={{ position: [0, 0, 2.4], fov: 35 }}

                gl={{
                  antialias:    true,
                  alpha:        true,
                  toneMapping:  THREE.ACESFilmicToneMapping,

                  toneMappingExposure: 1.0,
                  outputColorSpace: THREE.SRGBColorSpace,
                }}
              >
                {}
                <ambientLight intensity={0.3 * lightIntensity} />
                <directionalLight position={[ 1.5,  1.5,  1.5]} intensity={1.5 * lightIntensity} color="#ffffff" />
                <directionalLight position={[-1.5,  0.0,  0.75]} intensity={0.5 * lightIntensity} color="#ffffff" />
                <directionalLight position={[ 0.0,  0.75, -2.25]} intensity={0.7 * lightIntensity} color="#ffffff" />

                <Suspense fallback={null}>
                  {}
                  <RoomEnv />
                  <Bounds fit clip observe margin={1.2}>
                    <Center>
                      <ModelMesh url={glbUrl} />
                    </Center>
                  </Bounds>
                  {}
                  <OrbitControls
                    enablePan
                    enableDamping
                    dampingFactor={0.08}
                    rotateSpeed={0.7}
                    panSpeed={0.8}
                    minDistance={0.3}
                    maxDistance={10}
                  />
                </Suspense>
              </Canvas>
            </ViewerErrorBoundary>
          ) : (
            <NoModelMessage subjectKind={subjectKind} />
          )}
        </div>

        {}
        {glbUrl && <LoadingOverlay />}
      </motion.div>
    </motion.div>
  );
}

function NoModelMessage({ subjectKind }: { subjectKind: 'gun' | 'armor' }) {
  const { t } = useTranslation();
  const headline = subjectKind === 'armor'
    ? t('guns.viewer.noModelArmor', '3D-модель брони ещё не загружена.')
    : t('guns.viewer.noModelGun', '3D-модель этой пушки не загружена.');
  const detail = subjectKind === 'armor'
    ? t('guns.viewer.noModelArmorDetail', '3D-превью для этой брони ещё готовится и появится после обновления сборки.')
    : t('guns.viewer.noModelGunDetail', 'Автор загрузил пак без 3D-превью. Модель появится после обновления пака.');
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center text-text-muted gap-2 p-6 text-center">
      <AlertTriangle size={36} className="opacity-40" />
      <p className="text-sm">{headline}</p>
      <p className="text-xs max-w-[380px]">{detail}</p>
    </div>
  );
}

const MAX_AUTO_RETRIES = 2;

class ViewerErrorBoundary extends Component<
  { children: ReactNode; url: string | null },
  { error: Error | null; attempt: number }
> {
  state = { error: null as Error | null, attempt: 0 };
  private retryTimer: number | null = null;

  static getDerivedStateFromError(error: Error) { return { error }; }

  componentDidCatch(err: Error, info: unknown) {
    console.warn('[GlbViewerModal] error in 3D subtree', err, info);
    if (this.state.attempt < MAX_AUTO_RETRIES) {
      const delay = 400 * (this.state.attempt + 1);
      this.retryTimer = window.setTimeout(this.recover, delay);
    }
  }

  componentWillUnmount() {
    if (this.retryTimer != null) window.clearTimeout(this.retryTimer);
  }

  private recover = () => {
    this.retryTimer = null;
    if (this.props.url) { try { useGLTF.clear(this.props.url); } catch {  } }
    this.setState(s => ({ error: null, attempt: s.attempt + 1 }));
  };

  private manualRetry = () => {
    if (this.retryTimer != null) { window.clearTimeout(this.retryTimer); this.retryTimer = null; }
    if (this.props.url) { try { useGLTF.clear(this.props.url); } catch {  } }
    this.setState({ error: null, attempt: 0 });
  };

  render() {
    if (this.state.error) {
      if (this.state.attempt < MAX_AUTO_RETRIES) {
        return (
          <div className="absolute inset-0 flex flex-col items-center justify-center text-text-primary gap-2">
            <Loader2 size={24} className="animate-spin text-accent" />
          </div>
        );
      }
      return <ViewerErrorFallback error={this.state.error} onRetry={this.manualRetry} />;
    }
    return this.props.children;
  }
}

function ViewerErrorFallback({ error, onRetry }: { error: Error; onRetry: () => void }) {
  const { t } = useTranslation();
  return (
    <div className="absolute inset-0 flex flex-col items-center justify-center text-text-muted gap-3 p-6 text-center">
      <AlertTriangle size={36} className="text-status-error opacity-70" />
      <p className="text-sm text-text-primary">{t('guns.viewer.loadFailed', 'Не удалось показать модель')}</p>
      <button
        type="button"
        onClick={onRetry}
        className="inline-flex items-center gap-2 px-3 py-1.5 rounded-lg
                   text-xs text-white/90 bg-black/50 hover:bg-black/80 hover:text-white
                   transition-colors"
      >
        <RefreshCw size={13} />
        {t('guns.viewer.retry', 'Повторить')}
      </button>
      <p className="text-xs max-w-[420px] font-mono break-all">
        {error.message}
      </p>
      <p className="text-[10px] text-text-muted/70 max-w-[420px]">
        {t('guns.viewer.errorHint', 'Обычно помогает кнопка «Повторить». Если ошибка не уходит - проверь интернет-соединение и напиши нам в Discord.')}
      </p>
    </div>
  );
}

function RoomEnv() {
  const { scene, gl } = useThree();
  useEffect(() => {
    const pmrem = new THREE.PMREMGenerator(gl);
    const envTex = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    scene.environment = envTex;
    return () => {

      scene.environment = null;
      envTex.dispose();
      pmrem.dispose();
    };
  }, [scene, gl]);
  return null;
}

function ModelMesh({ url }: { url: string }) {

  useEffect(() => {
    useGLTF.clear(url);
    return () => useGLTF.clear(url);
  }, [url]);

  const { scene } = useGLTF(url);
  const cloned = useMemo(() => scene.clone(true), [scene]);

  useEffect(() => {
    cloned.traverse((obj) => {
      const mesh = obj as THREE.Mesh;
      if (!mesh.isMesh) return;
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      let hasTransparent = false;
      mats.forEach((m) => {
        if (!m) return;
        const mat = m as THREE.Material & { alphaTest?: number; transparent?: boolean };
        if (mat.transparent === true || (mat.alphaTest ?? 0) > 0) {
          hasTransparent = true;
        }

        const std = m as THREE.MeshStandardMaterial;
        if (std.isMeshStandardMaterial && !std.normalMap && !std.metalnessMap && !std.roughnessMap) {
          if (std.roughness < 0.92) std.roughness = 0.92;
          std.metalness = 0;
          std.needsUpdate = true;
        }
      });

      if (hasTransparent) mesh.renderOrder = 1;
    });
  }, [cloned]);

  const animated = useMemo(() => {
    const out: Array<{
      obj: THREE.Object3D; mode: string; su: number; sv: number;
      period: number; axis: THREE.Vector3; amp: number; maps: THREE.Texture[];
      baseQuat: THREE.Quaternion;
    }> = [];
    cloned.traverse((obj) => {
      const mesh = obj as THREE.Mesh;
      if (!mesh.isMesh || !mesh.name.startsWith('MGANIM~')) return;
      const p = mesh.name.split('~');
      const num = (i: number, d: number) => {
        const v = parseFloat(p[i]);
        return Number.isFinite(v) ? v : d;
      };
      const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
      const maps: THREE.Texture[] = [];
      mats.forEach((m) => {
        const map = (m as THREE.MeshStandardMaterial)?.map;
        if (!map) return;
        map.wrapS = map.wrapT = THREE.RepeatWrapping;
        map.needsUpdate = true;
        maps.push(map);
      });
      out.push({
        obj: mesh,
        mode: p[1] || 'uv',
        su: num(2, 1), sv: num(3, 0),
        period: Math.max(0.05, num(4, 2)),
        axis: new THREE.Vector3(num(5, 1), num(6, 0), num(7, 0)),
        amp: num(8, 20),
        maps,
        baseQuat: mesh.quaternion.clone(),
      });
    });
    return out;
  }, [cloned]);

  useFrame((state) => {
    if (animated.length === 0) return;
    const t = state.clock.getElapsedTime();
    for (const a of animated) {
      if (a.mode === 'uv') {
        const k = t / a.period;
        for (const map of a.maps) map.offset.set((a.su * k) % 1, (a.sv * k) % 1);
      } else {
        const ang = (a.amp * Math.PI / 180) * Math.sin((2 * Math.PI * t) / a.period);
        const axis = a.axis.lengthSq() > 1e-6 ? a.axis.clone().normalize() : new THREE.Vector3(1, 0, 0);
        a.obj.quaternion.copy(a.baseQuat).multiply(new THREE.Quaternion().setFromAxisAngle(axis, ang));
      }
    }
  });

  useEffect(() => {
    return () => {
      cloned.traverse((obj) => {
        const mesh = obj as THREE.Mesh;
        if (mesh.geometry) mesh.geometry.dispose?.();
        const mats = Array.isArray(mesh.material) ? mesh.material : [mesh.material];
        mats.forEach((m) => {
          if (!m) return;

          (m as THREE.Material).dispose?.();
        });
      });
    };
  }, [cloned]);

  return <primitive object={cloned} />;
}

function LoadingOverlay() {
  const { progress, active, total } = useProgress();

  const visible = active && (total === 0 || progress < 99.9);
  if (!visible) return null;
  return (
    <motion.div
      className="absolute inset-0 z-10 flex items-center justify-center pointer-events-none
                 bg-gradient-to-b from-black/30 via-transparent to-black/30"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      transition={{ duration: 0.18 }}
    >
      <div className="flex flex-col items-center gap-2 text-text-primary">
        <Loader2 size={24} className="animate-spin text-accent" />
        <span className="text-xs tabular-nums">{Math.round(progress)}%</span>
      </div>
    </motion.div>
  );
}

function HelpHint() {
  const { t } = useTranslation();
  const [show, setShow] = useState(() => {
    try { return !sessionStorage.getItem('gunpack.viewer.hint.seen'); } catch { return true; }
  });
  useEffect(() => {
    if (!show) return;
    const t = window.setTimeout(() => {
      setShow(false);
      try { sessionStorage.setItem('gunpack.viewer.hint.seen', '1'); } catch {  }
    }, 4500);
    return () => window.clearTimeout(t);
  }, [show]);
  if (!show) return null;
  return (
    <motion.div
      className="absolute bottom-4 left-4 z-10 px-3 py-1.5 rounded-md
                 bg-black/60 backdrop-blur-md border border-glass-border
                 text-[10px] uppercase tracking-wider text-white/80 pointer-events-none"
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit   ={{ opacity: 0 }}
      transition={{ duration: 0.4, delay: 0.3 }}
    >
      {t('guns.viewer.hint', 'ЛКМ - крутить · ПКМ - двигать · колесо - приближение · Esc - закрыть')}
    </motion.div>
  );
}

function TintNotice() {
  const { t } = useTranslation();
  return (
    <motion.div
      className="absolute bottom-4 right-4 z-10 max-w-[340px]
                 flex items-start gap-3
                 pl-3 pr-4 py-3 rounded-xl
                 bg-glass-strong backdrop-blur-glass-heavy backdrop-saturate-liquid
                 border border-glass-border
                 pointer-events-none"
      style={{

        boxShadow: [
          '0 14px 36px -14px rgba(0,0,0,0.65)',
          '0 2px 6px -2px rgba(0,0,0,0.5)',
          'inset 0 1px 0 rgba(255,255,255,0.08)',
          '0 0 0 1px color-mix(in srgb, var(--accent) 8%, transparent)',
        ].join(', '),
      }}
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      exit   ={{ opacity: 0 }}
      transition={{ duration: 0.4, delay: 0.4 }}
    >
      {}
      <div
        className="shrink-0 mt-0.5 w-8 h-8 rounded-lg
                   flex items-center justify-center
                   bg-[color-mix(in_srgb,var(--accent)_18%,transparent)]
                   border border-[color-mix(in_srgb,var(--accent)_28%,transparent)]"
        aria-hidden
      >
        <Info size={14} className="text-accent" />
      </div>
      <div className="flex-1 min-w-0">
        <div className="font-display font-bold text-[11.5px] uppercase tracking-[0.06em] text-white leading-tight">
          {t('guns.viewer.tintTitle', 'Возможны отличия в цвете')}
        </div>
        <div className="text-[11px] text-white/65 mt-1 leading-snug">
          {t('guns.viewer.tintBody', 'В редких случаях часть шейдеров подгружается уже в игре, и в просмотрщике вы видите базовый вид. Если что-то с цветами не так - ориентируйтесь на баннер ганпака.')}
        </div>
      </div>
    </motion.div>
  );
}

function BrightnessChip({ value, onChange }: { value: number; onChange: (v: number) => void }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open) return;
    const t = window.setTimeout(() => setOpen(false), 3000);
    return () => window.clearTimeout(t);
  }, [open, value]);

  const pct = Math.round(value * 100);

  return (
    <div
      className="absolute top-3 right-16 z-20 flex items-center gap-2"
      onMouseEnter={() => setOpen(true)}
    >
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        aria-label={t('guns.viewer.lighting', 'Освещение')}
        title={t('guns.viewer.lighting', 'Освещение')}
        className="w-9 h-9 rounded-lg flex items-center justify-center
                   text-white/85 bg-black/50 hover:bg-black/80 hover:text-white
                   transition-colors"
      >
        <Sun size={15} />
      </button>
      <motion.div

        initial={false}
        animate={{
          width:    open ? 180 : 0,
          opacity:  open ? 1   : 0,
          paddingLeft:  open ? 12 : 0,
          paddingRight: open ? 12 : 0,
        }}
        transition={{ duration: 0.18, ease: [0.2, 0.8, 0.2, 1] }}
        className="overflow-hidden h-9 rounded-lg flex items-center gap-2
                   bg-black/50 backdrop-blur-md"
      >
        <input
          type="range"
          min={0.2}
          max={2}
          step={0.05}
          value={value}
          onChange={(e) => onChange(parseFloat(e.target.value))}
          aria-label={t('guns.viewer.lightingIntensity', 'Интенсивность освещения')}
          className="lv-range flex-1 cursor-pointer"
          style={{
            background:
              `linear-gradient(to right,` +
              ` var(--accent) 0%, var(--accent) ${((value - 0.2) / 1.8) * 100}%,` +
              ` rgba(255,255,255,0.16) ${((value - 0.2) / 1.8) * 100}%, rgba(255,255,255,0.16) 100%)`,
          }}
        />
        <span className="text-[10px] font-mono tabular-nums text-white/80 w-10 text-right">
          {pct}%
        </span>
      </motion.div>
    </div>
  );
}
