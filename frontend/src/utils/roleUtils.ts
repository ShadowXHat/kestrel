export type RoleType = 'Viewer' | 'Analyst' | 'Admin';

const ROLE_WEIGHTS: Record<string, number> = {
  admin: 3,
  analyst: 2,
  viewer: 1,
};

export function normalizeRole(role: string): string {
  return role.trim().toLowerCase();
}

/**
 * Checks if the user's roles satisfy the required role level based on hierarchy:
 * Admin (3) > Analyst (2) > Viewer (1)
 */
export function hasRole(userRoles: string[] | undefined, requiredRole: RoleType): boolean {
  if (!userRoles || userRoles.length === 0) return false;

  const requiredWeight = ROLE_WEIGHTS[requiredRole.toLowerCase()] || 1;

  return userRoles.some((userRole) => {
    const userWeight = ROLE_WEIGHTS[normalizeRole(userRole)] || 0;
    return userWeight >= requiredWeight;
  });
}

/**
 * Returns the highest role for rendering badges.
 */
export function getHighestRole(userRoles: string[] | undefined): 'admin' | 'analyst' | 'viewer' {
  if (!userRoles || userRoles.length === 0) return 'viewer';

  let highestWeight = 0;
  let highestRole: 'admin' | 'analyst' | 'viewer' = 'viewer';

  userRoles.forEach((r) => {
    const normalized = normalizeRole(r);
    const weight = ROLE_WEIGHTS[normalized] || 0;
    if (weight > highestWeight) {
      highestWeight = weight;
      if (normalized === 'admin') highestRole = 'admin';
      else if (normalized === 'analyst') highestRole = 'analyst';
      else if (normalized === 'viewer') highestRole = 'viewer';
    }
  });

  return highestRole;
}
