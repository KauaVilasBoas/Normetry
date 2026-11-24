import { vi } from "vitest";

// Stands in for @microsoft/signalr: `vi.mock("@microsoft/signalr", () => import("../test/signalr"))`.
// A hub per connection the screen opens; the test plays the server's side through `hubs`.

type Callback = () => void;

export class FakeHub {
  state = "Disconnected";
  events = new Map<string, (change: unknown) => void>();
  reconnecting: Callback = () => {};
  reconnected: Callback = () => {};
  closed: Callback = () => {};
  invoke = vi.fn(() => Promise.resolve());
  start = vi.fn(() => {
    this.state = "Connected";
    return Promise.resolve();
  });
  stop = vi.fn(() => {
    this.state = "Disconnected";
    return Promise.resolve();
  });
  on(name: string, handler: (change: unknown) => void) {
    this.events.set(name, handler);
  }
  onreconnecting(callback: Callback) {
    this.reconnecting = callback;
  }
  onreconnected(callback: Callback) {
    this.reconnected = callback;
  }
  onclose(callback: Callback) {
    this.closed = callback;
  }
}

/** Every hub built in this test, and the URL and options each was built with (setup.ts empties them). */
export const hubs = { all: [] as FakeHub[], urls: [] as unknown[] };

export const latestHub = () => hubs.all[hubs.all.length - 1];

export const HttpTransportType = { WebSockets: 1 };
export const HubConnectionState = { Connected: "Connected" };
export const LogLevel = { Warning: 3 };

export class HubConnectionBuilder {
  withUrl(url: string, options: unknown) {
    hubs.urls.push({ url, options });
    return this;
  }
  withAutomaticReconnect() {
    return this;
  }
  configureLogging() {
    return this;
  }
  build() {
    const hub = new FakeHub();
    hubs.all.push(hub);
    return hub;
  }
}
