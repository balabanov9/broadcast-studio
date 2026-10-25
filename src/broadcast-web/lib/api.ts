export type Channel = {
  id: string;
  name: string;
  slug: string;
  slot: number;
  revision: number;
  autopilot: boolean;
  previewId: string | null;
  programId: string | null;
  fallbackId: string;
  programSince: string | null;
  graphicsOnAir: boolean;
  graphicsJson: string;
  status: string;
  error: string | null;
  ingestKey: string;
};
export type Source = {
  id: string;
  name: string;
  kind: 0 | 1 | 2 | 3;
  locator: string;
  durationSeconds: number;
};
export type Playlist = {
  id: string;
  name: string;
  items: { sourceId: string; durationSeconds: number; position: number }[];
};
export type Block = {
  id: string;
  title: string;
  sourceId: string | null;
  playlistId: string | null;
  startsAt: string;
  endsAt: string;
};
export type Graphic = {
  id: string;
  kind: "logo" | "title" | "ticker" | "subtitle" | "image";
  text: string;
  x: number;
  y: number;
  width: number;
  height: number;
  fontSize: number;
  color: string;
  background: string;
  imageId?: string | null;
};
export type Detail = {
  channel: Channel;
  sources: Source[];
  playlists: Playlist[];
  schedule: Block[];
  events: { id: number; at: string; message: string }[];
};
export class ApiError extends Error {
  constructor(
    public status: number,
    message: string,
  ) {
    super(message);
  }
}
export async function api<T = void>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  const form = body instanceof FormData;
  const response = await fetch(path, {
    method,
    credentials: "same-origin",
    headers: body && !form ? { "Content-Type": "application/json" } : {},
    body: body ? (form ? body : JSON.stringify(body)) : undefined,
    cache: "no-store",
  });
  if (!response.ok) {
    const data = await response.json().catch(() => null);
    throw new ApiError(
      response.status,
      data?.error ??
        (response.status === 401
          ? "Требуется вход оператора"
          : response.status === 429
            ? "Слишком много попыток. Подождите минуту."
            : `Ошибка запроса: ${response.status}`),
    );
  }
  return response.status === 204
    ? (undefined as T)
    : response.json().catch(() => undefined);
}
export function duration(seconds: number) {
  return new Date(Math.max(0, seconds) * 1000).toISOString().slice(11, 19);
}
export function time(value: string) {
  return new Date(value).toLocaleTimeString("ru-RU", {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
  });
}
