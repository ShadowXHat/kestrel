import React from 'react';
import { ChevronDown } from 'lucide-react';

export interface SelectOption {
  value: string | number;
  label: string;
  disabled?: boolean;
}

export interface SelectProps extends React.SelectHTMLAttributes<HTMLSelectElement> {
  label?: string;
  helperText?: string;
  error?: string;
  options?: SelectOption[];
  isMono?: boolean;
}

export const Select = React.forwardRef<HTMLSelectElement, SelectProps>(
  (
    {
      label,
      helperText,
      error,
      options,
      isMono = false,
      disabled,
      className = '',
      id,
      children,
      ...props
    },
    ref
  ) => {
    const generatedId = React.useId();
    const selectId = id || generatedId;

    return (
      <div className="w-full flex flex-col gap-1.5 text-left">
        {label && (
          <label
            htmlFor={selectId}
            className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted select-none"
          >
            {label}
          </label>
        )}

        <div className="relative flex items-center">
          <select
            ref={ref}
            id={selectId}
            disabled={disabled}
            className={`w-full h-9 bg-[#0b1019] text-kestrel-text text-sm rounded-lg border appearance-none pl-3 pr-9 transition-all duration-150 ease-in-out cursor-pointer
              ${isMono ? 'font-mono text-xs' : 'font-sans'}
              ${
                error
                  ? 'border-rose-500/80 focus-visible:ring-rose-500 focus-visible:border-rose-500'
                  : 'border-kestrel-border hover:border-kestrel-border-strong focus-visible:border-sky-500 focus-visible:ring-sky-500/30'
              }
              focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-offset-1 focus-visible:ring-offset-kestrel-base
              disabled:opacity-50 disabled:cursor-not-allowed disabled:bg-kestrel-panel/40
              ${className}`}
            {...props}
          >
            {options
              ? options.map((opt) => (
                  <option
                    key={opt.value}
                    value={opt.value}
                    disabled={opt.disabled}
                    className="bg-kestrel-surface text-kestrel-text py-1"
                  >
                    {opt.label}
                  </option>
                ))
              : children}
          </select>

          <div className="absolute right-3 text-kestrel-muted pointer-events-none flex items-center justify-center">
            <ChevronDown className="w-4 h-4" />
          </div>
        </div>

        {error ? (
          <span className="text-xs text-rose-400 font-medium">{error}</span>
        ) : helperText ? (
          <span className="text-xs text-kestrel-subtle">{helperText}</span>
        ) : null}
      </div>
    );
  }
);

Select.displayName = 'Select';
