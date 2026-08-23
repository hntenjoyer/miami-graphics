import { useEffect, useRef, useState, type ImgHTMLAttributes } from 'react';

interface LazyImageProps extends Omit<ImgHTMLAttributes<HTMLImageElement>, 'src'> {
  src: string;
  eager?: boolean;
  rootMargin?: string;
}

const TRANSPARENT_PIXEL =
  'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';

export function LazyImage({
  src, eager = false, rootMargin = '200px', onLoad, ...rest
}: LazyImageProps) {
  const ref = useRef<HTMLImageElement | null>(null);
  const [visible, setVisible] = useState(eager);

  useEffect(() => {
    if (visible) return;
    const el = ref.current;
    if (!el || typeof IntersectionObserver === 'undefined') {
      setVisible(true);
      return;
    }
    const io = new IntersectionObserver(entries => {
      for (const entry of entries) {
        if (entry.isIntersecting) {
          setVisible(true);
          io.disconnect();
          return;
        }
      }
    }, { rootMargin });
    io.observe(el);
    return () => io.disconnect();
  }, [visible, rootMargin]);

  const isLoaded = visible;
  return (
    <img
      {...rest}
      ref={ref}
      src={isLoaded ? src : TRANSPARENT_PIXEL}
      loading={isLoaded ? 'eager' : 'lazy'}
      decoding="async"
      onLoad={onLoad}
    />
  );
}
