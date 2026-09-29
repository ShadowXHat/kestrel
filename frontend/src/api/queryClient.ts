import { QueryClient, QueryCache, MutationCache } from '@tanstack/react-query';
import { ApiError } from './apiClient';

const handleUnauthorized = (error: unknown) => {
  if (error instanceof ApiError && error.status === 401) {
    // Avoid redirect loops if already on login page
    if (window.location.pathname !== '/login') {
      console.warn('Unauthorized API request detected (401). Redirecting to /login...');
      window.location.href = '/login';
    }
  }
};

export const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error) => {
      handleUnauthorized(error);
    },
  }),
  mutationCache: new MutationCache({
    onError: (error) => {
      handleUnauthorized(error);
    },
  }),
  defaultOptions: {
    queries: {
      retry: (failureCount, error) => {
        // Do NOT retry 401 or 403 authorization failures
        if (error instanceof ApiError && (error.status === 401 || error.status === 403)) {
          return false;
        }
        return failureCount < 2;
      },
      refetchOnWindowFocus: false,
      staleTime: 1000 * 60 * 5, // 5 minutes default
    },
  },
});
