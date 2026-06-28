"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import React, { Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";

interface FacebookMetaApp {
  id: number;
  userId: string;
  name: string;
  appId?: string;
  hasAppSecret: boolean;
  appSecretLength: number;
  verifyToken: string;
  webhookKey: string;
  callbackPath: string;
  graphApiVersion: string;
  isDefault: boolean;
  updatedAt: string;
}

interface MetaAppForm {
  id?: number;
  name: string;
  appId: string;
  appSecret: string;
  verifyToken: string;
  webhookKey: string;
  graphApiVersion: string;
  isDefault: boolean;
}

const EMPTY_FORM: MetaAppForm = {
  name: "",
  appId: "",
  appSecret: "",
  verifyToken: "",
  webhookKey: "",
  graphApiVersion: "v24.0",
  isDefault: false,
};

export default function FacebookMetaAppsPage() {
  return (
    <Suspense fallback={<div className="p-8 text-sm text-neutral-400">Loading Meta Apps...</div>}>
      <FacebookMetaAppsContent />
    </Suspense>
  );
}

function FacebookMetaAppsContent() {
  const searchParams = useSearchParams();
  const initialUserId = searchParams.get("userId") || "";
  const [userId, setUserId] = useState(initialUserId);
  const [metaApps, setMetaApps] = useState<FacebookMetaApp[]>([]);
  const [form, setForm] = useState<MetaAppForm>(EMPTY_FORM);
  const [message, setMessage] = useState("");
  const [origin, setOrigin] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const hasLoaded = useRef(false);

  const effectiveUserId = userId.trim();
  const isEditing = Boolean(form.id);

  const loadMetaApps = useCallback(async (nextUserId = effectiveUserId) => {
    setMessage("");
    if (!nextUserId) {
      setMessage("Enter a User ID to load Meta Developer Apps.");
      return;
    }

    setIsLoading(true);
    window.localStorage.setItem("smapi_user_id", nextUserId);

    try {
      const response = await fetch(`/api/smapi/FacebookMetaApps?userId=${encodeURIComponent(nextUserId)}`);
      if (!response.ok) {
        setMessage(`Could not load Meta Apps. Backend status ${response.status}.`);
        setMetaApps([]);
        return;
      }

      const data = await response.json();
      setMetaApps(Array.isArray(data) ? data as FacebookMetaApp[] : []);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsLoading(false);
    }
  }, [effectiveUserId]);

  useEffect(() => {
    if (hasLoaded.current) {
      return;
    }

    hasLoaded.current = true;
    setOrigin(window.location.origin);
    const storedUserId = initialUserId || window.localStorage.getItem("smapi_user_id") || "";
    if (storedUserId) {
      window.setTimeout(() => {
        setUserId(storedUserId);
        void loadMetaApps(storedUserId);
      }, 0);
    }
  }, [initialUserId, loadMetaApps]);

  const resetForm = () => {
    setForm(EMPTY_FORM);
  };

  const editMetaApp = (metaApp: FacebookMetaApp) => {
    setForm({
      id: metaApp.id,
      name: metaApp.name,
      appId: metaApp.appId || "",
      appSecret: "",
      verifyToken: metaApp.verifyToken,
      webhookKey: metaApp.webhookKey,
      graphApiVersion: metaApp.graphApiVersion,
      isDefault: metaApp.isDefault,
    });
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  const saveMetaApp = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage("");

    if (!effectiveUserId) {
      setMessage("User ID is required.");
      return;
    }

    if (!form.name.trim()) {
      setMessage("Meta App name is required.");
      return;
    }

    setIsSaving(true);
    try {
      const response = await fetch(isEditing ? `/api/smapi/FacebookMetaApps/${form.id}` : "/api/smapi/FacebookMetaApps", {
        method: isEditing ? "PUT" : "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: effectiveUserId,
          name: form.name.trim(),
          appId: form.appId.trim() || null,
          appSecret: form.appSecret.trim() || null,
          verifyToken: form.verifyToken.trim() || null,
          webhookKey: form.webhookKey.trim() || null,
          graphApiVersion: form.graphApiVersion.trim() || "v24.0",
          isDefault: form.isDefault,
        }),
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Failed to save Meta App. Backend status ${response.status}.`);
        return;
      }

      setMessage(data.message || "Meta App saved.");
      resetForm();
      await loadMetaApps(effectiveUserId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsSaving(false);
    }
  };

  const deleteMetaApp = async (metaApp: FacebookMetaApp) => {
    if (!window.confirm(`Delete Meta App "${metaApp.name}"?`)) {
      return;
    }

    setMessage("");
    try {
      const response = await fetch(`/api/smapi/FacebookMetaApps/${metaApp.id}`, {
        method: "DELETE",
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Failed to delete Meta App. Backend status ${response.status}.`);
        return;
      }

      setMessage(data.message || "Meta App deleted.");
      await loadMetaApps(effectiveUserId);
    } catch {
      setMessage("Could not connect to the backend server.");
    }
  };

  const callbackUrlPreview = useMemo(() => {
    if (!origin) {
      return "";
    }

    const callbackPath = form.webhookKey
      ? `/api/smapi/FacebookWebhooks/meta/${encodeURIComponent(form.webhookKey.trim())}`
      : "/api/smapi/FacebookWebhooks/meta/{auto-generated}";

    return `${origin}${callbackPath}`;
  }, [form.webhookKey, origin]);

  const copyText = async (value: string, label: string) => {
    if (!value) {
      return;
    }

    await navigator.clipboard.writeText(value);
    setMessage(`${label} copied.`);
  };

  return (
    <div className="p-8 space-y-8 animate-in fade-in duration-700">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <Link href="/accounts/facebook" className="text-xs text-blue-300 hover:text-blue-200">← Facebook Pages</Link>
          <h1 className="text-3xl font-bold text-white tracking-tight mt-2">Meta Developer Apps</h1>
          <p className="text-neutral-500 text-sm mt-1">Keep separate Meta Apps for you, your brother, or any other page owner.</p>
        </div>
        <div className="flex flex-col sm:flex-row gap-2">
          <input
            value={userId}
            onChange={(event) => setUserId(event.target.value)}
            className="w-full sm:w-56 bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            placeholder="Owner/User ID"
          />
          <button
            type="button"
            onClick={() => loadMetaApps()}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-sm">sync</span>
            {isLoading ? "Loading" : "Load Apps"}
          </button>
        </div>
      </div>

      {message && (
        <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
          {message}
        </div>
      )}

      <form onSubmit={saveMetaApp} className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
        <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h2 className="text-lg font-bold text-white">{isEditing ? "Edit Meta App" : "Add Meta App"}</h2>
            <p className="text-sm text-neutral-500 mt-1">Each Meta App gets its own callback URL and verify token.</p>
          </div>
          {isEditing && (
            <button
              type="button"
              onClick={resetForm}
              className="px-4 py-2 rounded-lg bg-white/5 text-sm font-bold text-white hover:bg-white/10 border border-white/5"
            >
              New App
            </button>
          )}
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Display Name</span>
            <input
              required
              value={form.name}
              onChange={(event) => setForm({ ...form, name: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="My Meta App / Ayya Meta App"
            />
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Meta App ID</span>
            <input
              value={form.appId}
              onChange={(event) => setForm({ ...form, appId: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="1234567890"
            />
          </label>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">App Secret</span>
            <input
              type="password"
              value={form.appSecret}
              onChange={(event) => setForm({ ...form, appSecret: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder={isEditing ? "Leave blank to keep existing secret" : "Optional but recommended"}
              autoComplete="off"
            />
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Graph API Version</span>
            <input
              value={form.graphApiVersion}
              onChange={(event) => setForm({ ...form, graphApiVersion: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="v24.0"
            />
          </label>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Verify Token</span>
            <input
              value={form.verifyToken}
              onChange={(event) => setForm({ ...form, verifyToken: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="Leave blank to auto-generate"
            />
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Webhook Key</span>
            <input
              value={form.webhookKey}
              onChange={(event) => setForm({ ...form, webhookKey: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="Leave blank to auto-generate"
            />
          </label>
        </div>

        <div className="rounded-lg border border-blue-500/10 bg-blue-500/5 px-4 py-4 text-sm text-blue-100 space-y-2">
          <p className="font-bold">Callback URL preview</p>
          <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
            <code className="flex-1 rounded bg-black/40 px-3 py-2 text-xs text-blue-100 break-all">
              {callbackUrlPreview || "Save the app to generate the callback URL."}
            </code>
            <button
              type="button"
              onClick={() => copyText(callbackUrlPreview, "Callback URL")}
              className="px-3 py-2 rounded bg-blue-600 text-xs font-bold text-white hover:bg-blue-500 disabled:opacity-50"
              disabled={!callbackUrlPreview}
            >
              Copy
            </button>
          </div>
        </div>

        <label className="flex items-center gap-3 rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-200">
          <input
            checked={form.isDefault}
            onChange={(event) => setForm({ ...form, isDefault: event.target.checked })}
            type="checkbox"
            className="h-4 w-4 accent-blue-500"
          />
          Use as default Meta App for this user
        </label>

        <button
          type="submit"
          disabled={isSaving}
          className="w-full sm:w-auto px-6 py-3 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all disabled:opacity-50"
        >
          {isSaving ? "Saving..." : isEditing ? "Save Meta App" : "Add Meta App"}
        </button>
      </form>

      <section className="grid grid-cols-1 xl:grid-cols-2 gap-6">
        {metaApps.map((metaApp) => {
          const callbackUrl = `${origin}${metaApp.callbackPath}`;
          return (
            <article key={metaApp.id} className="glass-panel rounded-xl border border-white/5 p-6 space-y-4">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <h3 className="text-xl font-bold text-white">{metaApp.name}</h3>
                    {metaApp.isDefault && (
                      <span className="rounded-full border border-emerald-500/20 bg-emerald-500/10 px-2 py-1 text-[10px] font-bold text-emerald-200">
                        DEFAULT
                      </span>
                    )}
                  </div>
                  <p className="text-xs text-neutral-500 mt-1">App ID: {metaApp.appId || "Not set"}</p>
                  <p className="text-xs text-neutral-500">Secret: {metaApp.hasAppSecret ? `Saved (${metaApp.appSecretLength} chars)` : "Not set"}</p>
                </div>
                <div className="flex gap-2">
                  <button
                    type="button"
                    onClick={() => editMetaApp(metaApp)}
                    className="px-3 py-2 rounded bg-white/5 text-xs font-bold text-white hover:bg-white/10 border border-white/5"
                  >
                    Edit
                  </button>
                  <button
                    type="button"
                    onClick={() => deleteMetaApp(metaApp)}
                    className="px-3 py-2 rounded bg-red-500/10 text-xs font-bold text-red-200 hover:bg-red-500/20 border border-red-500/20"
                  >
                    Delete
                  </button>
                </div>
              </div>

              <div className="space-y-2">
                <p className="text-[10px] uppercase tracking-widest font-bold text-neutral-400">Meta Callback URL</p>
                <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
                  <code className="flex-1 rounded bg-black/40 px-3 py-2 text-xs text-neutral-200 break-all">{callbackUrl}</code>
                  <button
                    type="button"
                    onClick={() => copyText(callbackUrl, "Callback URL")}
                    className="px-3 py-2 rounded bg-white/5 text-xs font-bold text-white hover:bg-white/10 border border-white/5"
                  >
                    Copy
                  </button>
                </div>
              </div>

              <div className="space-y-2">
                <p className="text-[10px] uppercase tracking-widest font-bold text-neutral-400">Verify Token</p>
                <div className="flex flex-col gap-2 lg:flex-row lg:items-center">
                  <code className="flex-1 rounded bg-black/40 px-3 py-2 text-xs text-neutral-200 break-all">{metaApp.verifyToken}</code>
                  <button
                    type="button"
                    onClick={() => copyText(metaApp.verifyToken, "Verify token")}
                    className="px-3 py-2 rounded bg-white/5 text-xs font-bold text-white hover:bg-white/10 border border-white/5"
                  >
                    Copy
                  </button>
                </div>
              </div>
            </article>
          );
        })}

        {metaApps.length === 0 && (
          <div className="rounded-xl border border-white/5 bg-black/20 px-4 py-12 text-center text-sm text-neutral-500 xl:col-span-2">
            No Meta Developer Apps saved for this User ID yet.
          </div>
        )}
      </section>
    </div>
  );
}
