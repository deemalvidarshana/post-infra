"use client";

import Link from "next/link";
import { useParams, useSearchParams } from "next/navigation";
import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";

interface RedNoteDownload {
  id: number;
  platform?: string;
  permalinkUrl: string;
  pageId?: string;
  caption?: string;
  s3UploadStatus: string;
  s3Key?: string;
  s3UploadedAt?: string;
  s3UploadError?: string;
  scrapedAt: string;
}

interface RedNoteQueueResponse {
  success?: boolean;
  savedCount?: number;
  updatedCount?: number;
  queuedCount?: number;
  skippedCount?: number;
  message?: string;
  posts?: RedNoteDownload[];
}

interface RedNoteCaptionPromptResponse {
  userId: string;
  pageId: string;
  prompt: string;
  updatedAt?: string;
}

interface RedNoteCaptionRetryResponse {
  success?: boolean;
  message?: string;
  post?: RedNoteDownload;
}

export default function RedNoteDownloaderPage() {
  const params = useParams<{ pageId: string }>();
  const searchParams = useSearchParams();
  const pageId = useMemo(() => safeDecode(params.pageId), [params.pageId]);
  const routeUserId = searchParams.get("userId") || "";
  const [userId, setUserId] = useState("");
  const [links, setLinks] = useState("");
  const [captionPrompt, setCaptionPrompt] = useState("");
  const [promptUpdatedAt, setPromptUpdatedAt] = useState("");
  const [downloads, setDownloads] = useState<RedNoteDownload[]>([]);
  const [message, setMessage] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isQueueing, setIsQueueing] = useState(false);
  const [isSavingPrompt, setIsSavingPrompt] = useState(false);
  const [busyPostIds, setBusyPostIds] = useState<number[]>([]);
  const hasLoadedStoredUser = useRef(false);
  const appliedRouteUserId = useRef("");
  const appliedStoredUserId = useRef(false);

  const runningDownloads = useMemo(
    () => downloads.filter((item) => canStopLocalDownload(item.s3UploadStatus)),
    [downloads]
  );
  const failedDownloads = useMemo(
    () => downloads.filter((item) => canQueueLocalDownload(item.s3UploadStatus)),
    [downloads]
  );
  const busyPostIdSet = useMemo(() => new Set(busyPostIds), [busyPostIds]);

  const loadDownloads = useCallback(async (nextUserId = userId) => {
    if (!nextUserId.trim()) {
      setMessage("Enter a User ID before loading RedNote downloads.");
      return;
    }

    setIsLoading(true);
    setMessage("");
    window.localStorage.setItem("smapi_user_id", nextUserId.trim());

    try {
      const response = await fetch(
        `/api/smapi/Pages/rednote/downloads/${encodeURIComponent(nextUserId.trim())}?pageId=${encodeURIComponent(pageId)}`
      );
      const data = await response.json();
      if (Array.isArray(data)) {
        setDownloads((data as RedNoteDownload[]).filter((item) => item.pageId === pageId));
      }
    } catch {
      setMessage("Could not load RedNote downloads from the backend.");
    } finally {
      setIsLoading(false);
    }
  }, [pageId, userId]);

  const loadCaptionPrompt = useCallback(async (nextUserId = userId) => {
    if (!nextUserId.trim()) {
      return;
    }

    try {
      const response = await fetch(
        `/api/smapi/Pages/rednote/caption-prompt/${encodeURIComponent(nextUserId.trim())}?pageId=${encodeURIComponent(pageId)}`
      );

      if (response.status === 404) {
        setCaptionPrompt("");
        setPromptUpdatedAt("");
        return;
      }

      const data = await response.json().catch(() => null) as RedNoteCaptionPromptResponse | null;
      if (response.ok && data) {
        setCaptionPrompt(data.prompt || "");
        setPromptUpdatedAt(data.updatedAt || "");
      }
    } catch {
      setMessage("Could not load the saved RedNote caption prompt.");
    }
  }, [pageId, userId]);

  useEffect(() => {
    if (routeUserId && appliedRouteUserId.current !== routeUserId) {
      appliedRouteUserId.current = routeUserId;
      window.setTimeout(() => setUserId(routeUserId), 0);
      window.localStorage.setItem("smapi_user_id", routeUserId);
      hasLoadedStoredUser.current = false;
      return;
    }

    if (!routeUserId && !appliedStoredUserId.current) {
      appliedStoredUserId.current = true;
      const storedUserId = getStoredUserId();
      if (storedUserId && storedUserId !== userId) {
        window.setTimeout(() => setUserId(storedUserId), 0);
        return;
      }
    }

    if (hasLoadedStoredUser.current || !userId.trim()) {
      return;
    }

    hasLoadedStoredUser.current = true;
    void loadDownloads(userId);
    void loadCaptionPrompt(userId);
  }, [loadCaptionPrompt, loadDownloads, routeUserId, userId]);

  useEffect(() => {
    if (!userId.trim() || runningDownloads.length === 0) {
      return;
    }

    const timer = window.setInterval(() => {
      void loadDownloads(userId);
    }, 4000);

    return () => window.clearInterval(timer);
  }, [loadDownloads, runningDownloads.length, userId]);

  const queueLinks = async (urls: string[], singlePostId?: number) => {
    setMessage("");

    if (!userId.trim()) {
      setMessage("Please enter a User ID.");
      return;
    }

    if (urls.length === 0) {
      setMessage("Please enter at least one RedNote link.");
      return;
    }

    if (!captionPrompt.trim()) {
      setMessage("Enter a caption prompt for this Facebook Page before queueing downloads.");
      return;
    }

    if (singlePostId) {
      setBusyPostIds((currentIds) => Array.from(new Set([...currentIds, singlePostId])));
    } else {
      setIsQueueing(true);
    }

    window.localStorage.setItem("smapi_user_id", userId.trim());

    try {
      const response = await fetch("/api/smapi/Pages/rednote/downloads", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId,
          urls,
          captionPrompt: captionPrompt.trim()
        })
      });

      const responseText = await response.text();
      let data: RedNoteQueueResponse | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success) {
        setMessage(data?.message || `RedNote download request failed with status ${response.status}.`);
        return;
      }

      setMessage(data.message || `Queued ${data.queuedCount ?? 0} RedNote video(s).`);
      setDownloads((previousDownloads) => {
        const merged = [...(data.posts || []).filter((item) => item.pageId === pageId), ...previousDownloads];
        const seen = new Set<string>();
        return merged.filter((item) => {
          const key = item.permalinkUrl.toLowerCase();
          if (seen.has(key)) {
            return false;
          }
          seen.add(key);
          return true;
        });
      });
      setLinks("");
      setPromptUpdatedAt(new Date().toISOString());
      await loadDownloads(userId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      if (singlePostId) {
        setBusyPostIds((currentIds) => currentIds.filter((postId) => postId !== singlePostId));
      } else {
        setIsQueueing(false);
      }
    }
  };

  const handleQueueLinks = async (event: React.FormEvent) => {
    event.preventDefault();
    const urls = links
      .split(/\r?\n/)
      .map((item) => item.trim())
      .filter(Boolean);

    await queueLinks(urls);
  };

  const handleSaveCaptionPrompt = async () => {
    setMessage("");

    if (!userId.trim()) {
      setMessage("Please enter a User ID.");
      return;
    }

    if (!captionPrompt.trim()) {
      setMessage("Enter a caption prompt for this Facebook Page.");
      return;
    }

    setIsSavingPrompt(true);
    window.localStorage.setItem("smapi_user_id", userId.trim());

    try {
      const response = await fetch("/api/smapi/Pages/rednote/caption-prompt", {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId,
          prompt: captionPrompt.trim()
        })
      });
      const data = await response.json().catch(() => null);
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Prompt save failed with status ${response.status}.`);
        return;
      }

      const savedPrompt = data.prompt as RedNoteCaptionPromptResponse | undefined;
      setCaptionPrompt(savedPrompt?.prompt || captionPrompt.trim());
      setPromptUpdatedAt(savedPrompt?.updatedAt || new Date().toISOString());
      setMessage(data.message || "RedNote caption prompt saved.");
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsSavingPrompt(false);
    }
  };

  const handleStopDownloads = async (postsToStop: RedNoteDownload[]) => {
    if (!userId.trim()) {
      setMessage("Please enter a User ID.");
      return;
    }

    const postIds = postsToStop
      .filter((post) => canStopLocalDownload(post.s3UploadStatus))
      .map((post) => post.id);

    if (postIds.length === 0) {
      setMessage("No queued or active downloads are available to stop.");
      return;
    }

    setBusyPostIds((currentIds) => Array.from(new Set([...currentIds, ...postIds])));

    try {
      const response = await fetch("/api/smapi/FacebookS3Uploads/facebook/reels/stop", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId,
          postIds
        })
      });
      const data = await response.json().catch(() => null);
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Stop request failed with status ${response.status}.`);
        return;
      }

      setMessage(data.message || `Stopped ${data.stoppedCount ?? 0} download(s).`);
      await loadDownloads(userId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setBusyPostIds((currentIds) => currentIds.filter((postId) => !postIds.includes(postId)));
    }
  };

  const handleViewVideo = async (post: RedNoteDownload) => {
    if (!userId.trim()) {
      setMessage("Please enter a User ID.");
      return;
    }

    if (!isLocallyDownloaded(post.s3UploadStatus) || !post.s3Key) {
      setMessage("This RedNote video has not been downloaded locally yet.");
      return;
    }

    const viewer = window.open("about:blank", "_blank");
    if (!viewer) {
      setMessage("Allow pop-ups for this site to open the local video.");
      return;
    }

    viewer.opener = null;
    viewer.document.write('<p style="font-family: sans-serif; padding: 24px;">Loading local video...</p>');
    setBusyPostIds((currentIds) => Array.from(new Set([...currentIds, post.id])));

    try {
      const response = await fetch(
        `/api/smapi/FacebookS3Uploads/facebook/reels/${post.id}/url?userId=${encodeURIComponent(userId.trim())}&pageId=${encodeURIComponent(pageId)}&expiresMinutes=60`
      );
      const data = await response.json().catch(() => null);
      if (!response.ok || !data?.success || !data.url) {
        viewer.close();
        setMessage(data?.message || `Could not open local video. Backend status ${response.status}.`);
        return;
      }

      viewer.location.href = data.url;
    } catch {
      viewer.close();
      setMessage("Could not connect to the backend server.");
    } finally {
      setBusyPostIds((currentIds) => currentIds.filter((postId) => postId !== post.id));
    }
  };

  const handleRetryCaption = async (post: RedNoteDownload) => {
    setMessage("");

    if (!userId.trim()) {
      setMessage("Please enter a User ID.");
      return;
    }

    if (!isLocallyDownloaded(post.s3UploadStatus) || !post.s3Key) {
      setMessage("Download the RedNote video before retrying the AI caption.");
      return;
    }

    if (!captionPrompt.trim()) {
      setMessage("Enter the caption prompt for this Facebook Page before retrying AI captions.");
      return;
    }

    setBusyPostIds((currentIds) => Array.from(new Set([...currentIds, post.id])));

    try {
      const response = await fetch(`/api/smapi/Pages/rednote/downloads/${post.id}/caption/retry`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId,
          captionPrompt: captionPrompt.trim()
        })
      });
      const data = await response.json().catch(() => null) as RedNoteCaptionRetryResponse | null;
      if (!response.ok || !data?.success || !data.post) {
        if (data?.post) {
          setDownloads((currentDownloads) => currentDownloads.map((item) => (
            item.id === data.post?.id ? data.post : item
          )));
        }
        setMessage(data?.message || `AI caption retry failed with status ${response.status}.`);
        return;
      }

      setDownloads((currentDownloads) => currentDownloads.map((item) => (
        item.id === data.post?.id ? data.post : item
      )));
      setMessage(data.message || "RedNote AI caption regenerated.");
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setBusyPostIds((currentIds) => currentIds.filter((postId) => postId !== post.id));
    }
  };

  const handleDeletePost = async (post: RedNoteDownload) => {
    setMessage("");
    setBusyPostIds((currentIds) => Array.from(new Set([...currentIds, post.id])));

    try {
      const response = await fetch(
        `/api/smapi/Pages/facebook/posts/${post.id}?userId=${encodeURIComponent(userId.trim())}&pageId=${encodeURIComponent(pageId)}`,
        { method: "DELETE" }
      );
      const data = await response.json().catch(() => null);
      if (!response.ok) {
        setMessage(data?.message || `Delete failed with status ${response.status}.`);
        return;
      }

      setDownloads((currentDownloads) => currentDownloads.filter((item) => item.id !== post.id));
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setBusyPostIds((currentIds) => currentIds.filter((postId) => postId !== post.id));
    }
  };

  return (
    <div className="space-y-8 animate-in fade-in duration-700">
      <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <Link href="/accounts/facebook" className="inline-flex items-center gap-2 text-sm text-neutral-500 hover:text-white transition-colors mb-4">
            <span className="material-symbols-outlined text-sm">arrow_back</span>
            Facebook Pages
          </Link>
          <h1 className="text-3xl font-bold text-white tracking-tight">RedNote Downloader</h1>
          <p className="text-neutral-500 text-sm mt-1">Page ID: {pageId}</p>
        </div>
        <div className="flex flex-col sm:flex-row gap-2">
          <input
            value={userId}
            onChange={(event) => setUserId(event.target.value)}
            className="w-full sm:w-56 bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-rose-500 focus:outline-none"
            placeholder="User ID"
          />
          <button
            type="button"
            onClick={() => {
              void loadDownloads();
              void loadCaptionPrompt();
            }}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-sm">sync</span>
            {isLoading ? "Loading" : "Load"}
          </button>
        </div>
      </header>

      <section className="glass-panel rounded-xl border border-white/5 p-6">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div>
            <h2 className="text-xl font-bold text-white">Bulk Download</h2>
            <p className="text-neutral-500 text-sm mt-1">Add RedNote or Xiaohongshu links and queue local downloads.</p>
          </div>
          <div className="flex flex-wrap gap-3 text-[10px] uppercase tracking-widest text-neutral-500">
            <span>{downloads.length} stored</span>
            <span>{runningDownloads.length} running</span>
            <span>{failedDownloads.length} failed</span>
          </div>
        </div>

        <form onSubmit={handleQueueLinks} className="mt-6 grid grid-cols-1 xl:grid-cols-[1.3fr_0.7fr] gap-6">
          <div className="space-y-4">
            <label className="space-y-2 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">RedNote Links</span>
              <textarea
                required
                rows={6}
                value={links}
                onChange={(event) => setLinks(event.target.value)}
                placeholder="http://xhslink.com/o/AibcYPm7lWn"
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-rose-500 focus:outline-none transition-all resize-none"
              />
            </label>

            <label className="space-y-2 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Caption Prompt</span>
              <textarea
                required
                rows={4}
                value={captionPrompt}
                onChange={(event) => setCaptionPrompt(event.target.value)}
                placeholder="Write a short Facebook Reel caption from the video frames. Return only the caption."
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-rose-500 focus:outline-none transition-all resize-none"
              />
            </label>
          </div>

          <div className="flex flex-col gap-4">
            <button
              type="button"
              onClick={handleSaveCaptionPrompt}
              disabled={isSavingPrompt || !captionPrompt.trim()}
              className="w-full py-3 rounded-lg border border-white/10 bg-white/5 text-sm font-bold text-neutral-200 hover:bg-white/10 disabled:opacity-50 flex items-center justify-center gap-2"
            >
              {isSavingPrompt ? (
                <>
                  <span className="h-4 w-4 rounded-full border-2 border-white/30 border-t-white animate-spin" />
                  Saving...
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined text-sm">save</span>
                  Save Prompt
                </>
              )}
            </button>
            <button
              type="submit"
              disabled={isQueueing}
              className="w-full py-4 bg-rose-600 hover:bg-rose-500 text-white rounded-lg font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-rose-600/20"
            >
              {isQueueing ? (
                <>
                  <span className="h-5 w-5 rounded-full border-2 border-white/30 border-t-white animate-spin" />
                  Queueing...
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined text-sm">download</span>
                  Queue Downloads
                </>
              )}
            </button>
            <button
              type="button"
              onClick={() => handleStopDownloads(runningDownloads)}
              disabled={runningDownloads.length === 0 || busyPostIds.length > 0}
              className="w-full py-3 rounded-lg border border-red-500/20 bg-red-500/10 text-sm font-bold text-red-200 hover:bg-red-500/20 disabled:opacity-50 flex items-center justify-center gap-2"
            >
              <span className="material-symbols-outlined text-sm">stop_circle</span>
              Stop Running
            </button>
            {promptUpdatedAt && (
              <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-xs text-neutral-500">
                Prompt saved: {formatDateTime(promptUpdatedAt)}
              </div>
            )}
            {message && (
              <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
                {message}
              </div>
            )}
          </div>
        </form>
      </section>

      <section className="glass-panel rounded-xl border border-white/5 p-6">
        <div className="mb-4 flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
          <div>
            <h2 className="text-xl font-bold text-white">Download Queue</h2>
            <p className="text-neutral-500 text-sm mt-1">Queued, downloading, downloaded, failed, and local file paths.</p>
          </div>
          <span className="text-[10px] uppercase tracking-widest text-neutral-500">{downloads.length} videos</span>
        </div>

        <div className="rounded-lg border border-white/5 overflow-hidden">
          <div className="hidden md:grid grid-cols-[minmax(220px,0.9fr)_minmax(240px,360px)_130px_160px_130px_124px] gap-4 bg-black/40 px-4 py-3 text-[10px] uppercase tracking-widest font-bold text-neutral-500">
            <span>Link</span>
            <span>Caption</span>
            <span>Status</span>
            <span>Local File</span>
            <span>Queued</span>
            <span className="sr-only">Actions</span>
          </div>

          {downloads.length > 0 ? downloads.map((post) => (
            <div key={post.id} className="grid grid-cols-1 md:grid-cols-[minmax(220px,0.9fr)_minmax(240px,360px)_130px_160px_130px_124px] gap-3 md:gap-4 bg-black/20 px-4 py-4 border-t border-white/5">
              <div className="min-w-0 flex items-center gap-3">
                <span className="material-symbols-outlined text-rose-300 text-sm">link</span>
                <a href={post.permalinkUrl} target="_blank" rel="noreferrer" className="truncate text-sm text-neutral-200 hover:text-white">
                  {post.permalinkUrl}
                </a>
              </div>
              <div className="min-w-0 max-w-[360px] text-sm leading-6 text-neutral-300 whitespace-normal break-words">
                {post.caption || "Pending AI caption"}
              </div>
              <div className="flex min-w-0 items-center gap-2">
                <span className={`inline-flex w-fit rounded px-2 py-1 text-[10px] font-bold uppercase tracking-widest ${statusClass(post.s3UploadStatus)}`}>
                  {localStatusLabel(post.s3UploadStatus)}
                </span>
              </div>
              <div className="min-w-0 text-xs text-neutral-500">
                <span className="line-clamp-1">{post.s3Key || "Pending"}</span>
                {post.s3UploadError && <span className="mt-1 block line-clamp-1 text-red-300">{post.s3UploadError}</span>}
              </div>
              <span className="text-xs text-neutral-500">{formatDateTime(post.scrapedAt)}</span>
              <div className="flex flex-wrap justify-start gap-2 md:justify-end">
                {hasPendingCaption(post) && isLocallyDownloaded(post.s3UploadStatus) && (
                  <button
                    type="button"
                    onClick={() => handleRetryCaption(post)}
                    disabled={busyPostIdSet.has(post.id)}
                    title="Retry AI caption"
                    aria-label="Retry AI caption"
                    className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-amber-500/20 bg-amber-500/10 text-amber-200 hover:bg-amber-500/20 disabled:opacity-40"
                  >
                    {busyPostIdSet.has(post.id) ? (
                      <span className="h-4 w-4 rounded-full border-2 border-amber-200/30 border-t-amber-100 animate-spin" />
                    ) : (
                      <span className="material-symbols-outlined text-[16px]">refresh</span>
                    )}
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => handleViewVideo(post)}
                  disabled={!isLocallyDownloaded(post.s3UploadStatus) || busyPostIdSet.has(post.id)}
                  title="View local video"
                  aria-label="View local video"
                  className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-white/5 bg-white/5 text-neutral-300 hover:bg-white/10 hover:text-white disabled:opacity-40"
                >
                  <span className="material-symbols-outlined text-[16px]">open_in_new</span>
                </button>
                {canQueueLocalDownload(post.s3UploadStatus) && (
                  <button
                    type="button"
                    onClick={() => queueLinks([post.permalinkUrl], post.id)}
                    disabled={busyPostIdSet.has(post.id)}
                    title="Retry download"
                    aria-label="Retry download"
                    className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-blue-500/20 bg-blue-500/10 text-blue-300 hover:bg-blue-500/20 disabled:opacity-40"
                  >
                    <span className="material-symbols-outlined text-[16px]">refresh</span>
                  </button>
                )}
                {canStopLocalDownload(post.s3UploadStatus) && (
                  <button
                    type="button"
                    onClick={() => handleStopDownloads([post])}
                    disabled={busyPostIdSet.has(post.id)}
                    title="Stop download"
                    aria-label="Stop download"
                    className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-red-500/20 bg-red-500/10 text-red-300 hover:bg-red-500/20 disabled:opacity-40"
                  >
                    <span className="material-symbols-outlined text-[16px]">stop_circle</span>
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => handleDeletePost(post)}
                  disabled={busyPostIdSet.has(post.id)}
                  title="Delete row"
                  aria-label="Delete row"
                  className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-red-500/20 bg-red-500/10 text-red-300 hover:bg-red-500/20 disabled:opacity-40"
                >
                  <span className="material-symbols-outlined text-[16px]">delete</span>
                </button>
              </div>
            </div>
          )) : (
            <div className="px-4 py-10 text-center text-sm text-neutral-500">No RedNote downloads yet.</div>
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

function getStoredUserId() {
  if (typeof window === "undefined") {
    return "";
  }

  return window.localStorage.getItem("smapi_user_id") || "";
}

function canStopLocalDownload(status?: string) {
  return status === "Queued" || status === "Downloading" || status === "Uploading";
}

function canQueueLocalDownload(status?: string) {
  return !isLocallyDownloaded(status) && !canStopLocalDownload(status);
}

function isLocallyDownloaded(status?: string) {
  return status === "Downloaded" || status === "Uploaded";
}

function hasPendingCaption(post: RedNoteDownload) {
  return !post.caption?.trim();
}

function statusClass(status?: string) {
  switch (status) {
    case "Downloaded":
    case "Uploaded":
      return "bg-emerald-500/10 text-emerald-300 border border-emerald-500/20";
    case "Queued":
    case "Downloading":
    case "Uploading":
      return "bg-blue-500/10 text-blue-300 border border-blue-500/20";
    case "Failed":
      return "bg-red-500/10 text-red-300 border border-red-500/20";
    case "Cancelled":
      return "bg-amber-500/10 text-amber-300 border border-amber-500/20";
    default:
      return "bg-zinc-500/10 text-zinc-300 border border-zinc-500/20";
  }
}

function localStatusLabel(status?: string) {
  switch (status) {
    case "Downloaded":
    case "Uploaded":
      return "Downloaded";
    case "Downloading":
    case "Uploading":
      return "Downloading";
    case "NotUploaded":
    case undefined:
    case "":
      return "NotDownloaded";
    case "Cancelled":
      return "Stopped";
    default:
      return status;
  }
}

function formatDateTime(value?: string) {
  if (!value) {
    return "Pending";
  }

  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Pending";
  }

  return date.toLocaleString();
}
