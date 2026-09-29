import React from 'react';

export interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
  helperText?: string;
  error?: string;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  isMono?: boolean;
}

export const Input = React.forwardRef<HTMLInputElement, InputProps>(
  (
    {
      label,
      helperText,
      error,
      leftIcon,
      rightIcon,
      isMono = false,
      disabled,
      className = '',
      id,
      ...props
    },
    ref
  ) => {
    const generatedId = React.useId();
    const inputId = id || generatedId;

    return (
      <div className="w-full flex flex-col gap-1.5 text-left">
        {label && (
          <label
            htmlFor={inputId}
            className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted select-none"
          >
            {label}
          </label>
        )}
        
        <div className="relative flex items-center">
          {leftIcon && (
            <div className="absolute left-3 text-kestrel-muted pointer-events-none flex items-center justify-center">
              {leftIcon}
            </div>
          )}
          
          <input
            ref={ref}
            id={inputId}
            disabled={disabled}
            className={`w-full h-9 bg-[#0b1019] text-kestrel-text text-sm rounded-lg border transition-all duration-150 ease-in-out placeholder:text-kestrel-subtle/70
              ${leftIcon ? 'pl-9' : 'pl-3'}
              ${rightIcon ? 'pr-9' : 'pr-3'}
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
          />
          
          {rightIcon && (
            <div className="absolute right-3 text-kestrel-muted flex items-center justify-center">
              {rightIcon}
            </div>
          )}
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

Input.displayName = 'Input';
