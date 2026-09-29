import { api } from './apiClient';

export interface UserDto {
  id: string;
  username: string;
  roles: string[];
  lockoutEndUtc?: string | null;
  accessFailedCount: number;
}

export interface CreateUserPayload {
  username: string;
  password?: string;
  roles?: string[];
}

export interface ReplaceRolesPayload {
  roles: string[];
}

export const usersApi = {
  list: async (): Promise<UserDto[]> => {
    return api.get<UserDto[]>('/users');
  },

  create: async (payload: CreateUserPayload): Promise<UserDto> => {
    return api.post<UserDto>('/users', payload);
  },

  replaceRoles: async (id: string, roles: string[]): Promise<UserDto> => {
    return api.put<UserDto>(`/users/${id}/roles`, { roles });
  },

  delete: async (id: string): Promise<void> => {
    return api.delete<void>(`/users/${id}`);
  },

  unlock: async (id: string): Promise<void> => {
    return api.post<void>(`/users/${id}/unlock`);
  },
};
