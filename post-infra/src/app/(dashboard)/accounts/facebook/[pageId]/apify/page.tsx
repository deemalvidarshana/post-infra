"use client";

import Link from 'next/link';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';

interface FacebookPageApi {
  id?: number;
  userId?: string;
  pageId: string;
  pageName: string;
  category?: string;
  accessToken: string;
  avatarUrl?: string;
}

interface ScrapedPost {
  id: number;
  platform?: 'Facebook' | 'TikTok' | string;
  permalinkUrl: string;
  postId?: string;
  pageId?: string;
  sourcePageUrl?: string;
  videoUrl?: string;
  postCreatedAt?: string;
  caption?: string;
  authorName?: string;
  likeCount?: number;
  shareCount?: number;
  playCount?: number;
  commentCount?: number;
  durationSeconds?: number;
  musicName?: string;
  musicAuthor?: string;
  s3UploadStatus: string;
  s3Bucket?: string;
  s3Region?: string;
  s3Key?: string;
  s3UploadedAt?: string;
  s3UploadError?: string;
  scrapedAt: string;
}

interface ScrapeResponse {
  success: boolean;
  scrapedCount: number;
  savedCount: number;
  updatedCount: number;
  skippedCount: number;
  posts: ScrapedPost[];
  message?: string;
}

type ScrapePlatform = 'facebook' | 'tiktok';

export default function FacebookApifyPage() {
  const params = useParams<{ pageId: string }>();
  const router = useRouter();
  const searchParams = useSearchParams();
  const pageId = useMemo(() => safeDecode(params.pageId), [params.pageId]);
  const routeUserId = searchParams.get('userId') || '';
  const [userId, setUserId] = useState('');
  const [page, setPage] = useState<FacebookPageApi | null>(null);
  const [scrapedPosts, setScrapedPosts] = useState<ScrapedPost[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [isScraping, setIsScraping] = useState(false);
  const [isUploadingToS3, setIsUploadingToS3] = useState(false);
  const [isStoppingDownloads, setIsStoppingDownloads] = useState(false);
  const [uploadingPostIds, setUploadingPostIds] = useState<number[]>([]);
  const [stoppingPostIds, setStoppingPostIds] = useState<number[]>([]);
  const [viewingPostId, setViewingPostId] = useState<number | null>(null);
  const [openActionPostId, setOpenActionPostId] = useState<number | null>(null);
  const [scrapeMessage, setScrapeMessage] = useState('');
  const [scrapeForm, setScrapeForm] = useState({
    platform: 'facebook' as ScrapePlatform,
    urls: '',
    newerThan: '',
    resultsLimit: ''
  });
  const hasLoadedStoredUser = useRef(false);
  const appliedRouteUserId = useRef('');
  const appliedStoredUserId = useRef(false);

  const visibleLogPosts = useMemo(() => scrapedPosts, [scrapedPosts]);
  const uploadableLogPosts = useMemo(
    () => visibleLogPosts.filter((post) => canQueueS3Upload(post.s3UploadStatus)),
    [visibleLogPosts]
  );
  const stoppableLogPosts = useMemo(
    () => visibleLogPosts.filter((post) => canStopLocalDownload(post.s3UploadStatus)),
    [visibleLogPosts]
  );
  const uploadingPostIdSet = useMemo(() => new Set(uploadingPostIds), [uploadingPostIds]);
  const stoppingPostIdSet = useMemo(() => new Set(stoppingPostIds), [stoppingPostIds]);
  const selectedPlatformLabel = platformLabel(scrapeForm.platform);
  const firstScrapeUrl = scrapeForm.urls.split(/\r?\n/).map((url) => url.trim()).find(Boolean) || '#';

  const fetchPageData = useCallback(async (nextUserId = userId) => {
    if (!nextUserId.trim()) {
      setScrapeMessage('Enter a User ID before loading page data.');
      return;
    }

    setIsLoading(true);
    let effectiveUserId = nextUserId.trim();
    let effectivePageId = pageId;
    window.localStorage.setItem('smapi_user_id', effectiveUserId);

    try {
      const pagesResponse = await fetch(`/api/smapi/Pages/facebook/${encodeURIComponent(effectiveUserId)}`);
      const pagesData = await pagesResponse.json();
      let currentPage: FacebookPageApi | null = null;
      if (Array.isArray(pagesData)) {
        const pages = pagesData as FacebookPageApi[];
        currentPage = pages.find((item) => item.pageId === pageId) ?? null;
        if (!currentPage && pages.length === 1) {
          currentPage = pages[0];
          effectivePageId = currentPage.pageId;
          router.replace(`/accounts/facebook/${encodeURIComponent(effectivePageId)}/apify`);
        }
      }

      if (!currentPage) {
        const pageResponse = await fetch(`/api/smapi/Pages/facebook/by-page/${encodeURIComponent(pageId)}`);
        if (pageResponse.ok) {
          const pageData = await pageResponse.json();
          if (pageData?.pageId === pageId) {
            currentPage = pageData as FacebookPageApi;
            effectivePageId = currentPage.pageId;
            if (currentPage.userId && currentPage.userId !== effectiveUserId) {
              effectiveUserId = currentPage.userId;
              setUserId(effectiveUserId);
              window.localStorage.setItem('smapi_user_id', effectiveUserId);
            }
          }
        }
      }

      setPage(currentPage);
      const postsResponse = await fetch(`/api/smapi/Pages/facebook/posts/${encodeURIComponent(effectiveUserId)}?pageId=${encodeURIComponent(effectivePageId)}`);
      const postsData = await postsResponse.json();
      if (Array.isArray(postsData)) {
        setScrapedPosts((postsData as ScrapedPost[]).filter((post) => post.pageId === effectivePageId));
      }
    } catch (err) {
      console.error('Failed to fetch Facebook Apify page data', err);
      setScrapeMessage('Could not load page data from the backend.');
    } finally {
      setIsLoading(false);
    }
  }, [pageId, router, userId]);

  useEffect(() => {
    if (routeUserId && appliedRouteUserId.current !== routeUserId) {
      appliedRouteUserId.current = routeUserId;
      window.setTimeout(() => setUserId(routeUserId), 0);
      window.localStorage.setItem('smapi_user_id', routeUserId);
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
    void fetchPageData(userId);
  }, [fetchPageData, routeUserId, userId]);

  useEffect(() => {
    if (!userId.trim() || !visibleLogPosts.some((post) => post.s3UploadStatus === 'Queued' || post.s3UploadStatus === 'Downloading' || post.s3UploadStatus === 'Uploading')) {
      return;
    }

    const timer = window.setInterval(() => {
      void fetchPageData(userId);
    }, 5000);

    return () => window.clearInterval(timer);
  }, [fetchPageData, userId, visibleLogPosts]);

  const handleScrapePosts = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsScraping(true);
    setScrapeMessage('');

    const inputUrls = scrapeForm.urls
      .split(/\r?\n/)
      .map((url) => url.trim())
      .filter(Boolean);

    if (inputUrls.length === 0) {
      setScrapeMessage(`Please enter at least one ${platformLabel(scrapeForm.platform)} URL.`);
      setIsScraping(false);
      return;
    }

    if (!userId.trim()) {
      setScrapeMessage('Please enter a User ID.');
      setIsScraping(false);
      return;
    }

    if (!scrapeForm.resultsLimit || Number(scrapeForm.resultsLimit) < 1) {
      setScrapeMessage('Please enter a results limit greater than zero.');
      setIsScraping(false);
      return;
    }

    window.localStorage.setItem('smapi_user_id', userId.trim());

    try {
      const response = await fetch(scrapeForm.platform === 'facebook'
        ? '/api/smapi/Pages/facebook/scrape'
        : '/api/smapi/Pages/tiktok/scrape', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(scrapeForm.platform === 'facebook'
          ? {
              onlyPostsNewerThan: scrapeForm.newerThan || null,
              resultsLimit: Number(scrapeForm.resultsLimit),
              startUrls: inputUrls.map((url) => ({ url })),
              userId: userId.trim(),
              pageId,
            }
          : {
              newestPostDate: todayDateInputValue(),
              oldestPostDateUnified: scrapeForm.newerThan || null,
              resultsPerPage: Number(scrapeForm.resultsLimit),
              profiles: inputUrls,
              userId: userId.trim(),
              pageId,
            }),
      });

      const responseText = await response.text();
      let data: ScrapeResponse | null = null;

      try {
        data = JSON.parse(responseText) as ScrapeResponse;
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success) {
        setScrapeMessage(data?.message || `Backend request failed with status ${response.status}.`);
        return;
      }

      setScrapeMessage(`Scraped ${data.scrapedCount} ${scrapeForm.platform === 'tiktok' ? 'TikTok posts' : 'reels'}. Saved ${data.savedCount}, updated ${data.updatedCount}, skipped ${data.skippedCount}.`);
      setScrapedPosts((previousPosts) => {
        const merged = [...data.posts.filter((post) => post.pageId === pageId), ...previousPosts];
        const seen = new Set<string>();
        return merged.filter((post) => {
          const postKey = `${platformValue(post)}:${post.permalinkUrl}`;
          if (seen.has(postKey)) {
            return false;
          }
          seen.add(postKey);
          return true;
        });
      });
    } catch {
      setScrapeMessage('Could not connect to the backend server.');
    } finally {
      setIsScraping(false);
    }
  };

  const queuePostsToS3 = async (postsToQueue: ScrapedPost[], isSinglePost = false) => {
    setScrapeMessage('');

    if (!userId.trim()) {
      setScrapeMessage('Please enter a User ID.');
      return;
    }

    const uploadablePosts = postsToQueue.filter((post) => canQueueS3Upload(post.s3UploadStatus));
    if (uploadablePosts.length === 0) {
      setScrapeMessage('No new or failed videos are available for local download.');
      return;
    }

    const postIds = uploadablePosts.map((post) => post.id);
    if (isSinglePost) {
      setUploadingPostIds((currentPostIds) => Array.from(new Set([...currentPostIds, ...postIds])));
    } else {
      setIsUploadingToS3(true);
    }

    window.localStorage.setItem('smapi_user_id', userId.trim());

    try {
      const response = await fetch('/api/smapi/FacebookS3Uploads/facebook/reels', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId,
          postIds
        })
      });

      const responseText = await response.text();
      let data: { success?: boolean; message?: string; queuedCount?: number; skippedCount?: number } | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success) {
        setScrapeMessage(data?.message || `Local download request failed with status ${response.status}.`);
        return;
      }

      setScrapeMessage(data.message || `Queued ${data.queuedCount ?? 0} video(s) for local download.`);
      setScrapedPosts((posts) => posts.map((post) => {
        if (!postIds.includes(post.id)) {
          return post;
        }

        return { ...post, s3UploadStatus: 'Queued', s3UploadError: undefined };
      }));
      await fetchPageData(userId);
    } catch {
      setScrapeMessage('Could not connect to the backend server.');
    } finally {
      if (isSinglePost) {
        setUploadingPostIds((currentPostIds) => currentPostIds.filter((postId) => !postIds.includes(postId)));
      } else {
        setIsUploadingToS3(false);
      }
    }
  };

  const handleUploadVisibleVideosToS3 = () => queuePostsToS3(uploadableLogPosts);

  const handleUploadSingleVideoToS3 = (post: ScrapedPost) => queuePostsToS3([post], true);

  const stopDownloads = async (postsToStop: ScrapedPost[], isSinglePost = false) => {
    setScrapeMessage('');

    if (!userId.trim()) {
      setScrapeMessage('Please enter a User ID.');
      return;
    }

    const stoppablePosts = postsToStop.filter((post) => canStopLocalDownload(post.s3UploadStatus));
    if (stoppablePosts.length === 0) {
      setScrapeMessage('No queued or active local downloads are available to stop.');
      return;
    }

    const postIds = stoppablePosts.map((post) => post.id);
    if (isSinglePost) {
      setStoppingPostIds((currentPostIds) => Array.from(new Set([...currentPostIds, ...postIds])));
    } else {
      setIsStoppingDownloads(true);
    }

    try {
      const response = await fetch('/api/smapi/FacebookS3Uploads/facebook/reels/stop', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userId: userId.trim(),
          pageId,
          postIds
        })
      });

      const responseText = await response.text();
      let data: { success?: boolean; message?: string; stoppedCount?: number } | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success) {
        setScrapeMessage(data?.message || `Stop download request failed with status ${response.status}.`);
        return;
      }

      setScrapeMessage(data.message || `Stopped ${data.stoppedCount ?? 0} local download(s).`);
      setScrapedPosts((posts) => posts.map((post) => {
        if (!postIds.includes(post.id)) {
          return post;
        }

        return { ...post, s3UploadStatus: 'Cancelled', s3UploadError: 'Download stopped by user.' };
      }));
      await fetchPageData(userId);
    } catch {
      setScrapeMessage('Could not connect to the backend server.');
    } finally {
      if (isSinglePost) {
        setStoppingPostIds((currentPostIds) => currentPostIds.filter((postId) => !postIds.includes(postId)));
      } else {
        setIsStoppingDownloads(false);
      }
    }
  };

  const handleStopVisibleDownloads = () => stopDownloads(stoppableLogPosts);

  const handleStopSingleDownload = (post: ScrapedPost) => stopDownloads([post], true);

  const handleDeletePost = async (post: ScrapedPost) => {
    console.log('Attempting to delete post:', post);
    if (!post.id) {
      console.error('Post ID is missing!', post);
      setScrapeMessage('Error: Post ID is missing. Cannot delete from database.');
      return;
    }

    setScrapeMessage('');
    try {
      const response = await fetch(
        `/api/smapi/Pages/facebook/posts/${post.id}?userId=${encodeURIComponent(userId.trim())}&pageId=${encodeURIComponent(pageId)}`,
        { method: 'DELETE' }
      );

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        console.error('Delete failed:', response.status, errorData);
        setScrapeMessage(errorData?.message || `Failed to delete post. Status: ${response.status}`);
        return;
      }

      console.log('Post deleted successfully from backend.');
      setScrapedPosts((previousPosts) => previousPosts.filter((p) => p.id !== post.id));
    } catch (err) {
      console.error('Delete post failed', err);
      setScrapeMessage('Could not connect to the backend server to delete the post.');
    }
  };

  const handleDeleteAllPosts = async () => {
    if (!window.confirm('Are you sure you want to delete ALL scraped reels for this page? This action cannot be undone.')) {
      return;
    }

    if (!userId.trim() || !pageId) {
      setScrapeMessage('User ID and Page ID are required to delete all posts.');
      return;
    }

    setScrapeMessage('');
    setIsLoading(true);

    try {
      const response = await fetch(`/api/smapi/Pages/facebook/posts/all/${encodeURIComponent(userId.trim())}/${encodeURIComponent(pageId)}`, {
        method: 'DELETE'
      });

      if (!response.ok) {
        const errorData = await response.json().catch(() => null);
        setScrapeMessage(errorData?.message || `Failed to delete all posts. Status: ${response.status}`);
        return;
      }

      setScrapedPosts([]);
      const data = await response.json().catch(() => null);
      setScrapeMessage(data?.message || 'All posts deleted successfully.');
    } catch (err) {
      console.error('Delete all posts failed', err);
      setScrapeMessage('Could not connect to the backend server to delete all posts.');
    } finally {
      setIsLoading(false);
    }
  };

  const handleViewUploadedVideo = async (post: ScrapedPost) => {
    setOpenActionPostId(null);
    setScrapeMessage('');

    if (!userId.trim()) {
      setScrapeMessage('Please enter a User ID.');
      return;
    }

    if (!isLocallyDownloaded(post.s3UploadStatus) || !post.s3Key) {
      setScrapeMessage('This reel has not been downloaded locally yet.');
      return;
    }

    const viewer = window.open('about:blank', '_blank');
    if (!viewer) {
      setScrapeMessage('Allow pop-ups for this site to open the local video.');
      return;
    }

    viewer.opener = null;
    viewer.document.write('<p style="font-family: sans-serif; padding: 24px;">Loading local video...</p>');
    setViewingPostId(post.id);

    try {
      const response = await fetch(
        `/api/smapi/FacebookS3Uploads/facebook/reels/${post.id}/url?userId=${encodeURIComponent(userId.trim())}&pageId=${encodeURIComponent(pageId)}&expiresMinutes=60`
      );
      const responseText = await response.text();
      let data: { success?: boolean; url?: string; message?: string } | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (!response.ok || !data?.success || !data.url) {
        viewer.close();
        setScrapeMessage(data?.message || `Could not open local video. Backend status ${response.status}.`);
        return;
      }

      viewer.location.href = data.url;
    } catch {
      viewer.close();
      setScrapeMessage('Could not connect to the backend server.');
    } finally {
      setViewingPostId(null);
    }
  };

  return (
    <div className="space-y-8 animate-in fade-in duration-700">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
        <div>
          <Link href="/accounts/facebook" className="inline-flex items-center gap-2 text-sm text-neutral-500 hover:text-white transition-colors mb-4">
            <span className="material-symbols-outlined text-sm">arrow_back</span>
            Facebook Pages
          </Link>
          <h1 className="text-3xl font-bold text-white tracking-tight">{page?.pageName || 'Facebook Page'} Reels</h1>
          <p className="text-neutral-500 text-sm mt-1">Page ID: {pageId}</p>
        </div>
        <div className="flex flex-col sm:flex-row gap-2">
          <input
            value={userId}
            onChange={(e) => setUserId(e.target.value)}
            className="w-full sm:w-56 bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none"
            placeholder="User ID"
          />
          <button
            type="button"
            onClick={() => fetchPageData()}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-sm">sync</span>
            {isLoading ? 'Loading' : 'Load'}
          </button>
        </div>
      </div>

      <section className="glass-panel p-6 rounded-xl border border-white/5">
        <div className="flex flex-col lg:flex-row lg:items-start lg:justify-between gap-4">
          <div className="space-y-1">
            <h2 className="text-xl font-bold text-white">{selectedPlatformLabel} Scraper</h2>
            <p className="text-neutral-500 text-sm">Scrape public Facebook reels or TikTok profile posts and save them into the same log table.</p>
          </div>
          <div className="flex flex-col sm:flex-row gap-2">
            <label className="sr-only" htmlFor="scrape-platform">Scrape platform</label>
            <select
              id="scrape-platform"
              value={scrapeForm.platform}
              onChange={(e) => setScrapeForm({ ...scrapeForm, platform: e.target.value as ScrapePlatform })}
              className="h-11 rounded-lg border border-white/10 bg-black/50 px-3 text-sm font-semibold text-white focus:border-blue-500 focus:outline-none"
            >
              <option value="facebook">Facebook</option>
              <option value="tiktok">TikTok</option>
            </select>
            <a
              href={firstScrapeUrl}
              target="_blank"
              rel="noreferrer"
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-white/5 bg-white/5 px-4 py-2 text-xs font-bold text-white hover:bg-white/10 transition-colors"
            >
              <span className="material-symbols-outlined text-sm">open_in_new</span>
              Open Page
            </a>
          </div>
        </div>

        <form onSubmit={handleScrapePosts} className="mt-6 grid grid-cols-1 xl:grid-cols-[1.4fr_0.8fr] gap-6">
          <div className="space-y-2">
            <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">{selectedPlatformLabel} URLs</label>
            <textarea
              required
              rows={7}
              placeholder={scrapeForm.platform === 'tiktok' ? 'https://www.tiktok.com/@yamaha.sri.lanka' : 'https://www.facebook.com/page-or-reel'}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all resize-none"
              value={scrapeForm.urls}
              onChange={(e) => setScrapeForm({ ...scrapeForm, urls: e.target.value })}
            />
          </div>

          <div className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-1 gap-4">
            <div className="space-y-2">
              <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Newer Than</label>
            <input
              type="date"
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                value={scrapeForm.newerThan}
                onChange={(e) => setScrapeForm({ ...scrapeForm, newerThan: e.target.value })}
              />
            </div>
            <div className="space-y-2">
              <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Results Limit</label>
              <input
                required
                min="1"
                max="1000"
                type="number"
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                value={scrapeForm.resultsLimit}
                onChange={(e) => setScrapeForm({ ...scrapeForm, resultsLimit: e.target.value })}
              />
            </div>
            <button
              type="submit"
              disabled={isScraping}
              className="w-full py-3 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
            >
              {isScraping ? (
                <>
                  <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                  Scraping...
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined text-sm">travel_explore</span>
                  {scrapeForm.platform === 'tiktok' ? 'Scrape TikTok' : 'Scrape Reels'}
                </>
              )}
            </button>
          </div>
        </form>

        {scrapeMessage && (
          <div className="mt-4 rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
            {scrapeMessage}
          </div>
        )}
      </section>

      <section id="logs" className="glass-panel p-6 rounded-xl border border-white/5 scroll-mt-24">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between mb-4">
          <div>
            <h2 className="text-xl font-bold text-white">View Logs</h2>
            <p className="text-neutral-500 text-sm mt-1">Saved Facebook and TikTok URLs from recent Apify scraper runs.</p>
          </div>
          <div className="flex flex-col sm:flex-row sm:items-center gap-3">
            <span className="text-[10px] uppercase tracking-widest text-neutral-500">{visibleLogPosts.length} stored / {uploadableLogPosts.length} ready / {stoppableLogPosts.length} running</span>
            <button
              type="button"
              onClick={handleStopVisibleDownloads}
              disabled={isStoppingDownloads || stoppableLogPosts.length === 0}
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-red-500/20 bg-red-500/10 px-4 py-2 text-xs font-bold text-red-200 hover:bg-red-500/20 disabled:opacity-50"
            >
              {isStoppingDownloads ? (
                <>
                  <div className="w-4 h-4 border-2 border-red-200/30 border-t-red-200 rounded-full animate-spin"></div>
                  Stopping...
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined text-sm">stop_circle</span>
                  Stop Downloads
                </>
              )}
            </button>
            <button
              type="button"
              onClick={handleUploadVisibleVideosToS3}
              disabled={isUploadingToS3 || uploadableLogPosts.length === 0}
              className="inline-flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-xs font-bold text-white hover:bg-blue-500 disabled:opacity-50 shadow-lg shadow-blue-600/20"
            >
              {isUploadingToS3 ? (
                <>
                  <div className="w-4 h-4 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                  Queueing...
                </>
              ) : (
                <>
                  <span className="material-symbols-outlined text-sm">download</span>
                  Download New/Failed
                </>
              )}
            </button>
            <button
              type="button"
              onClick={handleDeleteAllPosts}
              disabled={visibleLogPosts.length === 0}
              className="inline-flex items-center justify-center gap-2 rounded-lg border border-red-500/20 bg-red-500/10 px-4 py-2 text-xs font-bold text-red-200 hover:bg-red-500/20 disabled:opacity-50"
            >
              <span className="material-symbols-outlined text-sm">delete</span>
              Delete All
            </button>
          </div>
        </div>

        {visibleLogPosts.length > 0 ? (
          <div className="rounded-lg border border-white/5">
            <div className="hidden md:grid grid-cols-[minmax(240px,0.9fr)_minmax(320px,1.35fr)_180px_120px_44px] gap-4 bg-black/40 px-4 py-3 text-[10px] uppercase tracking-widest font-bold text-neutral-500">
              <span>Post URL</span>
              <span>Caption</span>
              <span>Local Download</span>
              <span>Date</span>
              <span className="sr-only">Actions</span>
            </div>
            {visibleLogPosts.slice(0, 30).map((post) => (
              <div key={post.permalinkUrl} className="relative grid grid-cols-1 md:grid-cols-[minmax(240px,0.9fr)_minmax(320px,1.35fr)_180px_120px_44px] gap-3 md:gap-4 bg-black/20 px-4 py-3 border-t border-white/5">
                <div className="min-w-0 flex items-center gap-3">
                  <span className={`material-symbols-outlined text-sm ${isTikTokPost(post) ? 'text-pink-300' : 'text-blue-400'}`}>
                    {isTikTokPost(post) ? 'music_note' : 'link'}
                  </span>
                  <span className={`hidden shrink-0 rounded px-2 py-1 text-[10px] font-bold uppercase tracking-widest sm:inline-flex ${platformBadgeClass(post)}`}>
                    {platformDisplay(post)}
                  </span>
                  <a
                    href={post.permalinkUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="block truncate text-sm text-neutral-200 hover:text-white"
                  >
                    {post.permalinkUrl}
                  </a>
                </div>
                <div className="min-w-0 text-xs text-neutral-400">
                  <span className="md:hidden mr-2 text-[10px] uppercase tracking-widest text-neutral-600">Caption</span>
                  <span className="line-clamp-2">{post.caption || '-'}</span>
                  {isTikTokPost(post) && (
                    <div className="mt-2 flex flex-wrap gap-2 text-[10px] uppercase tracking-widest text-neutral-500">
                      {post.authorName && <span>{authorDisplay(post.authorName)}</span>}
                      {metricLabel('plays', post.playCount)}
                      {metricLabel('likes', post.likeCount)}
                      {metricLabel('shares', post.shareCount)}
                      {metricLabel('comments', post.commentCount)}
                      {post.durationSeconds ? <span>{post.durationSeconds}s</span> : null}
                    </div>
                  )}
                  {isTikTokPost(post) && (post.musicName || post.musicAuthor) && (
                    <div className="mt-1 line-clamp-1 text-[10px] text-neutral-600">
                      {[post.musicName, post.musicAuthor].filter(Boolean).join(' - ')}
                    </div>
                  )}
                </div>
                <div className="min-w-0 text-xs text-neutral-400">
                  <span className="md:hidden mr-2 text-[10px] uppercase tracking-widest text-neutral-600">Local Download</span>
                  <div className="flex min-w-0 items-center gap-2">
                    <span className={`inline-flex w-fit rounded px-2 py-1 text-[10px] font-bold uppercase tracking-widest ${s3StatusClass(post.s3UploadStatus)}`}>
                      {localStatusLabel(post.s3UploadStatus)}
                    </span>
                    {canQueueS3Upload(post.s3UploadStatus) && (
                      <button
                        type="button"
                        onClick={() => handleUploadSingleVideoToS3(post)}
                        disabled={uploadingPostIdSet.has(post.id)}
                        title={post.s3UploadStatus === 'Failed' ? 'Retry download' : 'Download video'}
                        aria-label={post.s3UploadStatus === 'Failed' ? 'Retry download' : 'Download video'}
                        className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md border border-blue-500/20 bg-blue-500/10 text-blue-300 hover:border-blue-400/50 hover:bg-blue-500/20 disabled:opacity-50"
                      >
                        {uploadingPostIdSet.has(post.id) ? (
                          <span className="h-3.5 w-3.5 rounded-full border-2 border-blue-200/30 border-t-blue-200 animate-spin" />
                        ) : (
                          <span className="material-symbols-outlined text-[15px]">{post.s3UploadStatus === 'Failed' ? 'refresh' : 'download'}</span>
                        )}
                      </button>
                    )}
                    {canStopLocalDownload(post.s3UploadStatus) && (
                      <button
                        type="button"
                        onClick={() => handleStopSingleDownload(post)}
                        disabled={stoppingPostIdSet.has(post.id)}
                        title="Stop download"
                        aria-label="Stop download"
                        className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-md border border-red-500/20 bg-red-500/10 text-red-300 hover:border-red-400/50 hover:bg-red-500/20 disabled:opacity-50"
                      >
                        {stoppingPostIdSet.has(post.id) ? (
                          <span className="h-3.5 w-3.5 rounded-full border-2 border-red-200/30 border-t-red-200 animate-spin" />
                        ) : (
                          <span className="material-symbols-outlined text-[15px]">stop_circle</span>
                        )}
                      </button>
                    )}
                  </div>
                  {post.s3Key && <span className="mt-1 block truncate text-[10px] text-neutral-500">{post.s3Key}</span>}
                  {post.s3UploadError && <span className="mt-1 block line-clamp-1 text-[10px] text-red-300">{post.s3UploadError}</span>}
                </div>
                <span className="text-[10px] text-neutral-500">
                  {post.postCreatedAt ? new Date(post.postCreatedAt).toLocaleDateString() : 'No date'}
                </span>
                <div className="relative flex justify-start md:justify-end">
                  <button
                    type="button"
                    onClick={() => setOpenActionPostId((currentPostId) => currentPostId === post.id ? null : post.id)}
                    aria-label="Open row actions"
                    title="Actions"
                    className="inline-flex h-8 w-8 items-center justify-center rounded-md border border-white/5 bg-white/5 text-neutral-300 hover:bg-white/10 hover:text-white"
                  >
                    <span className="material-symbols-outlined text-[18px]">more_horiz</span>
                  </button>

                  {openActionPostId === post.id && (
                    <div className="absolute right-0 top-9 z-30 w-52 rounded-lg border border-white/10 bg-neutral-950 p-1 shadow-2xl shadow-black/40">
                      <button
                        type="button"
                        onClick={() => handleViewUploadedVideo(post)}
                        disabled={!isLocallyDownloaded(post.s3UploadStatus) || viewingPostId === post.id}
                        className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-xs font-semibold text-neutral-200 hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
                      >
                        {viewingPostId === post.id ? (
                          <span className="h-3.5 w-3.5 rounded-full border-2 border-neutral-400/30 border-t-neutral-200 animate-spin" />
                        ) : (
                          <span className="material-symbols-outlined text-[15px]">open_in_new</span>
                        )}
                        View local video
                      </button>
                      {canQueueS3Upload(post.s3UploadStatus) && (
                        <button
                          type="button"
                          onClick={() => {
                            setOpenActionPostId(null);
                            void handleUploadSingleVideoToS3(post);
                          }}
                          disabled={uploadingPostIdSet.has(post.id)}
                          className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-xs font-semibold text-neutral-200 hover:bg-white/10 disabled:cursor-not-allowed disabled:opacity-40"
                        >
                          <span className="material-symbols-outlined text-[15px]">
                            {post.s3UploadStatus === 'Failed' ? 'refresh' : 'download'}
                          </span>
                          {post.s3UploadStatus === 'Failed' ? 'Retry download' : 'Download video'}
                        </button>
                      )}
                      {canStopLocalDownload(post.s3UploadStatus) && (
                        <button
                          type="button"
                          onClick={() => {
                            setOpenActionPostId(null);
                            void handleStopSingleDownload(post);
                          }}
                          disabled={stoppingPostIdSet.has(post.id)}
                          className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-xs font-semibold text-red-200 hover:bg-red-500/10 disabled:cursor-not-allowed disabled:opacity-40"
                        >
                          <span className="material-symbols-outlined text-[15px]">stop_circle</span>
                          Stop download
                        </button>
                      )}
                      <button
                        type="button"
                        onClick={() => {
                          setOpenActionPostId(null);
                          handleDeletePost(post);
                        }}
                        className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-left text-xs font-semibold text-red-200 hover:bg-red-500/10"
                      >
                        <span className="material-symbols-outlined text-[15px]">delete</span>
                        Delete
                      </button>
                    </div>
                  )}
                </div>
              </div>
            ))}
          </div>
        ) : (
          <div className="rounded-lg border border-white/5 bg-black/20 px-4 py-10 text-center text-sm text-neutral-500">
            No scraped URLs yet.
          </div>
        )}
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

function platformLabel(platform: ScrapePlatform) {
  return platform === 'tiktok' ? 'TikTok' : 'Facebook';
}

function platformValue(post: ScrapedPost) {
  return (post.platform || 'Facebook').toLowerCase() === 'tiktok' ? 'tiktok' : 'facebook';
}

function platformDisplay(post: ScrapedPost) {
  return platformValue(post) === 'tiktok' ? 'TikTok' : 'Facebook';
}

function isTikTokPost(post: ScrapedPost) {
  return platformValue(post) === 'tiktok';
}

function platformBadgeClass(post: ScrapedPost) {
  return isTikTokPost(post)
    ? 'border border-pink-400/20 bg-pink-500/10 text-pink-200'
    : 'border border-blue-400/20 bg-blue-500/10 text-blue-200';
}

function metricLabel(label: string, value?: number) {
  if (typeof value !== 'number') {
    return null;
  }

  return <span>{compactNumber(value)} {label}</span>;
}

function compactNumber(value: number) {
  return new Intl.NumberFormat(undefined, {
    notation: 'compact',
    maximumFractionDigits: 1,
  }).format(value);
}

function authorDisplay(authorName: string) {
  const trimmed = authorName.trim();
  return trimmed.startsWith('@') ? trimmed : `@${trimmed}`;
}

function todayDateInputValue() {
  return new Date().toISOString().slice(0, 10);
}

function getStoredUserId() {
  if (typeof window === 'undefined') {
    return '';
  }

  return window.localStorage.getItem('smapi_user_id') || '';
}

function canQueueS3Upload(status?: string) {
  return !isLocallyDownloaded(status) && status !== 'Queued' && status !== 'Downloading' && status !== 'Uploading';
}

function canStopLocalDownload(status?: string) {
  return status === 'Queued' || status === 'Downloading' || status === 'Uploading';
}

function s3StatusClass(status?: string) {
  switch (status) {
    case 'Downloaded':
    case 'Uploaded':
      return 'bg-emerald-500/10 text-emerald-300 border border-emerald-500/20';
    case 'Queued':
    case 'Downloading':
    case 'Uploading':
      return 'bg-blue-500/10 text-blue-300 border border-blue-500/20';
    case 'Failed':
      return 'bg-red-500/10 text-red-300 border border-red-500/20';
    case 'Cancelled':
      return 'bg-amber-500/10 text-amber-300 border border-amber-500/20';
    default:
      return 'bg-zinc-500/10 text-zinc-300 border border-zinc-500/20';
  }
}

function isLocallyDownloaded(status?: string) {
  return status === 'Downloaded' || status === 'Uploaded';
}

function localStatusLabel(status?: string) {
  switch (status) {
    case 'Downloaded':
    case 'Uploaded':
      return 'Downloaded';
    case 'Downloading':
    case 'Uploading':
      return 'Downloading';
    case 'NotUploaded':
    case undefined:
    case '':
      return 'NotDownloaded';
    case 'Cancelled':
      return 'Stopped';
    default:
      return status;
  }
}
