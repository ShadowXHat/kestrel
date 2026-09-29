import React, { useState } from 'react';
import { useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../hooks/useAuth';
import { ApiError } from '../api/apiClient';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '../components/ui/Card';
import { Input } from '../components/ui/Input';
import { Button } from '../components/ui/Button';
import { ShieldAlert, User, Lock, AlertCircle } from 'lucide-react';

export const LoginPage: React.FC = () => {
  const { login } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const from = (location.state as any)?.from?.pathname || '/';

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    if (!username || !password) {
      setErrorMessage('invalid username or password');
      return;
    }

    setIsSubmitting(true);

    try {
      await login({ username, password });
      navigate(from, { replace: true });
    } catch (err: any) {
      // Distinguish "rejected credentials" from "API unreachable/broken".
      // Masking every failure as invalid credentials made correct logins look
      // wrong whenever the backend was down (.clinerules: no silent failures).
      // Security rule kept: a 401 never hints WHICH field was incorrect.
      if (err instanceof ApiError && err.status === 401) {
        setErrorMessage('invalid username or password');
      } else if (err instanceof ApiError && err.status === 429) {
        setErrorMessage('too many attempts — wait a minute and try again');
      } else {
        const detail = err instanceof Error && err.message ? ` (${err.message})` : '';
        setErrorMessage(`cannot reach the Kestrel API${detail} — is the backend running?`);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="relative min-h-screen bg-kestrel-base flex items-center justify-center p-4 overflow-hidden select-none">
      {/* Ambient background glow accents */}
      <div className="absolute -top-32 -left-32 w-96 h-96 bg-sky-500/10 rounded-full blur-3xl pointer-events-none animate-pulse" />
      <div className="absolute -bottom-32 -right-32 w-96 h-96 bg-emerald-500/10 rounded-full blur-3xl pointer-events-none animate-pulse" />

      <div className="relative w-full max-w-sm z-10">
        <Card variant="glow" className="rounded-2xl p-1">
          <CardHeader className="text-center pb-4 border-b border-kestrel-border/50 space-y-3">
            <div className="flex justify-center">
              <div className="w-14 h-14 rounded-2xl bg-sky-950/90 border border-sky-400/40 flex items-center justify-center text-sky-400 shadow-[0_0_20px_rgba(56,189,248,0.3)] transition-transform hover:scale-105">
                <ShieldAlert className="w-8 h-8 text-sky-400" />
              </div>
            </div>

            <div>
              <CardTitle className="text-2xl font-bold tracking-tight text-white font-sans flex items-center justify-center gap-2">
                Kestrel
                <span className="text-[10px] font-mono font-semibold px-2 py-0.5 rounded-full bg-sky-950 text-sky-300 border border-sky-500/40">
                  SOC
                </span>
              </CardTitle>
              <CardDescription className="text-xs text-kestrel-muted font-mono mt-1">
                Windows Event Log Analyzer
              </CardDescription>
            </div>
          </CardHeader>

          <CardContent className="pt-4">
            <form onSubmit={handleSubmit} className="space-y-4">
              {errorMessage && (
                <div
                  role="alert"
                  className="flex items-center gap-2 p-3 bg-rose-950/60 border border-rose-500/40 rounded-lg text-xs text-rose-300 font-mono"
                >
                  <AlertCircle className="w-4 h-4 text-rose-400 shrink-0" />
                  <span>{errorMessage}</span>
                </div>
              )}

              <Input
                label="Username"
                type="text"
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                placeholder="Enter username"
                leftIcon={<User className="w-4 h-4" />}
                required
                autoFocus
                disabled={isSubmitting}
                autoComplete="username"
              />

              <Input
                label="Password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Enter password"
                leftIcon={<Lock className="w-4 h-4" />}
                required
                disabled={isSubmitting}
                autoComplete="current-password"
              />

              <Button
                type="submit"
                variant="primary"
                size="md"
                className="w-full mt-2 font-mono shadow-[0_0_20px_-3px_rgba(14,165,233,0.4)]"
                isLoading={isSubmitting}
                disabled={isSubmitting}
              >
                {isSubmitting ? 'Authenticating...' : 'Sign In'}
              </Button>
            </form>
          </CardContent>
        </Card>

        <p className="text-[11px] text-center text-kestrel-subtle/80 font-mono mt-4">
          Session Cookie Auth • Restored via ASP.NET Core Identity
        </p>
      </div>
    </div>
  );
};

export default LoginPage;
