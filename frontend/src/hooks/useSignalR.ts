import { useState, useEffect, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';

export type SignalRStatus = 'connected' | 'reconnecting' | 'disconnected';

export interface UseSignalROptions {
  hubUrl?: string;
  autoSubscribeGroup?: string;
}

export function useSignalR(options: UseSignalROptions = {}) {
  const { hubUrl = '/hubs/events', autoSubscribeGroup } = options;

  const [status, setStatus] = useState<SignalRStatus>('disconnected');
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        withCredentials: true,
      })
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.onreconnecting(() => {
      setStatus('reconnecting');
    });

    connection.onreconnected(async () => {
      setStatus('connected');
      if (autoSubscribeGroup) {
        try {
          await connection.invoke('Subscribe', autoSubscribeGroup);
        } catch (err) {
          console.warn('Failed to re-subscribe to SignalR group:', autoSubscribeGroup, err);
        }
      }
    });

    connection.onclose(() => {
      setStatus('disconnected');
    });

    const startConnection = async () => {
      try {
        await connection.start();
        setStatus('connected');

        if (autoSubscribeGroup) {
          await connection.invoke('Subscribe', autoSubscribeGroup);
        }
      } catch (err) {
        console.warn('SignalR initial connection failed:', err);
        setStatus('disconnected');
      }
    };

    startConnection();

    return () => {
      if (connectionRef.current) {
        connectionRef.current.stop();
        connectionRef.current = null;
      }
    };
  }, [hubUrl, autoSubscribeGroup]);

  const onEvent = useCallback(<T>(eventName: string, callback: (data: T) => void) => {
    const connection = connectionRef.current;
    if (!connection) return () => {};

    connection.on(eventName, callback);

    return () => {
      connection.off(eventName, callback);
    };
  }, []);

  return {
    status,
    isConnected: status === 'connected',
    connection: connectionRef.current,
    onEvent,
  };
}
