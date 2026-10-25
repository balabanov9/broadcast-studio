"use client";
import { useCallback, useEffect, useRef, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import {
  Activity,
  ArrowRight,
  CalendarDays,
  Clapperboard,
  Copy,
  Film,
  Layers3,
  LogOut,
  MonitorPlay,
  Plus,
  Radio,
  Settings2,
  ShieldCheck,
  Upload,
  Waves,
  X,
  Trash2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Monitor } from "@/components/monitor";
import { StoragePanel } from "@/components/storage-panel";
import { OperatorSettings } from "@/components/operator-settings";
import { RegionalPolicy } from "@/components/regional-policy";
import { GraphicsEditor } from "@/components/graphics-editor";
import {
  api,
  ApiError,
  duration,
  time,
  type Channel,
  type Detail,
  type Graphic,
} from "@/lib/api";
const tabs = [
  { id: "control", title: "Аппаратная", icon: MonitorPlay },
  { id: "media", title: "Медиатека", icon: Film },
  { id: "schedule", title: "Расписание", icon: CalendarDays },
  { id: "graphics", title: "Графика", icon: Layers3 },
  { id: "iptv", title: "IPTV и подключения", icon: Waves },
  { id: "settings", title: "Настройки", icon: Settings2 },
];
function Logo() {
  return (
    <div className="brand">
      <div className="brand-symbol">
        <span />
        <span />
        <span />
      </div>
      <div>
        LUMIÈRE<small>BROADCAST SYSTEMS</small>
      </div>
    </div>
  );
}
export default function Studio() {
  const [auth, setAuth] = useState<boolean | null>(null);
  const [password, setPassword] = useState("");
  const [channels, setChannels] = useState<Channel[]>([]);
  const [id, setId] = useState("");
  const [data, setData] = useState<Detail | null>(null);
  const [tab, setTab] = useState("control");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [connected, setConnected] = useState(false);
  const [clock, setClock] = useState("");
  const [now, setNow] = useState(0);
  const [newChannel, setNewChannel] = useState(false);
  const [frames, setFrames] = useState(25);
  const idRef = useRef(id);
  const refresh = useCallback(async () => {
    const list = await api<Channel[]>("/api/channels");
    setChannels(list);
    const selected = idRef.current || list[0]?.id;
    if (selected && !idRef.current) setId(selected);
    if (selected) {
      const detail = await api<Detail>(`/api/channels/${selected}`);
      if (selected === idRef.current || !idRef.current) setData(detail);
    }
  }, []);
  useEffect(() => {
    api("/api/session")
      .then(() => setAuth(true))
      .catch((e) => {
        setAuth(false);
        if (!(e instanceof ApiError && e.status === 401)) setError(e.message);
      });
    const timer = setInterval(() => {
      setClock(new Date().toLocaleTimeString("ru-RU"));
      setNow(Date.now());
    }, 1000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    idRef.current = id;
    if (auth) {
      let cancelled = false;
      Promise.resolve()
        .then(() => {
          if (!cancelled) return refresh();
        })
        .catch((e) => setError(e.message));
      return () => {
        cancelled = true;
      };
    }
  }, [id, auth, refresh]);
  useEffect(() => {
    if (!auth) return;
    let active = true;
    const report = (e: Error) => {
      if (active) {
        setError(e.message);
        if (e instanceof ApiError && e.status === 401) setAuth(false);
      }
    };
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/air")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Error)
      .build();
    connection.on("changed", () => {
      refresh().catch(report);
    });
    connection.onreconnected(() => {
      if (active) setConnected(true);
      refresh().catch(report);
    });
    connection.onreconnecting(() => {
      if (active) setConnected(false);
    });
    connection.onclose(() => {
      if (active) setConnected(false);
    });
    connection
      .start()
      .then(() => {
        if (active) setConnected(true);
      })
      .catch(() => {
        if (active) setConnected(false);
      });
    const timer = setInterval(() => refresh().catch(report), 5000);
    return () => {
      active = false;
      clearInterval(timer);
      connection.stop();
    };
  }, [auth, refresh]);
  async function action(work: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    try {
      await work();
      if (auth) await refresh();
    } catch (e) {
      setError((e as Error).message);
      if (e instanceof ApiError && e.status === 401) setAuth(false);
      throw e;
    } finally {
      setBusy(false);
    }
  }
  function mutate(path: string, body: unknown, method = "POST") {
    return action(() => api(`/api/channels/${id}/${path}`, method, body)).catch(
      () => {},
    );
  }
  if (auth === null)
    return (
      <main className="login-screen">
        <Logo />
        <p className="mt-6 text-slate-400">Подключение к аппаратной…</p>
      </main>
    );
  if (!auth)
    return (
      <main className="login-screen">
        <div className="login-card">
          <Logo />
          <div className="eyebrow mt-12">РАБОЧЕЕ МЕСТО ОПЕРАТОРА</div>
          <h1 className="mt-3 text-3xl">Ваш эфир. Под контролем.</h1>
          <p className="mb-8 mt-3 text-sm leading-6 text-slate-400">
            Войдите, чтобы управлять каналами, программой и эфирной графикой.
          </p>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              action(async () => {
                await api("/api/login", "POST", { password });
                setPassword("");
                setAuth(true);
              }).catch(() => {});
            }}
          >
            <label>
              Пароль оператора
              <input
                type="password"
                autoComplete="current-password"
                required
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                placeholder="Введите пароль"
              />
            </label>
            {error && (
              <div role="alert" className="error mt-4">
                {error}
              </div>
            )}
            <Button className="mt-6 w-full" disabled={busy}>
              Войти в аппаратную
              <ArrowRight size={15} />
            </Button>
          </form>
          <div className="mt-8 flex items-center gap-2 text-xs text-slate-500">
            <ShieldCheck size={14} />
            Доступ к управлению защищён
          </div>
        </div>
        <span className="login-footer">
          LUMIÈRE / BROADCAST AUTOMATION / MVP 0.1
        </span>
      </main>
    );
  const visible = data?.channel.id === id ? data : null;
  const c = visible?.channel;
  const preview = data?.sources.find((s) => s.id === c?.previewId);
  const program = data?.sources.find((s) => s.id === c?.programId);
  const activeBlock = data?.schedule.find(
    (b) =>
      new Date(b.startsAt).getTime() <= now &&
      new Date(b.endsAt).getTime() > now,
  );
  return (
    <div className="studio-shell">
      <aside className="sidebar">
        <Logo />
        <div className="sidebar-label">РАБОЧЕЕ ПРОСТРАНСТВО</div>
        <nav>
          {tabs.map((item) => (
            <button
              key={item.id}
              className={tab === item.id ? "active" : ""}
              onClick={() => setTab(item.id)}
            >
              <item.icon size={18} />
              <span>{item.title}</span>
              {tab === item.id && <span className="nav-dot" />}
            </button>
          ))}
        </nav>
        <div className="sidebar-bottom">
          <div className="mb-5 flex items-center gap-2 text-xs text-slate-400">
            <span className={`status-led ${connected ? "online" : ""}`} />
            {connected ? "События подключены" : "Обновление каждые 5 с"}
          </div>
          <button
            onClick={() =>
              action(async () => {
                await api("/api/logout", "POST");
                setAuth(false);
                setData(null);
              }).catch(() => {})
            }
          >
            <LogOut size={16} />
            Выйти из студии
          </button>
          <small>ВЕРСИЯ 0.1 · MVP</small>
        </div>
      </aside>
      <div className="main-shell">
        <header className="topbar">
          <div className="flex items-center gap-3">
            <span className="workspace-icon">
              <Radio size={17} />
            </span>
            <select
              aria-label="Телеканал"
              value={id}
              onChange={(e) => setId(e.target.value)}
            >
              {!channels.length && <option value="">Нет телеканалов</option>}
              {channels.map((ch) => (
                <option key={ch.id} value={ch.id}>
                  {ch.name}
                </option>
              ))}
            </select>
            <Button
              variant="ghost"
              size="icon"
              aria-label="Добавить телеканал"
              onClick={() => setNewChannel(true)}
            >
              <Plus size={16} />
            </Button>
          </div>
          <div className="topbar-right">
            <span className="tag">1080p25</span>
            <div className="clock">
              {clock || "—:—:—"}
              <small>МЕСТНОЕ ВРЕМЯ</small>
            </div>
            <div className="avatar">ОП</div>
          </div>
        </header>
        <main className="workspace">
          <div className="page-heading">
            <div>
              <div className="eyebrow">
                СТУДИЯ /{" "}
                {tabs
                  .find((t) => t.id === tab)
                  ?.title.toLocaleUpperCase("ru-RU")}
              </div>
              <h1>
                {tab === "control"
                  ? "Эфирная аппаратная"
                  : tabs.find((t) => t.id === tab)?.title}
              </h1>
              <p>
                {tab === "control"
                  ? "Каждый кадр на своём месте."
                  : "Единое пространство управления телеканалом."}
              </p>
            </div>
            {c && (
              <span
                className={`air-badge ${c.programId && !c.error ? "on" : ""}`}
              >
                <span className="dot" />
                {c.error
                  ? "ОШИБКА ДВИЖКА"
                  : c.programId
                    ? "КОМАНДА ЭФИРА ПРИНЯТА"
                    : "ЭФИР НЕ ЗАПУЩЕН"}
              </span>
            )}
          </div>
          {error && (
            <div
              role="alert"
              className="error mb-5 flex items-center justify-between"
            >
              {error}
              <button
                aria-label="Закрыть уведомление"
                onClick={() => setError("")}
              >
                <X size={16} />
              </button>
            </div>
          )}
          {newChannel && (
            <form
              className="panel mb-6 flex flex-wrap items-end gap-4 p-5"
              onSubmit={(e) => {
                e.preventDefault();
                const f = new FormData(e.currentTarget);
                action(async () => {
                  const ch = await api<Channel>("/api/channels", "POST", {
                    name: f.get("name"),
                    slug: f.get("slug"),
                  });
                  setId(ch.id);
                  setNewChannel(false);
                }).catch(() => {});
              }}
            >
              <label>
                Название
                <input
                  name="name"
                  maxLength={80}
                  required
                  placeholder="Первый канал"
                />
              </label>
              <label>
                Адрес канала
                <input
                  name="slug"
                  pattern="[a-z][a-z0-9-]{1,39}"
                  required
                  placeholder="channel-one"
                />
              </label>
              <Button disabled={busy}>Создать канал</Button>
              <Button
                type="button"
                variant="ghost"
                onClick={() => setNewChannel(false)}
              >
                Отмена
              </Button>
            </form>
          )}
          {!channels.length ? (
            <section className="empty-state panel">
              <Radio size={40} />
              <h2>Здесь начинается ваш эфир</h2>
              <p>
                Создайте первый телеканал. Подготовим OBS-вход и резервный
                источник.
              </p>
              <Button onClick={() => setNewChannel(true)}>
                <Plus size={16} />
                Создать телеканал
              </Button>
            </section>
          ) : !data || !c ? (
            <section className="panel p-10 text-slate-400">
              Загрузка состояния канала…
            </section>
          ) : (
            <>
              {c.error && (
                <div className="error mb-5">
                  {c.error}. Проверьте CasparCG и MediaMTX; состояние
                  видеопотока видно в мониторе.
                </div>
              )}
              {tab === "control" && (
                <>
                  <div className="monitors">
                    <Monitor
                      key={`pv-${id}`}
                      label="PREVIEW"
                      name={preview?.name}
                      url={
                        preview
                          ? `/hls/preview/${c.slug}/index.m3u8`
                          : undefined
                      }
                    />
                    <Monitor
                      key={`pg-${id}`}
                      label="PROGRAM"
                      program
                      name={program?.name}
                      url={
                        program
                          ? `/hls/program/${c.slug}/index.m3u8`
                          : undefined
                      }
                    />
                  </div>
                  <section className="switcher panel">
                    <div className="flex items-center gap-3">
                      <span className="switcher-icon">
                        <Clapperboard size={20} />
                      </span>
                      <div>
                        <h3>Переход в эфир</h3>
                        <p>Выберите Preview, затем выполните переход</p>
                      </div>
                    </div>
                    <div className="flex flex-wrap items-center gap-3">
                      <label className="inline-label">
                        Длительность
                        <select
                          value={frames}
                          onChange={(e) => setFrames(Number(e.target.value))}
                        >
                          <option value={12}>12 кадров</option>
                          <option value={25}>25 кадров</option>
                          <option value={50}>50 кадров</option>
                        </select>
                      </label>
                      <Button
                        variant="outline"
                        className="cut-button"
                        disabled={busy || !preview}
                        onClick={() =>
                          mutate("take", {
                            transition: "CUT",
                            frames: 0,
                            revision: c.revision,
                          })
                        }
                      >
                        CUT
                        <ArrowRight size={15} />
                      </Button>
                      <Button
                        className="mix-button"
                        disabled={busy || !preview}
                        onClick={() =>
                          mutate("take", {
                            transition: "MIX",
                            frames,
                            revision: c.revision,
                          })
                        }
                      >
                        MIX
                        <ArrowRight size={15} />
                      </Button>
                    </div>
                  </section>
                  <div className="control-bottom">
                    <section className="panel">
                      <div className="panel-title">
                        <span>
                          Источники сигнала <small>{data.sources.length}</small>
                        </span>
                        <button
                          onClick={() => setTab("media")}
                          className="text-xs text-teal-300"
                        >
                          Медиатека →
                        </button>
                      </div>
                      <div className="source-grid">
                        {data.sources.map((s, n) => (
                          <button
                            key={s.id}
                            disabled={busy}
                            onClick={() =>
                              mutate("preview", {
                                sourceId: s.id,
                                revision: c.revision,
                              })
                            }
                            className={`source-card ${s.id === c.previewId ? "is-preview" : ""} ${s.id === c.programId ? "is-program" : ""}`}
                          >
                            <div
                              className="source-image"
                              style={
                                s.kind === 0
                                  ? { background: s.locator }
                                  : undefined
                              }
                            >
                              {s.kind === 2 ? (
                                <Radio size={26} />
                              ) : s.kind === 1 || s.kind === 3 ? (
                                <Film size={26} />
                              ) : (
                                <span className="color-mark" />
                              )}
                              <span className="source-number">
                                {String(n + 1).padStart(2, "0")}
                              </span>
                              <div className="source-flags">
                                {s.id === c.programId && (
                                  <b className="pgm">PGM</b>
                                )}
                                {s.id === c.previewId && (
                                  <b className="pvw">PVW</b>
                                )}
                              </div>
                            </div>
                            <div className="source-caption">
                              <strong>{s.name}</strong>
                              <small>
                                {s.kind === 2
                                  ? "OBS / RTMP"
                                  : s.kind === 3
                                    ? "S3 / ПРЯМОЙ ИСТОЧНИК"
                                    : s.kind === 1
                                      ? `ВИДЕО / ${duration(s.durationSeconds)}`
                                      : "ЦВЕТОВОЕ ПОЛЕ"}
                              </small>
                            </div>
                          </button>
                        ))}
                      </div>
                    </section>
                    <section className="panel autopilot">
                      <div className="panel-title">
                        <span>
                          <Activity size={16} />
                          Автопилот
                        </span>
                        <span
                          className={`tag ${c.autopilot ? "text-teal-300" : ""}`}
                        >
                          {c.autopilot ? "ВКЛЮЧЁН" : "РУЧНОЙ"}
                        </span>
                      </div>
                      <div className="p-5">
                        <div className="eyebrow mb-2">
                          ТЕКУЩИЙ БЛОК ПО РАСПИСАНИЮ
                        </div>
                        <h3 className="text-lg">
                          {activeBlock?.title || "Резервный эфир"}
                        </h3>
                        <p className="my-3 text-xs text-slate-400">
                          {activeBlock
                            ? `${time(activeBlock.startsAt)} — ${time(activeBlock.endsAt)}`
                            : "Вне расписания воспроизводится резерв"}
                        </p>
                        <label>
                          Резервный источник
                          <select
                            value={c.fallbackId}
                            disabled={busy}
                            onChange={(e) =>
                              mutate("autopilot", {
                                enabled: c.autopilot,
                                fallbackId: e.target.value,
                                revision: c.revision,
                              })
                            }
                          >
                            {data.sources
                              .filter((s) => s.kind !== 2)
                              .map((s) => (
                                <option key={s.id} value={s.id}>
                                  {s.name}
                                </option>
                              ))}
                          </select>
                        </label>
                        <Button
                          className="mt-4 w-full"
                          variant={c.autopilot ? "outline" : "default"}
                          disabled={busy}
                          onClick={() =>
                            mutate("autopilot", {
                              enabled: !c.autopilot,
                              fallbackId: c.fallbackId,
                              revision: c.revision,
                            })
                          }
                        >
                          {c.autopilot
                            ? "Отключить автопилот"
                            : "Запустить автопилот"}
                        </Button>
                      </div>
                    </section>
                  </div>
                  <section className="panel mt-5">
                    <div className="panel-title">
                      <span>Журнал эфира</span>
                      <span className="tag">ПОСЛЕДНИЕ СОБЫТИЯ</span>
                    </div>
                    <div className="event-log">
                      {data.events.length ? (
                        data.events.map((e) => (
                          <div key={e.id}>
                            <time>{time(e.at)}</time>
                            <span className="status-led online" />
                            <span>{e.message}</span>
                          </div>
                        ))
                      ) : (
                        <p className="p-5 text-sm text-slate-500">
                          События появятся после первой команды управления.
                        </p>
                      )}
                    </div>
                  </section>
                </>
              )}
              {tab === "media" && (
                <div className="grid gap-5 lg:grid-cols-[1fr_340px]">
                  <section className="panel">
                    <div className="panel-title">
                      Библиотека канала
                      <span className="tag">
                        {data.sources.length} ИСТОЧНИКОВ
                      </span>
                    </div>
                    <div className="p-5">
                      <label className="upload-zone">
                        <Upload size={30} />
                        <strong>Загрузить видео</strong>
                        <span>
                          MP4, MOV, MKV, WebM · до 2 ГБ · проверка FFprobe
                        </span>
                        <input
                          type="file"
                          accept=".mp4,.mov,.mkv,.webm"
                          disabled={busy}
                          onChange={(e) => {
                            const file = e.target.files?.[0];
                            if (!file) return;
                            const form = new FormData();
                            form.append("file", file);
                            mutate("media", form);
                            e.target.value = "";
                          }}
                        />
                      </label>
                      {busy && (
                        <p className="my-3 text-sm text-teal-300">
                          Обработка запроса…
                        </p>
                      )}
                      <div className="table-wrap">
                        <table>
                          <thead>
                            <tr>
                              <th>Название</th>
                              <th>Тип</th>
                              <th>Длительность</th>
                            </tr>
                          </thead>
                          <tbody>
                            {data.sources.map((s) => (
                              <tr key={s.id}>
                                <td>{s.name}</td>
                                <td>
                                  {
                                    [
                                      "Цветовое поле",
                                      "Видео",
                                      "Прямой эфир",
                                      "S3 · напрямую",
                                    ][s.kind]
                                  }
                                </td>
                                <td>
                                  {s.kind === 1 || s.kind === 3
                                    ? duration(s.durationSeconds)
                                    : "—"}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                    </div>
                  </section>
                  <section className="panel self-start p-5">
                    <h3 className="mb-5">Добавить цветовой источник</h3>
                    <form
                      className="space-y-4"
                      onSubmit={(e) => {
                        e.preventDefault();
                        const form = new FormData(e.currentTarget);
                        mutate("sources", {
                          name: form.get("name"),
                          locator: form.get("color"),
                          kind: 0,
                        });
                      }}
                    >
                      <label>
                        Название
                        <input
                          required
                          name="name"
                          maxLength={100}
                          placeholder="Перебивка"
                        />
                      </label>
                      <label>
                        Цвет
                        <input
                          name="color"
                          type="color"
                          defaultValue="#143536"
                        />
                      </label>
                      <Button disabled={busy}>
                        <Plus size={15} />
                        Добавить
                      </Button>
                    </form>
                  </section>
                </div>
              )}
              {tab === "media" && (
                <StoragePanel key={id} id={id} refresh={refresh} />
              )}
              {tab === "settings" && (
                <OperatorSettings
                  onChanged={() => {
                    setAuth(false);
                    setError("Пароль изменён. Войдите с новым паролем.");
                  }}
                />
              )}
              {tab === "schedule" && (
                <SchedulePanel
                  key={id}
                  data={data}
                  busy={busy}
                  mutate={mutate}
                />
              )}
              {tab === "graphics" && (
                <GraphicsEditor
                  key={id}
                  initial={JSON.parse(c.graphicsJson).elements as Graphic[]}
                  busy={busy}
                  on={c.graphicsOnAir}
                  onSave={async (scene) => {
                    try {
                      await action(() =>
                        api(`/api/channels/${id}/graphics`, "PUT", {
                          scene: { elements: scene },
                          revision: c.revision,
                        }),
                      );
                      return true;
                    } catch {
                      return false;
                    }
                  }}
                  onAir={() =>
                    mutate("graphics", { enabled: true, revision: c.revision })
                  }
                  onHide={() =>
                    mutate("graphics", { enabled: false, revision: c.revision })
                  }
                />
              )}
              {tab === "iptv" && (
                <>
                  <Connections c={c} />
                  <RegionalPolicy key={id} c={c} refresh={refresh} />
                </>
              )}
            </>
          )}
          <footer className="workspace-footer">
            <span>LUMIÈRE BROADCAST</span>
            <span>
              {c
                ? `КАНАЛ ${String(c.slot).padStart(2, "0")} / РЕВИЗИЯ ${c.revision}`
                : "СТУДИЯ ГОТОВА К НАСТРОЙКЕ"}
            </span>
          </footer>
        </main>
      </div>
    </div>
  );
}
function SchedulePanel({
  data,
  busy,
  mutate,
}: {
  data: Detail;
  busy: boolean;
  mutate: (path: string, body: unknown, method?: string) => Promise<void>;
}) {
  const [items, setItems] = useState<
    { sourceId: string; durationSeconds: number }[]
  >([]);
  const [choice, setChoice] = useState(data.sources[0]?.id ?? "");
  const [seconds, setSeconds] = useState(60);
  return (
    <div className="schedule-layout">
      <section className="panel">
        <div className="panel-title">
          <span>Программа передач</span>
          <span className="tag">ВРЕМЯ ВАШЕГО БРАУЗЕРА</span>
        </div>
        <div className="p-5">
          <form
            className="grid gap-4 sm:grid-cols-2"
            onSubmit={(e) => {
              e.preventDefault();
              const f = new FormData(e.currentTarget);
              const [kind, selected] = String(f.get("target")).split(":");
              mutate("schedule", {
                title: f.get("title"),
                sourceId: kind === "source" ? selected : null,
                playlistId: kind === "playlist" ? selected : null,
                startsAt: new Date(String(f.get("start"))).toISOString(),
                endsAt: new Date(String(f.get("end"))).toISOString(),
              });
            }}
          >
            <label>
              Название передачи
              <input
                name="title"
                required
                maxLength={140}
                placeholder="Утренний эфир"
              />
            </label>
            <label>
              Наполнение
              <select name="target">
                {data.sources.map((s) => (
                  <option key={s.id} value={`source:${s.id}`}>
                    {s.name}
                  </option>
                ))}
                {data.playlists.map((p) => (
                  <option key={p.id} value={`playlist:${p.id}`}>
                    Плейлист: {p.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Начало
              <input type="datetime-local" name="start" required />
            </label>
            <label>
              Окончание
              <input type="datetime-local" name="end" required />
            </label>
            <Button className="sm:col-span-2" disabled={busy}>
              <Plus size={14} />
              Добавить блок эфира
            </Button>
          </form>
        </div>
        <div className="rundown">
          {data.schedule.length ? (
            data.schedule.map((b) => (
              <article key={b.id}>
                <div className="rundown-time">
                  <strong>{time(b.startsAt).slice(0, 5)}</strong>
                  <small>
                    {new Date(b.startsAt).toLocaleDateString("ru-RU")}
                  </small>
                </div>
                <div className="flex-1">
                  <h3>{b.title}</h3>
                  <p>
                    До {time(b.endsAt).slice(0, 5)} ·{" "}
                    {b.playlistId
                      ? "Плейлист"
                      : data.sources.find((s) => s.id === b.sourceId)?.name}
                  </p>
                </div>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Удалить блок ${b.title}`}
                  disabled={busy}
                  onClick={() =>
                    mutate(`schedule/${b.id}`, undefined, "DELETE")
                  }
                >
                  <Trash2 size={15} />
                </Button>
              </article>
            ))
          ) : (
            <p className="px-5 pb-6 text-sm text-slate-500">
              Расписание пока пустое. Автопилот использует резервный источник.
            </p>
          )}
        </div>
      </section>
      <section className="panel self-start p-5">
        <h3 className="mb-5">Новый плейлист</h3>
        <div className="space-y-4">
          <label>
            Источник
            <select value={choice} onChange={(e) => setChoice(e.target.value)}>
              {data.sources.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Длительность, секунд
            <input
              type="number"
              min={1}
              max={86400}
              value={seconds}
              onChange={(e) => setSeconds(Number(e.target.value))}
            />
          </label>
          <Button
            variant="outline"
            disabled={
              !choice || seconds < 1 || seconds > 86400 || items.length >= 100
            }
            onClick={() =>
              setItems([
                ...items,
                { sourceId: choice, durationSeconds: seconds },
              ])
            }
          >
            <Plus size={14} />В очередь
          </Button>
          <ol className="playlist-items">
            {items.map((item, i) => (
              <li key={i}>
                <span>
                  {i + 1}.{" "}
                  {data.sources.find((s) => s.id === item.sourceId)?.name}{" "}
                  <small>{duration(item.durationSeconds)}</small>
                </span>
                <button
                  aria-label={`Удалить позицию ${i + 1}`}
                  onClick={() => setItems(items.filter((_, n) => n !== i))}
                >
                  <X size={14} />
                </button>
              </li>
            ))}
          </ol>
          <form
            className="space-y-3"
            onSubmit={(e) => {
              e.preventDefault();
              const f = new FormData(e.currentTarget);
              mutate("playlists", { name: f.get("name"), items });
            }}
          >
            <label>
              Название
              <input
                name="name"
                required
                maxLength={100}
                placeholder="Дневная программа"
              />
            </label>
            <Button disabled={busy || !items.length} className="w-full">
              Сохранить плейлист
            </Button>
          </form>
          <p className="text-xs leading-5 text-slate-500">
            Позиции повторяются до конца блока. Сохранённый плейлист доступен в
            наполнении расписания.
          </p>
          <div className="border-t border-white/10 pt-4">
            {data.playlists.map((p) => (
              <div key={p.id} className="mb-3 text-sm">
                {p.name}
                <small className="ml-2 text-slate-500">
                  {p.items.length} поз.
                </small>
              </div>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
function Connections({ c }: { c: Channel }) {
  const [show, setShow] = useState(false);
  const [notice, setNotice] = useState("");
  const host =
    typeof window !== "undefined" ? window.location.hostname : "localhost";
  const origin = typeof window !== "undefined" ? window.location.origin : "";
  const values = [
    { label: "Сервер OBS", value: `rtmp://${host}:1935/ingest` },
    {
      label: "Ключ потока OBS",
      value: `${c.id.replaceAll("-", "")}?user=obs&pass=${c.ingestKey}`,
      secret: true,
    },
    {
      label: "HLS-поток канала",
      value: `${origin}/hls/program/${c.slug}/index.m3u8`,
    },
    { label: "M3U — все телеканалы", value: `${origin}/iptv/playlist.m3u` },
    { label: "XMLTV EPG", value: `${origin}/iptv/epg.xml` },
  ];
  return (
    <div className="grid gap-5 lg:grid-cols-[1fr_320px]">
      <section className="panel p-6">
        <h3 className="mb-6 text-lg">Подключения и выходы</h3>
        <div className="space-y-6">
          {values.map((item) => (
            <div key={item.label}>
              <div className="mb-2 flex justify-between text-xs text-slate-400">
                <span>{item.label}</span>
                {item.secret && (
                  <button
                    className="text-teal-300"
                    onClick={() => setShow(!show)}
                  >
                    {show ? "Скрыть" : "Показать"}
                  </button>
                )}
              </div>
              <div className="connection-value">
                <code>
                  {item.secret && !show
                    ? "••••••••••••••••••••••••"
                    : item.value}
                </code>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Скопировать ${item.label}`}
                  onClick={async () => {
                    try {
                      await navigator.clipboard.writeText(item.value);
                      setNotice("Скопировано");
                    } catch {
                      setNotice(
                        "Не удалось скопировать. Выделите адрес вручную.",
                      );
                    }
                  }}
                >
                  <Copy size={15} />
                </Button>
              </div>
            </div>
          ))}
        </div>
        <p role="status" className="mt-4 text-sm text-teal-300">
          {notice}
        </p>
      </section>
      <aside className="panel self-start p-6">
        <Settings2 className="mb-4 text-teal-300" size={25} />
        <h3>OBS → эфир → IPTV</h3>
        <p className="mt-3 text-sm leading-7 text-slate-400">
          В OBS укажите тип сервиса «Настраиваемый», сервер и ключ потока.
          Запустите передачу, выберите источник OBS в Preview и нажмите CUT или
          MIX.
        </p>
        <p className="mt-4 text-sm leading-7 text-slate-400">
          Для плееров IPTV добавьте M3U-плейлист. EPG строится из расписания;
          время экспортируется в UTC.
        </p>
        <p className="mt-4 text-xs leading-6 text-slate-500">
          Адрес OBS рассчитан для стандартного порта 1935. При внешнем CasparCG
          перенесите шаблон lumiere.html из папки graphics.
        </p>
      </aside>
    </div>
  );
}
