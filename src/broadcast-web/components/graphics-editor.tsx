"use client";
import { useRef, useState } from "react";
import Image from "next/image";
import { Plus, Save, Trash2, Radio, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { api, type Graphic } from "@/lib/api";
const labels = {
  image: "Логотип",
  logo: "Текст",
  title: "Плашка",
  ticker: "Бегущая строка",
  subtitle: "Субтитр",
};
export function GraphicsEditor({
  channelId,
  initial,
  onSave,
  onAir,
  onHide,
  busy,
  on,
}: {
  channelId: string;
  initial: Graphic[];
  onSave: (scene: Graphic[]) => Promise<boolean>;
  onAir: () => void;
  onHide: () => void;
  busy: boolean;
  on: boolean;
}) {
  const [elements, setElements] = useState(initial);
  const [selected, setSelected] = useState<string | null>(null);
  const [dirty, setDirty] = useState(false);
  const [uploading, setUploading] = useState(false);
  const [uploadError, setUploadError] = useState("");
  const fileInput = useRef<HTMLInputElement>(null);
  async function upload(file: File) {
    setUploading(true);
    setUploadError("");
    try {
      const body = new FormData();
      body.append("file", file);
      const result = await api<{
        imageId: string;
        width: number;
        height: number;
      }>(`/api/channels/${channelId}/graphics/assets`, "POST", body);
      const id = crypto.randomUUID();
      const width = Math.min(260, (220 * result.width) / result.height);
      const height = (width * result.height) / result.width;
      const element: Graphic = {
        id,
        kind: "image",
        text: "Логотип канала",
        x: Math.round(1820 - width),
        y: 70,
        width: Math.max(20, Math.round(width)),
        height: Math.max(20, Math.round(height)),
        fontSize: 44,
        color: "#ffffff",
        background: "#000000",
        imageId: result.imageId,
      };
      setElements((list) => [...list, element]);
      setSelected(id);
      setDirty(true);
    } catch (e) {
      setUploadError((e as Error).message);
    } finally {
      setUploading(false);
    }
  }
  const canvas = useRef<HTMLDivElement>(null);
  const drag = useRef<{
    id: string;
    x: number;
    y: number;
    originX: number;
    originY: number;
  } | null>(null);
  const active = elements.find((e) => e.id === selected);
  function update(id: string, patch: Partial<Graphic>) {
    setDirty(true);
    setElements((list) =>
      list.map((e) => {
        if (e.id !== id) return e;
        const next = { ...e, ...patch };
        next.width = Math.max(20, Math.min(1920, next.width));
        next.height = Math.max(20, Math.min(1080, next.height));
        next.x = Math.max(0, Math.min(1920 - next.width, next.x));
        next.y = Math.max(0, Math.min(1080 - next.height, next.y));
        return next;
      }),
    );
  }
  function add(kind: Graphic["kind"]) {
    const id = crypto.randomUUID();
    const e: Graphic = {
      id,
      kind,
      text:
        kind === "logo"
          ? "LUMIÈRE"
          : kind === "ticker"
            ? "Введите текст бегущей строки"
            : "Введите текст",
      x: 100,
      y: kind === "logo" ? 80 : kind === "ticker" ? 970 : 760,
      width: kind === "logo" ? 320 : 1500,
      height: kind === "logo" ? 90 : 100,
      fontSize: 44,
      color: "#ffffff",
      background: "#1d1d1f",
    };
    setElements([...elements, e]);
    setSelected(id);
    setDirty(true);
  }
  return (
    <div className="editor-layout">
      <section className="panel">
        <div className="panel-title">
          <span>Логотип и надписи</span>
          <span className="tag">1920 × 1080</span>
        </div>
        <div
          className="editor-canvas"
          ref={canvas}
          onPointerMove={(event) => {
            const d = drag.current;
            const rect = canvas.current?.getBoundingClientRect();
            if (!d || !rect) return;
            update(d.id, {
              x: Math.round(
                d.originX + ((event.clientX - d.x) * 1920) / rect.width,
              ),
              y: Math.round(
                d.originY + ((event.clientY - d.y) * 1080) / rect.height,
              ),
            });
          }}
          onPointerUp={() => {
            drag.current = null;
          }}
          onPointerCancel={() => {
            drag.current = null;
          }}
        >
          <div className="editor-guides" />
          <span className="canvas-watermark">ПРОЗРАЧНЫЙ ФОН</span>
          {elements.map((e) => (
            <div
              key={e.id}
              role="button"
              tabIndex={0}
              aria-label={`Слой ${labels[e.kind]}`}
              className={`graphic-layer ${selected === e.id ? "selected" : ""}`}
              style={{
                left: `${e.x / 19.2}%`,
                top: `${e.y / 10.8}%`,
                width: `${e.width / 19.2}%`,
                height: `${e.height / 10.8}%`,
                color: e.color,
                background: e.kind === "image" ? "transparent" : e.background,
                fontSize: `${e.fontSize / 19.2}cqw`,
              }}
              onKeyDown={(event) => {
                if (event.key === "Enter") setSelected(e.id);
              }}
              onPointerDown={(event) => {
                event.currentTarget.setPointerCapture(event.pointerId);
                setSelected(e.id);
                drag.current = {
                  id: e.id,
                  x: event.clientX,
                  y: event.clientY,
                  originX: e.x,
                  originY: e.y,
                };
              }}
            >
              {e.kind === "image" ? (
                <Image
                  unoptimized
                  draggable={false}
                  src={`/api/channels/${channelId}/graphics/assets/${e.imageId}`}
                  alt="Логотип канала"
                  width={e.width}
                  height={e.height}
                  style={{
                    width: "100%",
                    height: "100%",
                    objectFit: "contain",
                    pointerEvents: "none",
                  }}
                />
              ) : (
                <span>{e.text}</span>
              )}
            </div>
          ))}
        </div>
        <div className="flex flex-wrap gap-2 p-4">
          <input
            ref={fileInput}
            type="file"
            accept="image/png,image/jpeg,image/webp"
            style={{ display: "none" }}
            aria-label="Файл логотипа"
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) upload(file);
              e.target.value = "";
            }}
          />
          <Button
            variant="outline"
            size="sm"
            disabled={busy || uploading || elements.length >= 20}
            onClick={() => fileInput.current?.click()}
          >
            <Upload size={14} />
            {uploading ? "Загружаем…" : "Загрузить логотип"}
          </Button>
          {Object.entries(labels)
            .filter(([key]) => key !== "image")
            .map(([key, label]) => (
              <Button
                key={key}
                variant="outline"
                size="sm"
                disabled={elements.length >= 20}
                onClick={() => add(key as Graphic["kind"])}
              >
                <Plus size={13} />
                {label}
              </Button>
            ))}
        </div>
        <div className="flex flex-wrap items-center gap-3 border-t border-white/10 p-4">
          <Button
            disabled={busy || uploading || !dirty}
            onClick={async () => {
              if (await onSave(elements)) setDirty(false);
            }}
          >
            <Save size={14} />
            Сохранить
          </Button>
          <Button
            variant={on ? "destructive" : "outline"}
            disabled={busy || uploading || dirty}
            onClick={on ? onHide : onAir}
          >
            <Radio size={14} />
            {on ? "Убрать из эфира" : "Показать в эфире"}
          </Button>
          <small className="text-slate-400">
            {dirty
              ? "Есть несохранённые изменения"
              : on
                ? "Графика в эфире"
                : "Изменения сохранены"}
          </small>
        </div>
      </section>
      <aside className="panel p-5">
        {uploadError && (
          <p role="alert" className="error mb-4">
            {uploadError}
          </p>
        )}
        <h3 className="mb-5">Свойства слоя</h3>
        {active ? (
          <div className="space-y-4">
            {active.kind !== "image" && (
              <label>
                Текст
                <textarea
                  value={active.text}
                  maxLength={1000}
                  onChange={(e) => update(active.id, { text: e.target.value })}
                />
              </label>
            )}
            <div className="grid grid-cols-2 gap-3">
              {(
                [
                  ["x", "X"],
                  ["y", "Y"],
                  ["width", "Ширина"],
                  ["height", "Высота"],
                  ["fontSize", "Кегль"],
                ] as const
              )
                .filter(
                  ([key]) => active.kind !== "image" || key !== "fontSize",
                )
                .map(([key, label]) => (
                  <label key={key}>
                    {label}
                    <input
                      type="number"
                      min={key === "fontSize" ? 12 : 0}
                      max={key === "fontSize" ? 160 : 1920}
                      value={active[key]}
                      onChange={(e) =>
                        update(active.id, { [key]: Number(e.target.value) })
                      }
                    />
                  </label>
                ))}
            </div>
            {active.kind !== "image" && (
              <div className="grid grid-cols-2 gap-3">
                <label>
                  Цвет текста
                  <input
                    type="color"
                    value={active.color}
                    onChange={(e) =>
                      update(active.id, { color: e.target.value })
                    }
                  />
                </label>
                <label>
                  Фон
                  <input
                    type="color"
                    value={active.background}
                    onChange={(e) =>
                      update(active.id, { background: e.target.value })
                    }
                  />
                </label>
              </div>
            )}
            {active.kind === "image" && (
              <p className="text-xs text-slate-500">
                Изображение вписывается в рамку без обрезки. PNG сохраняет
                прозрачный фон.
              </p>
            )}
            <Button
              variant="destructive"
              size="sm"
              onClick={() => {
                setElements(elements.filter((e) => e.id !== active.id));
                setSelected(null);
                setDirty(true);
              }}
            >
              <Trash2 size={14} />
              Удалить слой
            </Button>
          </div>
        ) : (
          <p className="text-sm leading-6 text-slate-400">
            Загрузите логотип или добавьте надпись. Выберите слой на холсте;
            точные координаты задавайте здесь.
          </p>
        )}
        <div className="mt-8 border-t border-white/10 pt-4 text-xs leading-5 text-slate-500">
          Пунктир — отступ от края кадра. После сохранения нажмите «Показать в
          эфире», чтобы добавить графику в выходной видеопоток.
        </div>
      </aside>
    </div>
  );
}
