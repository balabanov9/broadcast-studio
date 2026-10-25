"use client";
import { useCallback, useEffect, useState } from "react";
import { Cloud, RefreshCw, Link2, Download } from "lucide-react";
import { Button } from "@/components/ui/button";
import { api } from "@/lib/api";
type Config = {
  endpoint: string;
  bucket: string;
  region: string;
  configured: boolean;
  pathStyle: boolean;
};
type Preset = {
  id: string;
  name: string;
  endpoint: string;
  region: string;
  pathStyle: boolean;
};
type Page = {
  objects: { key: string; size: number }[];
  nextToken: string | null;
};
type Job = {
  id: string;
  objectKey: string;
  status: string;
  bytesReceived: number;
  totalBytes: number | null;
  error: string | null;
};
const bytes = (n: number) => `${(n / 1024 / 1024).toFixed(1)} МБ`;
export function StoragePanel({
  id,
  refresh,
}: {
  id: string;
  refresh: () => Promise<void>;
}) {
  const base = `/api/channels/${id}/storage`;
  const [config, setConfig] = useState<Config>({
    endpoint: "",
    bucket: "",
    region: "us-east-1",
    configured: false,
    pathStyle: true,
  });
  const [providers, setProviders] = useState<Preset[]>([]);
  const [provider, setProvider] = useState("");
  const [accessKey, setAccessKey] = useState("");
  const [secret, setSecret] = useState("");
  const [sessionToken, setSessionToken] = useState("");
  const [prefix, setPrefix] = useState("");
  const [objects, setObjects] = useState<Page>({
    objects: [],
    nextToken: null,
  });
  const [jobs, setJobs] = useState<Job[]>([]);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [seconds, setSeconds] = useState(60);
  const loadJobs = useCallback(
    () => api<Job[]>(`${base}/imports`).then(setJobs),
    [base],
  );
  useEffect(() => {
    let active = true;
    api<Preset[]>("/api/storage/providers")
      .then((list) => {
        if (active) setProviders(list);
      })
      .catch(() => {});
    api<Config | null>(base)
      .then((c) => {
        if (active && c) setConfig(c);
      })
      .catch((e) => {
        if (active) setMessage(e.message);
      });
    const timer = setInterval(() => {
      if (active) loadJobs().catch(() => {});
    }, 4000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [base, loadJobs]);
  async function work(fn: () => Promise<void>) {
    setBusy(true);
    setMessage("");
    try {
      await fn();
    } catch (e) {
      setMessage((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function list(token?: string) {
    await work(async () => {
      const result = await api<Page>(
        `${base}/objects?prefix=${encodeURIComponent(prefix)}${token ? `&token=${encodeURIComponent(token)}` : ""}`,
      );
      setObjects((previous) => ({
        ...result,
        objects: token
          ? [...previous.objects, ...result.objects]
          : result.objects,
      }));
    });
  }
  return (
    <section className="panel mt-5">
      <div className="panel-title">
        <span>
          <Cloud size={17} />
          Внешнее хранилище S3
        </span>
        <span className="tag">ПРЯМОЕ ВОСПРОИЗВЕДЕНИЕ</span>
      </div>
      <div className="grid gap-7 p-6 xl:grid-cols-[330px_1fr]">
        <form
          className="space-y-4"
          autoComplete="off"
          onSubmit={(e) => {
            e.preventDefault();
            work(async () => {
              await api(base, "PUT", {
                endpoint: config.endpoint,
                bucket: config.bucket,
                region: config.region,
                pathStyle: config.pathStyle,
                accessKey,
                secretKey: secret,
                sessionToken: sessionToken || null,
              });
              setConfig({ ...config, configured: true });
              setAccessKey("");
              setSecret("");
              setSessionToken("");
              setMessage(
                "Подключение сохранено. Ключи зашифрованы на сервере.",
              );
              await refresh();
            });
          }}
        >
          <label>
            Провайдер
            <select
              value={provider}
              onChange={(e) => {
                setProvider(e.target.value);
                const p = providers.find((x) => x.id === e.target.value);
                if (p)
                  setConfig({
                    ...config,
                    endpoint: p.endpoint,
                    region: p.region,
                    pathStyle: p.pathStyle,
                  });
              }}
            >
              <option value="">Свой S3-совместимый провайдер</option>
              {providers.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </label>
          <label>
            Endpoint S3
            <input
              type="url"
              required
              value={config.endpoint}
              placeholder="https://s3.example.com"
              onChange={(e) =>
                setConfig({ ...config, endpoint: e.target.value })
              }
            />
          </label>
          <div className="grid grid-cols-2 gap-3">
            <label>
              Bucket
              <input
                required
                value={config.bucket}
                placeholder="broadcast-media"
                onChange={(e) =>
                  setConfig({ ...config, bucket: e.target.value })
                }
              />
            </label>
            <label>
              Регион
              <input
                required
                value={config.region}
                onChange={(e) =>
                  setConfig({ ...config, region: e.target.value })
                }
              />
            </label>
          </div>
          <label className="flex items-center gap-3">
            <input
              className="!m-0 !w-4"
              type="checkbox"
              checked={config.pathStyle}
              onChange={(e) =>
                setConfig({ ...config, pathStyle: e.target.checked })
              }
            />
            Path-style (иначе bucket.endpoint)
          </label>
          <label>
            Access Key
            <input
              value={accessKey}
              autoComplete="off"
              placeholder={
                config.configured
                  ? "Сохранён на сервере"
                  : "Идентификатор ключа"
              }
              required={!config.configured || !!secret}
              onChange={(e) => setAccessKey(e.target.value)}
            />
          </label>
          <label>
            Secret Key
            <input
              type="password"
              value={secret}
              autoComplete="new-password"
              required={!config.configured}
              placeholder={
                config.configured
                  ? "Оставьте пустым, чтобы сохранить"
                  : "Секретный ключ"
              }
              onChange={(e) => setSecret(e.target.value)}
            />
          </label>
          <label>
            Session Token — необязательно
            <input
              type="password"
              value={sessionToken}
              autoComplete="new-password"
              onChange={(e) => setSessionToken(e.target.value)}
            />
          </label>
          <Button disabled={busy}>Сохранить подключение</Button>
          <p className="text-xs leading-6 text-slate-500">
            HTTPS, S3 Signature V4, адресация path-style и virtual-hosted.
            Достаточно прав ListBucket и GetObject. Для прямого эфира
            используйте MP4 с faststart и хранилище с поддержкой Range.
          </p>
        </form>
        <div>
          <h3 className="mb-3">Видео без загрузки на сервер</h3>
          <p className="mb-5 text-xs leading-6 text-slate-400">
            Кнопка «Подключить» добавляет сетевой источник. CasparCG читает
            видео напрямую из S3 по временной подписанной ссылке; файл на
            сервере не сохраняется. Локальную копию можно импортировать
            отдельно.
          </p>
          <div className="mb-5 flex flex-wrap items-end gap-3">
            <label className="flex-1">
              Префикс / папка
              <input
                value={prefix}
                placeholder="videos/"
                onChange={(e) => setPrefix(e.target.value)}
              />
            </label>
            <Button
              variant="outline"
              disabled={busy || !config.configured}
              onClick={() => list()}
            >
              <RefreshCw size={14} />
              Показать файлы
            </Button>
          </div>
          <label className="mb-4">
            Длительность подключаемого видео, секунд
            <input
              type="number"
              min={1}
              max={86400}
              value={seconds}
              onChange={(e) => setSeconds(Number(e.target.value))}
            />
            <small className="block mt-2 text-slate-500">
              Для расписания задайте длительность выбранного файла. Файл в
              прямом режиме не анализируется целиком.
            </small>
          </label>
          <div className="s3-objects">
            {objects.objects.map((object) => {
              const video = /\.(mp4|mkv|mov|webm)$/i.test(object.key);
              return (
                <div key={object.key} className="s3-object">
                  <div>
                    <strong>{object.key}</strong>
                    <small>{bytes(object.size)}</small>
                  </div>
                  <Button
                    size="sm"
                    disabled={busy || !video || seconds < 1 || seconds > 86400}
                    onClick={() =>
                      work(async () => {
                        await api(`${base}/direct`, "POST", {
                          key: object.key,
                          durationSeconds: seconds,
                        });
                        await refresh();
                        setMessage(
                          "Прямой источник S3 добавлен в медиатеку. Выберите его в Preview.",
                        );
                      })
                    }
                  >
                    <Link2 size={13} />
                    Подключить
                  </Button>
                  <Button
                    variant="outline"
                    size="sm"
                    disabled={busy || !video || object.size > 2_000_000_000}
                    onClick={() =>
                      work(async () => {
                        await api(`${base}/imports`, "POST", {
                          key: object.key,
                        });
                        await loadJobs();
                        setMessage(
                          "Файл поставлен в очередь локального импорта.",
                        );
                      })
                    }
                  >
                    <Download size={13} />
                    Импорт
                  </Button>
                </div>
              );
            })}
            {!objects.objects.length && (
              <p className="py-6 text-sm text-slate-500">
                Подключите хранилище и запросите список объектов.
              </p>
            )}
          </div>
          {objects.nextToken && (
            <Button
              className="mt-3"
              size="sm"
              variant="outline"
              disabled={busy}
              onClick={() => list(objects.nextToken!)}
            >
              Следующие файлы
            </Button>
          )}
          {jobs.length > 0 && (
            <div className="mt-6">
              <h3 className="mb-3">Локальный импорт</h3>
              {jobs.map((job) => (
                <div key={job.id} className="mb-4 text-xs">
                  <div className="flex justify-between gap-3">
                    <span className="break-all">{job.objectKey}</span>
                    <span className="text-teal-300">
                      {{
                        Queued: "В очереди",
                        Downloading: "Загрузка",
                        Ready: "Готово",
                        Failed: "Ошибка",
                      }[job.status] ?? job.status}
                    </span>
                  </div>
                  <p className="mt-1 text-slate-500">
                    {bytes(job.bytesReceived)}
                    {job.totalBytes ? ` / ${bytes(job.totalBytes)}` : ""}
                  </p>
                  {job.status === "Downloading" && (
                    <progress
                      className="mt-2 w-full accent-teal-300"
                      value={job.bytesReceived}
                      max={job.totalBytes ?? undefined}
                    />
                  )}
                  {job.error && (
                    <p className="mt-2 text-red-300">{job.error}</p>
                  )}
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
      {message && (
        <p
          role="status"
          className="border-t border-white/10 p-5 text-sm text-teal-200"
        >
          {message}
        </p>
      )}
    </section>
  );
}
