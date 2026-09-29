import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { usersApi, UserDto } from '../api/users';
import { useAuth } from '../hooks/useAuth';
import { Button } from '../components/ui/Button';
import { Badge } from '../components/ui/Badge';
import { Input } from '../components/ui/Input';
import { Modal } from '../components/ui/Modal';
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell, TableEmptyState } from '../components/ui/Table';
import { useToast } from '../components/ui/Toast';
import { Users, UserPlus, ShieldCheck, Unlock, Trash2, Edit3, User, Check } from 'lucide-react';

export const UsersPage: React.FC = () => {
  const { user: currentUser } = useAuth();
  const { toast } = useToast();
  const queryClient = useQueryClient();

  // Modals state
  const [isCreateModalOpen, setIsCreateModalOpen] = useState(false);
  const [editingUser, setEditingUser] = useState<UserDto | null>(null);

  // Form states for Create User
  const [newUsername, setNewUsername] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [selectedRoles, setSelectedRoles] = useState<string[]>(['Viewer']);
  const [createError, setCreateError] = useState<string | null>(null);

  // Form states for Role Edit
  const [editRoles, setEditRoles] = useState<string[]>([]);
  const [editError, setEditError] = useState<string | null>(null);

  // Fetch users list
  const {
    data: users = [],
    isLoading,
  } = useQuery<UserDto[]>({
    queryKey: ['users', 'list'],
    queryFn: usersApi.list,
  });

  // Create User Mutation
  const createUserMutation = useMutation({
    mutationFn: usersApi.create,
    onSuccess: (newUser) => {
      toast({
        title: 'User Created',
        description: `Account "${newUser.username}" created successfully.`,
        variant: 'success',
      });
      queryClient.invalidateQueries({ queryKey: ['users', 'list'] });
      handleCloseCreateModal();
    },
    onError: (err: any) => {
      let msg = 'Failed to create user.';
      if (err?.data?.errors && Array.isArray(err.data.errors)) {
        msg = err.data.errors.join('; ');
      } else if (err?.message) {
        msg = err.message;
      }
      setCreateError(msg);
    },
  });

  // Replace Roles Mutation
  const replaceRolesMutation = useMutation({
    mutationFn: ({ id, roles }: { id: string; roles: string[] }) =>
      usersApi.replaceRoles(id, roles),
    onSuccess: (updatedUser) => {
      toast({
        title: 'Roles Updated',
        description: `Roles for "${updatedUser.username}" set to [${updatedUser.roles.join(', ')}].`,
        variant: 'success',
      });
      queryClient.invalidateQueries({ queryKey: ['users', 'list'] });
      setEditingUser(null);
    },
    onError: (err: any) => {
      let msg = 'Failed to update user roles.';
      if (err?.data?.detail) msg = err.data.detail;
      else if (err?.data?.errors && Array.isArray(err.data.errors)) {
        msg = err.data.errors.join('; ');
      } else if (err?.message) msg = err.message;
      setEditError(msg);
    },
  });

  // Unlock Mutation
  const unlockMutation = useMutation({
    mutationFn: usersApi.unlock,
    onSuccess: (_, userId) => {
      const u = users.find((item) => item.id === userId);
      toast({
        title: 'Account Unlocked',
        description: `User "${u?.username || 'Account'}" failed logon count reset.`,
        variant: 'success',
      });
      queryClient.invalidateQueries({ queryKey: ['users', 'list'] });
    },
    onError: (err: any) => {
      toast({
        title: 'Unlock Failed',
        description: err?.message || 'Could not unlock user account.',
        variant: 'critical',
      });
    },
  });

  // Delete Mutation
  const deleteMutation = useMutation({
    mutationFn: usersApi.delete,
    onSuccess: () => {
      toast({
        title: 'User Deleted',
        description: 'Account deleted from Identity store.',
        variant: 'info',
      });
      queryClient.invalidateQueries({ queryKey: ['users', 'list'] });
    },
    onError: (err: any) => {
      let msg = 'Could not delete user account.';
      if (err?.data?.detail) msg = err.data.detail;
      else if (err?.message) msg = err.message;
      toast({
        title: 'Delete Refused',
        description: msg,
        variant: 'critical',
      });
    },
  });

  const handleCloseCreateModal = () => {
    setNewUsername('');
    setNewPassword('');
    setSelectedRoles(['Viewer']);
    setCreateError(null);
    setIsCreateModalOpen(false);
  };

  const handleCreateSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setCreateError(null);

    if (!newUsername || !newPassword) {
      setCreateError('Username and Password are required');
      return;
    }

    createUserMutation.mutate({
      username: newUsername,
      password: newPassword,
      roles: selectedRoles,
    });
  };

  const handleOpenEditRoles = (u: UserDto) => {
    setEditingUser(u);
    setEditRoles([...u.roles]);
    setEditError(null);
  };

  const handleEditRolesSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingUser) return;
    setEditError(null);
    replaceRolesMutation.mutate({ id: editingUser.id, roles: editRoles });
  };

  const toggleRoleSelection = (role: string, currentList: string[], setFn: (r: string[]) => void) => {
    if (currentList.includes(role)) {
      setFn(currentList.filter((r) => r !== role));
    } else {
      setFn([...currentList, role]);
    }
  };

  const isLockedOut = (userItem: UserDto): boolean => {
    if (!userItem.lockoutEndUtc) return false;
    return new Date(userItem.lockoutEndUtc) > new Date();
  };

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      {/* Page Header */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-emerald-950/80 border border-emerald-500/30 flex items-center justify-center text-emerald-400 shadow-[0_0_12px_rgba(16,185,129,0.25)]">
              <ShieldCheck className="w-4.5 h-4.5" />
            </div>
            <span>User Administration & RBAC</span>
          </h2>
          <p className="text-xs text-kestrel-muted mt-1">
            Manage operational user accounts, assign role privileges (Viewer, Analyst, Admin), and unlock lockout states.
          </p>
        </div>

        <Button
          variant="primary"
          size="sm"
          onClick={() => setIsCreateModalOpen(true)}
          leftIcon={<UserPlus className="w-4 h-4" />}
          className="bg-emerald-500 hover:bg-emerald-400 text-slate-950 border-emerald-400/40 shadow-[0_0_16px_rgba(16,185,129,0.3)]"
        >
          Create User Account
        </Button>
      </div>

      {/* Users Table */}
      <div className="glass-panel rounded-xl border border-kestrel-border overflow-hidden shadow-2xl">
        <Table>
        <TableHeader>
          <TableRow>
            <TableHead>Username</TableHead>
            <TableHead>Assigned Roles</TableHead>
            <TableHead>Account Status</TableHead>
            <TableHead>Failed Logons</TableHead>
            <TableHead className="text-right">Actions</TableHead>
          </TableRow>
        </TableHeader>
        <TableBody>
          {isLoading ? (
            <TableEmptyState colSpan={5} message="Loading user directory..." />
          ) : users.length === 0 ? (
            <TableEmptyState
              colSpan={5}
              message="No users registered in Identity store."
              icon={<Users className="w-8 h-8 text-kestrel-subtle" />}
            />
          ) : (
            users.map((u) => {
              const locked = isLockedOut(u);
              const isSelf = currentUser?.id === u.id;

              return (
                <TableRow key={u.id}>
                  <TableCell isMono className="font-bold text-white flex items-center gap-2">
                    <User className="w-4 h-4 text-kestrel-muted" />
                    <span>{u.username}</span>
                    {isSelf && (
                      <span className="text-[10px] font-mono px-1.5 py-0.2 rounded bg-sky-950 text-kestrel-accent border border-sky-500/30">
                        You
                      </span>
                    )}
                  </TableCell>

                  <TableCell>
                    <div className="flex flex-wrap gap-1.5">
                      {u.roles.map((r) => (
                        <Badge key={r} role={r.toLowerCase() as any} size="sm" />
                      ))}
                    </div>
                  </TableCell>

                  <TableCell>
                    {locked ? (
                      <Badge severity="critical" size="sm" showDot>
                        Locked Out
                      </Badge>
                    ) : (
                      <Badge variant="success" size="sm" showDot>
                        Active
                      </Badge>
                    )}
                  </TableCell>

                  <TableCell isMono className={u.accessFailedCount > 0 ? 'text-amber-400 font-bold' : 'text-kestrel-muted'}>
                    {u.accessFailedCount}
                  </TableCell>

                  <TableCell className="text-right">
                    <div className="flex items-center justify-end gap-2">
                      {/* Unlock button */}
                      {locked && (
                        <Button
                          variant="secondary"
                          size="sm"
                          className="h-7 px-2 text-xs text-amber-300 border-amber-500/40 hover:bg-amber-950/40"
                          onClick={() => unlockMutation.mutate(u.id)}
                          isLoading={unlockMutation.isPending}
                          leftIcon={<Unlock className="w-3.5 h-3.5" />}
                          title="Clear lockout and reset failed logon counter"
                        >
                          Unlock
                        </Button>
                      )}

                      {/* Edit Roles button */}
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-7 px-2 text-xs"
                        onClick={() => handleOpenEditRoles(u)}
                        leftIcon={<Edit3 className="w-3.5 h-3.5" />}
                      >
                        Edit Roles
                      </Button>

                      {/* Delete button */}
                      <Button
                        variant="ghost"
                        size="sm"
                        className="h-7 px-2 text-xs text-rose-400 hover:text-rose-300 hover:bg-rose-950/40"
                        disabled={isSelf}
                        onClick={() => {
                          if (window.confirm(`Are you sure you want to delete user "${u.username}"?`)) {
                            deleteMutation.mutate(u.id);
                          }
                        }}
                        leftIcon={<Trash2 className="w-3.5 h-3.5" />}
                        title={isSelf ? 'You cannot delete your own account' : 'Delete user account'}
                      >
                        Delete
                      </Button>
                    </div>
                  </TableCell>
                </TableRow>
              );
            })
          )}
        </TableBody>
      </Table>
      </div>

      {/* Create User Modal */}
      <Modal
        isOpen={isCreateModalOpen}
        onClose={handleCloseCreateModal}
        title={
          <div className="flex items-center gap-2">
            <UserPlus className="w-5 h-5 text-kestrel-accent" />
            <span>Create New User Account</span>
          </div>
        }
        subtitle="Operational accounts are created by Administrators. No open self-signup exists."
        footer={
          <>
            <Button variant="secondary" size="sm" onClick={handleCloseCreateModal}>
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleCreateSubmit}
              isLoading={createUserMutation.isPending}
              leftIcon={<Check className="w-4 h-4" />}
            >
              Create Account
            </Button>
          </>
        }
      >
        <form onSubmit={handleCreateSubmit} className="space-y-4">
          {createError && (
            <div className="p-3 bg-rose-950/60 border border-rose-500/40 rounded text-xs text-rose-300 font-medium font-mono">
              {createError}
            </div>
          )}

          <Input
            label="Username"
            type="text"
            value={newUsername}
            onChange={(e) => setNewUsername(e.target.value)}
            placeholder="e.g. analyst2"
            required
            autoFocus
          />

          <Input
            label="Password"
            type="password"
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            placeholder="At least 12 chars, upper, lower, digit, symbol"
            helperText="Password must satisfy security policy rules"
            required
          />

          <div className="space-y-2">
            <label className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted select-none">
              Assign Roles
            </label>
            <div className="grid grid-cols-3 gap-3 p-3 bg-kestrel-base border border-kestrel-border rounded">
              {['Viewer', 'Analyst', 'Admin'].map((roleName) => {
                const checked = selectedRoles.includes(roleName);
                return (
                  <label
                    key={roleName}
                    className={`flex items-center gap-2 p-2 rounded cursor-pointer border text-xs font-mono transition-all ${
                      checked
                        ? 'bg-sky-950/80 text-kestrel-accent border-sky-500/40'
                        : 'bg-kestrel-panel text-kestrel-muted border-kestrel-border'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleRoleSelection(roleName, selectedRoles, setSelectedRoles)}
                      className="rounded bg-kestrel-base border-kestrel-border text-kestrel-accent focus:ring-kestrel-accent"
                    />
                    <span>{roleName}</span>
                  </label>
                );
              })}
            </div>
          </div>
        </form>
      </Modal>

      {/* Edit Roles Modal */}
      <Modal
        isOpen={!!editingUser}
        onClose={() => setEditingUser(null)}
        title={
          <div className="flex items-center gap-2">
            <Edit3 className="w-5 h-5 text-sky-400" />
            <span>Update Roles: {editingUser?.username}</span>
          </div>
        }
        subtitle="Modify assigned RBAC roles for this account."
        footer={
          <>
            <Button variant="secondary" size="sm" onClick={() => setEditingUser(null)}>
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleEditRolesSubmit}
              isLoading={replaceRolesMutation.isPending}
              leftIcon={<Check className="w-4 h-4" />}
            >
              Save Roles
            </Button>
          </>
        }
      >
        <form onSubmit={handleEditRolesSubmit} className="space-y-4">
          {editError && (
            <div className="p-3 bg-rose-950/60 border border-rose-500/40 rounded text-xs text-rose-300 font-medium font-mono">
              {editError}
            </div>
          )}

          <div className="space-y-2">
            <label className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted select-none">
              Roles Selection
            </label>
            <div className="grid grid-cols-3 gap-3 p-3 bg-kestrel-base border border-kestrel-border rounded">
              {['Viewer', 'Analyst', 'Admin'].map((roleName) => {
                const checked = editRoles.includes(roleName);
                return (
                  <label
                    key={roleName}
                    className={`flex items-center gap-2 p-2 rounded cursor-pointer border text-xs font-mono transition-all ${
                      checked
                        ? 'bg-sky-950/80 text-sky-300 border-sky-500/40'
                        : 'bg-kestrel-panel text-kestrel-muted border-kestrel-border'
                    }`}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleRoleSelection(roleName, editRoles, setEditRoles)}
                      className="rounded bg-kestrel-base border-kestrel-border text-sky-400 focus:ring-sky-400"
                    />
                    <span>{roleName}</span>
                  </label>
                );
              })}
            </div>
          </div>
        </form>
      </Modal>
    </div>
  );
};

export default UsersPage;
