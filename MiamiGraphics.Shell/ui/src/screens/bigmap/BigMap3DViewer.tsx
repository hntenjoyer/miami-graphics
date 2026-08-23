import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { motion } from 'framer-motion';
import { Loader2, X, AlertTriangle, Move3d } from 'lucide-react';
import * as THREE from 'three';
import { GLTFLoader } from 'three/examples/jsm/loaders/GLTFLoader.js';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { bridge } from '@/bridge';
import { useInstallProgressStore } from '@/store/installProgressStore';

interface Props {
  mapId:   string;
  mapName: string;
  previewSrc?: string | null;
  onClose: () => void;
}

export function BigMap3DViewer({ mapId, mapName, previewSrc, onClose }: Props) {
  const { t } = useTranslation();
  const mountRef = useRef<HTMLDivElement>(null);
  const [glbUrl, setGlbUrl] = useState<string | null>(null);
  const [phase, setPhase] = useState<'fetching' | 'parsing' | 'ready' | 'error'>('fetching');

  const progressEntry = useInstallProgressStore(s => s.byId['redux:bigmap3d']);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      try {
        const url = await bridge.bigMapPreviewGlb(mapId);
        if (cancelled) return;
        if (!url) { setPhase('error'); return; }
        setGlbUrl(url);
        setPhase('parsing');
      } catch {
        if (!cancelled) setPhase('error');
      }
    })();
    return () => { cancelled = true; };
  }, [mapId]);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  useEffect(() => {
    if (!glbUrl) return;
    const mount = mountRef.current;
    if (!mount) return;

    const scene = new THREE.Scene();
    scene.background = new THREE.Color(0x0a0e14);

    const renderer = new THREE.WebGLRenderer({ antialias: true, logarithmicDepthBuffer: true });
    renderer.setSize(mount.clientWidth, mount.clientHeight);
    renderer.setPixelRatio(window.devicePixelRatio);
    mount.appendChild(renderer.domElement);

    const camera = new THREE.PerspectiveCamera(
      50, mount.clientWidth / mount.clientHeight, 10, 60000);
    const controls = new OrbitControls(camera, renderer.domElement);
    controls.maxPolarAngle = Math.PI * 0.35;
    controls.minDistance = 300;
    controls.maxDistance = 11000;
    controls.enableDamping = true;
    controls.dampingFactor = 0.08;

    let mapBounds: THREE.Box3 | null = null;
    let disposed = false;

    const loader = new GLTFLoader();
    loader.load(
      glbUrl,
      (gltf) => {
        if (disposed) return;
        scene.add(gltf.scene);
        const box = new THREE.Box3().setFromObject(gltf.scene);
        mapBounds = box;
        const center = box.getCenter(new THREE.Vector3());
        const size = box.getSize(new THREE.Vector3());
        camera.position.set(center.x, center.y + Math.max(size.x, size.z) * 0.9, center.z + 1);
        controls.target.copy(center);
        controls.update();
        setPhase('ready');
      },
      undefined,
      () => { if (!disposed) setPhase('error'); },
    );

    const keys = new Set<string>();
    const onKeyDown = (e: KeyboardEvent) => {
      if (['KeyW', 'KeyA', 'KeyS', 'KeyD', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight'].includes(e.code)) {
        keys.add(e.code);
        e.preventDefault();
      }
    };
    const onKeyUp = (e: KeyboardEvent) => keys.delete(e.code);
    const onBlur = () => keys.clear();
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onBlur);

    const clock = new THREE.Clock();
    const velocity = new THREE.Vector3();

    const applyWasd = (dt: number) => {
      const dist = camera.position.distanceTo(controls.target);
      const maxSpeed = dist * 0.7;

      const fwd = new THREE.Vector3();
      camera.getWorldDirection(fwd);
      fwd.y = 0;
      if (fwd.lengthSq() > 1e-8) fwd.normalize();
      const right = new THREE.Vector3().crossVectors(fwd, new THREE.Vector3(0, 1, 0)).normalize();

      const wish = new THREE.Vector3();
      if (keys.has('KeyW') || keys.has('ArrowUp'))    wish.add(fwd);
      if (keys.has('KeyS') || keys.has('ArrowDown'))  wish.sub(fwd);
      if (keys.has('KeyD') || keys.has('ArrowRight')) wish.add(right);
      if (keys.has('KeyA') || keys.has('ArrowLeft'))  wish.sub(right);
      if (wish.lengthSq() > 0) wish.normalize().multiplyScalar(maxSpeed);

      const k = 1 - Math.exp(-dt * 6);
      velocity.lerp(wish, k);
      if (velocity.lengthSq() < 1e-4) { velocity.set(0, 0, 0); return; }

      const move = velocity.clone().multiplyScalar(dt);
      controls.target.add(move);
      camera.position.add(move);
    };

    const clampToMap = () => {
      if (!mapBounds) return;
      const tgt = controls.target;
      const dist = camera.position.distanceTo(tgt);
      const inset = dist * 0.35;
      const ctr = mapBounds.getCenter(new THREE.Vector3());
      const minX = Math.min(mapBounds.min.x + inset, ctr.x);
      const maxX = Math.max(mapBounds.max.x - inset, ctr.x);
      const minZ = Math.min(mapBounds.min.z + inset, ctr.z);
      const maxZ = Math.max(mapBounds.max.z - inset, ctr.z);
      const cx = THREE.MathUtils.clamp(tgt.x, minX, maxX);
      const cz = THREE.MathUtils.clamp(tgt.z, minZ, maxZ);
      const dx = cx - tgt.x, dz = cz - tgt.z;
      if (dx !== 0 || dz !== 0) {
        tgt.x = cx; tgt.z = cz;
        camera.position.x += dx;
        camera.position.z += dz;
        velocity.set(0, 0, 0);
      }
    };

    renderer.setAnimationLoop(() => {
      const dt = Math.min(clock.getDelta(), 0.1);
      applyWasd(dt);
      controls.update();
      clampToMap();
      renderer.render(scene, camera);
    });

    const onResize = () => {
      if (!mount) return;
      camera.aspect = mount.clientWidth / mount.clientHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(mount.clientWidth, mount.clientHeight);
    };
    window.addEventListener('resize', onResize);

    return () => {
      disposed = true;
      renderer.setAnimationLoop(null);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('blur', onBlur);
      window.removeEventListener('resize', onResize);
      controls.dispose();
      scene.traverse(obj => {
        const mesh = obj as THREE.Mesh;
        if (mesh.geometry) mesh.geometry.dispose();
        const mat = mesh.material as THREE.Material | THREE.Material[] | undefined;
        if (Array.isArray(mat)) mat.forEach(m => m.dispose());
        else mat?.dispose();
      });
      renderer.dispose();
      renderer.domElement.remove();
    };
  }, [glbUrl]);

  const busy = phase === 'fetching' || phase === 'parsing';
  const loadingLabel = phase === 'fetching'
    ? (progressEntry && progressEntry.phase !== 'done' && progressEntry.phase !== 'error' && progressEntry.percent > 0
        ? `${t('bigmap.viewer.loading', 'Загрузка…')} ${Math.round(progressEntry.percent)}%`
        : t('bigmap.viewer.loading', 'Загрузка…'))
    : t('bigmap.viewer.loading', 'Загрузка…');

  return (
    <motion.div
      key="bigmap-3d-viewer"
      className="fixed inset-0 z-[100] bg-black/80 backdrop-blur-glass-ultra flex items-center justify-center p-4"
      initial={{ opacity: 0 }}
      animate={{ opacity: 1 }}
      exit={{ opacity: 0 }}
      transition={{ duration: 0.22, ease: [0.22, 1, 0.36, 1] }}
      onClick={onClose}
    >
      <motion.div
        className="relative w-[min(94vw,1500px)] h-[min(90vh,940px)] rounded-3xl overflow-hidden
                   bg-[#0a0e14] border border-white/[0.08]"
        initial={{ opacity: 0, scale: 0.94, y: 20 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        exit={{ opacity: 0, scale: 0.97, y: 10 }}
        transition={{
          opacity: { duration: 0.2 },
          default: { type: 'spring', stiffness: 300, damping: 28, mass: 0.8 },
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div ref={mountRef} className="absolute inset-0" />

        <div className="absolute top-0 inset-x-0 z-20 px-5 py-3.5 flex items-center gap-3
                        bg-gradient-to-b from-black/60 to-transparent pointer-events-none">
          <Move3d size={16} className="text-white/70 shrink-0" />
          <h2 className="text-sm font-semibold text-white truncate flex-1
                         drop-shadow-[0_2px_4px_rgba(0,0,0,0.8)]">
            {t('bigmap.viewer.title', '3D-просмотр · {{name}}', { name: mapName })}
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label={t('catalog.closeEsc')}
            className="pointer-events-auto w-9 h-9 rounded-lg flex items-center justify-center
                       text-white/85 bg-black/50 hover:bg-black/80 hover:text-white transition-colors"
          >
            <X size={16} />
          </button>
        </div>

        {phase === 'ready' && (
          <div className="absolute bottom-3 inset-x-0 z-20 flex justify-center pointer-events-none">
            <span className="px-3 py-1.5 rounded-lg bg-black/55 backdrop-blur-md
                             text-[11px] text-white/75 tabular-nums">
              {t('bigmap.viewer.hint', 'ЛКМ - вращение · колесо - зум · WASD / ПКМ - перемещение')}
            </span>
          </div>
        )}

        {busy && (
          <div className="absolute inset-0 z-10">
            {previewSrc && (
              <img
                src={previewSrc}
                alt=""
                aria-hidden
                draggable={false}
                className="absolute inset-0 w-full h-full object-cover object-center select-none"
                style={{ filter: 'brightness(0.55) saturate(0.85)' }}
              />
            )}
            <div className="absolute inset-0 flex flex-col items-center justify-center gap-3">
              <Loader2 size={28} className="animate-spin text-accent" />
              <span className="text-sm text-white/85 px-3 py-1.5 rounded-lg bg-black/45 backdrop-blur-md">
                {loadingLabel}
              </span>
            </div>
          </div>
        )}
        {phase === 'error' && (
          <div className="absolute inset-0 z-10 flex flex-col items-center justify-center gap-3
                          text-text-secondary">
            <AlertTriangle size={30} className="opacity-50" />
            <span className="text-sm text-center max-w-[360px]">
              {t('bigmap.viewer.error', 'Не удалось построить 3D-модель этой карты. Попробуй ещё раз или проверь интернет.')}
            </span>
          </div>
        )}
      </motion.div>
    </motion.div>
  );
}
