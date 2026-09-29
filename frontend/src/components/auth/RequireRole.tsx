import React from 'react';
import { useAuth } from '../../hooks/useAuth';
import { hasRole, RoleType } from '../../utils/roleUtils';
import { ShieldAlert } from 'lucide-react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '../ui/Card';
import { Badge } from '../ui/Badge';

export interface RequireRoleProps {
  role: RoleType;
  fallbackMode?: 'block' | 'hide';
  children: React.ReactNode;
}

export const RequireRole: React.FC<RequireRoleProps> = ({
  role,
  fallbackMode = 'block',
  children,
}) => {
  const { roles, user, isLoading } = useAuth();

  if (isLoading) return null;

  const isAuthorized = hasRole(roles, role);

  if (!isAuthorized) {
    if (fallbackMode === 'hide') {
      return null;
    }

    return (
      <div className="p-6 max-w-xl mx-auto my-12">
        <Card className="border-rose-500/40 bg-rose-950/10">
          <CardHeader>
            <div className="flex items-center gap-3">
              <div className="p-2.5 rounded bg-rose-950/80 border border-rose-500/40 text-rose-400">
                <ShieldAlert className="w-6 h-6" />
              </div>
              <div>
                <CardTitle className="text-rose-300">Access Denied: Insufficient Privilege</CardTitle>
                <CardDescription className="text-rose-200/70">
                  Your account does not possess the required RBAC role permission.
                </CardDescription>
              </div>
            </div>
          </CardHeader>
          <CardContent className="space-y-4 text-xs font-mono">
            <div className="bg-kestrel-base p-3 rounded border border-kestrel-border space-y-2">
              <div className="flex justify-between items-center">
                <span className="text-kestrel-subtle">Authenticated User:</span>
                <span className="text-white font-semibold">{user?.username || 'Unknown'}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-kestrel-subtle">Your Current Roles:</span>
                <div className="flex gap-1">
                  {roles.length > 0 ? (
                    roles.map((r) => (
                      <Badge key={r} variant="outline" size="sm">
                        {r}
                      </Badge>
                    ))
                  ) : (
                    <span className="text-rose-400">None</span>
                  )}
                </div>
              </div>
              <div className="flex justify-between items-center pt-1 border-t border-kestrel-border/50">
                <span className="text-kestrel-subtle">Required Minimum Role:</span>
                <Badge
                  role={role.toLowerCase() as any}
                  size="sm"
                  className="font-bold border-rose-500/40"
                >
                  {role}
                </Badge>
              </div>
            </div>
          </CardContent>
        </Card>
      </div>
    );
  }

  return <>{children}</>;
};
