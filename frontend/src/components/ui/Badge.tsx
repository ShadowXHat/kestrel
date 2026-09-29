import React from 'react';

export type SeverityLevel = 'info' | 'low' | 'medium' | 'high' | 'critical';
export type UserRole = 'viewer' | 'analyst' | 'admin';
export type BadgeVariant = 'default' | 'outline' | 'success' | 'danger' | 'accent';

export interface BadgeProps extends React.HTMLAttributes<HTMLSpanElement> {
  severity?: SeverityLevel;
  role?: UserRole;
  variant?: BadgeVariant;
  size?: 'sm' | 'md';
  showDot?: boolean;
}

const severityStyles: Record<SeverityLevel, { container: string; dot: string }> = {
  info: {
    container: 'bg-cyan-950/60 text-sky-300 border-sky-500/30',
    dot: 'bg-sky-400 shadow-[0_0_6px_#38bdf8]',
  },
  low: {
    container: 'bg-teal-950/60 text-teal-300 border-teal-500/30',
    dot: 'bg-teal-400 shadow-[0_0_6px_#2dd4bf]',
  },
  medium: {
    container: 'bg-amber-950/60 text-amber-300 border-amber-500/30',
    dot: 'bg-amber-400 shadow-[0_0_6px_#fbbf24]',
  },
  high: {
    container: 'bg-orange-950/60 text-orange-300 border-orange-500/30',
    dot: 'bg-orange-400 shadow-[0_0_6px_#f97316]',
  },
  critical: {
    container: 'bg-rose-950/70 text-rose-300 border-rose-500/40 font-semibold',
    dot: 'bg-rose-500 animate-pulse shadow-[0_0_8px_#f43f5e]',
  },
};

const roleStyles: Record<UserRole, { container: string; dot: string }> = {
  viewer: {
    container: 'bg-slate-900/80 text-slate-300 border-slate-700',
    dot: 'bg-slate-400',
  },
  analyst: {
    container: 'bg-sky-950/70 text-sky-300 border-sky-600/40',
    dot: 'bg-sky-400',
  },
  admin: {
    container: 'bg-emerald-950/70 text-emerald-300 border-emerald-500/40 font-semibold',
    dot: 'bg-emerald-400',
  },
};

const variantStyles: Record<BadgeVariant, { container: string; dot: string }> = {
  default: {
    container: 'bg-kestrel-surface text-kestrel-text border-kestrel-border',
    dot: 'bg-kestrel-muted',
  },
  outline: {
    container: 'bg-transparent text-kestrel-muted border-kestrel-border-strong',
    dot: 'bg-kestrel-subtle',
  },
  accent: {
    container: 'bg-sky-950/80 text-kestrel-accent border-sky-500/40',
    dot: 'bg-kestrel-accent',
  },
  success: {
    container: 'bg-emerald-950/60 text-emerald-300 border-emerald-500/30',
    dot: 'bg-emerald-400',
  },
  danger: {
    container: 'bg-rose-950/60 text-rose-300 border-rose-500/30',
    dot: 'bg-rose-400',
  },
};

export const Badge: React.FC<BadgeProps> = ({
  severity,
  role,
  variant = 'default',
  size = 'md',
  showDot = true,
  children,
  className = '',
  ...props
}) => {
  let styleConfig = variantStyles[variant];

  if (severity) {
    styleConfig = severityStyles[severity];
  } else if (role) {
    styleConfig = roleStyles[role];
  }

  const sizeClasses =
    size === 'sm'
      ? 'px-2 py-0.5 text-[10px] gap-1 rounded'
      : 'px-2.5 py-1 text-xs gap-1.5 rounded-md';

  const labelText = children || severity?.toUpperCase() || role?.toUpperCase();

  return (
    <span
      className={`inline-flex items-center font-mono font-medium border uppercase tracking-wider select-none shrink-0 ${sizeClasses} ${styleConfig.container} ${className}`}
      {...props}
    >
      {showDot && (
        <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${styleConfig.dot}`} />
      )}
      <span>{labelText}</span>
    </span>
  );
};
