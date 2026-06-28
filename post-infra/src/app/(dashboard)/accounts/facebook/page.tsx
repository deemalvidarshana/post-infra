"use client";

import Link from 'next/link';
import React, { useCallback, useEffect, useRef, useState } from 'react';

interface FBPage {
  databaseId: number;
  userId: string;
  pageId: string;
  name: string;
  category: string;
  accessToken: string;
  avatar: string;
  facebookMetaAppId?: number;
  status: 'connected';
}

interface FacebookPageApi {
  id: number;
  userId?: string;
  pageId: string;
  pageName: string;
  category?: string;
  accessToken: string;
  avatarUrl?: string;
  facebookMetaAppId?: number;
}

interface FacebookMetaAppApi {
  id: number;
  userId: string;
  name: string;
  appId?: string;
  verifyToken: string;
  webhookKey: string;
  callbackPath: string;
  graphApiVersion: string;
  isDefault: boolean;
}

export default function FacebookAccountsPage() {
  const [userId, setUserId] = useState('');
  const [pages, setPages] = useState<FBPage[]>([]);
  const [metaApps, setMetaApps] = useState<FacebookMetaAppApi[]>([]);
  const [message, setMessage] = useState('');
  const [modalMessage, setModalMessage] = useState('');
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isConnecting, setIsConnecting] = useState(false);
  const [isLoading, setIsLoading] = useState(false);
  const [editingPage, setEditingPage] = useState<FBPage | null>(null);
  const [subscribingPageId, setSubscribingPageId] = useState('');
  const [formData, setFormData] = useState({
    name: '',
    pageId: '',
    category: '',
    avatarUrl: '',
    accessToken: '',
    facebookMetaAppId: ''
  });

  const hasLoadedStoredUser = useRef(false);

  const fetchPages = useCallback(async (nextUserId = userId) => {
    setIsLoading(true);
    setMessage('');
    if (nextUserId.trim()) {
      window.localStorage.setItem('smapi_user_id', nextUserId.trim());
    }

    try {
      const [response, metaAppsResponse] = await Promise.all([
        fetch('/api/smapi/Pages/facebook'),
        nextUserId.trim()
          ? fetch(`/api/smapi/FacebookMetaApps?userId=${encodeURIComponent(nextUserId.trim())}`)
          : Promise.resolve(null)
      ]);
      const data = await response.json();
      if (Array.isArray(data)) {
        setPages((data as FacebookPageApi[]).map(toPageViewModel));
      } else {
        setPages([]);
      }

      if (metaAppsResponse?.ok) {
        const metaAppsData = await metaAppsResponse.json();
        setMetaApps(Array.isArray(metaAppsData) ? metaAppsData as FacebookMetaAppApi[] : []);
      } else if (!nextUserId.trim()) {
        setMetaApps([]);
      }
    } catch {
      setMessage('Failed to fetch Facebook pages from the backend.');
    } finally {
      setIsLoading(false);
    }
  }, [userId]);

  useEffect(() => {
    if (hasLoadedStoredUser.current) {
      return;
    }

    hasLoadedStoredUser.current = true;
    const storedUserId = getStoredUserId();
    if (storedUserId) {
      window.setTimeout(() => setUserId(storedUserId), 0);
    }

    void fetchPages(storedUserId);
  }, [fetchPages, userId]);

  const resetForm = () => {
    const defaultMetaApp = metaApps.find((item) => item.isDefault) ?? metaApps[0];
    setFormData({
      name: '',
      pageId: '',
      category: '',
      avatarUrl: '',
      accessToken: '',
      facebookMetaAppId: defaultMetaApp ? String(defaultMetaApp.id) : ''
    });
    setEditingPage(null);
    setModalMessage('');
  };

  const openConnectModal = () => {
    resetForm();
    setIsModalOpen(true);
  };

  const openEditModal = (page: FBPage) => {
    setEditingPage(page);
    setModalMessage('');
    if (page.userId) {
      setUserId(page.userId);
      window.localStorage.setItem('smapi_user_id', page.userId);
    }
    setFormData({
      name: page.name,
      pageId: page.pageId,
      category: page.category,
      avatarUrl: page.avatar,
      accessToken: page.accessToken,
      facebookMetaAppId: page.facebookMetaAppId ? String(page.facebookMetaAppId) : ''
    });
    setIsModalOpen(true);
  };

  const handleSavePage = async (e: React.FormEvent) => {
    e.preventDefault();
    setModalMessage('');
    setMessage('');

    if (!userId.trim()) {
      setModalMessage('Enter a User ID before connecting a page.');
      return;
    }

    setIsConnecting(true);
    window.localStorage.setItem('smapi_user_id', userId.trim());

    try {
      const response = await fetch(editingPage ? `/api/smapi/Pages/facebook/${editingPage.databaseId}` : '/api/smapi/Pages/facebook/connect', {
        method: editingPage ? 'PUT' : 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          pageId: formData.pageId.trim(),
          pageName: formData.name.trim(),
          accessToken: formData.accessToken,
          userId: userId.trim(),
          category: formData.category.trim() || null,
          avatarUrl: formData.avatarUrl.trim() || null,
          facebookMetaAppId: formData.facebookMetaAppId ? Number(formData.facebookMetaAppId) : null
        }),
      });

      const responseText = await response.text();
      let data: { success?: boolean; message?: string } | null = null;

      try {
        data = JSON.parse(responseText);
      } catch {
        data = null;
      }

      if (response.ok && data?.success) {
        await fetchPages(userId);
        setIsModalOpen(false);
        setModalMessage('');
        setMessage(editingPage ? 'Facebook Page updated successfully.' : 'Facebook Page connected successfully.');
        resetForm();
      } else {
        setModalMessage(data?.message || `Failed to save page. Backend status ${response.status}.`);
      }
    } catch {
      setModalMessage('Could not connect to the backend server.');
    } finally {
      setIsConnecting(false);
    }
  };

  const handleSubscribeWebhook = async (page: FBPage) => {
    setMessage('');

    if (!page.userId && !userId.trim()) {
      setMessage('Page owner User ID is required before subscribing the webhook.');
      return;
    }

    if (!page.facebookMetaAppId) {
      setMessage('Select a Meta App for this page before subscribing the webhook.');
      return;
    }

    setSubscribingPageId(page.pageId);
    try {
      const response = await fetch('/api/smapi/FacebookMetaApps/page-subscriptions', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          userId: page.userId || userId.trim(),
          pageId: page.pageId
        })
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Webhook subscription failed. Backend status ${response.status}.`);
        return;
      }

      setMessage(data.message || 'Webhook subscribed for this page.');
    } catch {
      setMessage('Could not connect to the backend server.');
    } finally {
      setSubscribingPageId('');
    }
  };

  const handleDeletePage = async (databaseId: number) => {
    setMessage('');

    try {
      const response = await fetch(`/api/smapi/Pages/facebook/${databaseId}`, {
        method: 'DELETE'
      });

      if (!response.ok) {
        setMessage(`Failed to delete page. Backend status ${response.status}.`);
        return;
      }

      setPages((previousPages) => previousPages.filter((page) => page.databaseId !== databaseId));
    } catch {
      setMessage('Could not connect to the backend server.');
    }
  };

  return (
    <div className="p-8 space-y-8 animate-in fade-in duration-700">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white tracking-tight">Facebook Pages</h1>
          <p className="text-neutral-500 text-sm mt-1">Connect and manage Facebook Pages for Reel publishing.</p>
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
            onClick={() => fetchPages()}
            disabled={isLoading}
            className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
          >
            <span className="material-symbols-outlined text-sm">sync</span>
            {isLoading ? 'Loading' : 'Load Pages'}
          </button>
          <button
            onClick={openConnectModal}
            className="flex items-center justify-center gap-2 px-6 py-3 bg-blue-600 text-white rounded-lg font-bold hover:bg-blue-500 transition-all shadow-lg shadow-blue-600/20 active:scale-95"
          >
            <span className="material-symbols-outlined">add_circle</span>
            Connect Page
          </button>
          <Link
            href={metaAppsHref(userId)}
            className="flex items-center justify-center gap-2 px-6 py-3 bg-emerald-600/20 text-emerald-100 rounded-lg font-bold hover:bg-emerald-600/30 transition-all border border-emerald-500/20"
          >
            <span className="material-symbols-outlined">apps</span>
            Meta Apps
          </Link>
        </div>
      </div>

      {message && (
        <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
          {message}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="glass-panel p-6 rounded-xl border border-white/5 space-y-2">
          <p className="text-neutral-500 text-[10px] uppercase tracking-widest font-bold">Total Pages</p>
          <p className="text-3xl font-bold text-white">{pages.length}</p>
        </div>
        <div className="glass-panel p-6 rounded-xl border border-white/5 space-y-2">
          <p className="text-neutral-500 text-[10px] uppercase tracking-widest font-bold">Connected Tokens</p>
          <p className="text-3xl font-bold text-green-400">{pages.length} Active</p>
        </div>
        <div className="glass-panel p-6 rounded-xl border border-white/5 space-y-2">
          <p className="text-neutral-500 text-[10px] uppercase tracking-widest font-bold">Queue Target</p>
          <p className="text-3xl font-bold text-blue-400">Reels</p>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-6">
        {pages.map((page) => (
          <div key={page.databaseId} className="glass-panel p-6 rounded-xl border border-white/5 group hover:border-primary/30 transition-all duration-300 relative overflow-hidden">
            <div className="absolute top-0 right-0 p-4">
              <span className="flex h-2 w-2 rounded-full bg-green-500 shadow-[0_0_10px_rgba(34,197,94,0.5)]"></span>
            </div>

            <div className="flex items-start gap-4">
              <div className="w-16 h-16 rounded-lg overflow-hidden border border-white/10 shrink-0 flex items-center justify-center bg-blue-600/10">
                {page.avatar ? (
                  <img src={page.avatar} alt={page.name} className="w-full h-full object-cover" />
                ) : (
                  <span className="material-symbols-outlined text-blue-500">facebook</span>
                )}
              </div>
              <div className="flex-grow min-w-0">
                <h3 className="text-lg font-bold text-white truncate">{page.name}</h3>
                <p className="text-neutral-500 text-[10px] truncate">ID: {page.pageId}</p>
                {page.userId && <p className="text-neutral-600 text-[10px] truncate">Owner: {page.userId}</p>}
                <p className="text-emerald-300 text-[10px] truncate">Meta App: {metaAppName(page.facebookMetaAppId, metaApps)}</p>
                <div className="mt-3 flex items-center gap-2 text-[10px] text-neutral-400 bg-black/30 p-2 rounded border border-white/5">
                  <span className="material-symbols-outlined text-xs">key</span>
                  <span className="truncate">Token: {maskToken(page.accessToken)}</span>
                </div>
              </div>
            </div>

            <div className="mt-6 space-y-3">
              <Link
                href={pageToolHref(page, 'apify', userId)}
                className="w-full py-2.5 rounded bg-blue-600 text-xs font-bold text-white hover:bg-blue-500 transition-colors border border-blue-400/20 flex items-center justify-center gap-2"
              >
                <span className="material-symbols-outlined text-sm">travel_explore</span>
                Reels Scraper
              </Link>
              <Link
                href={pageToolHref(page, 'rednote', userId)}
                className="w-full py-2.5 rounded bg-rose-600 text-xs font-bold text-white hover:bg-rose-500 transition-colors border border-rose-400/20 flex items-center justify-center gap-2"
              >
                <span className="material-symbols-outlined text-sm">download</span>
                RedNote Downloader
              </Link>
              <div className="flex gap-3">
                <Link
                  href={pageToolHref(page, 'queue', userId)}
                  className="flex-grow py-2 rounded bg-white/5 text-xs font-bold text-white hover:bg-white/10 transition-colors border border-white/5 text-center"
                >
                  Upload Queue
                </Link>
                <Link
                  href={pageToolHref(page, 'comments', userId)}
                  className="flex-grow py-2 rounded bg-emerald-600/20 text-xs font-bold text-emerald-100 hover:bg-emerald-600/30 transition-colors border border-emerald-500/20 text-center"
                >
                  Auto Replies
                </Link>
                <button
                  type="button"
                  onClick={() => openEditModal(page)}
                  className="px-3 py-2 rounded bg-white/5 text-neutral-300 hover:bg-white/10 hover:text-white transition-colors border border-white/5"
                  title="Edit page"
                >
                  <span className="material-symbols-outlined text-sm">edit</span>
                </button>
                <button
                  type="button"
                  onClick={() => handleDeletePage(page.databaseId)}
                  className="px-3 py-2 rounded bg-red-500/10 text-red-500 hover:bg-red-500/20 transition-colors border border-red-500/20"
                >
                  <span className="material-symbols-outlined text-sm">delete</span>
                </button>
              </div>
              <button
                type="button"
                onClick={() => handleSubscribeWebhook(page)}
                disabled={subscribingPageId === page.pageId || !page.facebookMetaAppId}
                className="w-full py-2 rounded bg-emerald-600 text-xs font-bold text-white hover:bg-emerald-500 transition-colors border border-emerald-400/20 disabled:opacity-50 disabled:cursor-not-allowed"
                title={page.facebookMetaAppId ? 'Subscribe this page to its selected Meta App webhook' : 'Select a Meta App first'}
              >
                {subscribingPageId === page.pageId ? 'Subscribing...' : 'Subscribe Webhook'}
              </button>
            </div>
          </div>
        ))}
      </div>

      {pages.length === 0 && (
        <div className="rounded-xl border border-white/5 bg-black/20 px-4 py-12 text-center text-sm text-neutral-500">
          Enter a User ID, load pages, or connect a Facebook Page.
        </div>
      )}

      {isModalOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-6">
          <div className="absolute inset-0 bg-black/80 backdrop-blur-sm" onClick={() => !isConnecting && setIsModalOpen(false)}></div>
          <div className="relative glass-panel w-full max-w-md max-h-[calc(100vh-3rem)] overflow-y-auto p-8 rounded-2xl border border-white/10 shadow-2xl animate-in zoom-in-95 duration-200">
            <div className="space-y-6">
              <div className="text-center">
                <h2 className="text-2xl font-bold text-white">{editingPage ? 'Edit Facebook Page' : 'Connect Facebook Page'}</h2>
                <p className="text-neutral-400 text-sm mt-1">{editingPage ? 'Update the saved Page details and access token.' : 'Enter the Page details and Page access token.'}</p>
              </div>

              <form onSubmit={handleSavePage} className="space-y-4">
                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">User ID</span>
                  <input
                    required
                    type="text"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    value={userId}
                    onChange={(e) => setUserId(e.target.value)}
                  />
                </label>

                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Page Name</span>
                  <input
                    required
                    type="text"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                  />
                </label>

                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Facebook Page ID</span>
                  <input
                    required
                    type="text"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    value={formData.pageId}
                    onChange={(e) => setFormData({ ...formData, pageId: e.target.value })}
                  />
                </label>

                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Category</span>
                  <input
                    type="text"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    value={formData.category}
                    onChange={(e) => setFormData({ ...formData, category: e.target.value })}
                  />
                </label>

                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Avatar URL</span>
                  <input
                    type="url"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    value={formData.avatarUrl}
                    onChange={(e) => setFormData({ ...formData, avatarUrl: e.target.value })}
                  />
                </label>

                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Page Access Token</span>
                  <textarea
                    required
                    rows={3}
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all resize-none"
                    value={formData.accessToken}
                    onChange={(e) => setFormData({ ...formData, accessToken: e.target.value })}
                  />
                </label>

                <label className="space-y-1 block">
                  <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Meta Developer App</span>
                  <select
                    value={formData.facebookMetaAppId}
                    onChange={(e) => setFormData({ ...formData, facebookMetaAppId: e.target.value })}
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                  >
                    <option value="">No Meta App selected</option>
                    {metaApps.map((metaApp) => (
                      <option key={metaApp.id} value={metaApp.id}>
                        {metaApp.name}{metaApp.isDefault ? ' (default)' : ''}
                      </option>
                    ))}
                  </select>
                  <span className="block text-xs text-neutral-500">
                    Create apps from the Meta Apps page if this list is empty.
                  </span>
                </label>

                {modalMessage && (
                  <div className="rounded-lg border border-red-500/20 bg-red-500/10 px-4 py-3 text-sm text-red-200">
                    {modalMessage}
                  </div>
                )}

                <div className="pt-4 flex flex-col gap-3">
                  <button
                    type="submit"
                    disabled={isConnecting}
                    className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white rounded-xl font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
                  >
                    {isConnecting ? (
                      <>
                        <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                        Connecting...
                      </>
                    ) : (
                      editingPage ? 'Save Changes' : 'Connect Page'
                    )}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setIsModalOpen(false);
                      resetForm();
                    }}
                    disabled={isConnecting}
                    className="w-full py-2 bg-transparent text-neutral-500 hover:text-white transition-colors font-medium text-sm"
                  >
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function toPageViewModel(page: FacebookPageApi): FBPage {
  return {
    databaseId: page.id,
    userId: page.userId || '',
    pageId: page.pageId,
    name: page.pageName,
    category: page.category || '',
    accessToken: page.accessToken,
    avatar: page.avatarUrl || '',
    facebookMetaAppId: page.facebookMetaAppId,
    status: 'connected'
  };
}

function pageToolHref(page: FBPage, tool: 'apify' | 'rednote' | 'queue' | 'comments', fallbackUserId: string) {
  const href = `/accounts/facebook/${encodeURIComponent(page.pageId)}/${tool}`;
  const ownerUserId = page.userId || fallbackUserId.trim();
  return ownerUserId ? `${href}?userId=${encodeURIComponent(ownerUserId)}` : href;
}

function maskToken(token: string) {
  if (!token) {
    return '-';
  }

  if (token.length <= 10) {
    return 'Saved';
  }

  return `${token.slice(0, 6)}...${token.slice(-4)}`;
}

function metaAppName(metaAppId: number | undefined, metaApps: FacebookMetaAppApi[]) {
  if (!metaAppId) {
    return 'Not selected';
  }

  return metaApps.find((item) => item.id === metaAppId)?.name || `Meta App #${metaAppId}`;
}

function metaAppsHref(userId: string) {
  const trimmedUserId = userId.trim();
  return trimmedUserId
    ? `/accounts/facebook/meta-apps?userId=${encodeURIComponent(trimmedUserId)}`
    : '/accounts/facebook/meta-apps';
}

function getStoredUserId() {
  if (typeof window === 'undefined') {
    return '';
  }

  return window.localStorage.getItem('smapi_user_id') || '';
}
