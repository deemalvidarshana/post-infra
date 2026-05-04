import React from 'react';

export default function Dashboard() {
  return (
    <div className="flex flex-col">
      <div className="mb-8">
        <h2 className="text-3xl font-bold text-white mb-2">Overview</h2>
        <p className="text-on-surface-variant">System performance and active infrastructure status.</p>
      </div>

      {/* Bento Box Grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        {/* Hero Card (Large) */}
        <div className="md:col-span-2 rounded-xl glass-panel p-6 flex flex-col relative overflow-hidden group">
          <div className="absolute inset-0 bg-gradient-to-br from-primary/5 to-transparent pointer-events-none"></div>
          <div className="flex justify-between items-start mb-8 relative z-10">
            <div>
              <h3 className="text-xl font-bold text-white mb-1">Content Engagement</h3>
              <p className="text-xs text-neutral-500 uppercase tracking-widest">Trailing 7 days activity</p>
            </div>
            <div className="flex gap-2">
              <span className="px-2 py-1 bg-white/5 border border-white/10 rounded text-[10px] text-white tracking-wider uppercase">Live</span>
            </div>
          </div>
          
          <div className="flex-1 relative min-h-[200px] flex items-end z-10">
            {/* Abstract Line Chart Visualization (Simplified SVG) */}
            <div className="w-full h-full relative">
              <div className="absolute inset-0 flex flex-col justify-between pointer-events-none">
                <div className="w-full h-[1px] bg-white/5"></div>
                <div className="w-full h-[1px] bg-white/5"></div>
                <div className="w-full h-[1px] bg-white/5"></div>
                <div className="w-full h-[1px] bg-white/5"></div>
              </div>
              <svg className="absolute inset-0 w-full h-full overflow-visible" preserveAspectRatio="none" viewBox="0 0 100 100">
                <defs>
                  <linearGradient id="lineGrad" x1="0%" x2="100%" y1="0%" y2="0%">
                    <stop offset="0%" stopColor="#8b5cf6" stopOpacity="0.2" />
                    <stop offset="50%" stopColor="#c0c1ff" stopOpacity="1" />
                    <stop offset="100%" stopColor="#ddb7ff" stopOpacity="0.8" />
                  </linearGradient>
                  <filter id="glow" x="-20%" y="-20%" width="140%" height="140%">
                    <feGaussianBlur stdDeviation="3" result="blur" />
                    <feComposite in="SourceGraphic" in2="blur" operator="over" />
                  </filter>
                </defs>
                <path d="M0,80 Q10,70 20,75 T40,60 T60,80 T80,40 T100,20" fill="none" stroke="url(#lineGrad)" strokeWidth="1.5" filter="url(#glow)" vectorEffect="non-scaling-stroke" />
                <path d="M0,80 Q10,70 20,75 T40,60 T60,80 T80,40 T100,20 L100,100 L0,100 Z" fill="url(#lineGrad)" opacity="0.1" />
                <circle cx="80" cy="40" fill="#fff" r="1.5" filter="url(#glow)" />
                <circle cx="100" cy="20" fill="#fff" r="2" filter="url(#glow)" />
              </svg>
            </div>
          </div>
        </div>

        {/* Metric Cards Column */}
        <div className="flex flex-col gap-6">
          {/* AI Generations */}
          <div className="rounded-xl glass-panel p-6 flex flex-col justify-between h-[160px]">
            <div className="flex justify-between items-start mb-4">
              <h3 className="text-[10px] text-neutral-400 uppercase tracking-widest font-bold">AI Generations</h3>
              <span className="material-symbols-outlined text-primary text-sm">auto_awesome</span>
            </div>
            <div>
              <div className="text-3xl font-bold text-white mb-1">1,284</div>
              <div className="flex items-center gap-2">
                <span className="text-emerald-400 text-xs font-bold flex items-center gap-1">
                  <span className="material-symbols-outlined text-[12px]">trending_up</span> 12%
                </span>
                <span className="text-[10px] text-neutral-500">vs last week</span>
              </div>
            </div>
          </div>

          {/* Pending Queues */}
          <div className="rounded-xl glass-panel p-6 flex flex-col justify-between h-[160px]">
            <div className="flex justify-between items-start mb-4">
              <h3 className="text-[10px] text-neutral-400 uppercase tracking-widest font-bold">Pending Queues</h3>
              <span className="material-symbols-outlined text-neutral-500 text-sm">sync</span>
            </div>
            <div>
              <div className="text-3xl font-bold text-white mb-1">0</div>
              <div className="flex items-center gap-2">
                <span className="w-2 h-2 rounded-full bg-emerald-500 shadow-[0_0_8px_rgba(16,185,129,0.5)]"></span>
                <span className="text-[10px] text-neutral-400">All Synced</span>
              </div>
            </div>
          </div>

          {/* Active Platforms */}
          <div className="rounded-xl glass-panel p-6 flex flex-col justify-between h-[160px]">
            <div className="flex justify-between items-start mb-4">
              <h3 className="text-[10px] text-neutral-400 uppercase tracking-widest font-bold">Active Platforms</h3>
              <span className="material-symbols-outlined text-neutral-500 text-sm">hub</span>
            </div>
            <div className="flex items-center justify-between">
              <div className="flex -space-x-2">
                <div className="w-8 h-8 rounded bg-surface-container-high border border-white/10 flex items-center justify-center relative z-30">
                  <span className="text-white font-bold text-[10px]">FB</span>
                </div>
                <div className="w-8 h-8 rounded bg-surface-container-high border border-white/10 flex items-center justify-center relative z-20">
                  <span className="text-white font-bold text-[10px]">IG</span>
                </div>
                <div className="w-8 h-8 rounded bg-surface-container-high border border-white/10 flex items-center justify-center relative z-10">
                  <span className="text-white font-bold text-[10px]">TK</span>
                </div>
              </div>
              <span className="text-[10px] text-neutral-300">3 Connected</span>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
