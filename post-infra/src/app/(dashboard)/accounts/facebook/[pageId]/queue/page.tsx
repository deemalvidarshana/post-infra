"use client";

import React, { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useParams, useRouter, useSearchParams } from "next/navigation";

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
  publishAsStory?: boolean;
  facebookStoryId?: string;
  storyPublishedAt?: string;
  storyErrorMessage?: string;
  errorMessage?: string;
  scheduledFor?: string;
  createdAt: string;
  updatedAt: string;
  startedAt?: string;
  completedAt?: string;
  retainUntil?: string;
}

type SourcePlatform = "Facebook" | "TikTok" | "RedNote";
const DEFAULT_DAILY_POST_COUNT = 6;
const JOB_POLL_INTERVAL_MS = 30000;

export default function QueuePage() {
  const params = useParams<{ pageId: string }>();
  const router = useRouter();
  const searchParams = useSearchParams();
  const routePageId = useMemo(() => safeDecode(params.pageId), [params.pageId]);
  const routeUserId = searchParams.get("userId") || "";
  const [userId, setUserId] = useState("");
  const [pages, setPages] = useState<FacebookPageApi[]>([]);
  const [posts, setPosts] = useState<ScrapedPost[]>([]);
  const [jobs, setJobs] = useState<UploadJob[]>([]);
  const [sourcePlatform, setSourcePlatform] = useState<SourcePlatform>("Facebook");
  const [pageForm, setPageForm] = useState({ pageId: routePageId, accessToken: "" });
  const [scheduleForm, setScheduleForm] = useState(() => ({ dailyPostCount: String(DEFAULT_DAILY_POST_COUNT), startAt: getDefaultStartAt() }));
  const [dailyTimes, setDailyTimes] = useState(() => getDefaultDailyTimes(DEFAULT_DAILY_POST_COUNT, getDefaultStartAt()));
  const [includeQueued, setIncludeQueued] = useState(false);
  const [message, setMessage] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isSavingPage, setIsSavingPage] = useState(false);
  const [deletingJobId, setDeletingJobId] = useState<number | null>(null);
  const [isDeletingAllVideos, setIsDeletingAllVideos] = useState(false);
  const [pausingJobId, setPausingJobId] = useState<number | null>(null);
  const [startingJobId, setStartingJobId] = useState<number | null>(null);
  const [storySavingJobId, setStorySavingJobId] = useState<number | null>(null);
  const [isSavingAllStories, setIsSavingAllStories] = useState(false);
  const [jobTimeEdits, setJobTimeEdits] = useState<Record<number, string>>({});
  const [publishForm, setPublishForm] = useState({
    graphApiVersion: "v24.0"
  });
  const hasLoadedStoredUser = useRef(false);
  const appliedRouteUserId = useRef("");
  const appliedStoredUserId = useRef(false);
  const previousSourcePlatform = useRef<SourcePlatform>("Facebook");
  const selectedPage = useMemo(
    () => pages.find((page) => page.pageId === routePageId) ?? null,
    [pages, routePageId]
  );

  const visibleJobs = useMemo(() => {
    const pageJobs = routePageId ? jobs.filter((job) => job.pageId === routePageId) : jobs;
    return [...pageJobs].sort((first, second) => compareSchedule(first, second));
  }, [jobs, routePageId]);
  const storyToggleableJobs = useMemo(
    () => visibleJobs.filter(canChangeStoryPublishing),
    [visibleJobs]
  );
  const areAllStoryToggleableJobsEnabled = storyToggleableJobs.length > 0
    && storyToggleableJobs.every((job) => Boolean(job.publishAsStory));
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
  const downloadedPostIds = useMemo(() => new Set(posts.map((post) => post.id)), [posts]);
  const publishedPostIds = useMemo(
    () => new Set(visibleJobs
      .filter((job) => job.facebookPostUrlId && job.status === "Published")
      .map((job) => job.facebookPostUrlId as number)),
    [visibleJobs]
  );
  const reschedulableQueuedJobs = useMemo(
    () => visibleJobs.filter((job) => job.status === "Queued"
      && job.facebookPostUrlId
      && downloadedPostIds.has(job.facebookPostUrlId)
      && !publishedPostIds.has(job.facebookPostUrlId)),
    [downloadedPostIds, publishedPostIds, visibleJobs]
  );
  const queueActionCount = queueablePosts.length + (includeQueued ? reschedulableQueuedJobs.length : 0);
  const schedulePreview = useMemo(
    () => dailyTimes.map((time) => formatTimeLabel(time)).join(", "),
    [dailyTimes]
  );

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
      setJobs(Array.isArray(jobsData) ? uniqueJobsById(jobsData) : []);
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
        setJobs(uniqueJobsById(data));
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
      if (document.visibilityState !== "visible") {
        return;
      }

      void loadJobs(userId);
    }, JOB_POLL_INTERVAL_MS);

    return () => window.clearInterval(timer);
  }, [loadJobs, userId]);

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
    void loadData(userId);
  }, [loadData, routeUserId, userId]);

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

  const getSchedulePayload = () => {
    const dailyPostCount = Number(scheduleForm.dailyPostCount);
    if (!Number.isInteger(dailyPostCount) || dailyPostCount < 1 || dailyPostCount > 48) {
      setMessage("Enter a daily post count between 1 and 48.");
      return null;
    }

    if (dailyTimes.length !== dailyPostCount || dailyTimes.some((time) => !isValidDailyTime(time))) {
      setMessage(`Set exactly ${dailyPostCount} valid daily publish time(s).`);
      return null;
    }

    const startAt = new Date(scheduleForm.startAt);
    if (Number.isNaN(startAt.getTime())) {
      setMessage("Select a valid schedule start date/time.");
      return null;
    }

    return {
      dailyPostCount,
      startAt: startAt.toISOString(),
      dailyTimes,
      timezoneOffsetMinutes: new Date().getTimezoneOffset()
    };
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage("");

    if (!userId.trim() || !routePageId) {
      setMessage("Enter a User ID and open this queue from a connected Facebook Page.");
      return;
    }

    const schedulePayload = getSchedulePayload();
    if (!schedulePayload) {
      return;
    }

    if (queueActionCount === 0) {
      setMessage(includeQueued
        ? `No new or queued ${sourcePlatform} videos are available for this schedule.`
        : `No new locally downloaded ${sourcePlatform} videos are available to queue. Enable reschedule to update existing queued jobs.`);
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
          ...schedulePayload,
          includeQueued,
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

      setJobs((previousJobs) => mergeJobsById(previousJobs, data.jobs || []));
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

  const handleDeleteJob = async (jobId: number) => {
    const targetJob = jobs.find((job) => job.id === jobId);
    const shouldDelete = window.confirm(`Permanently delete upload job #${jobId}, its database source record, and local video file? This cannot be undone.`);
    if (!shouldDelete) {
      return;
    }

    setMessage("");
    setDeletingJobId(jobId);

    try {
      const response = await fetch(`/api/smapi/FacebookReelUploads/${jobId}`, {
        method: "DELETE"
      });

      const data = await response.json();
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Delete failed with status ${response.status}.`);
        return;
      }

      setJobs((previousJobs) => previousJobs.filter((job) => {
        if (job.id === jobId) {
          return false;
        }

        return !targetJob?.facebookPostUrlId || job.facebookPostUrlId !== targetJob.facebookPostUrlId;
      }));
      if (targetJob?.facebookPostUrlId) {
        setPosts((previousPosts) => previousPosts.filter((post) => post.id !== targetJob.facebookPostUrlId));
      }
      setMessage(data.message || `Deleted upload job #${jobId}.`);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setDeletingJobId(null);
    }
  };

  const handleDeleteAllVideos = async () => {
    if (!userId.trim() || !routePageId) {
      setMessage("Enter a User ID and open this queue from a connected Facebook Page.");
      return;
    }

    const shouldDelete = window.confirm(`Permanently delete ALL ${sourcePlatform} videos for this page, including database records, local video files, and queued jobs? This cannot be undone.`);
    if (!shouldDelete) {
      return;
    }

    setMessage("");
    setIsDeletingAllVideos(true);

    try {
      const response = await fetch(
        `/api/smapi/FacebookReelUploads/page/${encodeURIComponent(userId.trim())}/${encodeURIComponent(routePageId)}?platform=${encodeURIComponent(sourcePlatform)}`,
        { method: "DELETE" }
      );

      const data = await response.json().catch(() => null);
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Delete all failed with status ${response.status}.`);
        return;
      }

      setPosts([]);
      setJobs((previousJobs) => previousJobs.filter((job) => job.pageId !== routePageId));
      await loadData(userId);
      setMessage(data.message || `Deleted all ${sourcePlatform} videos for this page.`);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setIsDeletingAllVideos(false);
    }
  };

  const handlePauseJob = async (jobId: number) => {
    setMessage("");
    setPausingJobId(jobId);

    try {
      const response = await fetch(`/api/smapi/FacebookReelUploads/${jobId}/pause`, {
        method: "POST"
      });

      const data = await response.json();
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Pause failed with status ${response.status}.`);
        return;
      }

      if (data.job) {
        setJobs((previousJobs) => previousJobs.map((job) => job.id === jobId ? data.job : job));
        setJobTimeEdits((previous) => ({
          ...previous,
          [jobId]: toDateTimeLocalValue(data.job.scheduledFor)
        }));
      }

      setMessage(data.message || `Paused upload job #${jobId}.`);
      await loadJobs(userId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setPausingJobId(null);
    }
  };

  const handleStartJob = async (jobId: number, fallbackScheduledFor?: string) => {
    setMessage("");

    const scheduledForValue = jobTimeEdits[jobId] || toDateTimeLocalValue(fallbackScheduledFor);
    const scheduledFor = new Date(scheduledForValue);
    if (Number.isNaN(scheduledFor.getTime())) {
      setMessage("Select a valid publish time before starting this job.");
      return;
    }

    setStartingJobId(jobId);

    try {
      const response = await fetch(`/api/smapi/FacebookReelUploads/${jobId}/resume`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          scheduledFor: scheduledFor.toISOString(),
          graphApiVersion: publishForm.graphApiVersion.trim() || "v24.0"
        })
      });

      const data = await response.json();
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Start failed with status ${response.status}.`);
        return;
      }

      if (data.job) {
        setJobs((previousJobs) => previousJobs.map((job) => job.id === jobId ? data.job : job));
      }

      setMessage(data.message || `Started upload job #${jobId}.`);
      await loadJobs(userId);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setStartingJobId(null);
    }
  };

  const handleStoryToggleJob = async (job: UploadJob, publishAsStory: boolean) => {
    if (!canChangeStoryPublishing(job)) {
      setMessage("Story publishing can only be changed before the job is published.");
      return;
    }

    setMessage("");
    setStorySavingJobId(job.id);

    try {
      const response = await fetch(`/api/smapi/FacebookReelUploads/${job.id}/story`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ publishAsStory })
      });

      const data = await response.json();
      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Story update failed with status ${response.status}.`);
        return;
      }

      if (data.job) {
        setJobs((previousJobs) => previousJobs.map((item) => item.id === job.id ? data.job : item));
      }

      setMessage(data.message || `Story publishing ${publishAsStory ? "enabled" : "disabled"} for job #${job.id}.`);
    } catch {
      setMessage("Could not connect to the backend server.");
    } finally {
      setStorySavingJobId(null);
    }
  };

  const handleStoryToggleAll = async () => {
    if (storyToggleableJobs.length === 0) {
      setMessage("No queued or paused jobs can be changed for Story publishing.");
      return;
    }

    const publishAsStory = !areAllStoryToggleableJobsEnabled;
    const jobsToUpdate = storyToggleableJobs.filter((job) => Boolean(job.publishAsStory) !== publishAsStory);

    if (jobsToUpdate.length === 0) {
      setMessage(`Story publishing is already ${publishAsStory ? "enabled" : "disabled"} for every queued job.`);
      return;
    }

    setMessage("");
    setIsSavingAllStories(true);

    const updatedJobs: UploadJob[] = [];

    try {
      for (const job of jobsToUpdate) {
        setStorySavingJobId(job.id);

        const response = await fetch(`/api/smapi/FacebookReelUploads/${job.id}/story`, {
          method: "PATCH",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ publishAsStory })
        });

        const data = await response.json();
        if (!response.ok || !data?.success) {
          throw new Error(data?.message || `Story update failed for job #${job.id} with status ${response.status}.`);
        }

        if (data.job) {
          updatedJobs.push(data.job);
        }
      }

      if (updatedJobs.length > 0) {
        const updatedJobsById = new Map(updatedJobs.map((job) => [job.id, job]));
        setJobs((previousJobs) => previousJobs.map((job) => updatedJobsById.get(job.id) ?? job));
      }

      setMessage(
        `Story publishing ${publishAsStory ? "enabled" : "disabled"} for ${jobsToUpdate.length} queued job${jobsToUpdate.length === 1 ? "" : "s"}.`
      );
    } catch (error) {
      if (updatedJobs.length > 0) {
        const updatedJobsById = new Map(updatedJobs.map((job) => [job.id, job]));
        setJobs((previousJobs) => previousJobs.map((job) => updatedJobsById.get(job.id) ?? job));
      }

      setMessage(error instanceof Error ? error.message : "Could not update Story publishing for all queued jobs.");
    } finally {
      setIsSavingAllStories(false);
      setStorySavingJobId(null);
    }
  };

  const handleDailyPostCountChange = (value: string) => {
    setScheduleForm((current) => ({ ...current, dailyPostCount: value }));

    const nextCount = Number(value);
    if (Number.isInteger(nextCount) && nextCount >= 1 && nextCount <= 48) {
      setDailyTimes((currentTimes) => resizeDailyTimes(currentTimes, nextCount, scheduleForm.startAt));
    }
  };

  const handleDailyTimeChange = (index: number, value: string) => {
    setDailyTimes((currentTimes) => currentTimes.map((time, currentIndex) => currentIndex === index ? value : time));
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

          <div className="grid grid-cols-1 md:grid-cols-4 gap-3 pt-2">
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Downloaded {sourcePlatform}</p>
              <p className="mt-1 text-2xl font-bold text-white">{posts.length}</p>
            </div>
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Ready To Queue</p>
              <p className="mt-1 text-2xl font-bold text-blue-300">{queueablePosts.length}</p>
            </div>
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Queued To Reschedule</p>
              <p className="mt-1 text-2xl font-bold text-amber-300">{reschedulableQueuedJobs.length}</p>
            </div>
            <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <p className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Daily Slots</p>
              <p className="mt-1 text-2xl font-bold text-emerald-300">{dailyTimes.length}</p>
            </div>
          </div>
        </section>

        <section className="glass-panel p-6 rounded-xl border border-white/5 space-y-5">
          <div>
            <h2 className="text-xl font-bold text-white">Daily Schedule</h2>
            <p className="text-neutral-500 text-sm mt-1">Queue only downloaded {sourcePlatform} videos at the selected daily publish times.</p>
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
                onChange={(event) => handleDailyPostCountChange(event.target.value)}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
              />
              <span className="block text-xs text-neutral-500">This creates the same number of publish time slots below.</span>
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

          <div className="space-y-3">
            <div className="flex items-center justify-between gap-3">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400">Daily Publish Times</span>
              <span className="text-[10px] uppercase tracking-widest text-neutral-500">{dailyTimes.length} slots</span>
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
              {dailyTimes.map((time, index) => (
                <label key={`${index}-${dailyTimes.length}`} className="space-y-1">
                  <span className="text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Time {index + 1}</span>
                  <input
                    required
                    type="time"
                    value={time}
                    onChange={(event) => handleDailyTimeChange(index, event.target.value)}
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-3 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
                  />
                </label>
              ))}
            </div>
            {schedulePreview && (
              <p className="text-xs text-neutral-500">Schedule slots: {schedulePreview}</p>
            )}
          </div>

          <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-4 text-sm text-neutral-300">
            This page uses the already downloaded video files from the scraper page and publishes them with their saved captions. It does not download videos again.
          </div>

          <label className="flex items-start gap-3 rounded-lg border border-white/5 bg-black/30 px-4 py-4 text-sm text-neutral-300">
            <input
              type="checkbox"
              checked={includeQueued}
              onChange={(event) => setIncludeQueued(event.target.checked)}
              className="mt-1 h-4 w-4 accent-blue-500"
            />
            <span>
              <span className="block font-bold text-white">Reschedule already queued jobs into this schedule</span>
              <span className="block text-xs text-neutral-500">Existing queued jobs will be rescheduled from Start At using the selected daily publish times.</span>
            </span>
          </label>

          <button
            type="submit"
            disabled={isSubmitting || queueActionCount === 0}
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
                {includeQueued ? "Reschedule" : "Queue"} {queueActionCount} {sourcePlatform} Video{queueActionCount === 1 ? "" : "s"}
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
        <div className="flex flex-col gap-3 mb-4 md:flex-row md:items-center md:justify-between">
          <div>
            <h2 className="text-xl font-bold text-white">Queued Videos</h2>
            <p className="text-neutral-500 text-sm mt-1">Scheduled publish time, status, captions, and local downloaded {sourcePlatform} source details for this Facebook Page.</p>
          </div>
          <div className="flex items-center gap-3">
            <span className="text-[10px] uppercase tracking-widest text-neutral-500">{visibleJobs.length} jobs</span>
            <button
              type="button"
              onClick={handleDeleteAllVideos}
              disabled={isDeletingAllVideos || (posts.length === 0 && visibleJobs.length === 0)}
              className="inline-flex items-center justify-center gap-1.5 rounded-lg border border-red-500/20 bg-red-500/10 px-3 py-2 text-[10px] font-bold uppercase tracking-widest text-red-200 hover:bg-red-500/20 disabled:opacity-50"
            >
              <span className="material-symbols-outlined text-[13px]">delete_sweep</span>
              {isDeletingAllVideos ? "Deleting" : `Delete All ${sourcePlatform}`}
            </button>
          </div>
        </div>

        <p className="mb-2 flex items-center gap-1 text-[10px] font-bold uppercase tracking-widest text-neutral-500 md:justify-end">
          <span className="material-symbols-outlined text-[13px]">swipe</span>
          Scroll horizontally to view every column
        </p>
        <div className="rounded-lg border border-white/5 overflow-hidden">
          <div className="overflow-x-auto pb-2 [scrollbar-color:rgba(96,165,250,0.45)_rgba(255,255,255,0.06)] [scrollbar-width:thin]">
            <div className="md:min-w-[1480px]">
          <div className="hidden md:grid grid-cols-[104px_130px_136px_150px_140px_140px_minmax(260px,1fr)_280px] gap-4 bg-black/40 px-4 py-3 text-[10px] uppercase tracking-widest font-bold text-neutral-500">
            <span>Job</span>
            <span>Status</span>
            <div className="flex flex-col items-start gap-1">
              <span>Story</span>
              <button
                type="button"
                role="switch"
                aria-checked={areAllStoryToggleableJobsEnabled}
                aria-label={`${areAllStoryToggleableJobsEnabled ? "Disable" : "Enable"} Story publishing for all queued jobs`}
                onClick={() => void handleStoryToggleAll()}
                disabled={storyToggleableJobs.length === 0 || isSavingAllStories}
                className={`inline-flex min-w-[82px] items-center justify-between gap-2 rounded-full border px-2 py-1 text-[10px] font-bold uppercase tracking-widest transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${areAllStoryToggleableJobsEnabled ? "border-emerald-400/35 bg-emerald-500/15 text-emerald-200" : "border-white/10 bg-white/5 text-neutral-500"}`}
                title={storyToggleableJobs.length === 0 ? "No queued jobs can be changed" : `${areAllStoryToggleableJobsEnabled ? "Disable" : "Enable"} Story publishing for all queued jobs`}
              >
                <span className={`relative h-4 w-7 rounded-full transition-colors ${areAllStoryToggleableJobsEnabled ? "bg-emerald-500/80" : "bg-neutral-700"}`}>
                  <span className={`absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-white shadow transition-transform ${areAllStoryToggleableJobsEnabled ? "translate-x-3.5" : "translate-x-0"}`} />
                </span>
                <span>{isSavingAllStories ? "Saving" : areAllStoryToggleableJobsEnabled ? "On" : "Off"}</span>
              </button>
            </div>
            <span>Scheduled</span>
            <span>Queued</span>
            <span>Completed</span>
            <span>Caption</span>
            <span>Source Link</span>
          </div>
          {visibleJobs.length > 0 ? visibleJobs.map((job) => (
            <div key={job.id} className="grid grid-cols-1 md:grid-cols-[104px_130px_136px_150px_140px_140px_minmax(260px,1fr)_280px] gap-3 md:gap-4 bg-black/20 px-4 py-3 border-t border-white/5">
              <div className="flex flex-col gap-1">
                <span className="text-sm font-bold text-white">#{job.id}</span>
                {job.status === "Queued" && (
                  <button
                    type="button"
                    onClick={() => handlePauseJob(job.id)}
                    disabled={pausingJobId === job.id}
                    className="text-[10px] text-amber-300 hover:text-amber-200 font-bold flex items-center gap-0.5 disabled:opacity-50"
                  >
                    <span className="material-symbols-outlined text-[12px]">pause_circle</span>
                    {pausingJobId === job.id ? "Pausing" : "Pause"}
                  </button>
                )}
                {job.status === "Paused" && (
                  <>
                    <input
                      type="datetime-local"
                      value={jobTimeEdits[job.id] ?? toDateTimeLocalValue(job.scheduledFor)}
                      onChange={(event) => setJobTimeEdits((previous) => ({ ...previous, [job.id]: event.target.value }))}
                      className="mt-1 w-full rounded border border-white/10 bg-black/50 px-2 py-1 text-[10px] text-white focus:border-blue-500 focus:outline-none"
                    />
                    <button
                      type="button"
                      onClick={() => handleStartJob(job.id, job.scheduledFor)}
                      disabled={startingJobId === job.id}
                      className="text-[10px] text-emerald-300 hover:text-emerald-200 font-bold flex items-center gap-0.5 disabled:opacity-50"
                    >
                      <span className="material-symbols-outlined text-[12px]">play_circle</span>
                      {startingJobId === job.id ? "Starting" : "Start"}
                    </button>
                  </>
                )}
                {job.status !== "Published" && job.status !== "Paused" && (
                  <button
                    onClick={() => handleRetryJob(job.id)}
                    disabled={isSubmitting}
                    className="text-[10px] text-blue-400 hover:text-blue-300 font-bold flex items-center gap-0.5"
                  >
                    <span className="material-symbols-outlined text-[12px]">replay</span>
                    Retry
                  </button>
                )}
                <button
                  type="button"
                  onClick={() => handleDeleteJob(job.id)}
                  disabled={deletingJobId === job.id}
                  className="text-[10px] text-red-300 hover:text-red-200 font-bold flex items-center gap-0.5 disabled:opacity-50"
                >
                  <span className="material-symbols-outlined text-[12px]">delete</span>
                  {deletingJobId === job.id ? "Deleting" : "Delete"}
                </button>
              </div>
              <span className={`inline-flex w-fit self-start items-center rounded px-2 py-1 text-[10px] font-bold uppercase tracking-widest ${statusClass(job.status)}`}>
                {job.status}
              </span>
              <div className="flex flex-col items-start gap-1">
                <button
                  type="button"
                  role="switch"
                  aria-checked={Boolean(job.publishAsStory)}
                  aria-label={`Publish job #${job.id} as Facebook Story`}
                  onClick={() => void handleStoryToggleJob(job, !job.publishAsStory)}
                  disabled={!canChangeStoryPublishing(job) || storySavingJobId === job.id || isSavingAllStories}
                  className={`inline-flex min-w-[82px] items-center justify-between gap-2 rounded-full border px-2 py-1 text-[10px] font-bold uppercase tracking-widest transition-colors disabled:cursor-not-allowed disabled:opacity-50 ${job.publishAsStory ? "border-emerald-400/35 bg-emerald-500/15 text-emerald-200" : "border-white/10 bg-white/5 text-neutral-500"}`}
                >
                  <span className={`relative h-4 w-7 rounded-full transition-colors ${job.publishAsStory ? "bg-emerald-500/80" : "bg-neutral-700"}`}>
                    <span className={`absolute left-0.5 top-0.5 h-3 w-3 rounded-full bg-white shadow transition-transform ${job.publishAsStory ? "translate-x-3.5" : "translate-x-0"}`} />
                  </span>
                  <span>{storySavingJobId === job.id ? "Saving" : job.publishAsStory ? "On" : "Off"}</span>
                </button>
                {job.storyPublishedAt && (
                  <span className="text-[10px] text-emerald-300">Story published</span>
                )}
                {job.storyErrorMessage && (
                  <span className="line-clamp-2 text-[10px] text-amber-300" title={job.storyErrorMessage}>
                    Story failed
                  </span>
                )}
              </div>
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
                {job.errorMessage && (
                  <p className="line-clamp-2 text-xs text-red-300" title={job.errorMessage}>
                    {job.errorMessage}
                  </p>
                )}
              </div>
              <div className="min-w-0 text-xs text-neutral-500">
                <a
                  href={job.videoSourceUrl}
                  target="_blank"
                  rel="noreferrer"
                  aria-label={`Open ${sourcePlatform} source video for job #${job.id}`}
                  className="inline-flex max-w-full items-center gap-1.5 rounded-md border border-blue-400/20 bg-blue-500/10 px-2.5 py-1.5 font-bold text-blue-300 transition-colors hover:border-blue-300/40 hover:bg-blue-500/20 hover:text-blue-100"
                >
                  <span className="material-symbols-outlined text-[14px]">open_in_new</span>
                  <span className="truncate">Open {sourcePlatform} video</span>
                </a>
                <p className="mt-1 truncate text-neutral-400" title={job.videoSourceUrl}>{job.videoSourceUrl}</p>
                <p className="truncate text-neutral-600" title={job.s3Key}>{job.s3Key || "Pending local download"}</p>
              </div>
            </div>
          )) : (
            <div className="px-4 py-10 text-center text-sm text-neutral-500">No jobs yet.</div>
          )}
            </div>
          </div>
        </div>
      </section>
    </div>
  );
}

function compareSchedule(first: UploadJob, second: UploadJob) {
  const firstTime = getSortTime(first.scheduledFor) ?? getSortTime(first.createdAt) ?? 0;
  const secondTime = getSortTime(second.scheduledFor) ?? getSortTime(second.createdAt) ?? 0;
  return firstTime - secondTime;
}

function mergeJobsById(existingJobs: UploadJob[], incomingJobs: UploadJob[]) {
  const jobsById = new Map<number, UploadJob>();
  for (const job of existingJobs) {
    jobsById.set(job.id, job);
  }

  for (const job of incomingJobs) {
    jobsById.set(job.id, job);
  }

  return Array.from(jobsById.values());
}

function uniqueJobsById(jobs: UploadJob[]) {
  return mergeJobsById([], jobs);
}

function canChangeStoryPublishing(job: UploadJob) {
  return job.status === "Queued" || job.status === "Paused";
}

function getSortTime(value?: string) {
  if (!value) {
    return null;
  }

  const timestamp = new Date(value).getTime();
  return Number.isNaN(timestamp) ? null : timestamp;
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
    case "Paused":
      return "bg-amber-500/10 text-amber-300 border border-amber-500/20";
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

function toDateTimeLocalValue(value?: string) {
  const date = value ? new Date(value) : new Date();
  if (Number.isNaN(date.getTime())) {
    return getDefaultStartAt();
  }

  date.setMinutes(date.getMinutes() - date.getTimezoneOffset());
  return date.toISOString().slice(0, 16);
}

function resizeDailyTimes(currentTimes: string[], nextCount: number, startAt: string) {
  const fallbackTimes = getDefaultDailyTimes(nextCount, startAt);

  return Array.from({ length: nextCount }, (_, index) => currentTimes[index] || fallbackTimes[index] || "00:00");
}

function getDefaultDailyTimes(count: number, startAt: string) {
  const safeCount = Math.max(1, Math.min(48, count));
  const startMinutes = getStartMinutes(startAt);
  const intervalMinutes = Math.floor((24 * 60) / safeCount);
  const times = Array.from({ length: safeCount }, (_, index) => {
    const minutes = (startMinutes + intervalMinutes * index) % (24 * 60);
    return minutesToTimeValue(minutes);
  });

  return [...times].sort();
}

function getStartMinutes(startAt: string) {
  const date = new Date(startAt);
  if (Number.isNaN(date.getTime())) {
    return 0;
  }

  return date.getHours() * 60 + date.getMinutes();
}

function minutesToTimeValue(minutes: number) {
  const hours = Math.floor(minutes / 60);
  const minutePart = minutes % 60;
  return `${String(hours).padStart(2, "0")}:${String(minutePart).padStart(2, "0")}`;
}

function isValidDailyTime(value: string) {
  return /^([01]\d|2[0-3]):[0-5]\d$/.test(value);
}

function formatTimeLabel(value: string) {
  if (!isValidDailyTime(value)) {
    return value;
  }

  const [hoursText, minutesText] = value.split(":");
  const date = new Date();
  date.setHours(Number(hoursText), Number(minutesText), 0, 0);
  return date.toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
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
