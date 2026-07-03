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

type AiProviderId = 'openrouter' | 'groq';

interface AiProviderOptionResponse {
  provider: AiProviderId;
  label: string;
  defaultModel: string;
  model: string;
  hasApiKey: boolean;
  apiKeyLength: number;
  isActive: boolean;
  updatedAt?: string;
}

interface AiProviderSettingsResponse {
  activeProvider: AiProviderId;
  provider: AiProviderId;
  label: string;
  model: string;
  hasApiKey: boolean;
  apiKeyLength: number;
  updatedAt?: string;
  providers: AiProviderOptionResponse[];
}

const normalizeAiProvider = (provider?: string): AiProviderId =>
  provider === 'openrouter' ? 'openrouter' : 'groq';

const getDefaultAiModel = (provider: AiProviderId) =>
  provider === 'groq' ? 'llama-3.1-8b-instant' : 'openrouter/free';

const getProviderLabel = (provider: AiProviderId) =>
  provider === 'groq' ? 'Groq' : 'OpenRouter';

const getProviderApiKeyPlaceholder = (provider: AiProviderId) =>
  provider === 'groq' ? 'gsk_...' : 'sk-or-v1-...';

export default function SettingsPage() {
  const [message, setMessage] = useState('');
  const [isSavingApify, setIsSavingApify] = useState(false);
  const [isSavingAiProvider, setIsSavingAiProvider] = useState(false);
  const [downloadFolder, setDownloadFolder] = useState('');
  const [publicBaseUrl, setPublicBaseUrl] = useState('');
  const [apifyToken, setApifyToken] = useState('');
  const [hasSavedApifyToken, setHasSavedApifyToken] = useState(false);
  const [apifyTokenLength, setApifyTokenLength] = useState(0);
  const [apifyUpdatedAt, setApifyUpdatedAt] = useState('');
  const [aiProvider, setAiProvider] = useState<AiProviderId>('groq');
  const [aiProviderOptions, setAiProviderOptions] = useState<AiProviderOptionResponse[]>([]);
  const [aiModel, setAiModel] = useState(getDefaultAiModel('groq'));
  const [aiApiKey, setAiApiKey] = useState('');
  const [hasSavedAiApiKey, setHasSavedAiApiKey] = useState(false);
  const [aiApiKeyLength, setAiApiKeyLength] = useState(0);
  const [aiUpdatedAt, setAiUpdatedAt] = useState('');
  const hasLoadedStoredUser = useRef(false);

  const activeProviderLabel = getProviderLabel(aiProvider);

  const loadSettings = useCallback(async () => {
    setMessage('');

    try {
      const [localResponse, apifyResponse, aiProviderResponse] = await Promise.all([
        fetch('/api/smapi/Settings/s3/global'),
        fetch('/api/smapi/Settings/apify'),
        fetch('/api/smapi/Settings/ai')
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

      if (aiProviderResponse.status === 404) {
        setAiProvider('groq');
        setAiProviderOptions([]);
        setAiModel(getDefaultAiModel('groq'));
        setAiApiKey('');
        setHasSavedAiApiKey(false);
        setAiApiKeyLength(0);
        setAiUpdatedAt('');
      } else if (aiProviderResponse.ok) {
        const aiData = await aiProviderResponse.json() as AiProviderSettingsResponse;
        const selectedProvider = normalizeAiProvider(aiData.provider || aiData.activeProvider);
        const providers = (aiData.providers || []).map((provider) => ({
          ...provider,
          provider: normalizeAiProvider(provider.provider)
        }));
        const selectedOption = providers.find((provider) => provider.provider === selectedProvider);
        setAiProvider(selectedProvider);
        setAiProviderOptions(providers);
        setAiModel(aiData.model || selectedOption?.model || getDefaultAiModel(selectedProvider));
        setAiApiKey('');
        setHasSavedAiApiKey(aiData.hasApiKey);
        setAiApiKeyLength(aiData.apiKeyLength || 0);
        setAiUpdatedAt(aiData.updatedAt || '');
      } else {
        setMessage('Local and Apify settings loaded, but AI provider settings could not be loaded.');
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

  const handleAiProviderChange = (nextProvider: AiProviderId) => {
    const nextOption = aiProviderOptions.find((provider) => provider.provider === nextProvider);
    setAiProvider(nextProvider);
    setAiModel(nextOption?.model || getDefaultAiModel(nextProvider));
    setAiApiKey('');
    setHasSavedAiApiKey(nextOption?.hasApiKey ?? false);
    setAiApiKeyLength(nextOption?.apiKeyLength || 0);
    setAiUpdatedAt(nextOption?.updatedAt || '');
  };

  const saveAiProviderSettings = async (event: React.FormEvent) => {
    event.preventDefault();
    setMessage('');

    if (!aiModel.trim()) {
      setMessage(`Enter a ${activeProviderLabel} model before saving.`);
      return;
    }

    if (!aiApiKey.trim() && !hasSavedAiApiKey) {
      setMessage(`Enter a ${activeProviderLabel} API key before saving.`);
      return;
    }

    setIsSavingAiProvider(true);
    try {
      const response = await fetch('/api/smapi/Settings/ai', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          provider: aiProvider,
          model: aiModel.trim(),
          apiKey: aiApiKey.trim() || undefined
        })
      });

      const data = await response.json();
      if (!response.ok || !data.success) {
        setMessage(data.message || `Failed to save ${activeProviderLabel} settings. Backend status ${response.status}.`);
        return;
      }

      const savedSettings = data.settings as AiProviderSettingsResponse | undefined;
      const savedProvider = normalizeAiProvider(savedSettings?.provider || aiProvider);
      const providers = (savedSettings?.providers || aiProviderOptions).map((provider) => ({
        ...provider,
        provider: normalizeAiProvider(provider.provider)
      }));
      setAiProvider(savedProvider);
      setAiProviderOptions(providers);
      setAiModel(savedSettings?.model || aiModel.trim());
      setAiApiKey('');
      setHasSavedAiApiKey(savedSettings?.hasApiKey ?? true);
      setAiApiKeyLength(savedSettings?.apiKeyLength || aiApiKey.trim().length);
      setAiUpdatedAt(savedSettings?.updatedAt || new Date().toISOString());
      setMessage(`${getProviderLabel(savedProvider)} settings saved and selected for AI replies.`);
    } catch {
      setMessage('Could not connect to the backend server.');
    } finally {
      setIsSavingAiProvider(false);
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
        <div className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
          <div>
            <h2 className="text-lg font-bold text-white">Backup & Migration</h2>
            <p className="text-sm text-neutral-500 mt-1">
              Download a portable backup before moving this project to another VPS.
            </p>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <div className="rounded-xl border border-white/5 bg-black/30 p-5 space-y-4">
              <div>
                <h3 className="font-bold text-white">DB Only Backup</h3>
                <p className="text-sm text-neutral-500 mt-1">
                  Saves pages, Meta apps, queue jobs, comments, AI settings, API keys, and tokens. Videos are not included.
                </p>
              </div>
              <a
                href="/api/backups/download?type=db-only"
                className="inline-flex w-full items-center justify-center gap-3 rounded-lg bg-blue-600 px-4 py-4 text-sm font-bold text-white shadow-lg shadow-blue-600/20 transition-all hover:bg-blue-500"
              >
                <span className="material-symbols-outlined text-sm">database</span>
                Download DB Only
              </a>
            </div>

            <div className="rounded-xl border border-emerald-500/10 bg-emerald-500/5 p-5 space-y-4">
              <div>
                <h3 className="font-bold text-white">Full Backup</h3>
                <p className="text-sm text-neutral-500 mt-1">
                  Saves the database plus locally downloaded videos. Use this if you need the new VPS to have the same files immediately.
                </p>
              </div>
              <a
                href="/api/backups/download?type=full"
                className="inline-flex w-full items-center justify-center gap-3 rounded-lg bg-emerald-600 px-4 py-4 text-sm font-bold text-white shadow-lg shadow-emerald-600/20 transition-all hover:bg-emerald-500"
              >
                <span className="material-symbols-outlined text-sm">archive</span>
                Download Full Backup
              </a>
            </div>
          </div>

          <div className="rounded-lg border border-amber-500/10 bg-amber-500/5 px-4 py-4 text-sm text-amber-100">
            Keep backup files private. They can contain Facebook page access tokens, Meta app secrets, AI provider keys, and Apify keys.
            DB-only is usually enough when source video links can be downloaded again during publish time.
          </div>
        </div>
      </section>

      <section>
        <form onSubmit={saveAiProviderSettings} className="glass-panel rounded-xl border border-white/5 p-6 space-y-5">
          <div>
            <h2 className="text-lg font-bold text-white">AI Provider</h2>
            <p className="text-sm text-neutral-500 mt-1">Choose Groq or OpenRouter for AI captions and Facebook auto-replies.</p>
          </div>

          <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
            <label className="space-y-1 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Provider</span>
              <select
                value={aiProvider}
                onChange={(event) => handleAiProviderChange(normalizeAiProvider(event.target.value))}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
              >
                <option value="groq">Groq</option>
                <option value="openrouter">OpenRouter</option>
              </select>
            </label>

            <label className="space-y-1 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Model</span>
              <input
                required
                type="text"
                value={aiModel}
                onChange={(event) => setAiModel(event.target.value)}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                placeholder={getDefaultAiModel(aiProvider)}
                autoComplete="off"
                spellCheck={false}
              />
            </label>

            <label className="space-y-1 block">
              <span className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">API Key</span>
              <input
                required={!hasSavedAiApiKey}
                type="text"
                value={aiApiKey}
                onChange={(event) => setAiApiKey(event.target.value)}
                className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                placeholder={hasSavedAiApiKey ? 'Leave blank to keep saved key' : getProviderApiKeyPlaceholder(aiProvider)}
                autoComplete="off"
                spellCheck={false}
              />
            </label>
          </div>

          <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
            {hasSavedAiApiKey ? (
              <div className="space-y-1">
                <p className="font-semibold text-emerald-200">{activeProviderLabel} is saved and selected for AI replies.</p>
                <p className="text-xs text-neutral-500">
                  Model: {aiModel || '-'} | Key length: {aiApiKeyLength || '-'} characters
                  {aiUpdatedAt ? ` | Updated: ${new Date(aiUpdatedAt).toLocaleString()}` : ''}
                </p>
              </div>
            ) : (
              `No ${activeProviderLabel} API key saved yet.`
            )}
          </div>

          <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
            {(['groq', 'openrouter'] as AiProviderId[]).map((provider) => {
              const option = aiProviderOptions.find((item) => item.provider === provider);
              return (
                <div
                  key={provider}
                  className={`rounded-lg border px-4 py-3 text-xs ${
                    provider === aiProvider
                      ? 'border-emerald-500/30 bg-emerald-500/10 text-emerald-100'
                      : 'border-white/5 bg-black/20 text-neutral-400'
                  }`}
                >
                  <p className="font-semibold text-sm">{getProviderLabel(provider)}</p>
                  <p>Model: {option?.model || getDefaultAiModel(provider)}</p>
                  <p>{option?.hasApiKey ? `Saved key: ${option.apiKeyLength} characters` : 'No saved key'}</p>
                </div>
              );
            })}
          </div>

          <button
            type="submit"
            disabled={isSavingAiProvider}
            className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white rounded-lg font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
          >
            {isSavingAiProvider ? (
              <>
                <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                Saving...
              </>
            ) : (
              <>
                <span className="material-symbols-outlined text-sm">key</span>
                Save {activeProviderLabel} Settings
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
