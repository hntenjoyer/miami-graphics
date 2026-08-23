export interface SocialLink {
  id: string;
  url: string;
  iconKind: 'discord' | 'telegram';
  labelKey: string;
}

export const SOCIAL_LINKS: SocialLink[] = [
  { id: 'discord',  url: 'https://discord.gg/rtneyc6dV2', iconKind: 'discord',  labelKey: 'sidebar.discord' },
  { id: 'telegram', url: 'https://t.me/miamimods',        iconKind: 'telegram', labelKey: 'sidebar.telegram' },
];
