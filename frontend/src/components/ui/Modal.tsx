import React, { useEffect } from 'react';
import { X } from 'lucide-react';
import { Button } from './Button';

export interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  title?: React.ReactNode;
  subtitle?: string;
  children: React.ReactNode;
  footer?: React.ReactNode;
  size?: 'sm' | 'md' | 'lg' | 'xl';
}

const sizeClasses = {
  sm: 'max-w-md',
  md: 'max-w-lg',
  lg: 'max-w-2xl',
  xl: 'max-w-4xl',
};

export const Modal: React.FC<ModalProps> = ({
  isOpen,
  onClose,
  title,
  subtitle,
  children,
  footer,
  size = 'md',
}) => {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && isOpen) {
        onClose();
      }
    };

    if (isOpen) {
      document.body.style.overflow = 'hidden';
      window.addEventListener('keydown', handleKeyDown);
    }

    return () => {
      document.body.style.overflow = '';
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 sm:p-6 overflow-y-auto">
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-slate-950/80 backdrop-blur-sm transition-opacity animate-in fade-in duration-200"
        onClick={onClose}
        aria-hidden="true"
      />

      {/* Modal Dialog Box */}
      <div
        className={`relative w-full ${sizeClasses[size]} glass-panel glow-border rounded-xl shadow-2xl z-10 flex flex-col max-h-[90vh] overflow-hidden animate-in zoom-in-95 duration-150`}
        role="dialog"
        aria-modal="true"
      >
        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-kestrel-border bg-kestrel-surface/60">
          <div>
            {title && typeof title === 'string' ? (
              <h2 className="text-base font-semibold text-kestrel-text">{title}</h2>
            ) : (
              title
            )}
            {subtitle && <p className="text-xs text-kestrel-muted mt-0.5">{subtitle}</p>}
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={onClose}
            aria-label="Close dialog"
            className="h-8 w-8 p-0 rounded-md text-kestrel-muted hover:text-white"
          >
            <X className="w-4 h-4" />
          </Button>
        </div>

        {/* Content Body */}
        <div className="p-5 overflow-y-auto flex-1 text-sm text-kestrel-text">
          {children}
        </div>

        {/* Footer */}
        {footer && (
          <div className="flex items-center justify-end gap-3 px-5 py-3.5 border-t border-kestrel-border bg-kestrel-surface/40">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
};
