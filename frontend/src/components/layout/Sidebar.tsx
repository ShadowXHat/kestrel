import React, { useState } from 'react';
import { NavLink } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { hasRole, RoleType } from '../../utils/roleUtils';
import {
  LayoutDashboard,
  Database,
  Upload,
  ShieldAlert,
  Sliders,
  FileCode,
  Users,
  ChevronLeft,
  ChevronRight,
  Palette,
} from 'lucide-react';

export interface SidebarItem {
  label: string;
  path: string;
  icon: React.ReactNode;
  minRole: RoleType;
  section?: 'main' | 'admin' | 'dev';
}

const SIDEBAR_ITEMS: SidebarItem[] = [
  {
    label: 'Dashboard',
    path: '/',
    icon: <LayoutDashboard className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'main',
  },
  {
    label: 'Events',
    path: '/events',
    icon: <Database className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'main',
  },
  {
    label: 'Ingest',
    path: '/ingest',
    icon: <Upload className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'main',
  },
  {
    label: 'Detection',
    path: '/detection',
    icon: <ShieldAlert className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'main',
  },
  {
    label: 'Rules Manager',
    path: '/rules',
    icon: <Sliders className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'main',
  },
  {
    label: 'Alerts',
    path: '/alerts',
    icon: <FileCode className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'main',
  },
  {
    label: 'Users',
    path: '/users',
    icon: <Users className="w-4 h-4 shrink-0" />,
    minRole: 'Admin',
    section: 'admin',
  },
  {
    label: 'UI Showcase',
    path: '/showcase',
    icon: <Palette className="w-4 h-4 shrink-0" />,
    minRole: 'Viewer',
    section: 'dev',
  },
];

export const Sidebar: React.FC = () => {
  const [isCollapsed, setIsCollapsed] = useState(false);
  const { roles } = useAuth();

  const mainItems = SIDEBAR_ITEMS.filter(
    (item) => item.section === 'main' && hasRole(roles, item.minRole)
  );
  const adminItems = SIDEBAR_ITEMS.filter(
    (item) => item.section === 'admin' && hasRole(roles, item.minRole)
  );
  const devItems = SIDEBAR_ITEMS.filter(
    (item) => item.section === 'dev' && hasRole(roles, item.minRole)
  );

  return (
    <aside
      className={`relative bg-kestrel-panel/95 backdrop-blur-md border-r border-kestrel-border flex flex-col transition-all duration-200 ease-in-out shrink-0 select-none z-40 ${
        isCollapsed ? 'w-16' : 'w-60'
      }`}
    >
      {/* Brand Header */}
      <div className="h-14 px-4 flex items-center justify-between border-b border-kestrel-border/80">
        {!isCollapsed && (
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/40 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.3)]">
              <ShieldAlert className="w-4.5 h-4.5" />
            </div>
            <span className="font-bold text-base tracking-tight text-white font-sans flex items-center gap-1.5">
              Kestrel
              <span className="text-[10px] font-mono font-medium px-1.5 py-0.5 rounded-md bg-sky-950/80 text-sky-400 border border-sky-500/30">
                SOC
              </span>
            </span>
          </div>
        )}

        {isCollapsed && (
          <div className="mx-auto w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/40 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.3)]">
            <ShieldAlert className="w-4.5 h-4.5" />
          </div>
        )}
      </div>

      {/* Navigation Sections */}
      <nav className="flex-1 p-3 space-y-6 overflow-y-auto">
        {/* Main Section */}
        <div className="space-y-1">
          {!isCollapsed && (
            <p className="px-2.5 text-[10px] font-mono uppercase tracking-wider text-kestrel-subtle/80 mb-1.5 font-semibold">
              Operational
            </p>
          )}

          {mainItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              end={item.path === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 text-xs font-medium rounded-lg transition-all duration-150 ${
                  isActive
                    ? 'bg-sky-500/10 text-sky-400 border border-sky-500/30 shadow-[0_0_12px_-2px_rgba(14,165,233,0.15)] font-semibold'
                    : 'text-kestrel-muted hover:text-white hover:bg-kestrel-surface/80 hover:translate-x-0.5'
                } ${isCollapsed ? 'justify-center px-0' : ''}`
              }
              title={isCollapsed ? item.label : undefined}
            >
              {item.icon}
              {!isCollapsed && <span>{item.label}</span>}
            </NavLink>
          ))}
        </div>

        {/* Admin Section */}
        {adminItems.length > 0 && (
          <div className="space-y-1 pt-2 border-t border-kestrel-border/50">
            {!isCollapsed && (
              <p className="px-2.5 text-[10px] font-mono uppercase tracking-wider text-emerald-400/80 mb-1.5 font-semibold">
                Administration
              </p>
            )}

            {adminItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className={({ isActive }) =>
                  `flex items-center gap-3 px-3 py-2 text-xs font-medium rounded-lg transition-all duration-150 ${
                    isActive
                      ? 'bg-emerald-500/10 text-emerald-300 border border-emerald-500/30 shadow-[0_0_12px_-2px_rgba(16,185,129,0.15)] font-semibold'
                      : 'text-kestrel-muted hover:text-white hover:bg-kestrel-surface/80 hover:translate-x-0.5'
                  } ${isCollapsed ? 'justify-center px-0' : ''}`
                }
                title={isCollapsed ? item.label : undefined}
              >
                {item.icon}
                {!isCollapsed && <span>{item.label}</span>}
              </NavLink>
            ))}
          </div>
        )}

        {/* Dev / Design System */}
        {devItems.length > 0 && (
          <div className="space-y-1 pt-2 border-t border-kestrel-border/50">
            {!isCollapsed && (
              <p className="px-2.5 text-[10px] font-mono uppercase tracking-wider text-kestrel-subtle/80 mb-1.5 font-semibold">
                Design System
              </p>
            )}

            {devItems.map((item) => (
              <NavLink
                key={item.path}
                to={item.path}
                className={({ isActive }) =>
                  `flex items-center gap-3 px-3 py-2 text-xs font-medium rounded-lg transition-all duration-150 ${
                    isActive
                      ? 'bg-sky-500/10 text-sky-400 border border-sky-500/30 shadow-[0_0_12px_-2px_rgba(14,165,233,0.15)] font-semibold'
                      : 'text-kestrel-muted hover:text-white hover:bg-kestrel-surface/80 hover:translate-x-0.5'
                  } ${isCollapsed ? 'justify-center px-0' : ''}`
                }
                title={isCollapsed ? item.label : undefined}
              >
                {item.icon}
                {!isCollapsed && <span>{item.label}</span>}
              </NavLink>
            ))}
          </div>
        )}
      </nav>

      {/* Sidebar Collapse Toggle Button */}
      <div className="p-3 border-t border-kestrel-border/80">
        <button
          onClick={() => setIsCollapsed((prev) => !prev)}
          className="w-full flex items-center justify-center gap-2 py-1.5 text-xs text-kestrel-muted hover:text-white hover:bg-kestrel-surface rounded-lg border border-kestrel-border/60 transition-all active:scale-[0.98]"
          aria-label={isCollapsed ? 'Expand sidebar' : 'Collapse sidebar'}
        >
          {isCollapsed ? (
            <ChevronRight className="w-4 h-4" />
          ) : (
            <>
              <ChevronLeft className="w-4 h-4" />
              <span className="font-mono text-[11px]">Collapse</span>
            </>
          )}
        </button>
      </div>
    </aside>
  );
};
