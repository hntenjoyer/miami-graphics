export const SITE_SUPABASE_URL: string = 'https://api.miamigraphicsstorage.uk';
const SITE_API_URLS = [
  SITE_SUPABASE_URL,
  'https://eu.miamigraphicsstorage.uk',
  'https://ru.miamigraphicsstorage.uk',
];

export function isPlayersConfigured(): boolean {
  return !!SITE_SUPABASE_URL;
}

export interface ProPlayer {
  id:             string;
  name:           string;
  roleEn:         string | null;
  roleRu:         string | null;
  descriptionEn:  string | null;
  descriptionRu:  string | null;
  image:          string | null;
  tier:           string | null;
  youtube:        string | null;
  twitch:         string | null;
  discord:        string | null;
  videoIds:       string[];
  reduxLink:      string | null;
  reduxId:        string | null;
  reduxName:      string | null;
  gunpackId:      string | null;
  gunpackName:    string | null;
  settingsLink:   string | null;
  specs:          Record<string, unknown> | null;
  devices:        Record<string, unknown> | null;
  createdAt:      string;
  views:          number;
  lastUpdated:    string | null;
}

interface ProPlayerRaw {
  id:               string;
  name:             string;
  role_en:          string | null;
  role_ru:          string | null;
  description_en:   string | null;
  description_ru:   string | null;
  image:            string | null;
  tier:             string | null;
  youtube:          string | null;
  twitch:           string | null;
  discord:          string | null;
  videoIds:         string[] | null;
  reduxLink:        string | null;
  settingsLink:     string | null;
  specs:            Record<string, unknown> | null;
  devices:          Record<string, unknown> | null;
  created_at:       string;
}

interface PlayerStatRaw {
  player_id:    string;
  views:        number | null;
  last_updated: string | null;
}

export async function fetchPlayers(): Promise<ProPlayer[]> {
  if (!isPlayersConfigured()) {

    return MOCK_PLAYERS;
  }

  const [players, stats] = await Promise.all([
    fetchTable<ProPlayerRaw>('pro_players'),
    fetchTable<PlayerStatRaw>('player_stats', 'player_id,views,last_updated'),
  ]);

  const byId = new Map<string, PlayerStatRaw>();
  for (const s of stats) byId.set(s.player_id, s);

  return players.map(p => normalize(p, byId.get(p.id) ?? null));
}

async function fetchTable<T>(table: string, select = '*'): Promise<T[]> {
  let lastError: unknown = null;
  for (const baseUrl of SITE_API_URLS) {
    const url = `${baseUrl.replace(/\/+$/, '')}/rest/v1/${table}?select=${encodeURIComponent(select)}`;
    try {
      const res = await fetch(url, {
        headers: {
          Accept: 'application/json',
        },
      });
      if (res.ok) return (await res.json()) as T[];

      const body = await res.text().catch(() => '');
      lastError = new Error(`API /${table} ${res.status}: ${body || res.statusText}`);
      if (res.status < 500) break;
    } catch (err) {
      lastError = err;
    }
  }
  throw lastError instanceof Error ? lastError : new Error(`API /${table} unavailable`);
}

function normalize(r: ProPlayerRaw, stat: PlayerStatRaw | null): ProPlayer {
  return {
    id:            r.id,
    name:          r.name,
    roleEn:        r.role_en,
    roleRu:        r.role_ru,
    descriptionEn: r.description_en,
    descriptionRu: r.description_ru,
    image:         r.image,
    tier:          r.tier,
    youtube:       r.youtube,
    twitch:        r.twitch,
    discord:       r.discord,
    videoIds:      Array.isArray(r.videoIds) ? r.videoIds : [],
    reduxLink:     r.reduxLink,
    reduxId:       null,
    reduxName:     null,
    gunpackId:     null,
    gunpackName:   null,
    settingsLink:  r.settingsLink,
    specs:         r.specs,
    devices:       r.devices,
    createdAt:     r.created_at,
    views:         stat?.views ?? 0,
    lastUpdated:   stat?.last_updated ?? null,
  };
}

const MOCK_PLAYERS: ProPlayer[] = [
  {
    id: 'juju', name: 'JUJU',
    roleEn: 'Elite Player', roleRu: 'Элитный игрок',
    descriptionEn: null, descriptionRu: null,
    image: null,
    tier: 'player', youtube: null, twitch: null, discord: null,
    videoIds: [],
    reduxLink: null, reduxId: null, reduxName: null, gunpackId: null, gunpackName: null, settingsLink: null,
    specs: null, devices: null,
    createdAt: new Date().toISOString(),
    views: 1955, lastUpdated: null,
  },
  {
    id: 'dobby', name: 'DOBBY',
    roleEn: 'Elite Player', roleRu: 'Элитный игрок',
    descriptionEn: null, descriptionRu: null,
    image: null,
    tier: 'player', youtube: null, twitch: null, discord: null,
    videoIds: [],
    reduxLink: null, reduxId: null, reduxName: null, gunpackId: null, gunpackName: null, settingsLink: null,
    specs: null, devices: null,
    createdAt: new Date().toISOString(),
    views: 343, lastUpdated: null,
  },
  {
    id: 'luntik', name: 'LUNTIK',
    roleEn: 'Elite Player', roleRu: 'Элитный игрок',
    descriptionEn: null, descriptionRu: null,
    image: null,
    tier: 'player', youtube: null, twitch: null, discord: null,
    videoIds: [],
    reduxLink: null, reduxId: null, reduxName: null, gunpackId: null, gunpackName: null, settingsLink: null,
    specs: null, devices: null,
    createdAt: new Date().toISOString(),
    views: 897, lastUpdated: null,
  },
  {
    id: 'panch', name: 'PANCH',
    roleEn: 'Elite Player', roleRu: 'Элитный игрок',
    descriptionEn: null, descriptionRu: null,
    image: null,
    tier: 'player', youtube: null, twitch: null, discord: null,
    videoIds: [],
    reduxLink: null, reduxId: null, reduxName: null, gunpackId: null, gunpackName: null, settingsLink: null,
    specs: null, devices: null,
    createdAt: new Date().toISOString(),
    views: 667, lastUpdated: null,
  },
];
