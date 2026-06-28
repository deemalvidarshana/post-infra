"use client";

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";

interface AutoReplySettings {
  userId: string;
  pageId: string;
  enabled: boolean;
  mode: "ManualApproval" | "Auto";
  prompt: string;
  tone: string;
  language: string;
  maxRepliesPerPostPerDay: number;
  ignoreKeywords?: string;
  escalationKeywords?: string;
  graphApiVersion: string;
  updatedAt?: string;
}

interface CommentEvent {
  id: number;
  userId: string;
  pageId: string;
  postId?: string;
  commentId: string;
  parentCommentId?: string;
  commentText?: string;
  commentAuthorName?: string;
  verb: string;
  status: string;
  generatedReply?: string;
  replyCommentId?: string;
  skipReason?: string;
  errorMessage?: string;
  attempts: number;
  receivedAt: string;
  processedAt?: string;
}

const DEFAULT_SETTINGS: AutoReplySettings = {
  userId: "",
  pageId: "",
  enabled: false,
  mode: "ManualApproval",
  prompt: "Reply as the Facebook Page. Be helpful, natural, and short. Answer only what the commenter asked. Do not mention that you are AI.",
  tone: "Friendly",
  language: "Sinhala/English",
  maxRepliesPerPostPerDay: 20,
  graphApiVersion: "v24.0",
};

export default function FacebookCommentsPage() {
  const params = useParams<{ pageId: string }>();
  const searchParams = useSearchParams();
  const routePageId = useMemo(() => safeDecode(params.pageId), [params.pageId]);
  const initialUserId = searchParams.get("userId") || "";
  const [userId, setUserId] = useState(initialUserId);
  const [settings, setSettings] = useState<AutoReplySettings>({ ...DEFAULT_SETTINGS, pageId: routePageId });
  const [events, setEvents] = useState<CommentEvent[]>([]);
  const [message, setMessage] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isSaving, setIsSaving] = useState(false);
  const hasLoaded = useRef(false);

  const effectiveUserId = userId.trim();

  const loadData = useCallback(async (nextUserId = effectiveUserId) => {
    setMessage("");
    if (!nextUserId || !routePageId) {
      setMessage("Enter the Page owner User ID to load auto-reply settings.");
      return;
    }

    setIsLoading(true);
    window.localStorage.setItem("smapi_user_id", nextUserId);

    try {
      const [settingsResponse, eventsResponse] = await Promise.all([
        fetch(`/api/smapi/FacebookWebhooks/settings/${encodeURIComponent(nextUserId)}/${encodeURIComponent(routePageId)}`),
        fetch(`/api/smapi/FacebookWebhooks/events?userId=${encodeURIComponent(nextUserId)}&pageId=${encodeURIComponent(routePageId)}&take=100`),
      ]);

      if (!settingsResponse.ok) {
        setMessage(`Could not load settings. Backend status ${settingsResponse.status}.`);
        return;
      }

      const settingsData = await settingsResponse.json() as AutoReplySettings;
      setSettings({
        ...DEFAULT_SETTINGS,
        ...settingsData,
        userId: nextUserId,
        pageId: routePageId,
      });

      if (eventsResponse.ok) {
        const eventsData = await eventsResponse.json();
        setEvents(Array.isArray(eventsData) ? eventsData as CommentEvent[] : []);
      } else {
        setEvents([]);
        setMessage(`Settings loaded, but events could not be loaded. Backend status ${eventsResponse.status}.`);
      }
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsLoading(false);
    }
  }, [effectiveUserId, routePageId]);

  useEffect(() => {
    if (hasLoaded.current) {
      return;
    }

    hasLoaded.current = true;
    const storedUserId = initialUserId || window.localStorage.getItem("smapi_user_id") || "";
    if (storedUserId) {
      window.setTimeout(() => {
        setUserId(storedUserId);
        void loadData(storedUserId);
      }, 0);
    }
  }, [initialUserId, loadData]);

  const saveSettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage("");

    if (!effectiveUserId || !routePageId) {
      setMessage("User ID and Page ID are required.");
      return;
    }

    setIsSaving(true);
    try {
      const response = await fetch("/api/smapi/FacebookWebhooks/settings", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          ...settings,
          userId: effectiveUserId,
          pageId: routePageId,
        }),
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Failed to save settings. Backend status ${response.status}.`);
        return;
      }

      setSettings(data.settings as AutoReplySettings);
      setMessage("Auto-reply settings saved.");
      await loadData(effectiveUserId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsSaving(false);
    }
  };

  const approveEvent = async (commentEvent: CommentEvent) => {
    const reply = window.prompt("Edit reply before publishing:", commentEvent.generatedReply || "");
    if (reply === null) {
      return;
    }

    await runEventAction(commentEvent.id, "approve", { reply });
  };

  const runEventAction = async (id: number, action: "approve" | "retry" | "skip", body?: Record<string, string>) => {
    setMessage("");
    try {
      const response = await fetch(`/api/smapi/FacebookWebhooks/events/${id}/${action}`, {
        method: "POST",
        headers: body ? { "Content-Type": "application/json" } : undefined,
        body: body ? JSON.stringify(body) : undefined,
      });

      const data = await response.json();
      if (!response.ok) {
        setMessage(data.message || `Action failed. Backend status ${response.status}.`);
        return;
      }

      setEvents((previousEvents) => previousEvents.map((item) => item.id === id ? data as CommentEvent : item));
      setMessage(`${action} completed.`);
    } catch {
      setMessage("Could not connect to the backend server.");
    }
  };

  return (
    <div className="p-8 space-y-8 animate-in fade-in duration-700">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <Link href="/accounts/facebook" className="text-xs text-blue-300 hover:text-blue-200">← Facebook Pages</Link>
          <h1 className="text-3xl font-bold text-white tracking-tight mt-2">Facebook Auto Replies</h1>
          <p className="text-neutral-500 text-sm mt-1">Page ID: {routePageId}</p>
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
            onClick={() => loadData()}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-sm">sync</span>
            {isLoading ? "Loading" : "Load"}
          </button>
        </div>
      </div>

      {message && (
        <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
          {message}
        </div>
      )}

      <form onSubmit={saveSettings} className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h2 className="text-lg font-bold text-white">Automation Settings</h2>
            <p className="text-sm text-neutral-500 mt-1">Manual approval is safest: AI drafts a reply, then you approve before publishing.</p>
          </div>
          <label className="flex items-center gap-3 rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-200">
            <input
              checked={settings.enabled}
              onChange={(event) => setSettings({ ...settings, enabled: event.target.checked })}
              type="checkbox"
              className="h-4 w-4 accent-blue-500"
            />
            Enable auto-reply watcher
          </label>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-4 gap-4">
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Mode</span>
            <select
              value={settings.mode}
              onChange={(event) => setSettings({ ...settings, mode: event.target.value as AutoReplySettings["mode"] })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            >
              <option value="ManualApproval">Manual approval</option>
              <option value="Auto">Full auto publish</option>
            </select>
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Language</span>
            <input
              value={settings.language}
              onChange={(event) => setSettings({ ...settings, language: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            />
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Tone</span>
            <input
              value={settings.tone}
              onChange={(event) => setSettings({ ...settings, tone: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            />
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Daily replies/post</span>
            <input
              type="number"
              min={1}
              value={settings.maxRepliesPerPostPerDay}
              onChange={(event) => setSettings({ ...settings, maxRepliesPerPostPerDay: Number(event.target.value) || 1 })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            />
          </label>
        </div>

        <label className="space-y-1 block">
          <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">AI Reply Prompt</span>
          <textarea
            rows={4}
            value={settings.prompt}
            onChange={(event) => setSettings({ ...settings, prompt: event.target.value })}
            className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none resize-y"
          />
        </label>

        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Ignore keywords</span>
            <input
              value={settings.ignoreKeywords || ""}
              onChange={(event) => setSettings({ ...settings, ignoreKeywords: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="spam, price, refund"
            />
          </label>
          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Manual approval keywords</span>
            <input
              value={settings.escalationKeywords || ""}
              onChange={(event) => setSettings({ ...settings, escalationKeywords: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="complaint, angry, refund"
            />
          </label>
        </div>

        <button
          type="submit"
          disabled={isSaving}
          className="w-full sm:w-auto px-6 py-3 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all disabled:opacity-50"
        >
          {isSaving ? "Saving..." : "Save Auto Reply Settings"}
        </button>
      </form>

      <section className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
          <div>
            <h2 className="text-lg font-bold text-white">Comment Inbox</h2>
            <p className="text-sm text-neutral-500 mt-1">Webhook comments, AI drafts, and publish status.</p>
          </div>
          <button
            type="button"
            onClick={() => loadData()}
            className="px-4 py-2 rounded-lg bg-white/5 text-sm font-bold text-white hover:bg-white/10 border border-white/5"
          >
            Refresh
          </button>
        </div>

        <div className="space-y-3">
          {events.map((commentEvent) => (
            <article key={commentEvent.id} className="rounded-xl border border-white/5 bg-black/30 p-4 space-y-3">
              <div className="flex flex-col gap-2 lg:flex-row lg:items-start lg:justify-between">
                <div>
                  <div className="flex flex-wrap items-center gap-2">
                    <span className={`rounded-full px-2 py-1 text-[10px] font-bold uppercase tracking-wider ${statusClass(commentEvent.status)}`}>
                      {commentEvent.status}
                    </span>
                    <span className="text-xs text-neutral-500">{new Date(commentEvent.receivedAt).toLocaleString()}</span>
                  </div>
                  <p className="mt-2 text-sm text-neutral-400">
                    {commentEvent.commentAuthorName || "Unknown commenter"} commented:
                  </p>
                  <p className="text-white mt-1">{commentEvent.commentText || "-"}</p>
                </div>
                <div className="flex flex-wrap gap-2">
                  {(commentEvent.status === "PendingApproval" || commentEvent.generatedReply) && commentEvent.status !== "Replied" && (
                    <button
                      type="button"
                      onClick={() => approveEvent(commentEvent)}
                      className="px-3 py-2 rounded-lg bg-emerald-600 text-xs font-bold text-white hover:bg-emerald-500"
                    >
                      Approve
                    </button>
                  )}
                  {commentEvent.status !== "Replied" && (
                    <>
                      <button
                        type="button"
                        onClick={() => runEventAction(commentEvent.id, "retry")}
                        className="px-3 py-2 rounded-lg bg-white/5 text-xs font-bold text-white hover:bg-white/10 border border-white/5"
                      >
                        Retry
                      </button>
                      <button
                        type="button"
                        onClick={() => runEventAction(commentEvent.id, "skip")}
                        className="px-3 py-2 rounded-lg bg-red-500/10 text-xs font-bold text-red-200 hover:bg-red-500/20 border border-red-500/20"
                      >
                        Skip
                      </button>
                    </>
                  )}
                </div>
              </div>

              {commentEvent.generatedReply && (
                <div className="rounded-lg border border-blue-500/10 bg-blue-500/5 px-4 py-3">
                  <p className="text-[10px] uppercase tracking-widest font-bold text-blue-200">AI draft / published reply</p>
                  <p className="text-sm text-blue-50 mt-1">{commentEvent.generatedReply}</p>
                </div>
              )}

              {(commentEvent.skipReason || commentEvent.errorMessage) && (
                <p className="text-xs text-amber-200">
                  {commentEvent.skipReason || commentEvent.errorMessage}
                </p>
              )}
            </article>
          ))}

          {events.length === 0 && (
            <div className="rounded-xl border border-white/5 bg-black/20 px-4 py-12 text-center text-sm text-neutral-500">
              No Facebook comment webhook events yet.
            </div>
          )}
        </div>
      </section>
    </div>
  );
}

function safeDecode(value: string) {
  try {
    return decodeURIComponent(value);
  } catch {
    return value;
  }
}

function statusClass(status: string) {
  switch (status) {
    case "Replied":
      return "bg-emerald-500/15 text-emerald-200 border border-emerald-500/20";
    case "PendingApproval":
      return "bg-blue-500/15 text-blue-200 border border-blue-500/20";
    case "Failed":
      return "bg-red-500/15 text-red-200 border border-red-500/20";
    case "Skipped":
      return "bg-amber-500/15 text-amber-200 border border-amber-500/20";
    default:
      return "bg-white/10 text-neutral-200 border border-white/10";
  }
}
