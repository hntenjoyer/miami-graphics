import allegriNewZone from '@/assets/bigmaps/allegri-new-zone.webp';
import dinero from '@/assets/bigmaps/dinero.webp';
import extended from '@/assets/bigmaps/extended.webp';
import gucci5rp from '@/assets/bigmaps/gucci-5rp.webp';
import sadovskyy from '@/assets/bigmaps/sadovskyy.webp';
import uziNoLogo from '@/assets/bigmaps/uzi-no-logo.webp';
import uzi from '@/assets/bigmaps/uzi.webp';

const PREVIEWS: Record<string, string> = {
  'allegri-new-zone': allegriNewZone,
  'dinero': dinero,
  'extended': extended,
  'gucci-5rp': gucci5rp,
  'sadovskyy': sadovskyy,
  'uzi-no-logo': uziNoLogo,
  'uzi': uzi,
};

export function bigMapVectorPreview(id: string): string | null {
  return PREVIEWS[id] ?? null;
}
