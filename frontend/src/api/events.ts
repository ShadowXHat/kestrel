import { api } from './apiClient';

export type EventLevel =
  | 'Critical'
  | 'Error'
  | 'Warning'
  | 'Information'
  | 'Audit Success'
  | 'Audit Failure';

export interface EventRecord {
  id: string;
  timeCreatedUtc: string;
  eventId: number;
  channel: string;
  provider: string;
  level: EventLevel;
  computer: string;
  userSid: string;
  username?: string;
  message?: string;
  rawXml?: string;
  taskCategory?: string;
  keywords?: string;
}

export interface EventQueryParams {
  startRow?: number;
  endRow?: number;
  sortField?: string;
  sortOrder?: 'asc' | 'desc';
  filterChannel?: string;
  filterLevel?: string;
  searchQuery?: string;
}

export interface EventQueryResult {
  events: EventRecord[];
  totalCount: number;
}

// ============================================================================
// PUBLIC EVENTS API SURFACE
// Connects to the real backend via apiClient (uses Vite proxy → /api).
// ============================================================================

function mapBackendEvent(raw: any): EventRecord {
  const levelMap: Record<string, EventLevel> = {
    'Critical': 'Critical',
    'Error': 'Error',
    'Warning': 'Warning',
    'Information': 'Information',
    'Audit Success': 'Audit Success',
    'Audit Failure': 'Audit Failure',
    'LogAlways': 'Information',
    'Verbose': 'Information',
  };

  const levelText = raw.LevelText || raw.level_text || '';
  const level = levelMap[levelText] || 'Information';

  return {
    id: String(raw.RecordId ?? raw.id ?? 0),
    timeCreatedUtc: raw.TimeCreatedUtc || raw.time_created_utc || '',
    eventId: raw.EventId ?? raw.event_id ?? 0,
    channel: raw.Channel || raw.channel || '',
    provider: raw.Provider || raw.provider || '',
    level,
    computer: raw.Computer || raw.computer || '',
    userSid: raw.UserSid || raw.user_sid || '',
  };
}

const LEVEL_MAP: Record<string, number> = {
  'Critical': 1,
  'Error': 2,
  'Warning': 3,
  'Information': 4,
  'Audit Success': 0,
  'Audit Failure': 0,
};

function buildSearchParams(params: EventQueryParams): Record<string, string | number | undefined> {
  const sp: Record<string, string | number | undefined> = {};
  if (params.searchQuery && params.searchQuery.trim()) sp.q = params.searchQuery.trim();
  if (params.filterChannel && params.filterChannel !== 'ALL') sp.channel = params.filterChannel;
  if (params.filterLevel && params.filterLevel !== 'ALL') {
    const levelNum = LEVEL_MAP[params.filterLevel];
    if (levelNum !== undefined) sp.level = levelNum;
  }
  const pageSize = 50;
  const startRow = params.startRow ?? 0;
  sp.page = Math.floor(startRow / pageSize) + 1;
  sp.size = pageSize;
  return sp;
}

export const eventsApi = {
  /**
   * Queries Windows Event Logs via /api/search.
   * Uses server-side pagination, filtering, and FTS5 full-text search.
   */
  query: async (params: EventQueryParams): Promise<EventQueryResult> => {
    const sp = buildSearchParams(params);
    const data: any = await api.get('/search', { params: sp });
    return {
      events: (data.results || []).map(mapBackendEvent),
      totalCount: data.total ?? data.totalCount ?? 0,
    };
  },

  /**
   * Retrieves single event record detail by ID.
   */
  getById: async (id: string): Promise<EventRecord | null> => {
    try {
      const raw: any = await api.get(`/events/${id}`);
      return mapBackendEvent(raw);
    } catch {
      return null;
    }
  },

  /**
   * Exports events to a downloadable file via POST /api/export.
   * Returns the blob content and filename.
   */
  export: async (format: 'csv' | 'json' | 'jsonl', params: EventQueryParams): Promise<{ blob: Blob; fileName: string }> => {
    const body: Record<string, any> = { format };
    if (params.searchQuery && params.searchQuery.trim()) body.query = params.searchQuery.trim();
    if (params.filterChannel && params.filterChannel !== 'ALL') body.channel = params.filterChannel;
    if (params.filterLevel && params.filterLevel !== 'ALL') {
      const levelNum = LEVEL_MAP[params.filterLevel];
      if (levelNum !== undefined) body.level = levelNum;
    }
    return api.blob('/export', body);
  },
};
