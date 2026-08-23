import { useRef } from 'react';
import { useMotionValue, useSpring, useTransform, type MotionValue } from 'framer-motion';

interface UseMouseTiltOptions {
  maxDegrees?: number;
  stiffness?: number;
  damping?: number;
}

interface MouseTiltResult<T extends HTMLElement> {
  ref:       React.RefObject<T>;
  rotateX:   MotionValue<number>;
  rotateY:   MotionValue<number>;
  px:        MotionValue<number>;
  py:        MotionValue<number>;
  onPointerMove: (e: React.PointerEvent<T>) => void;
  onPointerLeave: () => void;
}

export function useMouseTilt<T extends HTMLElement>({
  maxDegrees = 8,
  stiffness  = 220,
  damping    = 22,
}: UseMouseTiltOptions = {}): MouseTiltResult<T> {
  const ref = useRef<T>(null);

  const px = useMotionValue(0);
  const py = useMotionValue(0);

  const sx = useSpring(px, { stiffness, damping });
  const sy = useSpring(py, { stiffness, damping });

  const rotateY = useTransform(sx, [-0.5, 0.5], [-maxDegrees, maxDegrees]);
  const rotateX = useTransform(sy, [-0.5, 0.5], [maxDegrees, -maxDegrees]);

  const onPointerMove = (e: React.PointerEvent<T>) => {
    const el = ref.current;
    if (!el) return;
    const r = el.getBoundingClientRect();
    px.set((e.clientX - r.left) / r.width  - 0.5);
    py.set((e.clientY - r.top)  / r.height - 0.5);
  };

  const onPointerLeave = () => {

    px.set(0);
    py.set(0);
  };

  return { ref, rotateX, rotateY, px, py, onPointerMove, onPointerLeave };
}
