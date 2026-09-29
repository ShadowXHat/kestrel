import { api } from './apiClient';

export interface UserSession {
  id: string;
  username: string;
  roles: string[];
}

export interface LoginCredentials {
  username: string;
  password?: string;
}

export interface ChangePasswordPayload {
  currentPassword?: string;
  newPassword?: string;
}

export const authApi = {
  login: async (credentials: { username: string; password: string }): Promise<UserSession> => {
    return api.post<UserSession>('/auth/login', credentials);
  },

  logout: async (): Promise<void> => {
    return api.post<void>('/auth/logout');
  },

  getMe: async (): Promise<UserSession> => {
    return api.get<UserSession>('/auth/me');
  },

  changePassword: async (payload: { currentPassword: string; newPassword: string }): Promise<void> => {
    return api.post<void>('/auth/change-password', payload);
  },
};
