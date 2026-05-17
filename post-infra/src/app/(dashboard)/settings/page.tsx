"use client";

import React, { useCallback, useEffect, useRef, useState } from 'react';

interface LocalStorageSettingsResponse {
  userId: string;
  bucket: string;
  region: string;
  endpointUrl?: string;
}

interface ApifySettingsResponse {
  userId: string;
  apiToken?: string;
  hasApiToken: boolean;
  apiTokenLength: number;
  updatedAt?: string;
}

interface GeminiSettingsResponse {
  userId: string;
  model: string;
  apiKey?: string;
  hasApiKey: boolean;
  apiKeyLength: number;
  updatedAt?: string;
}

export default function SettingsPage() {
  const [message, setMessage] = useState('');
  const [isSavingApify, setIsSavingApify] = useState(false);
  const [isSavingGemini, setIsSavingGemini] = useState(false);
  const [downloadFolder, setDownloadFolder] = useState('');
  const [publicBaseUrl, setPublicBaseUrl] = useState('');
  const [apifyToken, setApifyToken] = useState('');
  const [hasSavedApifyToken, setHasSavedApifyToken] = useState(false);
  const [apifyTokenLength, setApifyTokenLength] = useState(0);
  const [apifyUpdatedAt, setApifyUpdatedAt] = useState('');
  const [geminiModel, setGeminiModel] = useState('');
  const [geminiApiKey, setGeminiApiKey] = useState('');
  const [hasSavedGeminiApiKey, setHasSavedGeminiApiKey] = useState(false);
  const [geminiApiKeyLength, setGeminiApiKeyLength] = useState(0);
  const [geminiUpdatedAt, setGeminiUpdatedAt] = useState('');
  const hasLoadedStoredUser = useRef(false);

  const loadSettings = useCallback(async () => {
    setMessage('');

    try {
      const [localResponse, apifyResponse, geminiResponse] = await Promise.all([
        fetch('/api/smapi/Settings/s3/global'),
        fetch('/api/smapi/Settings/apify'),
        fetch('/api/smapi/Settings/gemini')
      ]);

      if (!localResponse.ok) {
        setMessage('Could not load local storage settings.');
        return;
      }

      const localData = await localResponse.json() as LocalStorageSettingsResponse;
      setDownloadFolder(localData.bucket || '');
      setPublicBaseUrl(localData.endpointUrl || '');

      if (apifyResponse.status === 404) {
        setHasSavedApifyToken(false);
        setApifyTokenLength(0);
        setApifyUpdatedAt('');
        setApifyToken('');
      } else if (apifyResponse.ok) {
        const apifyData = await apifyResponse.json() as ApifySettingsResponse;
        setHasSavedApifyToken(apifyData.hasApiToken);
        setApifyTokenLength(apifyData.apiTokenLength || 0);
        setApifyUpdatedAt(apifyData.updatedAt || '');
        setApifyToken(apifyData.apiToken || '');
      } else {
        setMessage('Local settings loaded, but Apify settings could not be loaded.');
        return;
      }

      if (geminiResponse.status === 404) {
        setGeminiModel('');
        setGeminiApiKey('');
        setHasSavedGeminiApiKey(false);
        setGeminiApiKeyLength(0);
        setGeminiUpdatedAt('');
      } else if (geminiResponse.ok) {
        const geminiData = await geminiResponse.json() as GeminiSettingsResponse;
        setGeminiModel(geminiData.model || '');
        setGeminiApiKey(geminiData.apiKey || '');
        setHasSavedGeminiApiKey(geminiData.hasApiKey);
        setGeminiApiKeyLength(geminiData.apiKeyLength || 0);
        setGeminiUpdatedAt(geminiData.updatedAt || '');
      } else {
        setMessage('Local and Apify settings loaded, but Gemini settings could not be loaded.');
        return;
      }

      setMessage('Settings loaded.');
    } catch {
      setMessage('Could not connect to the backend server.');
    }
  }, []);

  useEffect(() => {
    if (hasLoadedStoredUser.current) {
      return;
    }

    hasLoadedStoredUser.current = true;
    void loadSettings();
  }, [loadSettings]);

  const saveApifySettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage('');

    if (!apifyToken.trim()) {
      setMessage('Enter an Apify API key before saving.');
      return;
    }

    setIsSavingApify(true);
    try {
      const response = await fetch('/api/smapi/Settings/apify', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          apiToken: apifyToken.trim()
        })
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Failed to save Apify settings. Backend status ${response.status}.`);
        return;
      }

      const savedSettings = data.settings as ApifySettingsResponse | undefined;
      setHasSavedApifyToken(savedSettings?.hasApiToken ?? true);
      setApifyTokenLength(savedSettings?.apiTokenLength || apifyToken.trim().length);
      setApifyUpdatedAt(savedSettings?.updatedAt || new Date().toISOString());
      setApifyToken(savedSettings?.apiToken || apifyToken.trim());
      setMessage('Global Apify API key saved successfully.');
    } catch {
      setMessage('Could not connect to the backend server.');
    } finally {
      setIsSavingApify(false);
    }
  };

  const saveGeminiSettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage('');

    if (!geminiModel.trim()) {
      setMessage('Enter a Gemini model before saving.');
      return;
    }

    if (!geminiApiKey.trim()) {
      setMessage('Enter a Gemini API key before saving.');
      return;
    }

    setIsSavingGemini(true);
    try {
      const response = await fetch('/api/smapi/Settings/gemini', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          model: geminiModel.trim(),
          apiKey: geminiApiKey.trim()
        })
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Failed to save Gemini settings. Backend status ${response.status}.`);
        return;
      }

      const savedSettings = data.settings as GeminiSettingsResponse | undefined;
      setGeminiModel(savedSettings?.model || geminiModel.trim());
      setGeminiApiKey(savedSettings?.apiKey || geminiApiKey.trim());
      setHasSavedGeminiApiKey(savedSettings?.hasApiKey ?? true);
      setGeminiApiKeyLength(savedSettings?.apiKeyLength || geminiApiKey.trim().length);
      setGeminiUpdatedAt(savedSettings?.updatedAt || new Date().toISOString());
      setMessage('Global Gemini settings saved successfully.');
    } catch {
      setMessage('Could not connect to the backend server.');
    } finally {
      setIsSavingGemini(false);
    }
  };

  return (
    <div className="space-y-8 animate-in fade-in duration-700">
      <header className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white tracking-tight">Settings</h1>
          <p className="text-neutral-500 text-sm mt-1">Manage workspace preferences and automation defaults.</p>
        </div>
      </header>

      {message && (
        <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
          {message}
        </div>
      )}

      <section className="grid grid-cols-1 lg:grid-cols-[1.1fr_0.9fr] gap-6">
        <div className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
          <div>
            <h2 className="text-lg font-bold text-white">Local Downloads</h2>
            <p className="text-sm text-neutral-500 mt-1">Scraped reels are saved on this machine inside a folder named after each Facebook Page.</p>
          </div>

          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Download Folder</span>
            <input
              readOnly
              value={downloadFolder}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:outline-none"
              placeholder="Load settings to see the folder"
            />
          </label>

          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Public Base URL</span>
            <input
              readOnly
              value={publicBaseUrl || 'Not configured'}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-neutral-300 focus:outline-none"
            />
            <span className="block text-xs text-neutral-500">Only needed if queued jobs should publish automatically after saving locally.</span>
          </label>

          <div className="rounded-lg border border-blue-500/10 bg-blue-500/5 px-4 py-4 text-sm text-blue-100">
            AWS S3 is disabled. New downloads will be written under the local download folder using the page name as the first folder.
          </div>
        </div>

        <form onSubmit={saveApifySettings} className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
          <div>
            <h2 className="text-lg font-bold text-white">Apify API</h2>
            <p className="text-sm text-neutral-500 mt-1">One global key used by every Facebook Page scrape.</p>
          </div>

          <label className="space-y-1 block">
            <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">API Key</span>
            <input
              required
              type="text"
              value={apifyToken}
              onChange={(event) => setApifyToken(event.target.value)}
              className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
              placeholder="apify_api_..."
              autoComplete="off"
              spellCheck={false}
            />
          </label>

          <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
            {hasSavedApifyToken ? (
              <div className="space-y-1">
                <p className="font-semibold text-emerald-200">Apify key is saved in the database.</p>
                <p className="text-xs text-neutral-500">
                  Global key | Length: {apifyTokenLength || '-'} characters
                  {apifyUpdatedAt ? ` | Updated: ${new Date(apifyUpdatedAt).toLocaleString()}` : ''}
                </p>
              </div>
            ) : (
              'No Apify key saved for this user yet.'
            )}
          </div>

          <button
            type="submit"
            disabled={isSavingApify}
            className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
          >
            {isSavingApify ? (
              <>
                <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                Saving...
              </>
            ) : (
              <>
                <span className="material-symbols-outlined text-sm">vpn_key</span>
                Save Apify Key
              </>
            )}
          </button>
        </form>
      </section>

      <section>
        <form onSubmit={saveGeminiSettings} className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
          <div>
            <h2 className="text-lg font-bold text-white">Gemini API</h2>
            <p className="text-sm text-neutral-500 mt-1">One global model and API key used by Gemini caption automation.</p>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <label className="space-y-1 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Model</span>
              <input
                required
                type="text"
                value={geminiModel}
                onChange={(event) => setGeminiModel(event.target.value)}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                placeholder="gemini-2.0-flash"
                autoComplete="off"
                spellCheck={false}
              />
            </label>

            <label className="space-y-1 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">API Key</span>
              <input
                required
                type="text"
                value={geminiApiKey}
                onChange={(event) => setGeminiApiKey(event.target.value)}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                placeholder="AIza..."
                autoComplete="off"
                spellCheck={false}
              />
            </label>
          </div>

          <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
            {hasSavedGeminiApiKey ? (
              <div className="space-y-1">
                <p className="font-semibold text-emerald-200">Gemini settings are saved in the database.</p>
                <p className="text-xs text-neutral-500">
                  Model: {geminiModel || '-'} | Key length: {geminiApiKeyLength || '-'} characters
                  {geminiUpdatedAt ? ` | Updated: ${new Date(geminiUpdatedAt).toLocaleString()}` : ''}
                </p>
              </div>
            ) : (
              'No Gemini settings saved for this user yet.'
            )}
          </div>

          <button
            type="submit"
            disabled={isSavingGemini}
            className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
          >
            {isSavingGemini ? (
              <>
                <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                Saving...
              </>
            ) : (
              <>
                <span className="material-symbols-outlined text-sm">key</span>
                Save Gemini Settings
              </>
            )}
          </button>
        </form>
      </section>

      <section>
        <div className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
          <div>
            <h2 className="text-lg font-bold text-white">Automation</h2>
            <p className="text-sm text-neutral-500 mt-1">Defaults for social media collection workflows.</p>
          </div>

          <div className="space-y-3">
            <label className="flex items-center justify-between gap-4 rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <span className="text-sm text-neutral-300">Save scraped Facebook post URLs</span>
              <input className="h-4 w-4 accent-blue-500" defaultChecked type="checkbox" />
            </label>
            <label className="flex items-center justify-between gap-4 rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <span className="text-sm text-neutral-300">Skip duplicate post URLs</span>
              <input className="h-4 w-4 accent-blue-500" defaultChecked type="checkbox" />
            </label>
            <label className="flex items-center justify-between gap-4 rounded-lg border border-white/5 bg-black/30 px-4 py-3">
              <span className="text-sm text-neutral-300">Include captions by default</span>
              <input className="h-4 w-4 accent-blue-500" type="checkbox" />
            </label>
          </div>
        </div>
      </section>
    </div>
  );
}
