interface Props {
  size?: number;
  className?: string;
}

export function FlagEu({ size = 16, className = '' }: Props) {
  const w = Math.round(size * 1.5);
  return (
    <svg
      width={w}
      height={size}
      viewBox="0 0 24 16"
      className={className}
      aria-hidden
    >
      <rect width="24" height="16" rx="2" fill="#003399" />
      {}
      <g fill="#FFCC00">
        {[
          [12, 3.5], [16.5, 4.6], [19.5, 8],
          [16.5, 11.4], [12, 12.5], [7.5, 11.4],
          [4.5, 8], [7.5, 4.6],
          [12, 5.8], [15, 8], [12, 10.2], [9, 8],
        ].map(([cx, cy], i) => (
          <circle key={i} cx={cx} cy={cy} r="0.65" />
        ))}
      </g>
    </svg>
  );
}

export function FlagRu({ size = 16, className = '' }: Props) {
  const w = Math.round(size * 1.5);
  return (
    <svg
      width={w}
      height={size}
      viewBox="0 0 24 16"
      className={className}
      aria-hidden
    >
      <rect width="24" height="16" rx="2" fill="#FFFFFF" />
      <rect y="5.33" width="24" height="5.33" fill="#0039A6" />
      <rect y="10.66" width="24" height="5.34" fill="#D52B1E" />
    </svg>
  );
}
