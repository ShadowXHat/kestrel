import React from 'react';
import { useLocation, Outlet } from 'react-router-dom';
import { Sidebar } from './Sidebar';
import { UserMenu } from './UserMenu';
import { useSignalR } from '../../hooks/useSignalR';

const SECTION_TITLES: Record<string, string> = {
  '/': 'Dashboard Overview',
  '/events': 'Event Log Browser',
  '/ingest': 'EVTX Ingestion Pipeline',
  '/detection': 'Sigma Threat Detection Engine',
  '/rules': 'Sigma Rule CRUD & YAML Manager',
  '/alerts': 'Threat Alerts & Detections',
  '/users': 'User Administration (RBAC)',
  '/showcase': 'Design System Foundation Showcase',
};

const statusDotColors = {
  connected: 'bg-emerald-400 shadow-[0_0_6px_#34d399]',
  reconnecting: 'bg-amber-400 shadow-[0_0_6px_#fbbf24] animate-pulse',
  disconnected: 'bg-rose-500 shadow-[0_0_6px_#f43f5e]',
};

const statusLabels = {
  connected: 'Connected',
  reconnecting: 'Reconnecting...',
  disconnected: 'Offline',
};

export const AppShell: React.FC = () => {
  const location = useLocation();
  const { status: socketStatus } = useSignalR({
    hubUrl: '/hubs/events',
    autoSubscribeGroup: 'ingest-jobs',
  });

  const sectionTitle = SECTION_TITLES[location.pathname] || 'Kestrel Incident Response';

  return (
    <div className="min-h-screen bg-kestrel-base text-kestrel-text flex overflow-hidden">
      {/* Fixed Left Sidebar */}
      <Sidebar />

      {/* Main Container */}
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        {/* Top Bar */}
        <header className="h-14 bg-kestrel-panel/90 backdrop-blur-md border-b border-kestrel-border/80 px-6 flex items-center justify-between shrink-0 select-none z-30">
          {/* Section Title */}
          <div className="flex items-center gap-3">
            <div className="w-2 h-2 rounded-full bg-sky-400 shadow-[0_0_8px_#38bdf8]" />
            <h1 className="text-sm font-semibold tracking-tight text-white font-sans">
              {sectionTitle}
            </h1>
          </div>

          {/* Connection Indicator + User Menu */}
          <div className="flex items-center gap-4">
            {/* Connection status indicator slot */}
            <div
              className="flex items-center gap-2 px-3 py-1 rounded-full bg-[#0b1019] border border-kestrel-border/70 text-xs font-mono select-none"
              title="Real-time event stream connection (/hubs/events)"
            >
              <span className={`w-2 h-2 rounded-full shrink-0 ${statusDotColors[socketStatus]}`} />
              <span className="text-kestrel-muted text-[11px] font-medium">
                {statusLabels[socketStatus]}
              </span>
            </div>

            {/* Top Right User Menu */}
            <UserMenu />
          </div>
        </header>

        {/* Scrollable Main Content */}
        <main className="flex-1 overflow-y-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
};
