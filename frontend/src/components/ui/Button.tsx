import React from 'react';

export type ButtonVariant = 'primary' | 'secondary' | 'danger' | 'ghost';
export type ButtonSize = 'sm' | 'md' | 'lg';

export interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  isLoading?: boolean;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
}

const variantStyles: Record<ButtonVariant, string> = {
  primary:
    'bg-sky-500 text-slate-950 font-semibold hover:bg-sky-400 active:bg-sky-600 shadow-[0_0_20px_-3px_rgba(14,165,233,0.4)] border border-sky-400/40',
  secondary:
    'bg-kestrel-surface/80 text-kestrel-text border border-kestrel-border hover:bg-kestrel-surface hover:border-kestrel-border-strong hover:text-white active:bg-kestrel-panel backdrop-blur-sm',
  danger:
    'bg-rose-600 text-white font-semibold hover:bg-rose-500 active:bg-rose-700 shadow-glow-danger border border-rose-500/50',
  ghost:
    'bg-transparent text-kestrel-muted hover:text-white hover:bg-kestrel-surface/60 active:bg-kestrel-surface',
};

const sizeStyles: Record<ButtonSize, string> = {
  sm: 'h-8 px-3 text-xs gap-1.5 rounded-lg font-mono',
  md: 'h-9 px-4 text-xs gap-2 rounded-lg font-mono',
  lg: 'h-11 px-5 text-sm gap-2.5 rounded-xl font-mono',
};

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  (
    {
      variant = 'primary',
      size = 'md',
      isLoading = false,
      leftIcon,
      rightIcon,
      children,
      disabled,
      className = '',
      ...props
    },
    ref
  ) => {
    return (
      <button
        ref={ref}
        disabled={disabled || isLoading}
        className={`inline-flex items-center justify-center font-medium transition-all duration-150 ease-in-out select-none
          focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-kestrel-accent focus-visible:ring-offset-2 focus-visible:ring-offset-kestrel-base
          disabled:opacity-50 disabled:cursor-not-allowed disabled:pointer-events-none active:scale-[0.98]
          ${variantStyles[variant]}
          ${sizeStyles[size]}
          ${className}`}
        {...props}
      >
        {isLoading ? (
          <svg
            className="animate-spin -ml-0.5 mr-2 h-4 w-4 text-current"
            xmlns="http://www.w3.org/2000/svg"
            fill="none"
            viewBox="0 0 24 24"
          >
            <circle
              className="opacity-25"
              cx="12"
              cy="12"
              r="10"
              stroke="currentColor"
              strokeWidth="4"
            ></circle>
            <path
              className="opacity-75"
              fill="currentColor"
              d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z"
            ></path>
          </svg>
        ) : leftIcon ? (
          <span className="shrink-0">{leftIcon}</span>
        ) : null}
        
        {children}
        
        {!isLoading && rightIcon && <span className="shrink-0">{rightIcon}</span>}
      </button>
    );
  }
);

Button.displayName = 'Button';
