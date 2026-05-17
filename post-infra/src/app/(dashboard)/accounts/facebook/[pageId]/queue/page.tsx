"use client";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useParams, useRouter } from "next/navigation";

interface FacebookPageApi {
  id: number;
  userId?: string;
  pageId: string;
  pageName: string;
  category?: string;
  accessToken: string;
  avatarUrl?: string;
}

interface ScrapedPost {
  id: number;
  platform?: string;
  permalinkUrl: string;
  videoUrl?: string;
  postCreatedAt?: string;
  caption?: string;
  s3UploadStatus?: string;
  s3Key?: string;
  s3UploadedAt?: string;
  scrapedAt: string;
}

interface UploadJob {
  id: number;
  pageId: string;
  pageName?: string;
  facebookPostUrlId?: number;
  videoSourceUrl: string;
  caption?: string;
  status: string;
  s3Bucket?: string;
  s3Region?: string;
  s3Key?: string;
  graphApiVersion: string;
  facebookVideoId?: string;
  facebookPostId?: string;
  errorMessage?: string;
  scheduledFor?: string;
  createdAt: string;
  updatedAt: string;
  startedAt?: string;
  completedAt?: string;
  retainUntil?: string;
}

type SourcePlatform = "Facebook" | "TikTok" | "RedNote";

export default function QueuePage() {
  const params = useParams<{ pageId: string }>();
  const router = useRouter();
  const routePageId = useMemo(() => safeDecode(params.pageId), [params.pageId]);
  const [userId, setUserId] = useState(() => getStoredUserId());
  const [pages, setPages] = useState<FacebookPageApi[]>([]);
  const [posts, setPosts] = useState<ScrapedPost[]>([]);
  const [jobs, setJobs] = useState<UploadJob[]>([]);
  const [sourcePlatform, setSourcePlatform] = useState<SourcePlatform>("Facebook");
  const [pageForm, setPageForm] = useState({ pageId: routePageId, accessToken: "" });
  const [scheduleForm, setScheduleForm] = useState({ dailyPostCount: "6", startAt: getDefaultStartAt() });
  const [message, setMessage] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSavingPage, setIsSavingPage] = useState(false);
  const [publishForm, setPublishForm] = useState({
    graphApiVersion: "v24.0"
  });
  const hasLoadedStoredUser = useRef(false);
  const previousSourcePlatform = useRef<SourcePlatform>("Facebook");
  const selectedPage = useMemo(
    () => pages.find((page) => page.pageId === routePageId) ?? null,
    [pages, routePageId]
  );

  const visibleJobs = useMemo(() => {
    if (!routePageId) {
      return jobs;
    }

    return jobs.filter((job) => job.pageId === routePageId);
  }, [jobs, routePageId]);
  const activeJobPostIds = useMemo(
    () => new Set(visibleJobs
      .filter((job) => job.facebookPostUrlId && job.status !== "Failed")
      .map((job) => job.facebookPostUrlId as number)),
    [visibleJobs]
  );
  const queueablePosts = useMemo(
    () => posts.filter((post) => !activeJobPostIds.has(post.id)),
    [activeJobPostIds, posts]
  );
  const intervalHours = useMemo(() => {
    const dailyPostCount = Number(scheduleForm.dailyPostCount);
    return Number.isFinite(dailyPostCount) && dailyPostCount > 0 ? 24 / dailyPostCount : 0;
  }, [scheduleForm.dailyPostCount]);

  const loadData = useCallback(async (nextUserId = userId) => {
    if (!nextUserId.trim()) {
      setMessage("Enter a User ID before loading queue data.");
      return;
    }

    setIsLoading(true);
    setMessage("");
    let effectiveUserId = nextUserId.trim();
    let effectivePageId = routePageId;
    window.localStorage.setItem("smapi_user_id", effectiveUserId);

    try {
      const pagesResponse = await fetch(`/api/smapi/Pages/facebook/${encodeURIComponent(effectiveUserId)}`);
      const pagesData = await pagesResponse.json();
      const loadedPages = Array.isArray(pagesData) ? pagesData : [];
      let currentPage = loadedPages.find((page: FacebookPageApi) => page.pageId === routePageId);
      if (!currentPage && loadedPages.length === 1) {
        currentPage = loadedPages[0];
        effectivePageId = currentPage.pageId;
        router.replace(`/accounts/facebook/${encodeURIComponent(effectivePageId)}/queue`);
      }

      if (!currentPage) {
        const pageResponse = await fetch(`/api/smapi/Pages/facebook/by-page/${encodeURIComponent(effectivePageId)}`);
        if (pageResponse.ok) {
          const pageData = await pageResponse.json();
          if (pageData?.pageId === effectivePageId) {
            currentPage = pageData as FacebookPageApi;
            effectivePageId = currentPage.pageId;
            if (currentPage.userId && currentPage.userId !== effectiveUserId) {
              effectiveUserId = currentPage.userId;
              setUserId(effectiveUserId);
              window.localStorage.setItem("smapi_user_id", effectiveUserId);
            }
          }
        }
      }

      const [postsResponse, jobsResponse] = await Promise.all([
        fetch(`/api/smapi/Pages/facebook/posts/${encodeURIComponent(effectiveUserId)}?pageId=${encodeURIComponent(effectivePageId)}&platform=${encodeURIComponent(sourcePlatform)}&downloadedOnly=true`),
        fetch(`/api/smapi/FacebookReelUploads/${encodeURIComponent(effectiveUserId)}?pageId=${encodeURIComponent(effectivePageId)}&platform=${encodeURIComponent(sourcePlatform)}`)
      ]);
      const [postsData, jobsData] = await Promise.all([postsResponse.json(), jobsResponse.json()]);

      setPages(currentPage && !loadedPages.some((page: FacebookPageApi) => page.id === currentPage.id) ? [currentPage, ...loadedPages] : loadedPages);
      setPageForm({
        pageId: currentPage?.pageId || routePageId,
        accessToken: currentPage?.accessToken || ""
      });
      setPosts(Array.isArray(postsData) ? (postsData as ScrapedPost[]).filter((post) => isDownloadedPlatformVideo(post, sourcePlatform)) : []);
      setJobs(Array.isArray(jobsData) ? jobsData : []);
    } catch {
      setMessage("Could not load queue data from the backend.");
    } finally {
      setIsLoading(false);
    }
  }, [routePageId, router, sourcePlatform, userId]);

  const loadJobs = useCallback(async (nextUserId = userId) => {
    try {
      const response = await fetch(`/api/smapi/FacebookReelUploads/${encodeURIComponent(nextUserId.trim())}?pageId=${encodeURIComponent(routePageId)}&platform=${encodeURIComponent(sourcePlatform)}`);
      const data = await response.json();
      if (Array.isArray(data)) {
        setJobs(data);
      }
    } catch {
      // Polling failures are ignored so the active form stays usable.
    }
  }, [routePageId, sourcePlatform, userId]);

  useEffect(() => {
    if (!userId) {
      return;
    }

    const timer = window.setInterval(() => {
      void loadJobs(userId);
    }, 5000);

    return () => window.clearInterval(timer);
  }, [loadJobs, userId]);

  useEffect(() => {
    if (hasLoadedStoredUser.current || !userId.trim()) {
      return;
    }

    hasLoadedStoredUser.current = true;
    void loadData(userId);
  }, [loadData, userId]);

  useEffect(() => {
    if (!hasLoadedStoredUser.current || !userId.trim()) {
      return;
    }

    if (previousSourcePlatform.current === sourcePlatform) {
      return;
    }

    previousSourcePlatform.current = sourcePlatform;
    void loadData(userId);
  }, [loadData, sourcePlatform, userId]);

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage("");

    if (!userId.trim() || !routePageId) {
      setMessage("Enter a User ID and open this queue from a connected Facebook Page.");
      return;
    }

    const dailyPostCount = Number(scheduleForm.dailyPostCount);
    if (!Number.isInteger(dailyPostCount) || dailyPostCount < 1 || dailyPostCount > 48) {
      setMessage("Enter a daily post count between 1 and 48.");
      return;
    }

    const startAt = new Date(scheduleForm.startAt);
    if (Number.isNaN(startAt.getTime())) {
      setMessage("Select a valid schedule start date/time.");
      return;
    }

    if (queueablePosts.length === 0) {
      setMessage(`No locally downloaded ${sourcePlatform} videos are available to queue. Download videos from the scraper page first.`);
      return;
    }

    setIsSubmitting(true);

    try {
      const response = await fetch("/api/smapi/FacebookReelUploads/batch", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId: routePageId,
          platform: sourcePlatform,
          dailyPostCount,
          startAt: startAt.toISOString(),
          graphApiVersion: publishForm.graphApiVersion.trim() || "v24.0"
        })
      });

      const responseText = await response.text();
      let data: { success?: boolean; message?: string; jobs?: UploadJob[]; queuedCount?: number; skippedCount?: number; matchedCount?: number; intervalHours?: number } | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success) {
        setMessage(data?.message || responseText || `Upload job request failed with status ${response.status}.`);
        return;
      }

      setJobs((previousJobs) => [...(data.jobs || []), ...previousJobs]);
      setMessage(data.message || `Queued ${data.queuedCount ?? 0} ${sourcePlatform} video(s).`);
      await loadJobs(userId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSavePageCredentials = async () => {
    setMessage("");

    if (!selectedPage) {
      setMessage("Load the connected Facebook Page before saving changes.");
      return;
    }

    if (!userId.trim() || !pageForm.pageId.trim() || !pageForm.accessToken.trim()) {
      setMessage("User ID, Page ID and Page Access Token are required.");
      return;
    }

    setIsSavingPage(true);

    try {
      const nextPageId = pageForm.pageId.trim();
      const response = await fetch(`/api/smapi/Pages/facebook/${selectedPage.id}`, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId: nextPageId,
          pageName: selectedPage.pageName,
          accessToken: pageForm.accessToken,
          category: selectedPage.category || null,
          avatarUrl: selectedPage.avatarUrl || null
        })
      });

      const responseText = await response.text();
      let data: { success?: boolean; message?: string } | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Failed to update Facebook Page. Backend status ${response.status}.`);
        return;
      }

      setMessage("Facebook Page ID and token updated.");
      await loadData(userId);

      if (nextPageId !== routePageId) {
        router.replace(`/accounts/facebook/${encodeURIComponent(nextPageId)}/queue`);
      }
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsSavingPage(false);
    }
  };
  
  const handleRetryJob = async (jobId: number) => {
    setMessage("");
    setIsSubmitting(true);

    try {
      const response = await fetch(`/api/smapi/FacebookReelUploads/${jobId}/retry`, {
        method: "POST"
      });

      const data = await response.json();
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Retry failed with status ${response.status}.`);
        return;
      }

      setMessage(data.message || "Job queued for re-upload.");
      await loadJobs(userId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsSubmitting(false);
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
          <h1 className="text-3xl font-bold text-white tracking-tight">{selectedPage?.pageName || "Facebook Page"} Video Upload Queue</h1>
          <p className="text-neutral-500 text-sm mt-1">Page ID: {routePageId}</p>
        </div>
        <div className="flex flex-col sm:flex-row gap-2">
          <label className="sr-only" htmlFor="queue-source-platform">Source Platform</label>
          <select
            id="queue-source-platform"
            value={sourcePlatform}
            onChange={(event) => setSourcePlatform(event.target.value as SourcePlatform)}
            className="w-full sm:w-40 bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm font-semibold text-white focus:border-blue-500 focus:outline-none"
          >
            <option value="Facebook">Facebook</option>
            <option value="TikTok">TikTok</option>
            <option value="RedNote">RedNote</option>
          </select>
          <input
            value={userId}
            onChange={(event) => setUserId(event.target.value)}
            className="w-full sm:w-56 bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            placeholder="User ID"
          />
          <button
            type="button"
            onClick={() => loadData()}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-sm">sync</span>
            {isLoading ? "Loading" : "Load Data"}
          </button>
        </div>
      </header>

      <form onSubmit={handleSubmit} className="grid grid-cols-1 xl:grid-cols-[1.1fr_0.9fr] gap-6">
        <section className="glass-panel p-6 rounded-xl border border-white/5 space-y-5">
          <div>
            <h2 className="text-xl font-bold text-white">Page Access</h2>
            <p className="text-neutral-500 text-sm mt-1">Confirm the Facebook Page ID, token, and source platform used for publishing.</p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <label className="space-y-2">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Facebook Page ID</span>
              <input
                value={pageForm.pageId}
                onChange={(event) => setPageForm({ ...pageForm, pageId: event.target.value })}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              />
              {userId.trim() && !isLoading && pages.length === 0 && (
                <span className="block text-xs text-amber-300">
                  No connected pages found for this User ID. Check the User ID or load pages from Facebook Accounts.
                </span>
              )}
            </label>

            <label className="space-y-2">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Graph API Version</span>
              <input
                value={publishForm.graphApiVersion}
                onChange={(event) => setPublishForm({ ...publishForm, graphApiVersion: event.target.value })}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
                placeholder="v24.0"
              />
            </label>
          </div>

          <label className="space-y-2 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Page Access Token</span>
            <textarea
              rows={3}
              value={pageForm.accessToken}
              onChange={(event) => setPageForm({ ...pageForm, accessToken: event.target.value })}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              placeholder="EAAB..."
            />
          </label>

          <button
            type="button"
            onClick={handleSavePageCredentials}
            disabled={isSavingPage || !selectedPage}
            className="inline-flex items-center justify-center gap-2 rounded-lg border border-white/5 bg-white/5 px-4 py-3 text-xs font-bold text-white hover:bg-white/10 disabled:opacity-50"
          >
            {isSavingPage ? (
              <>
                <span className="h-4 w-4 rounded-full border-2 border-white/30 border-t-white animate-spin" />
                Saving...
              </>
            ) : (
              <>
                <span className="material-symbols-outlined text-sm">save</span>
                Save Page Details
              </>
            )}
          </button>

          <div className="grid grid-cols-1 md:grid-cols-3 gap-3 pt-2">
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Downloaded {sourcePlatform}</p>
              <p className="mt-1 text-2xl font-bold text-white">{posts.length}</p>
            </div>
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Ready To Queue</p>
              <p className="mt-1 text-2xl font-bold text-blue-300">{queueablePosts.length}</p>
            </div>
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Interval</p>
              <p className="mt-1 text-2xl font-bold text-emerald-300">{formatInterval(intervalHours)}</p>
            </div>
          </div>
        </section>

        <section className="glass-panel p-6 rounded-xl border border-white/5 space-y-5">
          <div>
            <h2 className="text-xl font-bold text-white">Daily Schedule</h2>
            <p className="text-neutral-500 text-sm mt-1">Queue only downloaded {sourcePlatform} videos. The queue spaces Facebook publishing evenly across 24 hours.</p>
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <label className="space-y-2">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Daily Post Count</span>
              <input
                required
                min="1"
                max="48"
                type="number"
                value={scheduleForm.dailyPostCount}
                onChange={(event) => setScheduleForm({ ...scheduleForm, dailyPostCount: event.target.value })}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              />
              <span className="block text-xs text-neutral-500">Example: 6 posts per day means one reel every 4 hours.</span>
            </label>
            <label className="space-y-2">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Start At</span>
              <input
                required
                type="datetime-local"
                value={scheduleForm.startAt}
                onChange={(event) => setScheduleForm({ ...scheduleForm, startAt: event.target.value })}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              />
            </label>
          </div>

          <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-4 text-sm text-neutral-300">
            This page uses the already downloaded video files from the scraper page and publishes them with their saved captions. It does not download videos again.
          </div>

          <button
            type="submit"
            disabled={isSubmitting || queueablePosts.length === 0}
            className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
          >
            {isSubmitting ? (
              <>
                <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                Queueing...
              </>
            ) : (
                <>
                  <span className="material-symbols-outlined text-sm">publish</span>
                Queue {queueablePosts.length} Downloaded {sourcePlatform} Video{queueablePosts.length === 1 ? "" : "s"}
                </>
            )}
          </button>

          {message && (
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
              {message}
            </div>
          )}
        </section>
      </form>

      <section className="glass-panel p-6 rounded-xl border border-white/5">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h2 className="text-xl font-bold text-white">Queued Videos</h2>
            <p className="text-neutral-500 text-sm mt-1">Scheduled publish time, status, captions, and local downloaded {sourcePlatform} source details for this Facebook Page.</p>
          </div>
          <span className="text-[10px] uppercase tracking-widest text-neutral-500">{visibleJobs.length} jobs</span>
        </div>

        <div className="rounded-lg border border-white/5 overflow-hidden">
          <div className="hidden md:grid grid-cols-[72px_130px_150px_140px_140px_minmax(220px,1fr)_minmax(180px,0.8fr)] gap-4 bg-black/40 px-4 py-3 text-[10px] uppercase tracking-widest font-bold text-neutral-500">
            <span>Job</span>
            <span>Status</span>
            <span>Scheduled</span>
            <span>Queued</span>
            <span>Completed</span>
            <span>Caption</span>
            <span>Source</span>
          </div>
          {visibleJobs.length > 0 ? visibleJobs.map((job) => (
            <div key={job.id} className="grid grid-cols-1 md:grid-cols-[72px_130px_150px_140px_140px_minmax(220px,1fr)_minmax(180px,0.8fr)] gap-3 md:gap-4 bg-black/20 px-4 py-3 border-t border-white/5">
              <div className="flex flex-col gap-1">
                <span className="text-sm font-bold text-white">#{job.id}</span>
                {job.status !== "Published" && (
                  <button
                    onClick={() => handleRetryJob(job.id)}
                    className="text-[10px] text-blue-400 hover:text-blue-300 font-bold flex items-center gap-0.5"
                  >
                    <span className="material-symbols-outlined text-[12px]">replay</span>
                    Retry
                  </button>
                )}
              </div>
              <span className={`inline-flex w-fit items-center rounded px-2 py-1 text-[10px] font-bold uppercase tracking-widest ${statusClass(job.status)}`}>
                {job.status}
              </span>
              <span className="text-xs text-blue-200">{formatDateTime(job.scheduledFor)}</span>
              <span className="text-xs text-neutral-500">{formatDateTime(job.createdAt)}</span>
              <span className="text-xs text-neutral-500">{formatDateTime(job.completedAt)}</span>
              <div className="min-w-0">
                <p className="line-clamp-2 text-sm text-neutral-300">{formatCaption(job.caption, sourcePlatform)}</p>
                {job.status === "StoredLocally" && (
                  <p className="mt-1 text-[10px] text-amber-300 flex items-center gap-1">
                    <span className="material-symbols-outlined text-[12px]">info</span>
                    Public URL required for Facebook upload
                  </p>
                )}
                {job.errorMessage && <p className="line-clamp-1 text-xs text-red-300">{job.errorMessage}</p>}
              </div>
              <div className="min-w-0 text-xs text-neutral-500">
                <a href={job.videoSourceUrl} target="_blank" rel="noreferrer" className="block truncate text-neutral-300 hover:text-white">
                  {job.videoSourceUrl}
                </a>
                <p className="truncate">{job.s3Key || "Pending local download"}</p>
              </div>
            </div>
          )) : (
            <div className="px-4 py-10 text-center text-sm text-neutral-500">No jobs yet.</div>
          )}
        </div>
      </section>
    </div>
  );
}

function statusClass(status: string) {
  switch (status) {
    case "Published":
      return "bg-emerald-500/10 text-emerald-300 border border-emerald-500/20";
    case "StoredLocally":
      return "bg-emerald-500/10 text-emerald-300 border border-emerald-500/20";
    case "Failed":
      return "bg-red-500/10 text-red-300 border border-red-500/20";
    case "Queued":
      return "bg-zinc-500/10 text-zinc-300 border border-zinc-500/20";
    default:
      return "bg-blue-500/10 text-blue-300 border border-blue-500/20";
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

function formatInterval(hours: number) {
  if (!Number.isFinite(hours) || hours <= 0) {
    return "-";
  }

  if (hours >= 1) {
    return `${trimNumber(hours)}h`;
  }

  return `${Math.round(hours * 60)}m`;
}

function trimNumber(value: number) {
  return Number.isInteger(value) ? String(value) : value.toFixed(1);
}

function isDownloadedPlatformVideo(post: ScrapedPost, sourcePlatform: SourcePlatform) {
  const platform = (post.platform || "Facebook").toLowerCase();
  return platform === sourcePlatform.toLowerCase()
    && (post.s3UploadStatus === "Downloaded" || post.s3UploadStatus === "Uploaded")
    && Boolean(post.s3Key);
}

function formatCaption(caption: string | undefined, sourcePlatform: SourcePlatform) {
  if (caption) {
    return caption;
  }

  return sourcePlatform === "RedNote" ? "" : "No caption";
}

function getDefaultStartAt() {
  const date = new Date();
  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 16);
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
