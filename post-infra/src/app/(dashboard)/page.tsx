"use client";

import React, { useEffect, useMemo, useState } from "react";

interface SystemMetrics {
  capturedAtUtc: string;
  hostname: string;
  operatingSystem: string;
  architecture: string;
  processorCount: number;
  processCount: number;
  uptimeSeconds?: number;
  cpu: {
    usagePercent?: number;
    load1?: number;
    load5?: number;
    load15?: number;
  };
  memory: {
    totalBytes: number;
    usedBytes: number;
    availableBytes: number;
    usagePercent: number;
    swapTotalBytes: number;
    swapUsedBytes: number;
    swapFreeBytes: number;
    swapUsagePercent: number;
  };
  disk: {
    path: string;
    fileSystem?: string;
    totalBytes: number;
    usedBytes: number;
    freeBytes: number;
    usagePercent: number;
  };
  network: {
    receivedBytes: number;
    transmittedBytes: number;
  };
}

type MetricKey = "cpu" | "memory" | "swap" | "disk";

const POLL_INTERVAL_MS = 10_000;
const MAX_SAMPLES = 24;

export default function Dashboard() {
  const [metrics, setMetrics] = useState<SystemMetrics | null>(null);
  const [samples, setSamples] = useState<SystemMetrics[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState("");

  useEffect(() => {
    let isMounted = true;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    const loadMetrics = async () => {
      try {
        const response = await fetch("/api/smapi/SystemMetrics", { cache: "no-store" });
        const data = await response.json();

        if (!response.ok) {
          throw new Error(data?.message || `Metrics request failed with status ${response.status}.`);
        }

        if (!isMounted) {
          return;
        }

        setMetrics(data);
        setSamples((current) => [...current, data].slice(-MAX_SAMPLES));
        setError("");
      } catch (loadError) {
        if (isMounted) {
          setError(loadError instanceof Error ? loadError.message : "Could not load VPS metrics.");
        }
      } finally {
        if (isMounted) {
          setIsLoading(false);
          timeoutId = setTimeout(loadMetrics, POLL_INTERVAL_MS);
        }
      }
    };

    void loadMetrics();

    return () => {
      isMounted = false;
      if (timeoutId) {
        clearTimeout(timeoutId);
      }
    };
  }, []);

  const healthLabel = useMemo(() => {
    if (!metrics) {
      return "Loading";
    }

    const hottestMetric = Math.max(
      metrics.cpu.usagePercent ?? 0,
      metrics.memory.usagePercent,
      metrics.memory.swapUsagePercent,
      metrics.disk.usagePercent
    );

    if (hottestMetric >= 90 || (metrics.cpu.load1 ?? 0) > metrics.processorCount * 1.5) {
      return "Needs attention";
    }

    if (hottestMetric >= 75 || (metrics.cpu.load1 ?? 0) > metrics.processorCount) {
      return "Busy";
    }

    return "Healthy";
  }, [metrics]);

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-col gap-4 lg:flex-row lg:items-end lg:justify-between">
        <div>
          <h2 className="text-3xl font-bold text-white mb-2">VPS Performance</h2>
          <p className="text-on-surface-variant">Live server health, RAM, swap, storage, CPU, and network usage.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <span className={`rounded-full border px-3 py-1 text-[10px] font-bold uppercase tracking-widest ${healthClass(healthLabel)}`}>
            {healthLabel}
          </span>
          <span className="rounded-full border border-white/10 bg-white/5 px-3 py-1 text-[10px] font-bold uppercase tracking-widest text-neutral-300">
            Auto refresh {POLL_INTERVAL_MS / 1000}s
          </span>
        </div>
      </div>

      {error && (
        <div className="rounded-xl border border-red-500/20 bg-red-500/10 px-4 py-3 text-sm text-red-200">
          {error}
        </div>
      )}

      {isLoading && !metrics ? (
        <div className="glass-panel rounded-xl border border-white/5 p-6 text-sm text-neutral-400">
          Loading VPS metrics...
        </div>
      ) : metrics ? (
        <>
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
            <MetricCard
              title="CPU Usage"
              icon="memory"
              value={`${formatPercent(metrics.cpu.usagePercent ?? 0)}`}
              detail={`${metrics.processorCount} vCPU • Load ${formatLoad(metrics.cpu.load1)}`}
              percent={metrics.cpu.usagePercent ?? 0}
              accent="blue"
            />
            <MetricCard
              title="RAM Usage"
              icon="developer_board"
              value={formatPercent(metrics.memory.usagePercent)}
              detail={`${formatBytes(metrics.memory.usedBytes)} / ${formatBytes(metrics.memory.totalBytes)}`}
              percent={metrics.memory.usagePercent}
              accent="emerald"
            />
            <MetricCard
              title="Swap Usage"
              icon="swap_horiz"
              value={formatPercent(metrics.memory.swapUsagePercent)}
              detail={`${formatBytes(metrics.memory.swapUsedBytes)} / ${formatBytes(metrics.memory.swapTotalBytes)}`}
              percent={metrics.memory.swapUsagePercent}
              accent="amber"
            />
            <MetricCard
              title="Storage /"
              icon="storage"
              value={formatPercent(metrics.disk.usagePercent)}
              detail={`${formatBytes(metrics.disk.usedBytes)} / ${formatBytes(metrics.disk.totalBytes)}`}
              percent={metrics.disk.usagePercent}
              accent="purple"
            />
          </div>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-[minmax(0,1.6fr)_minmax(360px,0.9fr)]">
            <section className="glass-panel rounded-xl border border-white/5 p-6">
              <div className="mb-6 flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                <div>
                  <h3 className="text-xl font-bold text-white">Resource Trend</h3>
                  <p className="mt-1 text-xs uppercase tracking-widest text-neutral-500">Last {samples.length} samples</p>
                </div>
                <div className="flex flex-wrap gap-2 text-[10px] uppercase tracking-widest text-neutral-400">
                  <Legend color="bg-blue-400" label="CPU" />
                  <Legend color="bg-emerald-400" label="RAM" />
                  <Legend color="bg-amber-300" label="Swap" />
                  <Legend color="bg-purple-300" label="Disk" />
                </div>
              </div>
              <TrendChart samples={samples} />
            </section>

            <section className="glass-panel rounded-xl border border-white/5 p-6">
              <div className="mb-5 flex items-center justify-between">
                <div>
                  <h3 className="text-xl font-bold text-white">Server Snapshot</h3>
                  <p className="mt-1 text-xs uppercase tracking-widest text-neutral-500">Current VPS status</p>
                </div>
                <span className="material-symbols-outlined text-primary text-lg">monitor_heart</span>
              </div>

              <div className="space-y-3">
                <InfoRow label="Hostname" value={metrics.hostname || "Unknown"} />
                <InfoRow label="OS" value={metrics.operatingSystem || "Unknown"} />
                <InfoRow label="Architecture" value={metrics.architecture || "Unknown"} />
                <InfoRow label="Uptime" value={formatDuration(metrics.uptimeSeconds)} />
                <InfoRow label="Processes" value={String(metrics.processCount)} />
                <InfoRow label="Disk FS" value={metrics.disk.fileSystem || "Unknown"} />
                <InfoRow label="Last Updated" value={formatDateTime(metrics.capturedAtUtc)} />
              </div>
            </section>
          </div>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-3">
            <section className="glass-panel rounded-xl border border-white/5 p-6">
              <h3 className="mb-4 text-sm font-bold uppercase tracking-widest text-neutral-400">Load Average</h3>
              <div className="grid grid-cols-3 gap-3">
                <MiniStat label="1m" value={formatLoad(metrics.cpu.load1)} />
                <MiniStat label="5m" value={formatLoad(metrics.cpu.load5)} />
                <MiniStat label="15m" value={formatLoad(metrics.cpu.load15)} />
              </div>
            </section>

            <section className="glass-panel rounded-xl border border-white/5 p-6">
              <h3 className="mb-4 text-sm font-bold uppercase tracking-widest text-neutral-400">Storage Free</h3>
              <div className="text-3xl font-bold text-white">{formatBytes(metrics.disk.freeBytes)}</div>
              <p className="mt-2 text-xs text-neutral-500">Available from {formatBytes(metrics.disk.totalBytes)} root storage.</p>
            </section>

            <section className="glass-panel rounded-xl border border-white/5 p-6">
              <h3 className="mb-4 text-sm font-bold uppercase tracking-widest text-neutral-400">Network Total</h3>
              <div className="grid grid-cols-2 gap-3">
                <MiniStat label="Received" value={formatBytes(metrics.network.receivedBytes)} />
                <MiniStat label="Sent" value={formatBytes(metrics.network.transmittedBytes)} />
              </div>
            </section>
          </div>
        </>
      ) : null}
    </div>
  );
}

function MetricCard({
  title,
  icon,
  value,
  detail,
  percent,
  accent
}: {
  title: string;
  icon: string;
  value: string;
  detail: string;
  percent: number;
  accent: "blue" | "emerald" | "amber" | "purple";
}) {
  return (
    <section className="glass-panel rounded-xl border border-white/5 p-5">
      <div className="mb-4 flex items-start justify-between">
        <div>
          <h3 className="text-[10px] font-bold uppercase tracking-widest text-neutral-400">{title}</h3>
          <p className="mt-2 text-3xl font-bold text-white">{value}</p>
        </div>
        <span className={`material-symbols-outlined text-lg ${accentTextClass(accent)}`}>{icon}</span>
      </div>
      <div className="mb-2 h-2 overflow-hidden rounded-full bg-white/5">
        <div className={`h-full rounded-full ${accentBgClass(accent)}`} style={{ width: `${clampPercent(percent)}%` }} />
      </div>
      <p className="text-xs text-neutral-500">{detail}</p>
    </section>
  );
}

function TrendChart({ samples }: { samples: SystemMetrics[] }) {
  const series: Array<{ key: MetricKey; color: string; values: number[] }> = [
    { key: "cpu", color: "#60a5fa", values: samples.map((sample) => sample.cpu.usagePercent ?? 0) },
    { key: "memory", color: "#34d399", values: samples.map((sample) => sample.memory.usagePercent) },
    { key: "swap", color: "#fbbf24", values: samples.map((sample) => sample.memory.swapUsagePercent) },
    { key: "disk", color: "#c084fc", values: samples.map((sample) => sample.disk.usagePercent) }
  ];

  return (
    <div className="relative h-[320px] overflow-hidden rounded-lg border border-white/5 bg-black/20">
      <div className="absolute inset-0 flex flex-col justify-between p-5">
        {[100, 75, 50, 25, 0].map((value) => (
          <div key={value} className="flex items-center gap-3">
            <span className="w-8 text-right text-[10px] text-neutral-600">{value}%</span>
            <div className="h-px flex-1 bg-white/5" />
          </div>
        ))}
      </div>
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        {series.map((item) => (
          <polyline
            key={item.key}
            fill="none"
            points={toPolylinePoints(item.values)}
            stroke={item.color}
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth="1.8"
            vectorEffect="non-scaling-stroke"
          />
        ))}
      </svg>
      {samples.length < 2 && (
        <div className="absolute inset-0 flex items-center justify-center text-sm text-neutral-500">
          Collecting trend samples...
        </div>
      )}
    </div>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={`h-2 w-2 rounded-full ${color}`} />
      {label}
    </span>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-start justify-between gap-4 rounded-lg bg-white/[0.03] px-3 py-2">
      <span className="text-xs uppercase tracking-widest text-neutral-500">{label}</span>
      <span className="max-w-[70%] break-words text-right text-sm text-neutral-200">{value}</span>
    </div>
  );
}

function MiniStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-white/5 bg-black/20 p-3">
      <p className="text-[10px] font-bold uppercase tracking-widest text-neutral-500">{label}</p>
      <p className="mt-2 text-lg font-bold text-white">{value}</p>
    </div>
  );
}

function toPolylinePoints(values: number[]) {
  if (values.length === 0) {
    return "";
  }

  if (values.length === 1) {
    const y = 100 - clampPercent(values[0]);
    return `0,${y} 100,${y}`;
  }

  return values.map((value, index) => {
    const x = (index / (values.length - 1)) * 100;
    const y = 100 - clampPercent(value);
    return `${x.toFixed(2)},${y.toFixed(2)}`;
  }).join(" ");
}

function clampPercent(value: number) {
  return Math.max(0, Math.min(100, Number.isFinite(value) ? value : 0));
}

function formatPercent(value: number) {
  return `${clampPercent(value).toFixed(1)}%`;
}

function formatLoad(value?: number) {
  return typeof value === "number" ? value.toFixed(2) : "N/A";
}

function formatBytes(value?: number) {
  if (!value || value <= 0) {
    return "0 B";
  }

  const units = ["B", "KB", "MB", "GB", "TB"];
  let nextValue = value;
  let unitIndex = 0;

  while (nextValue >= 1024 && unitIndex < units.length - 1) {
    nextValue /= 1024;
    unitIndex += 1;
  }

  return `${nextValue >= 10 || unitIndex === 0 ? nextValue.toFixed(0) : nextValue.toFixed(1)} ${units[unitIndex]}`;
}

function formatDuration(seconds?: number) {
  if (!seconds || seconds < 0) {
    return "Unknown";
  }

  const days = Math.floor(seconds / 86400);
  const hours = Math.floor((seconds % 86400) / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);

  if (days > 0) {
    return `${days}d ${hours}h ${minutes}m`;
  }

  if (hours > 0) {
    return `${hours}h ${minutes}m`;
  }

  return `${minutes}m`;
}

function formatDateTime(value: string) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "Unknown" : date.toLocaleString();
}

function healthClass(label: string) {
  if (label === "Healthy") {
    return "border-emerald-400/20 bg-emerald-500/10 text-emerald-200";
  }

  if (label === "Busy") {
    return "border-amber-400/20 bg-amber-500/10 text-amber-200";
  }

  if (label === "Needs attention") {
    return "border-red-400/20 bg-red-500/10 text-red-200";
  }

  return "border-white/10 bg-white/5 text-neutral-300";
}

function accentTextClass(accent: "blue" | "emerald" | "amber" | "purple") {
  switch (accent) {
    case "blue":
      return "text-blue-300";
    case "emerald":
      return "text-emerald-300";
    case "amber":
      return "text-amber-300";
    case "purple":
      return "text-purple-300";
  }
}

function accentBgClass(accent: "blue" | "emerald" | "amber" | "purple") {
  switch (accent) {
    case "blue":
      return "bg-blue-400";
    case "emerald":
      return "bg-emerald-400";
    case "amber":
      return "bg-amber-300";
    case "purple":
      return "bg-purple-300";
  }
}
