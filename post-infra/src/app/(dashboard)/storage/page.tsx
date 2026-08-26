"use client";

import React, { useCallback, useEffect, useMemo, useState } from "react";

interface StorageEntry {
  name: string;
  relativePath: string;
  kind: "folder" | "file";
  sizeBytes: number;
  modifiedAtUtc: string;
  childCount?: number | null;
}

interface StorageBrowserResponse {
  rootName: string;
  currentPath: string;
  parentPath?: string | null;
  sizeBytes: number;
  entries: StorageEntry[];
}

export default function StorageManagerPage() {
  const [currentPath, setCurrentPath] = useState("");
  const [browserData, setBrowserData] = useState<StorageBrowserResponse | null>(null);
  const [isLoading, setIsLoading] = useState(false);
  const [deletingPath, setDeletingPath] = useState("");
  const [message, setMessage] = useState("");

  const loadStorage = useCallback(async (nextPath = currentPath) => {
    setIsLoading(true);
    setMessage("");

    try {
      const query = nextPath ? `?path=${encodeURIComponent(nextPath)}` : "";
      const response = await fetch(`/api/smapi/StorageBrowser${query}`);
      const data = await response.json();

      if (!response.ok) {
        setMessage(data?.message || `Could not load storage. Backend status ${response.status}.`);
        return;
      }

      setBrowserData(data as StorageBrowserResponse);
      setCurrentPath((data as StorageBrowserResponse).currentPath || "");
    } catch {
      setMessage("Could not connect to the backend storage browser.");
    } finally {
      setIsLoading(false);
    }
  }, [currentPath]);

  useEffect(() => {
    void loadStorage("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const breadcrumbs = useMemo(() => {
    const parts = currentPath.split("/").filter(Boolean);
    const items = [{ name: browserData?.rootName || "downloads", path: "" }];

    let path = "";
    for (const part of parts) {
      path = path ? `${path}/${part}` : part;
      items.push({ name: part, path });
    }

    return items;
  }, [browserData?.rootName, currentPath]);

  const deleteEntry = async (entry: StorageEntry) => {
    const label = entry.kind === "folder"
      ? `folder "${entry.name}" and everything inside it`
      : `file "${entry.name}"`;
    const confirmed = window.confirm(`Delete ${label} permanently from VPS storage? Database records will stay, but local video files will be removed.`);

    if (!confirmed) {
      return;
    }

    setDeletingPath(entry.relativePath);
    setMessage("");

    try {
      const response = await fetch(`/api/smapi/StorageBrowser/entry?path=${encodeURIComponent(entry.relativePath)}`, {
        method: "DELETE"
      });
      const data = await response.json();

      if (!response.ok || !data?.success) {
        setMessage(data?.message || `Delete failed. Backend status ${response.status}.`);
        return;
      }

      setMessage(data.message || "Storage item deleted.");
      await loadStorage(currentPath);
    } catch {
      setMessage("Could not connect to the backend storage browser.");
    } finally {
      setDeletingPath("");
    }
  };

  const entries = browserData?.entries || [];
  const folders = entries.filter((entry) => entry.kind === "folder").length;
  const files = entries.filter((entry) => entry.kind === "file").length;

  return (
    <div className="p-8 space-y-8 animate-in fade-in duration-700">
      <div className="flex flex-col gap-4 xl:flex-row xl:items-end xl:justify-between">
        <div>
          <h1 className="text-3xl font-bold text-white tracking-tight">Storage Manager</h1>
          <p className="text-neutral-500 text-sm mt-1">
            Browse local VPS video folders, check GB usage by client/page, and delete files or folders when needed.
          </p>
        </div>
        <button
          type="button"
          onClick={() => loadStorage(currentPath)}
          disabled={isLoading}
          className="inline-flex items-center justify-center gap-2 rounded-lg bg-white px-4 py-3 text-sm font-bold text-black hover:bg-neutral-200 disabled:opacity-50"
        >
          <span className={`material-symbols-outlined text-sm ${isLoading ? "animate-spin" : ""}`}>sync</span>
          {isLoading ? "Refreshing" : "Refresh"}
        </button>
      </div>

      {message && (
        <div className="rounded-lg border border-white/5 bg-black/30 px-4 py-3 text-sm text-neutral-300">
          {message}
        </div>
      )}

      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <MetricCard title="Current Folder" value={formatStorage(browserData?.sizeBytes ?? 0)} icon="database" />
        <MetricCard title="Folders" value={String(folders)} icon="folder" />
        <MetricCard title="Files" value={String(files)} icon="movie" />
      </div>

      <div className="glass-panel rounded-xl border border-white/5 overflow-hidden">
        <div className="border-b border-white/5 p-5 space-y-4">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            {breadcrumbs.map((breadcrumb, index) => (
              <React.Fragment key={breadcrumb.path || "root"}>
                {index > 0 && <span className="text-neutral-600">/</span>}
                <button
                  type="button"
                  onClick={() => loadStorage(breadcrumb.path)}
                  className={`rounded px-2 py-1 transition-colors ${
                    index === breadcrumbs.length - 1
                      ? "bg-blue-500/10 text-blue-200"
                      : "text-neutral-400 hover:bg-white/5 hover:text-white"
                  }`}
                >
                  {breadcrumb.name}
                </button>
              </React.Fragment>
            ))}
          </div>

          <div className="flex flex-wrap items-center gap-3 text-xs text-neutral-500">
            <span className="inline-flex items-center gap-1">
              <span className="material-symbols-outlined text-sm">shield_lock</span>
              Delete is locked to the configured downloads folder only.
            </span>
            {browserData?.parentPath !== null && browserData?.parentPath !== undefined && (
              <button
                type="button"
                onClick={() => loadStorage(browserData.parentPath || "")}
                className="inline-flex items-center gap-1 rounded border border-white/10 px-3 py-1.5 text-neutral-300 hover:bg-white/5 hover:text-white"
              >
                <span className="material-symbols-outlined text-sm">arrow_upward</span>
                Up one folder
              </button>
            )}
          </div>
        </div>

        <div className="overflow-x-auto">
          <table className="w-full min-w-[760px] text-left text-sm">
            <thead className="bg-black/30 text-[10px] uppercase tracking-widest text-neutral-500">
              <tr>
                <th className="px-5 py-3">Name</th>
                <th className="px-5 py-3">Type</th>
                <th className="px-5 py-3">Storage Used</th>
                <th className="px-5 py-3">Modified</th>
                <th className="px-5 py-3 text-right">Action</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-white/5">
              {entries.map((entry) => (
                <tr key={entry.relativePath} className="hover:bg-white/[0.02]">
                  <td className="px-5 py-4">
                    {entry.kind === "folder" ? (
                      <button
                        type="button"
                        onClick={() => loadStorage(entry.relativePath)}
                        className="inline-flex min-w-0 items-center gap-3 text-left text-white hover:text-blue-200"
                      >
                        <span className="material-symbols-outlined text-blue-300">folder</span>
                        <span>
                          <span className="block max-w-[360px] truncate font-bold">{entry.name}</span>
                          <span className="block max-w-[360px] truncate text-xs text-neutral-600">{entry.relativePath}</span>
                        </span>
                      </button>
                    ) : (
                      <div className="inline-flex min-w-0 items-center gap-3 text-white">
                        <span className="material-symbols-outlined text-emerald-300">movie</span>
                        <span>
                          <span className="block max-w-[360px] truncate font-bold">{entry.name}</span>
                          <span className="block max-w-[360px] truncate text-xs text-neutral-600">{entry.relativePath}</span>
                        </span>
                      </div>
                    )}
                  </td>
                  <td className="px-5 py-4">
                    <span className={`rounded-full px-3 py-1 text-[10px] font-bold uppercase tracking-widest ${
                      entry.kind === "folder"
                        ? "border border-blue-500/20 bg-blue-500/10 text-blue-200"
                        : "border border-emerald-500/20 bg-emerald-500/10 text-emerald-200"
                    }`}>
                      {entry.kind}
                    </span>
                  </td>
                  <td className="px-5 py-4 font-bold text-amber-100">{formatStorage(entry.sizeBytes)}</td>
                  <td className="px-5 py-4 text-neutral-400">{formatDate(entry.modifiedAtUtc)}</td>
                  <td className="px-5 py-4 text-right">
                    <button
                      type="button"
                      onClick={() => deleteEntry(entry)}
                      disabled={deletingPath === entry.relativePath}
                      className="inline-flex items-center gap-2 rounded border border-red-500/20 bg-red-500/10 px-3 py-2 text-xs font-bold text-red-300 hover:bg-red-500/20 disabled:opacity-50"
                    >
                      <span className="material-symbols-outlined text-sm">delete</span>
                      {deletingPath === entry.relativePath ? "Deleting" : "Delete"}
                    </button>
                  </td>
                </tr>
              ))}

              {!isLoading && entries.length === 0 && (
                <tr>
                  <td colSpan={5} className="px-5 py-16 text-center text-sm text-neutral-500">
                    This folder is empty.
                  </td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function MetricCard({ title, value, icon }: { title: string; value: string; icon: string }) {
  return (
    <div className="glass-panel rounded-xl border border-white/5 p-6">
      <div className="mb-4 flex items-center justify-between">
        <p className="text-[10px] font-bold uppercase tracking-widest text-neutral-500">{title}</p>
        <span className="material-symbols-outlined text-neutral-500">{icon}</span>
      </div>
      <p className="text-3xl font-bold text-white">{value}</p>
    </div>
  );
}

function formatStorage(bytes: number) {
  if (!Number.isFinite(bytes) || bytes <= 0) {
    return "0 GB";
  }

  const gb = bytes / 1024 / 1024 / 1024;
  if (gb >= 0.01) {
    return `${gb >= 10 ? gb.toFixed(1) : gb.toFixed(2)} GB`;
  }

  const mb = bytes / 1024 / 1024;
  return `${Math.max(mb, 0.01).toFixed(2)} MB`;
}

function formatDate(value: string) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "-";
  }

  return date.toLocaleString();
}
