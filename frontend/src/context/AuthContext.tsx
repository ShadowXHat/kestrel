import React, { createContext, useContext, useCallback } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { authApi, UserSession } from '../api/auth';

interface AuthContextType {
  user: UserSession | null;
  roles: string[];
  isLoading: boolean;
  isAuthenticated: boolean;
  login: (credentials: { username: string; password: string }) => Promise<UserSession>;
  logout: () => Promise<void>;
  changePassword: (payload: { currentPassword: string; newPassword: string }) => Promise<void>;
  refetchUser: () => Promise<any>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const queryClient = useQueryClient();

  const {
    data: user = null,
    isLoading,
    refetch: refetchUser,
  } = useQuery<UserSession | null>({
    queryKey: ['auth', 'me'],
    queryFn: async () => {
      try {
        return await authApi.getMe();
      } catch (err: any) {
        if (err?.status === 401) {
          return null;
        }
        throw err;
      }
    },
    retry: false,
    staleTime: 1000 * 60 * 5, // 5 minutes
  });

  const loginMutation = useMutation({
    mutationFn: authApi.login,
    onSuccess: (data) => {
      queryClient.setQueryData(['auth', 'me'], data);
    },
  });

  const logoutMutation = useMutation({
    mutationFn: authApi.logout,
    onSuccess: () => {
      queryClient.setQueryData(['auth', 'me'], null);
      queryClient.clear();
    },
  });

  const changePasswordMutation = useMutation({
    mutationFn: authApi.changePassword,
    onSuccess: () => {
      queryClient.setQueryData(['auth', 'me'], null);
      queryClient.clear();
    },
  });

  const login = useCallback(
    async (credentials: { username: string; password: string }) => {
      return loginMutation.mutateAsync(credentials);
    },
    [loginMutation]
  );

  const logout = useCallback(async () => {
    await logoutMutation.mutateAsync();
  }, [logoutMutation]);

  const changePassword = useCallback(
    async (payload: { currentPassword: string; newPassword: string }) => {
      await changePasswordMutation.mutateAsync(payload);
    },
    [changePasswordMutation]
  );

  const roles = user?.roles || [];
  const isAuthenticated = !!user;

  return (
    <AuthContext.Provider
      value={{
        user,
        roles,
        isLoading,
        isAuthenticated,
        login,
        logout,
        changePassword,
        refetchUser,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
