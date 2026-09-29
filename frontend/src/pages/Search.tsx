import React, { useState, useCallback } from 'react';
import { eventsApi, EventRecord } from '../api/events';
import { Button } from '../components/ui/Button';
import { Search, Shield } from 'lucide-react';

export const SearchPage: React.FC = () => {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<EventRecord[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleSearch = useCallback(async () => {
    if (!query.trim()) return;
    setIsLoading(true);
    setError(null);
    try {
      const res = await eventsApi.query({ searchQuery: query });
      setResults(res.events);
      setTotalCount(res.totalCount);
    } catch (err) {
      setError('Search failed. Make sure the backend is running.');
      console.error(err);
    } finally {
      setIsLoading(false);
    }
  }, [query]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleSearch();
  };

  return (
    <div className="p-6 md:p-8 max-w-[1200px] mx-auto space-y-6 flex flex-col h-[calc(100vh-4rem)]">
      <h1 className="text-2xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
        <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/30 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.25)]">
          <Search className="w-4.5 h-4.5" />
        </div>
        FTS5 Event Search
      </h1>

      <div className="flex gap-3">
        <input
          type="text"
          placeholder="Search events by keyword, channel, computer..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={handleKeyDown}
          className="flex-1 max-w-xl bg-[#0b1019] text-xs text-white placeholder-kestrel-subtle/70 px-4 py-2.5 rounded-lg border border-kestrel-border focus-ring font-mono"
        />
        <Button
          variant="primary"
          size="sm"
          onClick={handleSearch}
          isLoading={isLoading}
          leftIcon={<Search className="w-3.5 h-3.5" />}
        >
          Search
        </Button>
      </div>

      {error && (
        <div className="bg-rose-950/50 border border-rose-500/30 rounded-xl p-3 text-xs text-rose-300">
          {error}
        </div>
      )}

      {totalCount > 0 && (
        <div className="text-xs font-mono text-kestrel-muted">
          <strong className="text-sky-400">{totalCount.toLocaleString()}</strong> results found
        </div>
      )}

      {results.length > 0 ? (
        <div className="flex-1 min-h-0 overflow-auto glass-panel rounded-xl border border-kestrel-border">
          <table className="w-full text-left text-xs font-mono">
            <thead className="bg-[#070a0f] text-kestrel-muted uppercase tracking-wider font-semibold text-[11px] border-b border-kestrel-border sticky top-0">
              <tr>
                <th className="py-3 px-4">Time Created</th>
                <th className="py-3 px-4">Event ID</th>
                <th className="py-3 px-4">Channel</th>
                <th className="py-3 px-4">Provider</th>
                <th className="py-3 px-4">Level</th>
                <th className="py-3 px-4">Computer</th>
                <th className="py-3 px-4">User SID</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-kestrel-border/60">
              {results.map((event) => (
                <tr key={event.id} className="hover:bg-kestrel-surface/60 transition-colors">
                  <td className="py-2.5 px-4 text-kestrel-muted">{event.timeCreatedUtc}</td>
                  <td className="py-2.5 px-4 text-sky-400">{event.eventId}</td>
                  <td className="py-2.5 px-4">{event.channel}</td>
                  <td className="py-2.5 px-4 text-slate-300">{event.provider}</td>
                  <td className="py-2.5 px-4">{event.level}</td>
                  <td className="py-2.5 px-4 text-sky-300">{event.computer}</td>
                  <td className="py-2.5 px-4 text-kestrel-subtle">{event.userSid}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : !isLoading && query && (
        <div className="flex-1 flex items-center justify-center glass-panel rounded-xl border border-kestrel-border">
          <div className="text-center space-y-3">
            <Shield className="w-8 h-8 text-kestrel-subtle mx-auto" />
            <p className="text-kestrel-muted text-sm">No results found for "{query}"</p>
            <p className="text-kestrel-subtle text-xs">Try a different keyword or check that events have been ingested</p>
          </div>
        </div>
      )}
    </div>
  );
};

export default SearchPage;
