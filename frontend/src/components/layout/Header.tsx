import React from 'react';
import { NavLink } from 'react-router-dom';
import { UserMenu } from './UserMenu';
import { ShieldAlert, Database, Search, Bell, Settings, Palette } from 'lucide-react';

export const Header: React.FC = () => {
  const navItems = [
    { label: 'Events', path: '/events', icon: <Database className="w-4 h-4" /> },
    { label: 'Search', path: '/search', icon: <Search className="w-4 h-4" /> },
    { label: 'Alerts', path: '/alerts', icon: <Bell className="w-4 h-4" /> },
    { label: 'Settings', path: '/settings', icon: <Settings className="w-4 h-4" /> },
    { label: 'Showcase', path: '/showcase', icon: <Palette className="w-4 h-4" /> },
  ];

  return (
    <header className="h-14 bg-kestrel-panel border-b border-kestrel-border px-4 md:px-6 flex items-center justify-between sticky top-0 z-40">
      <div className="flex items-center gap-6">
        {/* Logo */}
        <NavLink to="/" className="flex items-center gap-2.5 group">
          <div className="w-8 h-8 rounded bg-sky-950/80 border border-sky-500/40 flex items-center justify-center text-kestrel-accent shadow-glow group-hover:border-kestrel-accent transition-colors">
            <ShieldAlert className="w-5 h-5" />
          </div>
          <span className="font-bold text-base tracking-tight text-white font-sans flex items-center gap-1.5">
            Kestrel
            <span className="text-[10px] font-mono font-medium px-1.5 py-0.2 rounded bg-kestrel-surface text-kestrel-subtle border border-kestrel-border">
              SOC
            </span>
          </span>
        </NavLink>

        {/* Navigation Tabs */}
        <nav className="hidden md:flex items-center gap-1">
          {navItems.map((item) => (
            <NavLink
              key={item.path}
              to={item.path}
              className={({ isActive }) =>
                `flex items-center gap-2 px-3 py-1.5 text-xs font-medium rounded-md transition-all duration-150 select-none ${
                  isActive
                    ? 'bg-sky-950/60 text-kestrel-accent border border-sky-500/30'
                    : 'text-kestrel-muted hover:text-kestrel-text hover:bg-kestrel-surface/60'
                }`
              }
            >
              {item.icon}
              <span>{item.label}</span>
            </NavLink>
          ))}
        </nav>
      </div>

      {/* User Menu */}
      <div className="flex items-center gap-3">
        <UserMenu />
      </div>
    </header>
  );
};
