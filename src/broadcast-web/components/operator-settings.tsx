"use client";
import { useState } from "react";
import { LockKeyhole } from "lucide-react";
import { api } from "@/lib/api";
import { Button } from "@/components/ui/button";
export function OperatorSettings({ onChanged }: { onChanged: () => void }) {
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  return (
    <section className="panel max-w-xl p-6">
      <LockKeyhole className="mb-4 text-teal-300" size={25} />
      <h3 className="text-lg">Пароль оператора</h3>
      <p className="mb-6 mt-3 text-sm leading-6 text-slate-400">
        После смены пароля все активные сеансы завершатся. Для нового входа
        понадобится новый пароль.
      </p>
      <form
        className="space-y-4"
        onSubmit={async (e) => {
          e.preventDefault();
          const form = new FormData(e.currentTarget);
          if (form.get("new") !== form.get("confirm")) {
            setError("Пароли не совпадают");
            return;
          }
          setBusy(true);
          setError("");
          try {
            await api("/api/password", "POST", {
              currentPassword: form.get("current"),
              newPassword: form.get("new"),
            });
            onChanged();
          } catch (e) {
            setError((e as Error).message);
          } finally {
            setBusy(false);
          }
        }}
      >
        <label>
          Текущий пароль
          <input
            name="current"
            type="password"
            autoComplete="current-password"
            required
          />
        </label>
        <label>
          Новый пароль
          <input
            name="new"
            type="password"
            autoComplete="new-password"
            minLength={12}
            maxLength={128}
            required
          />
        </label>
        <label>
          Подтверждение нового пароля
          <input
            name="confirm"
            type="password"
            autoComplete="new-password"
            minLength={12}
            maxLength={128}
            required
          />
        </label>
        {error && (
          <p role="alert" className="error">
            {error}
          </p>
        )}
        <Button disabled={busy}>Установить новый пароль</Button>
      </form>
    </section>
  );
}
