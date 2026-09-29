import React from 'react';

export const Table = React.forwardRef<
  HTMLTableElement,
  React.HTMLAttributes<HTMLTableElement> & { isFullWidth?: boolean }
>(({ className = '', isFullWidth = true, ...props }, ref) => (
  <div className="relative w-full overflow-x-auto rounded-lg border border-kestrel-border bg-kestrel-panel shadow-sm">
    <table
      ref={ref}
      className={`${isFullWidth ? 'w-full' : ''} caption-bottom text-sm text-left border-collapse ${className}`}
      {...props}
    />
  </div>
));
Table.displayName = 'Table';

export const TableHeader = React.forwardRef<
  HTMLTableSectionElement,
  React.HTMLAttributes<HTMLTableSectionElement>
>(({ className = '', ...props }, ref) => (
  <thead
    ref={ref}
    className={`bg-kestrel-surface/90 text-xs font-semibold uppercase tracking-wider text-kestrel-muted border-b border-kestrel-border select-none ${className}`}
    {...props}
  />
));
TableHeader.displayName = 'TableHeader';

export const TableBody = React.forwardRef<
  HTMLTableSectionElement,
  React.HTMLAttributes<HTMLTableSectionElement>
>(({ className = '', ...props }, ref) => (
  <tbody
    ref={ref}
    className={`divide-y divide-kestrel-border/60 text-kestrel-text ${className}`}
    {...props}
  />
));
TableBody.displayName = 'TableBody';

export const TableRow = React.forwardRef<
  HTMLTableRowElement,
  React.HTMLAttributes<HTMLTableRowElement> & { isSelected?: boolean }
>(({ className = '', isSelected = false, ...props }, ref) => (
  <tr
    ref={ref}
    className={`transition-colors duration-100 ease-in-out hover:bg-kestrel-surface-hover/80 ${
      isSelected ? 'bg-kestrel-accent-subtle/40 border-l-2 border-l-kestrel-accent' : ''
    } ${className}`}
    {...props}
  />
));
TableRow.displayName = 'TableRow';

export const TableHead = React.forwardRef<
  HTMLTableCellElement,
  React.ThHTMLAttributes<HTMLTableCellElement>
>(({ className = '', ...props }, ref) => (
  <th
    ref={ref}
    className={`h-9 px-3 text-left align-middle font-medium text-kestrel-muted whitespace-nowrap ${className}`}
    {...props}
  />
));
TableHead.displayName = 'TableHead';

export const TableCell = React.forwardRef<
  HTMLTableCellElement,
  React.TdHTMLAttributes<HTMLTableCellElement> & { isMono?: boolean }
>(({ className = '', isMono = false, ...props }, ref) => (
  <td
    ref={ref}
    className={`py-2 px-3 align-middle whitespace-nowrap text-xs ${
      isMono ? 'font-mono text-slate-300' : 'text-slate-200'
    } ${className}`}
    {...props}
  />
));
TableCell.displayName = 'TableCell';

export const TableEmptyState: React.FC<{
  colSpan: number;
  message?: string;
  icon?: React.ReactNode;
}> = ({ colSpan, message = 'No event records found', icon }) => (
  <tr>
    <td colSpan={colSpan} className="py-12 text-center text-kestrel-muted">
      <div className="flex flex-col items-center justify-center gap-2">
        {icon && <div className="text-kestrel-subtle">{icon}</div>}
        <p className="text-sm font-medium">{message}</p>
      </div>
    </td>
  </tr>
);
