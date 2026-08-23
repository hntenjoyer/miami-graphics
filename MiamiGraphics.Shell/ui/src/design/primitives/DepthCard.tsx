import { motion, useTransform, type MotionValue } from 'framer-motion';
import { clsx } from 'clsx';
import { type ReactNode, type CSSProperties, createContext, useContext } from 'react';
import { useMouseTilt } from '../useMouseTilt';
import { TILT, EASE_DEPTH } from '../tokens';

interface ParallaxAxes {
  px: MotionValue<number>;
  py: MotionValue<number>;
}
const ParallaxCtx = createContext<ParallaxAxes | null>(null);

interface DepthCardProps {
  children:    ReactNode;
  className?:  string;
  tilt?:       number;
  glowOnHover?: boolean;
  onClick?:    () => void;
}

export function DepthCard({
  children, className, tilt = TILT.card, glowOnHover = true, onClick,
}: DepthCardProps) {
  const { ref, rotateX, rotateY, px, py, onPointerMove, onPointerLeave } =
    useMouseTilt<HTMLDivElement>({ maxDegrees: tilt });

  return (
    <ParallaxCtx.Provider value={{ px, py }}>
      <motion.div
        ref={ref}
        onPointerMove={onPointerMove}
        onPointerLeave={onPointerLeave}
        onClick={onClick}
        whileHover={{ scale: 1.015, transition: { duration: 0.3, ease: EASE_DEPTH } }}
        whileTap={{ scale: 0.985, transition: { duration: 0.1, ease: EASE_DEPTH } }}
        style={{
          rotateX,
          rotateY,
          transformPerspective: 1200,
          transformStyle: 'preserve-3d',
        }}
        className={clsx(
          'relative cursor-pointer',
          'transition-shadow duration-300 ease-depth',
          glowOnHover && 'hover:shadow-glow-accent',
          className,
        )}
      >
        {children}
      </motion.div>
    </ParallaxCtx.Provider>
  );
}

interface LayerProps {
  children:   ReactNode;
  className?: string;
  depth?:     number;
  style?:     CSSProperties;
}

function Layer({ children, className, depth = 12, style }: LayerProps) {
  const ctx = useContext(ParallaxCtx);

  if (!ctx) return <div className={className} style={style}>{children}</div>;

  const tx = useTransform(ctx.px, v => v * depth * 2);
  const ty = useTransform(ctx.py, v => v * depth * 2);

  return (
    <motion.div
      style={{
        x: tx, y: ty,
        translateZ: depth,
        transformStyle: 'preserve-3d',
        ...style,
      }}
      className={className}
    >
      {children}
    </motion.div>
  );
}

DepthCard.Layer = Layer;
