import React, { createContext, useContext, useState, useCallback } from 'react';
import { AlertTriangle, CheckCircle, Info, XCircle, X } from 'lucide-react';

export type ToastVariant = 'info' | 'success' | 'warning' | 'error' | 'critical';

export interface ToastMessage {
  id: string;
  title: string;
  description?: string;
  variant?: ToastVariant;
  duration?: number;
}

interface ToastContextType {
  toast: (options: Omit<ToastMessage, 'id'>) => void;
  removeToast: (id: string) => void;
}

const ToastContext = createContext<ToastContextType | undefined>(undefined);

export const useToast = () => {
  const context = useContext(ToastContext);
  if (!context) {
    throw new Error('useToast must be used within a ToastProvider');
  }
  return context;
};

const variantIcons: Record<ToastVariant, React.ReactNode> = {
  info: <Info className="w-5 h-5 text-sky-400 shrink-0" />,
  success: <CheckCircle className="w-5 h-5 text-teal-400 shrink-0" />,
  warning: <AlertTriangle className="w-5 h-5 text-amber-400 shrink-0" />,
  error: <XCircle className="w-5 h-5 text-orange-400 shrink-0" />,
  critical: <AlertTriangle className="w-5 h-5 text-rose-400 shrink-0 animate-pulse" />,
};

const variantBorderStyles: Record<ToastVariant, string> = {
  info: 'border-sky-500/40 bg-kestrel-panel',
  success: 'border-teal-500/40 bg-kestrel-panel',
  warning: 'border-amber-500/40 bg-kestrel-panel',
  error: 'border-orange-500/40 bg-kestrel-panel',
  critical: 'border-rose-500/50 bg-rose-950/30',
};

export const ToastProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  const removeToast = useCallback((id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const toast = useCallback(
    ({ title, description, variant = 'info', duration = 4000 }: Omit<ToastMessage, 'id'>) => {
      const id = Math.random().toString(36).substring(2, 9);
      const newToast: ToastMessage = { id, title, description, variant, duration };

      setToasts((prev) => [...prev, newToast]);

      if (duration > 0) {
        setTimeout(() => {
          removeToast(id);
        }, duration);
      }
    },
    [removeToast]
  );

  return (
    <ToastContext.Provider value={{ toast, removeToast }}>
      {children}
      
      {/* Floating Toast Container */}
      <div
        aria-live="assertive"
        className="fixed bottom-4 right-4 z-50 flex flex-col gap-2.5 max-w-md w-full pointer-events-none p-2"
      >
        {toasts.map((t) => (
          <div
            key={t.id}
            className={`pointer-events-auto flex items-start gap-3 p-4 rounded-xl border glass-card shadow-2xl backdrop-blur-md transition-all duration-200 ease-out animate-in slide-in-from-bottom-2 ${
              variantBorderStyles[t.variant || 'info']
            }`}
          >
            {variantIcons[t.variant || 'info']}
            
            <div className="flex-1 text-left">
              <h4 className="text-sm font-semibold text-kestrel-text">{t.title}</h4>
              {t.description && (
                <p className="text-xs text-kestrel-muted mt-1 leading-relaxed">
                  {t.description}
                </p>
              )}
            </div>

            <button
              onClick={() => removeToast(t.id)}
              className="text-kestrel-muted hover:text-white transition-colors p-1 rounded"
              aria-label="Dismiss toast"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
};
