import React, { useState, useRef, useCallback, useEffect } from 'react';
import { AgGridReact } from 'ag-grid-react';
import {
  ColDef,
  IDatasource,
  IGetRowsParams,
  GridReadyEvent,
  GridApi,
  RowDoubleClickedEvent,
} from 'ag-grid-community';

import 'ag-grid-community/styles/ag-grid.css';
import 'ag-grid-community/styles/ag-theme-alpine.css';

import { eventsApi, EventRecord, EventLevel } from '../api/events';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import {
  Database,
  RefreshCw,
  Search,
  Filter,
  X,
  Copy,
  Check,
  FileCode,
  Terminal,
  Server,
  User,
  Shield,
  Calendar,
  Download,
} from 'lucide-react';

export const EventsPage: React.FC = () => {
  const gridApiRef = useRef<GridApi | null>(null);

  // Filter & Search Controls State
  const [searchQuery, setSearchQuery] = useState('');
  const [filterChannel, setFilterChannel] = useState('ALL');
  const [filterLevel, setFilterLevel] = useState('ALL');
  const [selectedEvent, setSelectedEvent] = useState<EventRecord | null>(null);
  const [copied, setCopied] = useState(false);
  const [totalRecordsCount, setTotalRecordsCount] = useState<number | null>(null);

  // Severity Level Badge Renderer
  const levelCellRenderer = useCallback((params: any) => {
    const level: EventLevel = params.value;
    switch (level) {
      case 'Critical':
        return <Badge severity="critical">Critical</Badge>;
      case 'Error':
        return <Badge severity="critical">Error</Badge>;
      case 'Warning':
        return (
          <Badge severity="medium" className="bg-amber-950/80 text-amber-300 border-amber-500/40">
            Warning
          </Badge>
        );
      case 'Audit Failure':
        return (
          <Badge severity="medium" className="bg-orange-950/80 text-orange-300 border-orange-500/40">
            Audit Failure
          </Badge>
        );
      case 'Audit Success':
        return <Badge severity="low">Audit Success</Badge>;
      case 'Information':
      default:
        return <Badge severity="info">Information</Badge>;
    }
  }, []);

  // Event ID Cell Renderer
  const eventIdCellRenderer = useCallback((params: any) => {
    if (!params.value) return '—';
    return (
      <span className="font-mono font-bold text-sky-400 bg-sky-950/60 px-2 py-0.5 rounded border border-sky-500/30 text-xs">
        {params.value}
      </span>
    );
  }, []);

  // Timestamp Cell Renderer
  const timestampCellRenderer = useCallback((params: any) => {
    if (!params.value) return '—';
    try {
      return (
        <span className="font-mono text-xs text-kestrel-muted">
          {new Date(params.value).toISOString().replace('T', ' ').substring(0, 19)}
        </span>
      );
    } catch {
      return params.value;
    }
  }, []);

  // Monospace Cell Renderer for User SID & Computer
  const monoCellRenderer = useCallback((params: any) => {
    if (!params.value) return <span className="text-kestrel-subtle">—</span>;
    return <span className="font-mono text-xs text-slate-300">{params.value}</span>;
  }, []);

  // AG Grid Column Definitions
  const columnDefs: ColDef[] = [
    {
      headerName: 'Time Created (UTC)',
      field: 'timeCreatedUtc',
      cellRenderer: timestampCellRenderer,
      sortable: true,
      filter: true,
      width: 185,
      pinned: 'left',
    },
    {
      headerName: 'Event ID',
      field: 'eventId',
      cellRenderer: eventIdCellRenderer,
      sortable: true,
      filter: true,
      width: 110,
    },
    {
      headerName: 'Channel',
      field: 'channel',
      sortable: true,
      filter: true,
      width: 220,
    },
    {
      headerName: 'Provider',
      field: 'provider',
      sortable: true,
      filter: true,
      width: 240,
    },
    {
      headerName: 'Level',
      field: 'level',
      cellRenderer: levelCellRenderer,
      sortable: true,
      filter: true,
      width: 140,
    },
    {
      headerName: 'Computer',
      field: 'computer',
      cellRenderer: monoCellRenderer,
      sortable: true,
      filter: true,
      width: 200,
    },
    {
      headerName: 'User SID',
      field: 'userSid',
      cellRenderer: monoCellRenderer,
      sortable: true,
      filter: true,
      width: 240,
    },
  ];

  // AG Grid Datasource Configuration (Infinite / Server-side Row Model)
  const createDataSource = useCallback(
    (): IDatasource => ({
      rowCount: undefined,
      getRows: async (params: IGetRowsParams) => {
        try {
          const sortCol = params.sortModel?.[0];
          const sortField = sortCol?.colId;
          const sortOrder = sortCol?.sort as 'asc' | 'desc';

          const res = await eventsApi.query({
            startRow: params.startRow,
            endRow: params.endRow,
            sortField,
            sortOrder,
            filterChannel,
            filterLevel,
            searchQuery,
          });

          setTotalRecordsCount(res.totalCount);
          params.successCallback(res.events, res.totalCount);
        } catch (err) {
          console.error('Failed to fetch events:', err);
          params.failCallback();
        }
      },
    }),
    [filterChannel, filterLevel, searchQuery]
  );

  const onGridReady = (params: GridReadyEvent) => {
    gridApiRef.current = params.api;
    const dataSource = createDataSource();
    if (params.api.setGridOption) {
      params.api.setGridOption('datasource', dataSource);
    } else if ((params.api as any).setDatasource) {
      (params.api as any).setDatasource(dataSource);
    }
  };

  // Re-fetch grid when filters or search query changes
  useEffect(() => {
    if (gridApiRef.current) {
      const dataSource = createDataSource();
      if (gridApiRef.current.setGridOption) {
        gridApiRef.current.setGridOption('datasource', dataSource);
      } else if ((gridApiRef.current as any).setDatasource) {
        (gridApiRef.current as any).setDatasource(dataSource);
      }
      gridApiRef.current.purgeInfiniteCache();
    }
  }, [createDataSource]);

  const handleRowClicked = (event: any) => {
    if (event.data) {
      setSelectedEvent(event.data);
    }
  };

  const handleCopyXml = () => {
    if (!selectedEvent?.rawXml) return;
    navigator.clipboard.writeText(selectedEvent.rawXml);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="p-6 md:p-8 max-w-[1600px] mx-auto space-y-6 flex flex-col h-[calc(100vh-4rem)]">
      {/* Header & Controls Bar */}
      <div className="space-y-4 shrink-0">
        <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
          <div>
            <h2 className="text-xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
              <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/30 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.25)]">
                <Database className="w-4.5 h-4.5" />
              </div>
              <span>Event Log Browser</span>
            </h2>
            <p className="text-xs text-kestrel-muted mt-1">
              High-throughput EVTX event browser powered by AG Grid with server-side pagination, sorting, and filtering.
            </p>
          </div>

          <div className="flex items-center gap-3">
            {totalRecordsCount !== null && (
              <span className="text-xs font-mono text-kestrel-muted bg-[#0b1019] px-3.5 py-1.5 rounded-lg border border-kestrel-border/80">
                Matched Events: <strong className="text-sky-400 font-semibold">{totalRecordsCount.toLocaleString()}</strong>
              </span>
            )}
            <Button
              variant="secondary"
              size="sm"
              onClick={() => {
                if (gridApiRef.current) {
                  gridApiRef.current.purgeInfiniteCache();
                }
              }}
              leftIcon={<RefreshCw className="w-3.5 h-3.5" />}
            >
              Refresh Grid
            </Button>
            <Button
              variant="secondary"
              size="sm"
              onClick={async () => {
                try {
                  const { blob, fileName } = await eventsApi.export('csv', {
                    searchQuery, filterChannel, filterLevel,
                  });
                  const url = URL.createObjectURL(blob);
                  const a = document.createElement('a');
                  a.href = url; a.download = fileName; a.click();
                  URL.revokeObjectURL(url);
                } catch (err) {
                  console.error('Export failed:', err);
                }
              }}
              leftIcon={<Download className="w-3.5 h-3.5" />}
            >
              Export CSV
            </Button>
          </div>
        </div>

        {/* Filters Toolbar */}
        <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-4 gap-3 glass-panel p-3.5 rounded-xl border border-kestrel-border">
          {/* Search Input */}
          <div className="relative">
            <Search className="w-4 h-4 text-kestrel-subtle absolute left-3 top-2.5" />
            <input
              type="text"
              placeholder="Search Event ID, computer, user..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              className="w-full bg-[#0b1019] text-xs text-white placeholder-kestrel-subtle/70 pl-9 pr-3 py-2 rounded-lg border border-kestrel-border focus-ring font-mono"
            />
          </div>

          {/* Channel Dropdown */}
          <div className="flex items-center gap-2">
            <Filter className="w-3.5 h-3.5 text-kestrel-subtle shrink-0" />
            <select
              value={filterChannel}
              onChange={(e) => setFilterChannel(e.target.value)}
              className="w-full bg-[#0b1019] text-xs text-white py-2 px-2.5 rounded-lg border border-kestrel-border focus-ring font-mono"
            >
              <option value="ALL">All Channels</option>
              <option value="Security">Security</option>
              <option value="System">System</option>
              <option value="Microsoft-Windows-Sysmon/Operational">Sysmon</option>
              <option value="Application">Application</option>
              <option value="Microsoft-Windows-PowerShell/Operational">PowerShell</option>
            </select>
          </div>

          {/* Level Dropdown */}
          <div className="flex items-center gap-2">
            <select
              value={filterLevel}
              onChange={(e) => setFilterLevel(e.target.value)}
              className="w-full bg-[#0b1019] text-xs text-white py-2 px-2.5 rounded-lg border border-kestrel-border focus-ring font-mono"
            >
              <option value="ALL">All Severity Levels</option>
              <option value="Critical">Critical</option>
              <option value="Error">Error</option>
              <option value="Warning">Warning</option>
              <option value="Audit Failure">Audit Failure</option>
              <option value="Audit Success">Audit Success</option>
              <option value="Information">Information</option>
            </select>
          </div>

          {/* Reset Filters */}
          <div className="flex items-center justify-end">
            {(searchQuery || filterChannel !== 'ALL' || filterLevel !== 'ALL') && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => {
                  setSearchQuery('');
                  setFilterChannel('ALL');
                  setFilterLevel('ALL');
                }}
                leftIcon={<X className="w-3.5 h-3.5" />}
                className="text-xs text-kestrel-subtle hover:text-white"
              >
                Clear Filters
              </Button>
            )}
          </div>
        </div>
      </div>

      {/* Main Content Area (AG Grid + Detail Inspector Drawer) */}
      <div className="flex-1 min-h-0 flex gap-4">
        {/* AG Grid Container */}
        <div className="flex-1 ag-theme-alpine-dark rounded-xl border border-kestrel-border overflow-hidden bg-kestrel-panel shadow-2xl">
          <AgGridReact
            columnDefs={columnDefs}
            rowModelType="infinite"
            datasource={createDataSource()}
            cacheBlockSize={50}
            maxBlocksInCache={10}
            infiniteInitialRowCount={50}
            onGridReady={onGridReady}
            onRowClicked={handleRowClicked}
            onRowDoubleClicked={(e: RowDoubleClickedEvent) => {
              if (e.data) setSelectedEvent(e.data);
            }}
            rowSelection="single"
            headerHeight={38}
            rowHeight={38}
            overlayLoadingTemplate={
              '<span class="ag-overlay-loading-center text-xs font-mono text-sky-300">Streaming Windows Event Logs...</span>'
            }
            overlayNoRowsTemplate={
              '<span class="ag-overlay-no-rows-center text-xs font-mono text-kestrel-muted">No matching Windows Event Log records found.</span>'
            }
          />
        </div>

        {/* Selected Event Detail Drawer / Inspector */}
        {selectedEvent && (
          <div className="w-96 glass-panel glow-border rounded-xl flex flex-col shrink-0 overflow-hidden shadow-2xl animate-in slide-in-from-right duration-200">
            {/* Drawer Header */}
            <div className="p-4 border-b border-kestrel-border/80 bg-kestrel-surface/60 flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Terminal className="w-4 h-4 text-sky-400" />
                <h3 className="text-sm font-bold text-white font-sans">
                  Event #{selectedEvent.eventId} Detail
                </h3>
              </div>
              <button
                onClick={() => setSelectedEvent(null)}
                className="p-1 text-kestrel-subtle hover:text-white rounded hover:bg-kestrel-base transition-colors"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Drawer Body */}
            <div className="p-4 flex-1 overflow-y-auto space-y-4 text-xs font-mono">
              {/* Properties Grid */}
              <div className="space-y-2 bg-[#0b1019] p-3 rounded-lg border border-kestrel-border/80">
                <div className="flex justify-between items-center">
                  <span className="text-kestrel-subtle flex items-center gap-1.5">
                    <Calendar className="w-3.5 h-3.5 text-sky-400" />
                    Time Created:
                  </span>
                  <span className="text-white font-semibold">{selectedEvent.timeCreatedUtc}</span>
                </div>
                <div className="flex justify-between items-center">
                  <span className="text-kestrel-subtle flex items-center gap-1.5">
                    <Shield className="w-3.5 h-3.5 text-amber-400" />
                    Severity Level:
                  </span>
                  <span>{levelCellRenderer({ value: selectedEvent.level })}</span>
                </div>
                <div className="flex justify-between items-center">
                  <span className="text-kestrel-subtle flex items-center gap-1.5">
                    <Server className="w-3.5 h-3.5 text-emerald-400" />
                    Computer Host:
                  </span>
                  <span className="text-sky-300 font-semibold">{selectedEvent.computer}</span>
                </div>
                <div className="flex justify-between items-center">
                  <span className="text-kestrel-subtle flex items-center gap-1.5">
                    <User className="w-3.5 h-3.5 text-purple-400" />
                    Target User:
                  </span>
                  <span className="text-white">{selectedEvent.username || selectedEvent.userSid}</span>
                </div>
              </div>

              {/* Event Metadata */}
              <div className="space-y-1.5 p-3 bg-[#0b1019] rounded-lg border border-kestrel-border/80">
                <div className="flex justify-between">
                  <span className="text-kestrel-subtle">Channel:</span>
                  <span className="text-sky-300">{selectedEvent.channel}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-kestrel-subtle">Provider:</span>
                  <span className="text-slate-300">{selectedEvent.provider}</span>
                </div>
                {selectedEvent.taskCategory && (
                  <div className="flex justify-between">
                    <span className="text-kestrel-subtle">Task Category:</span>
                    <span className="text-amber-300">{selectedEvent.taskCategory}</span>
                  </div>
                )}
              </div>

              {/* Event Message Payload */}
              {selectedEvent.message && (
                <div className="space-y-1">
                  <span className="text-[10px] uppercase font-semibold text-kestrel-subtle tracking-wider block">
                    Message Payload
                  </span>
                  <div className="p-3 bg-[#0b1019] rounded-lg border border-kestrel-border text-slate-200 whitespace-pre-wrap font-sans text-[11px] leading-relaxed">
                    {selectedEvent.message}
                  </div>
                </div>
              )}

              {/* Raw XML Snippet */}
              {selectedEvent.rawXml && (
                <div className="space-y-1.5">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] uppercase font-semibold text-kestrel-subtle tracking-wider flex items-center gap-1">
                      <FileCode className="w-3 h-3 text-emerald-400" />
                      Raw Event XML
                    </span>
                    <button
                      onClick={handleCopyXml}
                      className="text-[10px] text-sky-400 hover:underline flex items-center gap-1"
                    >
                      {copied ? (
                        <>
                          <Check className="w-3 h-3 text-emerald-400" />
                          <span>Copied</span>
                        </>
                      ) : (
                        <>
                          <Copy className="w-3 h-3" />
                          <span>Copy XML</span>
                        </>
                      )}
                    </button>
                  </div>
                  <pre className="p-3 bg-[#0b1019] rounded-lg border border-kestrel-border text-emerald-400 overflow-x-auto text-[10px] leading-normal font-mono max-h-48">
                    {selectedEvent.rawXml}
                  </pre>
                </div>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
};

export default EventsPage;
