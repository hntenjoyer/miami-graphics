type Bezier = [number, number, number, number];

export const EASE_DEPTH:       Bezier = [0.22, 1, 0.36, 1];

export const EASE_SPRING_FAST: Bezier = [0.34, 1.56, 0.64, 1];

export const EASE_DRIFT:       Bezier = [0.4, 0, 0.2, 1];

export const SPRING_TOGGLE = { type: 'spring', stiffness: 480, damping: 32, mass: 0.7 } as const;
export const SPRING_PRESS  = { type: 'spring', stiffness: 600, damping: 28, mass: 0.6 } as const;

export const DEPTH = {
  rest:   0,
  hover:  16,
  active: 24,
  press:  -2,
} as const;

export const TILT = {
  card:   8,
  hero:   12,
  button: 4,
} as const;

export const ACCENT = {
  base:    '#ffffff',
  hover:   '#ffffff',
  pressed: '#d4d4d8',
  glow:    '#e5e7eb',
} as const;

export const R3F_DPR: [number, number] = [1, 1.25];

export const CAMERA_OBJECT = { position: [0, 0, 4] as [number, number, number], fov: 35 };
export const CAMERA_AMBIENT = { position: [0, 0, 6] as [number, number, number], fov: 50 };

export const ENV_CARD_H = 'h-[clamp(300px,calc((100vh-180px)/2),420px)]';
