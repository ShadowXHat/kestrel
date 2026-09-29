import React, { useState } from 'react';
import { DetectionAlert, SigmaRuleSeverity } from '../api/sigma';
import { EventRecord } from '../api/events';
import { Button } from './ui/Button';
import {
  ShieldAlert,
  Search,
  Filter,
  ExternalLink,
  ChevronDown,
  ChevronUp,
  Terminal,
  CheckCircle2,
  Clock,
  FileCode,
} from 'lucide-react';

interface DetectionPanelProps {
  alerts: DetectionAlert[];
  isLoading?: boolean;
  onRefresh?: () => void;
}

export const DetectionPanel: React.FC<DetectionPanelProps> = ({
  alerts,
  onRefresh,
}) => {
  const [searchQuery, setSearchQuery] = useState('');
  const [severityFilter, setSeverityFilter] = useState<string>('ALL');
  const [statusFilter, setStatusFilter] = useState<string>('ALL');
  const [expandedAlertId, setExpandedAlertId] = useState<string | null>(null);
  const [selectedEventSnippet, setSelectedEventSnippet] = useState<EventRecord | null>(null);

  // Filter logic
  const filteredAlerts = alerts.filter((alert) => {
    // Severity filter
    if (severityFilter !== 'ALL' && alert.severity.toLowerCase() !== severityFilter.toLowerCase()) {
      return false;
    }
    // Status filter
    if (statusFilter !== 'ALL' && alert.status !== statusFilter) {
      return false;
    }
    // Search query
    if (searchQuery.trim().length > 0) {
      const q = searchQuery.toLowerCase();
      const matchTitle = alert.ruleTitle.toLowerCase().includes(q);
      const matchCategory = alert.ruleCategory.toLowerCase().includes(q);
      const matchTech = alert.attckTechniques.some(
        (t) => t.id.toLowerCase().includes(q) || t.name.toLowerCase().includes(q) || t.tactic.toLowerCase().includes(q)
      );
      return matchTitle || matchCategory || matchTech;
    }
    return true;
  });

  const getSeverityBadge = (severity: SigmaRuleSeverity) => {
    switch (severity) {
      case 'critical':
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-mono font-semibold bg-rose-950/80 text-rose-300 border border-rose-500/40 shadow-[0_0_10px_rgba(244,63,94,0.2)]">
            CRITICAL
          </span>
        );
      case 'high':
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-mono font-semibold bg-amber-950/80 text-amber-300 border border-amber-500/40 shadow-[0_0_10px_rgba(251,191,36,0.2)]">
            HIGH
          </span>
        );
      case 'medium':
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-mono font-semibold bg-orange-950/80 text-orange-300 border border-orange-500/40">
            MEDIUM
          </span>
        );
      case 'low':
      case 'informational':
      default:
        return (
          <span className="px-2.5 py-0.5 rounded-full text-[11px] font-mono font-semibold bg-sky-950/80 text-sky-300 border border-sky-500/40">
            LOW
          </span>
        );
    }
  };

  const toggleExpandAlert = (alertId: string) => {
    if (expandedAlertId === alertId) {
      setExpandedAlertId(null);
      setSelectedEventSnippet(null);
    } else {
      setExpandedAlertId(alertId);
      const targetAlert = alerts.find((a) => a.id === alertId);
      if (targetAlert && targetAlert.matchedEvents.length > 0) {
        setSelectedEventSnippet(targetAlert.matchedEvents[0]);
      }
    }
  };

  return (
    <div className="space-y-4">
      {/* Filters & Search Toolbar */}
      <div className="glass-panel p-3.5 rounded-xl border border-kestrel-border flex flex-col md:flex-row items-center justify-between gap-3">
        {/* Search */}
        <div className="relative w-full md:w-80">
          <Search className="w-4 h-4 text-kestrel-subtle absolute left-3 top-2.5" />
          <input
            type="text"
            placeholder="Search rule title, technique ID (e.g. T1059)..."
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            className="w-full bg-[#0b1019] text-xs text-white placeholder-kestrel-subtle/70 pl-9 pr-3 py-2 rounded-lg border border-kestrel-border focus-ring font-mono"
          />
        </div>

        {/* Severity & Status Dropdowns */}
        <div className="flex items-center gap-3 w-full md:w-auto">
          <div className="flex items-center gap-2">
            <Filter className="w-3.5 h-3.5 text-kestrel-subtle shrink-0" />
            <select
              value={severityFilter}
              onChange={(e) => setSeverityFilter(e.target.value)}
              className="bg-[#0b1019] text-xs text-white py-2 px-2.5 rounded-lg border border-kestrel-border focus-ring font-mono"
            >
              <option value="ALL">All Severities</option>
              <option value="critical">Critical</option>
              <option value="high">High</option>
              <option value="medium">Medium</option>
              <option value="low">Low / Info</option>
            </select>
          </div>

          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="bg-[#0b1019] text-xs text-white py-2 px-2.5 rounded-lg border border-kestrel-border focus-ring font-mono"
          >
            <option value="ALL">All Statuses</option>
            <option value="new">New</option>
            <option value="investigating">Investigating</option>
            <option value="resolved">Resolved</option>
            <option value="false_positive">False Positive</option>
          </select>
        </div>
      </div>

      {/* Alert Results Count Header */}
      <div className="flex items-center justify-between px-1">
        <div className="flex items-center gap-2">
          <ShieldAlert className="w-4 h-4 text-sky-400" />
          <h3 className="text-sm font-bold text-white font-sans">
            Detection Alerts Grid ({filteredAlerts.length})
          </h3>
        </div>

        {onRefresh && (
          <Button variant="ghost" size="sm" onClick={onRefresh} className="text-xs text-sky-400 hover:text-sky-300">
            Re-run Engine
          </Button>
        )}
      </div>

      {/* Grid Table Container */}
      <div className="space-y-3">
        {filteredAlerts.length === 0 ? (
          <div className="glass-panel p-12 rounded-xl text-center border border-kestrel-border/80 space-y-3">
            <div className="w-12 h-12 rounded-xl bg-sky-950/60 border border-sky-500/30 mx-auto flex items-center justify-center text-sky-400 shadow-inner">
              <CheckCircle2 className="w-6 h-6" />
            </div>
            <h4 className="text-sm font-semibold text-white font-sans">No Detection Alerts Found</h4>
            <p className="text-xs text-kestrel-muted max-w-md mx-auto leading-relaxed">
              No Sigma detection rules matched the specified filter criteria. Click "Run Detection Engine" above to trigger a fresh scan against stored EVTX logs.
            </p>
          </div>
        ) : (
          filteredAlerts.map((alert) => {
            const isExpanded = expandedAlertId === alert.id;
            return (
              <div
                key={alert.id}
                className={`glass-card rounded-xl border transition-all duration-200 overflow-hidden ${
                  isExpanded
                    ? 'border-sky-500/50 bg-[#0d1424] shadow-[0_0_20px_rgba(14,165,233,0.15)]'
                    : 'border-kestrel-border/80 hover:border-sky-500/30 hover:bg-kestrel-surface/60'
                }`}
              >
                {/* Alert Card Header Row */}
                <div className="p-4 flex flex-col lg:flex-row lg:items-center justify-between gap-4">
                  {/* Left info block */}
                  <div className="space-y-2 flex-1 min-w-0">
                    <div className="flex flex-wrap items-center gap-2.5">
                      {getSeverityBadge(alert.severity)}
                      <h4 className="text-sm font-bold text-white font-sans truncate tracking-tight">
                        {alert.ruleTitle}
                      </h4>
                      <span className="text-[10px] font-mono px-2 py-0.5 rounded bg-sky-950/60 text-sky-300 border border-sky-500/30 uppercase">
                        {alert.ruleCategory.replace('_', ' ')}
                      </span>
                    </div>

                    {/* Technique ID badges */}
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="text-[11px] font-mono text-kestrel-subtle">ATT&CK:</span>
                      {alert.attckTechniques.length === 0 ? (
                        <span className="text-[11px] font-mono text-kestrel-subtle">None tagged</span>
                      ) : (
                        alert.attckTechniques.map((tech) => (
                          <a
                            key={tech.id}
                            href={tech.url}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1 text-[11px] font-mono font-semibold bg-[#070a0f] text-amber-300 hover:text-amber-200 px-2 py-0.5 rounded border border-amber-500/30 hover:border-amber-400 transition-colors"
                            title={`${tech.tactic}: ${tech.name}`}
                          >
                            <span>{tech.id}</span>
                            <span className="text-[10px] text-amber-400/80 font-sans">({tech.name})</span>
                            <ExternalLink className="w-2.5 h-2.5 shrink-0" />
                          </a>
                        ))
                      )}
                    </div>
                  </div>

                  {/* Right metrics & expand toggle */}
                  <div className="flex items-center justify-between lg:justify-end gap-6 shrink-0 border-t lg:border-t-0 pt-3 lg:pt-0 border-kestrel-border/40">
                    {/* Matching Event Count */}
                    <div className="text-right">
                      <div className="text-[10px] font-mono uppercase text-kestrel-subtle tracking-wider">
                        Events Matched
                      </div>
                      <div className="text-base font-bold font-mono text-sky-400">
                        {alert.matchingEventCount.toLocaleString()}
                      </div>
                    </div>

                    {/* Time Window */}
                    <div className="text-right hidden sm:block">
                      <div className="text-[10px] font-mono uppercase text-kestrel-subtle tracking-wider flex items-center justify-end gap-1">
                        <Clock className="w-3 h-3 text-slate-400" />
                        Last Seen
                      </div>
                      <div className="text-xs font-mono text-slate-300">
                        {new Date(alert.lastSeenUtc).toISOString().substring(11, 19)} UTC
                      </div>
                    </div>

                    {/* Expand Details Trigger Button */}
                    <Button
                      variant={isExpanded ? 'primary' : 'secondary'}
                      size="sm"
                      onClick={() => toggleExpandAlert(alert.id)}
                      rightIcon={
                        isExpanded ? (
                          <ChevronUp className="w-3.5 h-3.5" />
                        ) : (
                          <ChevronDown className="w-3.5 h-3.5" />
                        )
                      }
                      className="font-mono text-xs"
                    >
                      {isExpanded ? 'Hide Payload' : 'Inspect Events'}
                    </Button>
                  </div>
                </div>

                {/* Expanded Payload & Event Inspector */}
                {isExpanded && (
                  <div className="border-t border-kestrel-border/80 bg-[#070a0f]/90 p-4 space-y-4 animate-in fade-in duration-150">
                    <div className="flex items-center justify-between border-b border-kestrel-border/50 pb-2">
                      <div className="flex items-center gap-2">
                        <Terminal className="w-4 h-4 text-emerald-400" />
                        <h5 className="text-xs font-bold text-white font-mono">
                          Matched Windows Event Samples ({alert.matchedEvents.length} events)
                        </h5>
                      </div>
                      <span className="text-[11px] font-mono text-kestrel-subtle">
                        Rule ID: {alert.ruleId}
                      </span>
                    </div>

                    {/* Events List Selector */}
                    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                      {/* Left: Event Selection List */}
                      <div className="space-y-1.5 overflow-y-auto max-h-56 pr-1">
                        {alert.matchedEvents.map((evt) => {
                          const isSelected = selectedEventSnippet?.id === evt.id;
                          return (
                            <button
                              key={evt.id}
                              onClick={() => setSelectedEventSnippet(evt)}
                              className={`w-full text-left p-2.5 rounded-lg border transition-all text-xs font-mono flex items-center justify-between ${
                                isSelected
                                  ? 'bg-sky-950/70 border-sky-500/50 text-white font-semibold'
                                  : 'bg-[#0b1019] border-kestrel-border/70 text-slate-300 hover:bg-kestrel-surface'
                              }`}
                            >
                              <div className="space-y-0.5 truncate">
                                <div className="text-sky-300 font-bold">Event ID #{evt.eventId}</div>
                                <div className="text-[10px] text-kestrel-subtle truncate">{evt.computer}</div>
                              </div>
                              <span className="text-[10px] text-slate-400">
                                {new Date(evt.timeCreatedUtc).toISOString().substring(11, 19)}
                              </span>
                            </button>
                          );
                        })}
                      </div>

                      {/* Right: Selected Event Detail Cards */}
                      {selectedEventSnippet ? (
                        <div className="md:col-span-2 space-y-3 bg-[#0b1019] p-3.5 rounded-xl border border-kestrel-border/80 text-xs font-mono">
                          <div className="grid grid-cols-2 gap-3 text-[11px]">
                            <div>
                              <span className="text-kestrel-subtle block">Time Created (UTC):</span>
                              <span className="text-white font-semibold">{selectedEventSnippet.timeCreatedUtc}</span>
                            </div>
                            <div>
                              <span className="text-kestrel-subtle block">Computer Host:</span>
                              <span className="text-emerald-300 font-semibold">{selectedEventSnippet.computer}</span>
                            </div>
                            <div>
                              <span className="text-kestrel-subtle block">Channel / Provider:</span>
                              <span className="text-slate-300">{selectedEventSnippet.channel}</span>
                            </div>
                            <div>
                              <span className="text-kestrel-subtle block">Target User:</span>
                              <span className="text-sky-300">{selectedEventSnippet.username || selectedEventSnippet.userSid}</span>
                            </div>
                          </div>

                          {selectedEventSnippet.message && (
                            <div className="space-y-1 pt-2 border-t border-kestrel-border/50">
                              <span className="text-[10px] uppercase font-semibold text-kestrel-subtle tracking-wider">
                                Message & Command Payload:
                              </span>
                              <div className="p-2.5 bg-[#070a0f] rounded-lg border border-kestrel-border/80 text-amber-200/90 whitespace-pre-wrap font-mono text-[11px] leading-relaxed max-h-32 overflow-y-auto">
                                {selectedEventSnippet.message}
                              </div>
                            </div>
                          )}

                          {selectedEventSnippet.rawXml && (
                            <div className="space-y-1">
                              <span className="text-[10px] uppercase font-semibold text-kestrel-subtle tracking-wider flex items-center gap-1">
                                <FileCode className="w-3 h-3 text-sky-400" />
                                Raw XML Snippet:
                              </span>
                              <pre className="p-2.5 bg-[#070a0f] rounded-lg border border-kestrel-border text-sky-400/90 text-[10px] overflow-x-auto max-h-28">
                                {selectedEventSnippet.rawXml}
                              </pre>
                            </div>
                          )}
                        </div>
                      ) : (
                        <div className="md:col-span-2 flex items-center justify-center p-6 text-xs text-kestrel-subtle font-mono">
                          Select an event from the list on the left to inspect detailed payload snippets.
                        </div>
                      )}
                    </div>
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
};

export default DetectionPanel;
