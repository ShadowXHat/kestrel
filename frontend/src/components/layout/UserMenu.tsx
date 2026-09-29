import React, { useState, useRef, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../../hooks/useAuth';
import { getHighestRole } from '../../utils/roleUtils';
import { Badge } from '../ui/Badge';
import { ChangePasswordModal } from '../auth/ChangePasswordModal';
import { useToast } from '../ui/Toast';
import { User, LogOut, KeyRound, ChevronDown } from 'lucide-react';

export const UserMenu: React.FC = () => {
  const { user, roles, logout } = useAuth();
  const { toast } = useToast();
  const navigate = useNavigate();

  const [isOpen, setIsOpen] = useState(false);
  const [isChangePasswordOpen, setIsChangePasswordOpen] = useState(false);
  const menuRef = useRef<HTMLDivElement>(null);

  const highestRole = getHighestRole(roles);

  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        setIsOpen(false);
      }
    };

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setIsOpen(false);
      }
    };

    document.addEventListener('mousedown', handleClickOutside);
    document.addEventListener('keydown', handleKeyDown);

    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
      document.removeEventListener('keydown', handleKeyDown);
    };
  }, []);

  const handleLogout = async () => {
    setIsOpen(false);
    try {
      await logout();
      toast({
        title: 'Logged Out',
        description: 'Your session has been terminated.',
        variant: 'info',
      });
      navigate('/login');
    } catch {
      navigate('/login');
    }
  };

  if (!user) return null;

  return (
    <div className="relative" ref={menuRef}>
      {/* User Trigger Button */}
      <button
        onClick={() => setIsOpen((prev) => !prev)}
        className="flex items-center gap-2.5 px-3 py-1.5 rounded-lg border border-kestrel-border/80 bg-[#0b1019] hover:bg-kestrel-surface/80 hover:border-kestrel-border-strong text-left transition-all duration-150 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-500/50 focus-visible:ring-offset-1 focus-visible:ring-offset-kestrel-base select-none active:scale-[0.98]"
        aria-expanded={isOpen}
        aria-haspopup="true"
      >
        <div className="w-6 h-6 rounded-md bg-sky-950 border border-sky-500/40 text-sky-400 flex items-center justify-center font-mono text-xs font-bold">
          <User className="w-3.5 h-3.5" />
        </div>

        <div className="flex items-center gap-2">
          <span className="text-xs font-semibold text-kestrel-text font-mono">
            {user.username}
          </span>
          <Badge role={highestRole} size="sm" />
        </div>

        <ChevronDown
          className={`w-3.5 h-3.5 text-kestrel-muted transition-transform duration-200 ${
            isOpen ? 'rotate-180' : ''
          }`}
        />
      </button>

      {/* Dropdown Menu */}
      {isOpen && (
        <div className="absolute right-0 mt-2 w-56 glass-panel glow-border rounded-xl shadow-2xl z-50 py-1 text-xs animate-in fade-in zoom-in-95 duration-100">
          <div className="px-3.5 py-2 border-b border-kestrel-border/60">
            <p className="text-kestrel-subtle text-[11px] uppercase tracking-wider font-mono">
              Signed in as
            </p>
            <p className="font-semibold text-kestrel-text font-mono truncate">{user.username}</p>
            <div className="flex flex-wrap gap-1 mt-1">
              {roles.map((r) => (
                <span
                  key={r}
                  className="px-1.5 py-0.5 text-[10px] font-mono rounded bg-kestrel-surface text-kestrel-muted border border-kestrel-border/50"
                >
                  {r}
                </span>
              ))}
            </div>
          </div>

          <div className="py-1">
            <button
              onClick={() => {
                setIsOpen(false);
                setIsChangePasswordOpen(true);
              }}
              className="w-full flex items-center gap-2 px-3.5 py-2 text-kestrel-muted hover:text-white hover:bg-kestrel-surface/80 transition-colors text-left"
            >
              <KeyRound className="w-4 h-4 text-kestrel-accent" />
              <span>Change Password</span>
            </button>

            <button
              onClick={handleLogout}
              className="w-full flex items-center gap-2 px-3.5 py-2 text-rose-400 hover:text-rose-300 hover:bg-rose-950/40 transition-colors text-left font-medium"
            >
              <LogOut className="w-4 h-4" />
              <span>Sign Out</span>
            </button>
          </div>
        </div>
      )}

      {/* Change Password Modal */}
      <ChangePasswordModal
        isOpen={isChangePasswordOpen}
        onClose={() => setIsChangePasswordOpen(false)}
      />
    </div>
  );
};
