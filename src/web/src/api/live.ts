import {
  HttpTransportType,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from "@microsoft/signalr";
import { useEffect, useEffectEvent, useRef, useState } from "react";
import { useSession } from "../auth/session";

/** `retrying`: Reconnect was chosen and the new connection is starting. */
export type LiveState = "live" | "reconnecting" | "stopped" | "retrying";

/** What a screen's live connection gives its connection notice. */
export interface Live {
  state: LiveState;
  reconnect: () => void;
}

// The hub closes a connection when its access token expires and the client reconnects at once
// with a fresh one, so "Reconnecting…" shows only once a reconnect takes longer than this.
const RECONNECTING_NOTICE_DELAY = 2000;

/**
 * Live status from a core hub (WebSockets only; the dev server forwards /hubs to the API): one
 * connection while the screen is open, in the group of `establishmentId`. `onChange` hears that
 * establishment's `event`s. `onResync` runs after every join (connect, establishment change,
 * reconnect), when changes may have been missed.
 */
export function useLiveStatus<T extends { establishmentId: string }>(
  hubUrl: string,
  event: string,
  establishmentId: string,
  onChange: (change: T) => void,
  onResync: () => void,
): Live {
  const { api } = useSession();
  const [state, setState] = useState<LiveState>("live");
  const [attempt, setAttempt] = useState(0);
  const hub = useRef<HubConnection | null>(null);

  const changed = useEffectEvent((change: T) => {
    if (change.establishmentId === establishmentId) onChange(change);
  });
  const join = useEffectEvent((connection: HubConnection) => {
    // A refused join (a permission taken away) shows on the refetch, which fails with 403.
    connection.invoke("JoinEstablishment", establishmentId).then(onResync, onResync);
  });

  useEffect(() => {
    let disposed = false;
    let notice: ReturnType<typeof setTimeout> | undefined;
    const connection = new HubConnectionBuilder()
      .withUrl(hubUrl, {
        transport: HttpTransportType.WebSockets,
        skipNegotiation: true,
        accessTokenFactory: async () => (await api.accessToken()) ?? "",
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();
    connection.on(event, (change: T) => changed(change));
    connection.onreconnecting(() => {
      notice = setTimeout(() => setState("reconnecting"), RECONNECTING_NOTICE_DELAY);
    });
    connection.onreconnected(() => {
      clearTimeout(notice);
      setState("live");
      join(connection);
    });
    connection.onclose(() => {
      clearTimeout(notice);
      if (!disposed) setState("stopped");
    });
    connection.start().then(
      () => {
        if (disposed) return;
        hub.current = connection;
        setState("live");
        join(connection);
      },
      () => {
        if (!disposed) setState("stopped");
      },
    );
    return () => {
      disposed = true;
      hub.current = null;
      clearTimeout(notice);
      void connection.stop();
    };
  }, [api, attempt, hubUrl, event]);

  // The connection stays; it moves to the new establishment's group.
  useEffect(() => {
    if (hub.current?.state === HubConnectionState.Connected) join(hub.current);
  }, [establishmentId]);

  return {
    state,
    /** Starts a new connection, which refetches once it has joined. */
    reconnect: () => {
      setState("retrying");
      setAttempt((count) => count + 1);
    },
  };
}
