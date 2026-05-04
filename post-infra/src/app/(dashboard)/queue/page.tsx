import React from 'react';

const queueItems = [
  {
    status: 'Generating',
    statusColor: 'secondary',
    title: 'AI Feature Demo - Generative Nodes',
    description: 'Sneak peek at the new generative node system mapping architecture. Render engine is currently assembling the 4k video asset.',
    time: 'Now',
    progress: 65,
    tag: 'Product',
    platforms: ['language', 'work'],
    image: 'https://lh3.googleusercontent.com/aida-public/AB6AXuDOClpZt_zCkF_PJZbgpzwT0SQ5xzDCAm66rKJGDRj-li7FtsRSGpJsJawz78UGM9qE5NjXgYtShMJ6LR5K7xLk0EK5fETEImXYqcfCOLxDRqBS0g8MzBQ57h78aycRYdfLY4Hxoc0hBc9zxavEwqbW1UZbISbjqp2CrWaKad5FexgLtZmwwf62Uo8kjZ3B3VmydeGytIV4xyxEyXNbu_vEYApz9RIspL304afLrBYuMJ8WWYC53mD8Z5GgTgJcvL8lUcQFPx49ODsU',
    active: true
  },
  {
    status: 'Ready',
    statusColor: 'emerald',
    title: 'Q3 Infrastructure Report Highlights',
    description: 'Key metrics and uptime statistics for the previous quarter. Includes latency improvements across core regions.',
    time: 'Today, 4:00 PM',
    tag: 'Corporate',
    platforms: ['work'],
    image: 'https://lh3.googleusercontent.com/aida-public/AB6AXuCEKcuE7R_60EaSMpztheLpFqwPMnk1sONRxRb_doSHNbTIhJwCCjpZnA8gisE7Mt1Cpm-95aQOqfn-UFaWBAKpWcjTuBygwoys7VQxSoeGG1oMW-EcdcHDiCa2-Ecfb72hwQEX54RhabNakog62Saew8V1awBYqwkT44_X9uafwRbBJWJVl1yfbx_E7J4Osb_lrhcJhD27sybR-oCYe3dXsa8IBfTpL3ZKNuLYHvfZ8D9ZtkU7kxvNchmlszfolnRPsf8JTieBVvTf',
    active: false
  },
  {
    status: 'Scheduled',
    statusColor: 'zinc',
    title: 'Community Update - V0.4.3 Beta',
    description: 'Changelog distribution detailing the new queuing mechanics and API rate limit increases for beta testers.',
    time: 'Tomorrow, 9AM',
    tag: 'Community',
    platforms: ['language', 'forum'],
    icon: 'campaign',
    active: false,
    opacity: 'opacity-80'
  }
];

export default function QueuePage() {
  return (
    <div className="flex flex-col gap-6 max-w-5xl mx-auto">
      <header className="mb-10">
        <h1 className="text-3xl font-bold text-white mb-1">Auto-Publishing Queue</h1>
        <p className="text-neutral-400 max-w-2xl">
          Review and manage your automated content pipeline. Real-time generation and syndication status across connected channels.
        </p>
      </header>

      {/* Timeline Layout Container */}
      <div className="relative pl-8 md:pl-0">
        {/* Central Timeline Line */}
        <div className="absolute left-[15px] md:left-[120px] top-4 bottom-0 w-[1px] bg-white/10 hidden sm:block"></div>
        
        <div className="space-y-12">
          {queueItems.map((item, idx) => (
            <div key={idx} className={`relative flex flex-col sm:flex-row gap-6 sm:gap-16 group ${item.opacity || ''}`}>
              {/* Time Marker & Node */}
              <div className="sm:w-[120px] flex sm:justify-end items-start pt-4 relative shrink-0">
                {/* Node */}
                <div className={`absolute left-[-29px] sm:left-auto sm:right-[-5px] top-[22px] w-2.5 h-2.5 rounded-full z-10 border ${
                  item.statusColor === 'secondary' ? 'bg-secondary shadow-[0_0_12px_rgba(111,0,190,0.6)] animate-pulse border-secondary' :
                  item.statusColor === 'emerald' ? 'bg-emerald-500 shadow-[0_0_12px_rgba(16,185,129,0.4)] border-emerald-400' :
                  'bg-zinc-700 border-zinc-600'
                }`}></div>
                <span className={`text-[10px] uppercase font-bold tracking-widest ${item.active ? 'text-secondary' : 'text-neutral-500'}`}>
                  {item.time}
                </span>
              </div>

              {/* Card */}
              <div className="flex-1 relative">
                <div className="glass-panel rounded-xl p-6 transition-all duration-300 hover:border-white/20 hover:bg-white/[0.05]">
                  <div className="flex items-start justify-between mb-4">
                    <div className="flex items-center gap-2">
                      <div className={`px-2 py-0.5 rounded text-[10px] font-bold uppercase tracking-widest border ${
                        item.statusColor === 'secondary' ? 'bg-secondary/10 text-secondary border-secondary/20' :
                        item.statusColor === 'emerald' ? 'bg-emerald-500/10 text-emerald-400 border-emerald-500/20' :
                        'bg-zinc-800 text-zinc-400 border-white/5'
                      }`}>
                        {item.status}
                      </div>
                    </div>
                    {/* Hover Actions */}
                    <div className="opacity-0 group-hover:opacity-100 transition-opacity flex items-center gap-1">
                      <button className="w-8 h-8 rounded hover:bg-white/10 flex items-center justify-center text-neutral-500 hover:text-white transition-colors">
                        <span className="material-symbols-outlined text-sm">{item.status === 'Generating' ? 'pause' : 'play_arrow'}</span>
                      </button>
                      <button className="w-8 h-8 rounded hover:bg-white/10 flex items-center justify-center text-neutral-500 hover:text-white transition-colors">
                        <span className="material-symbols-outlined text-sm">{item.status === 'Generating' ? 'skip_next' : 'edit'}</span>
                      </button>
                    </div>
                  </div>

                  <div className="flex gap-6">
                    {item.image ? (
                      <img src={item.image} alt={item.title} className="w-16 h-16 rounded-lg object-cover border border-white/10 shrink-0" />
                    ) : (
                      <div className="w-16 h-16 rounded-lg bg-zinc-800 border border-white/10 shrink-0 flex items-center justify-center">
                        <span className="material-symbols-outlined text-neutral-500">{item.icon}</span>
                      </div>
                    )}
                    <div>
                      <h3 className="text-lg font-bold text-white mb-1">{item.title}</h3>
                      <p className="text-sm text-neutral-400 line-clamp-2 mb-4">
                        {item.description}
                      </p>
                      <div className="flex items-center gap-3">
                        <div className="flex items-center gap-1">
                          <span className="material-symbols-outlined text-[14px] text-neutral-600">tag</span>
                          <span className="text-[10px] font-bold text-neutral-500 uppercase tracking-widest">{item.tag}</span>
                        </div>
                        <span className="w-1 h-1 rounded-full bg-neutral-700"></span>
                        <div className="flex gap-2">
                          {item.platforms.map((p, idx) => (
                            <span key={idx} className="material-symbols-outlined text-[14px] text-neutral-500">{p}</span>
                          ))}
                        </div>
                      </div>
                    </div>
                  </div>

                  {item.progress && (
                    <div className="mt-6 h-[2px] w-full bg-white/5 rounded-full overflow-hidden">
                      <div 
                        className="h-full bg-gradient-to-r from-primary to-secondary rounded-full shadow-[0_0_8px_rgba(192,193,255,0.4)] transition-all duration-500" 
                        style={{ width: `${item.progress}%` }}
                      ></div>
                    </div>
                  )}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
