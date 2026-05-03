import React from 'react';

const tableData = [
  {
    title: 'Q3 Infrastructure Update',
    id: '#INF-8821-A',
    timestamp: 'Oct 24, 2023',
    time: '14:30:00 UTC',
    platforms: ['X', 'IN', 'YT'],
    views: '124.5k',
    engagement: '8.2%',
    image: 'https://lh3.googleusercontent.com/aida-public/AB6AXuC23lOmYv-i_LmODuOuVvVrV3qyNSBn35L8lxFboDdZFKKUR-rhFqhah_bC3wriRT5j2gZswoTa4jdnG1V2QztgzsxO4QXQVrlRT-AxmjQdApTgSn-Ahfy0HoXm-UKu7srdJCA4ZyrGb4D-dLew3VX93Zy6rMVq6Gek40f-n-DZ04b_6hIBmIwJJjt6kynKD0N1UvNvB7i2OqgsKpOHn1RK4jvHErYfKHKjADpWz9Y54OpGtDYMmzT83chKkTEak0sAa3MZ3K2Lmpiv'
  },
  {
    title: 'Deploy Sequence Alpha',
    id: '#DEP-0912-B',
    timestamp: 'Oct 22, 2023',
    time: '09:15:00 UTC',
    platforms: ['GH', 'X'],
    views: '89.2k',
    engagement: '4.1%',
    image: 'https://lh3.googleusercontent.com/aida-public/AB6AXuBohnwWJYQFrq13vvVc5ZU-Z6D-ayvulkq1z4rUe8i0n2VtaaMNSPJjIMvosXeISDVu8QrLWMy7rPpWcxytNzm_AjFjxCqcD2jz1Pbp-94G7TXxgqtJCAYYMk8cPUPf-IIt8c2oQsoNp14sYpo7uERmr8DUa4-3h0LKqZKIcb44jk4XpivZ2vAISF3f3vNiAfFaXJYPM4uYwbZJMWSM9s3iuecjS0a5POqKN2EmbIPuD2wJvrSoJJcLvWlQ2SzlonXOz5OuOT8cfE_g'
  },
  {
    title: 'Security Patch Release Notes',
    id: '#DOC-1104-X',
    timestamp: 'Oct 18, 2023',
    time: '18:00:00 UTC',
    platforms: ['BG', 'IN'],
    views: '45.0k',
    engagement: '12.4%',
    icon: 'article'
  }
];

export default function InsightsPage() {
  return (
    <div className="flex flex-col gap-6">
      {/* Page Header & Filters */}
      <div className="flex flex-col md:flex-row md:items-end justify-between gap-4 mb-2">
        <div>
          <h1 className="text-3xl font-bold text-white tracking-tight">Insights</h1>
          <p className="text-on-surface-variant mt-1">Performance analysis across all distributed content.</p>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <button className="glass-panel flex items-center gap-2 px-4 py-2 rounded-full text-xs font-bold text-white hover:bg-white/5 transition-colors">
            <span className="material-symbols-outlined text-sm text-neutral-500">calendar_today</span>
            Last 7 Days
            <span className="material-symbols-outlined text-sm text-neutral-500">keyboard_arrow_down</span>
          </button>
          <button className="glass-panel flex items-center gap-2 px-4 py-2 rounded-full text-xs font-bold text-white hover:bg-white/5 transition-colors">
            <span className="material-symbols-outlined text-sm text-neutral-500">filter_list</span>
            All Platforms
            <span className="material-symbols-outlined text-sm text-neutral-500">keyboard_arrow_down</span>
          </button>
          <button className="flex items-center gap-2 px-4 py-2 rounded-full text-xs font-bold text-primary bg-primary/10 hover:bg-primary/20 border border-primary/20 transition-colors">
            <span className="material-symbols-outlined text-sm">download</span>
            Export
          </button>
        </div>
      </div>

      {/* Main Data Table Container */}
      <div className="glass-panel rounded-xl overflow-hidden flex flex-col relative">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse">
            <thead className="sticky top-0 bg-neutral-950/90 backdrop-blur-xl border-b border-white/10 z-10">
              <tr>
                <th className="py-4 px-6 text-[10px] uppercase tracking-widest font-bold text-neutral-500 whitespace-nowrap">Content Snippet</th>
                <th className="py-4 px-6 text-[10px] uppercase tracking-widest font-bold text-neutral-500 whitespace-nowrap">Auto-Publish Timestamp</th>
                <th className="py-4 px-6 text-[10px] uppercase tracking-widest font-bold text-neutral-500 whitespace-nowrap">Platforms</th>
                <th className="py-4 px-6 text-[10px] uppercase tracking-widest font-bold text-neutral-500 whitespace-nowrap text-right">Performance Metrics</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-white/5">
              {tableData.map((row, idx) => (
                <tr key={idx} className="hover:bg-white/[0.03] transition-colors group cursor-pointer">
                  <td className="py-4 px-6">
                    <div className="flex items-center gap-4">
                      <div className="w-12 h-12 rounded bg-surface-container overflow-hidden border border-white/10 shrink-0 relative">
                        {row.image ? (
                          <img className="w-full h-full object-cover" src={row.image} alt={row.title} />
                        ) : (
                          <div className="w-full h-full flex items-center justify-center">
                            <span className="material-symbols-outlined text-neutral-500">{row.icon}</span>
                          </div>
                        )}
                        <div className="absolute inset-0 flex items-center justify-center bg-black/40 opacity-0 group-hover:opacity-100 transition-opacity">
                          <span className="material-symbols-outlined text-white">play_arrow</span>
                        </div>
                      </div>
                      <div className="flex flex-col min-w-[200px]">
                        <span className="text-sm font-bold text-white truncate">{row.title}</span>
                        <span className="font-code text-[11px] text-neutral-500 truncate mt-1">ID: {row.id}</span>
                      </div>
                    </div>
                  </td>
                  <td className="py-4 px-6">
                    <div className="flex flex-col">
                      <span className="text-sm text-white">{row.timestamp}</span>
                      <span className="text-neutral-500 font-code text-xs mt-1">{row.time}</span>
                    </div>
                  </td>
                  <td className="py-4 px-6">
                    <div className="flex gap-1">
                      {row.platforms.map(p => (
                        <span key={p} className="px-2 py-1 rounded bg-surface-container border border-white/10 text-[10px] font-bold text-neutral-400 uppercase tracking-wider">{p}</span>
                      ))}
                    </div>
                  </td>
                  <td className="py-4 px-6 text-right">
                    <div className="flex items-center justify-end gap-8">
                      <div className="flex flex-col items-end">
                        <div className="flex items-center gap-2">
                          <span className="text-lg font-bold text-primary tracking-tight">{row.views}</span>
                          <div className="w-12 h-4 border-b-2 border-primary relative">
                            <div className="absolute right-0 bottom-[-3px] w-1 h-1 bg-primary rounded-full shadow-[0_0_4px_#c0c1ff]"></div>
                          </div>
                        </div>
                        <span className="text-[10px] uppercase font-bold text-neutral-500 mt-1">Views</span>
                      </div>
                      <div className="flex flex-col items-end w-24">
                        <div className="flex items-center gap-2">
                          <span className="text-lg font-bold text-secondary tracking-tight">{row.engagement}</span>
                          <div className="w-8 h-4 border-b-2 border-secondary relative">
                            <div className="absolute right-0 bottom-[-3px] w-1 h-1 bg-secondary rounded-full shadow-[0_0_4px_#ddb7ff]"></div>
                          </div>
                        </div>
                        <span className="text-[10px] uppercase font-bold text-neutral-500 mt-1">Engagement</span>
                      </div>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        
        {/* Pagination Footer */}
        <div className="border-t border-white/10 p-4 flex items-center justify-between bg-neutral-950/50">
          <span className="text-[10px] font-bold text-neutral-500 uppercase tracking-widest">Showing 1-3 of 42 entries</span>
          <div className="flex items-center gap-1">
            <button className="w-8 h-8 flex items-center justify-center rounded border border-white/10 text-neutral-500 hover:text-white hover:bg-white/5 transition-colors disabled:opacity-50" disabled>
              <span className="material-symbols-outlined text-sm">chevron_left</span>
            </button>
            <button className="w-8 h-8 flex items-center justify-center rounded border border-white/10 text-white bg-white/5 font-code text-xs">1</button>
            <button className="w-8 h-8 flex items-center justify-center rounded text-neutral-500 hover:text-white hover:bg-white/5 transition-colors font-code text-xs">2</button>
            <button className="w-8 h-8 flex items-center justify-center rounded text-neutral-500 hover:text-white hover:bg-white/5 transition-colors font-code text-xs">3</button>
            <span className="text-neutral-500 px-1">...</span>
            <button className="w-8 h-8 flex items-center justify-center rounded border border-white/10 text-neutral-500 hover:text-white hover:bg-white/5 transition-colors">
              <span className="material-symbols-outlined text-sm">chevron_right</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
