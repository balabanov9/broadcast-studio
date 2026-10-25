"use client";
import Image from "next/image";
import { useEffect, useState } from "react";
import { Globe2, Upload, ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { api, type Channel } from "@/lib/api";
type Policy = {
  enabled: boolean;
  rules: string[];
  blockUnknown: boolean;
  customImage: boolean;
  geoAvailable: boolean;
  regionsAvailable: boolean;
  slateRunning: boolean;
};
export function RegionalPolicy({
  c,
  refresh,
}: {
  c: Channel;
  refresh: () => Promise<void>;
}) {
  const [policy, setPolicy] = useState<Policy | null>(null);
  const [rules, setRules] = useState("");
  const [enabled, setEnabled] = useState(false);
  const [unknown, setUnknown] = useState(true);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [version, setVersion] = useState(0);
  const base = `/api/channels/${c.id}/restriction`;
  useEffect(() => {
    let alive = true;
    api<Policy>(base)
      .then((p) => {
        if (!alive) return;
        setPolicy(p);
        setRules(p.rules.join(", "));
        setEnabled(p.enabled);
        setUnknown(p.blockUnknown);
      })
      .catch((e) => {
        if (alive) setMessage(e.message);
      });
    return () => {
      alive = false;
    };
  }, [base]);
  async function change(work: () => Promise<unknown>) {
    setBusy(true);
    setMessage("");
    try {
      await work();
      await refresh();
      const p = await api<Policy>(base);
      setPolicy(p);
      setVersion((n) => n + 1);
      setMessage(
        "Настройки сохранены. При изменении политики перезапустите IPTV-плеер.",
      );
    } catch (e) {
      setMessage((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="panel mt-5">
      <div className="panel-title">
        <span>
          <Globe2 size={17} />
          Региональные ограничения IPTV
        </span>
        <span className="tag">
          {policy?.geoAvailable
            ? policy.regionsAvailable
              ? "GEOIP CITY"
              : "GEOIP COUNTRY"
            : "GEOIP НЕ ПОДКЛЮЧЁН"}
        </span>
      </div>
      <div className="grid gap-7 p-6 lg:grid-cols-2">
        <div>
          <h3 className="text-lg">Кто может смотреть эфир</h3>
          <p className="mb-6 mt-3 text-sm leading-6 text-slate-400">
            Зрители из запрещённых регионов получают видеопоток с заставкой и
            тишиной. Основная программа канала продолжает идти для остальных.
          </p>
          {!policy?.geoAvailable && (
            <div className="notice mb-5">
              Подключите GeoLite2 City или Country в папке geoip и перезапустите
              API. До подключения базы включение ограничений недоступно.
            </div>
          )}
          <form
            className="space-y-5"
            onSubmit={(e) => {
              e.preventDefault();
              change(() =>
                api(base, "PUT", {
                  enabled,
                  rules: rules.split(/[,;\s]+/).filter(Boolean),
                  blockUnknown: unknown,
                  revision: c.revision,
                }),
              );
            }}
          >
            <label className="flex items-center gap-3">
              <input
                className="!m-0 !w-4"
                type="checkbox"
                disabled={!policy?.geoAvailable}
                checked={enabled}
                onChange={(e) => setEnabled(e.target.checked)}
              />
              Включить региональные ограничения
            </label>
            <label>
              Запрещённые страны и регионы
              <textarea
                value={rules}
                onChange={(e) => setRules(e.target.value.toUpperCase())}
                placeholder="RU, NL"
              />
              <span className="mt-2 block text-[10px] text-slate-500">
                Двухбуквенные коды стран ISO 3166. Для областей — код
                страна-регион из GeoIP City; разделитель — запятая.
              </span>
            </label>
            <label className="flex items-center gap-3">
              <input
                className="!m-0 !w-4"
                type="checkbox"
                checked={unknown}
                onChange={(e) => setUnknown(e.target.checked)}
              />
              Показывать заставку, если регион не удалось определить
            </label>
            <Button disabled={busy || !policy}>
              <ShieldCheck size={15} />
              Сохранить ограничения
            </Button>
          </form>
        </div>
        <div>
          <div className="mb-3 flex justify-between text-xs text-slate-400">
            <span>
              {policy?.customImage
                ? "Ваша заставка"
                : "Стандартная заставка LUMIÈRE"}
            </span>
            <span>1920 × 1080</span>
          </div>
          <Image
            unoptimized
            src={`${base}/image?v=${version}`}
            width={1920}
            height={1080}
            alt="Заставка для зрителей из запрещённых регионов"
            className="w-full rounded-md border border-white/10"
          />
          <label className="upload-zone mt-4">
            <Upload size={20} />
            <strong>Загрузить свою картинку</strong>
            <span>PNG или JPEG · до 8 МБ · вписывается в Full HD</span>
            <input
              type="file"
              accept="image/png,image/jpeg"
              disabled={busy}
              onChange={(e) => {
                const file = e.target.files?.[0];
                if (!file) return;
                const form = new FormData();
                form.append("file", file);
                change(() => api(`${base}/image`, "POST", form));
                e.target.value = "";
              }}
            />
          </label>
          <Button
            className="mt-3"
            size="sm"
            variant="outline"
            disabled={busy || !policy?.customImage}
            onClick={() => change(() => api(`${base}/image`, "DELETE"))}
          >
            Вернуть стандартную заставку
          </Button>
          <p className="mt-4 text-xs leading-6 text-slate-500">
            Заставка выдаётся как HLS-видео через FFmpeg. Проверяются и
            плейлист, и сегменты. Точность определения зависит от GeoIP; адрес
            VPN определяется как регион VPN.
          </p>
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
