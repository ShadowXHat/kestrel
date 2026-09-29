import React from 'react';

export interface CardProps extends React.HTMLAttributes<HTMLDivElement> {
  hoverable?: boolean;
  variant?: 'default' | 'glass' | 'glow';
}

export const Card = React.forwardRef<HTMLDivElement, CardProps>(
  ({ className = '', hoverable = false, variant = 'default', ...props }, ref) => {
    const variantClasses = {
      default: 'bg-kestrel-panel border-kestrel-border shadow-md',
      glass: 'glass-card shadow-xl backdrop-blur-md',
      glow: 'glass-panel glow-border shadow-2xl',
    };

    return (
      <div
        ref={ref}
        className={`rounded-xl border text-kestrel-text transition-all duration-200 ease-in-out ${
          variantClasses[variant]
        } ${
          hoverable
            ? 'hover:border-kestrel-border-strong hover:bg-kestrel-surface/60 hover:-translate-y-[1px] hover:shadow-lg'
            : ''
        } ${className}`}
        {...props}
      />
    );
  }
);
Card.displayName = 'Card';

export const CardHeader = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement>
>(({ className = '', ...props }, ref) => (
  <div
    ref={ref}
    className={`flex flex-col space-y-1.5 p-5 border-b border-kestrel-border/50 ${className}`}
    {...props}
  />
));
CardHeader.displayName = 'CardHeader';

export const CardTitle = React.forwardRef<
  HTMLHeadingElement,
  React.HTMLAttributes<HTMLHeadingElement>
>(({ className = '', ...props }, ref) => (
  <h3
    ref={ref}
    className={`text-base font-semibold leading-none tracking-tight text-kestrel-text ${className}`}
    {...props}
  />
));
CardTitle.displayName = 'CardTitle';

export const CardDescription = React.forwardRef<
  HTMLParagraphElement,
  React.HTMLAttributes<HTMLParagraphElement>
>(({ className = '', ...props }, ref) => (
  <p
    ref={ref}
    className={`text-xs text-kestrel-muted ${className}`}
    {...props}
  />
));
CardDescription.displayName = 'CardDescription';

export const CardContent = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement>
>(({ className = '', ...props }, ref) => (
  <div ref={ref} className={`p-5 ${className}`} {...props} />
));
CardContent.displayName = 'CardContent';

export const CardFooter = React.forwardRef<
  HTMLDivElement,
  React.HTMLAttributes<HTMLDivElement>
>(({ className = '', ...props }, ref) => (
  <div
    ref={ref}
    className={`flex items-center p-5 pt-0 border-t border-kestrel-border/40 mt-3 ${className}`}
    {...props}
  />
));
CardFooter.displayName = 'CardFooter';
