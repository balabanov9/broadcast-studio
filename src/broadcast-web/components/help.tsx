"use client";
import { useState } from "react";
import { ArrowRight, BookOpen, Check } from "lucide-react";
import guide from "@/lib/user-guide.json";
import { Button } from "@/components/ui/button";
export function Help({ onOpen }: { onOpen: (tab: string) => void }) {
  const [topic, setTopic] = useState("first-air");
  const selected = guide.find((item) => item.id === topic)!;
  return (
    <div className="guide-layout">
      <aside className="guide-topics panel">
        <div className="panel-title">
          <span>
            <BookOpen size={16} />
            Руководство оператора
          </span>
        </div>
        <nav aria-label="Темы инструкции">
          {guide.map((item) => (
            <button
              key={item.id}
              aria-current={item.id === topic ? "page" : undefined}
              onClick={() => setTopic(item.id)}
            >
              {item.title}
              <ArrowRight size={13} />
            </button>
          ))}
        </nav>
      </aside>
      <article className="guide-article panel">
        <div className="guide-article-header">
          <span className="guide-kicker">Инструкция</span>
          <h2>{selected.title}</h2>
          <p>{selected.summary}</p>
        </div>
        <ol className="guide-steps">
          {selected.steps.map((step, i) => (
            <li key={i}>
              <span>{i + 1}</span>
              <p>{step}</p>
            </li>
          ))}
        </ol>
        {selected.notes.length > 0 && (
          <aside className="guide-notes">
            <h3>
              <Check size={16} />
              Что учитывать
            </h3>
            {selected.notes.map((note) => (
              <p key={note}>{note}</p>
            ))}
          </aside>
        )}
        <div className="guide-actions">
          <Button
            variant="outline"
            onClick={() =>
              onOpen(
                selected.id === "logo" || selected.id === "graphics"
                  ? "graphics"
                  : selected.id === "autopilot" || selected.id === "playlist"
                    ? "schedule"
                    : selected.id === "s3" || selected.id === "media"
                      ? "media"
                      : "control",
              )
            }
          >
            Перейти к работе
            <ArrowRight size={14} />
          </Button>
          <a
            href="https://github.com/balabanov9/broadcast-studio/blob/main/docs/user-guide.md"
            target="_blank"
            rel="noreferrer"
          >
            Полная инструкция на GitHub
          </a>
        </div>
      </article>
    </div>
  );
}
