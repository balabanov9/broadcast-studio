"use client";
import { useEffect, useRef, useState } from "react";
import Hls from "hls.js";
import { Maximize, Volume2, VolumeX } from "lucide-react";
import { Button } from "@/components/ui/button";
export function Monitor({
  url,
  label,
  name,
  program = false,
}: {
  url?: string;
  label: string;
  name?: string;
  program?: boolean;
}) {
  const ref = useRef<HTMLVideoElement>(null);
  const [playing, setPlaying] = useState(false);
  const [muted, setMuted] = useState(true);
  const [error, setError] = useState("");
  useEffect(() => {
    const video = ref.current;
    if (!video || !url) return;
    let player: Hls | undefined;
    if (Hls.isSupported()) {
      player = new Hls({ lowLatencyMode: true, manifestLoadingMaxRetry: 10 });
      player.loadSource(url);
      player.attachMedia(video);
      player.on(Hls.Events.ERROR, (_, data) => {
        if (data.fatal) setError("Поток недоступен. Проверьте медиадвижок.");
      });
    } else if (video.canPlayType("application/vnd.apple.mpegurl"))
      video.src = url;
    return () => {
      player?.destroy();
      video.removeAttribute("src");
      video.load();
    };
  }, [url]);
  return (
    <section className={`monitor ${program ? "program" : "preview"}`}>
      <div className="monitor-label">
        <span className="dot" />
        {label}
        <span className="ml-auto text-[10px] tracking-normal text-slate-400">
          {program ? "ВЫХОД КАНАЛА" : "ПРОСМОТР ИСТОЧНИКА"}
        </span>
      </div>
      <div className="video-frame">
        <video
          key={url}
          ref={ref}
          autoPlay
          playsInline
          muted={muted}
          onPlaying={() => {
            setPlaying(true);
            setError("");
          }}
          onWaiting={() => setPlaying(false)}
          onEmptied={() => setPlaying(false)}
        />
        {(!playing || !url) && (
          <div className="video-empty">
            <div className="crosshair" />
            <span>
              {error || (url ? "Ожидание видеопотока" : "Источник не выбран")}
            </span>
            <small>{name || "Выберите источник в коммутационной панели"}</small>
          </div>
        )}
        <span className="safe-area" />
        <div className="video-controls">
          <span>{name || "Нет источника"}</span>
          <Button
            variant="ghost"
            size="icon"
            aria-label={muted ? "Включить звук" : "Выключить звук"}
            onClick={() => setMuted(!muted)}
          >
            {muted ? <VolumeX size={15} /> : <Volume2 size={15} />}
          </Button>
          <Button
            variant="ghost"
            size="icon"
            aria-label="Полноэкранный режим"
            onClick={() => ref.current?.requestFullscreen()}
          >
            <Maximize size={15} />
          </Button>
        </div>
      </div>
    </section>
  );
}
