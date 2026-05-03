import React from 'react';

export default function StudioPage() {
  return (
    <div className="flex-1 p-6 lg:p-8 overflow-y-auto grid grid-cols-1 lg:grid-cols-2 gap-8 h-full max-w-7xl mx-auto w-full">
      {/* Left Column: Source & Timeline */}
      <div className="flex flex-col gap-8 h-full">
        {/* Source Fetcher */}
        <section className="glass-panel rounded-xl p-6 flex flex-col gap-4">
          <div className="flex items-center gap-2 mb-2">
            <div className="flex gap-1.5">
              <div className="w-2.5 h-2.5 rounded-full bg-[#ff5f56]"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-[#ffbd2e]"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-[#27c93f]"></div>
            </div>
            <span className="text-[10px] uppercase font-bold tracking-widest text-neutral-500 ml-2">Source Fetcher</span>
          </div>
          <div className="flex gap-2">
            <div className="flex-1 relative">
              <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-neutral-600 text-sm">link</span>
              <input 
                className="w-full bg-[#1c1b1b] border border-white/10 rounded-lg py-2.5 pl-10 pr-4 text-white focus:outline-none focus:border-primary/50 focus:ring-1 focus:ring-primary/50 font-code text-xs transition-all" 
                placeholder="Paste Third-Party API URL..." 
                type="text"
              />
            </div>
            <button className="bg-primary text-black px-6 py-2.5 rounded-lg text-xs font-bold hover:bg-primary/90 transition-colors shadow-[0_0_15px_rgba(192,193,255,0.2)]">Fetch</button>
          </div>
        </section>

        {/* Timeline */}
        <section className="glass-panel rounded-xl flex-1 flex flex-col overflow-hidden min-h-[400px]">
          <div className="flex items-center gap-2 p-3 border-b border-white/5 bg-white/[0.02]">
            <div className="flex gap-1.5">
              <div className="w-2.5 h-2.5 rounded-full bg-[#ff5f56]"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-[#ffbd2e]"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-[#27c93f]"></div>
            </div>
            <span className="text-[10px] uppercase font-bold tracking-widest text-neutral-500 ml-2">Timeline</span>
            <div className="ml-auto flex gap-1">
              <button className="p-1 text-neutral-500 hover:text-white rounded hover:bg-white/5"><span className="material-symbols-outlined text-sm">zoom_in</span></button>
              <button className="p-1 text-neutral-500 hover:text-white rounded hover:bg-white/5"><span className="material-symbols-outlined text-sm">zoom_out</span></button>
            </div>
          </div>
          
          <div className="flex-1 relative overflow-x-auto overflow-y-hidden p-6 bg-[#1c1c1e]" style={{ backgroundImage: 'linear-gradient(rgba(255,255,255,0.02) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.02) 1px, transparent 1px)', backgroundSize: '20px 20px' }}>
            {/* Playhead */}
            <div className="absolute top-0 bottom-0 left-[150px] w-px bg-red-500 z-10 flex flex-col items-center">
              <div className="w-0 h-0 border-l-[6px] border-r-[6px] border-t-[8px] border-l-transparent border-r-transparent border-t-red-500 -mt-[1px]"></div>
            </div>
            
            {/* Tracks */}
            <div className="flex flex-col gap-4 mt-8 w-[800px]">
              {/* Video Track */}
              <div className="flex items-center gap-4 h-[60px]">
                <div className="w-[60px] text-right pr-4 border-r border-white/10 text-[10px] font-bold text-neutral-600">V1</div>
                <div className="flex-1 relative h-[48px] bg-white/5 rounded border border-white/10 flex items-center overflow-hidden">
                  <img className="h-full w-auto opacity-30 object-cover" src="https://lh3.googleusercontent.com/aida-public/AB6AXuDCVIhsQT27D0SDyCktkOoeaTZDp8lKxpzQct_yneDfOG8MzwvX-XxirKm8a7OrI_EmREUmLqFJ9BiaKDXRkdGLZxyeFoeE_HjIDwJq9utrIF6k-7XLSi4iXidnGyPiEvWeZRSXoxbI66vSJhI_al1FHSzZaJ2kbS_2c20iAuahv4YokP0zW1_G_b3fydzspHbnPeW6M4t5qupHdzwIpS9A06I_HxvMLWX7zSNVcMdrVTzjrW1LtJkMYEe-ah_xn0UP-_V_8bThy2Os" alt="Clip" />
                  <div className="absolute left-0 top-0 bottom-0 w-[200px] bg-primary/20 border-l-2 border-r-2 border-primary rounded-sm flex items-center px-2">
                    <span className="font-code text-[10px] text-primary truncate">A-Roll_01.mp4</span>
                  </div>
                </div>
              </div>
              
              {/* Audio Track */}
              <div className="flex items-center gap-4 h-[40px]">
                <div className="w-[60px] text-right pr-4 border-r border-white/10 text-[10px] font-bold text-neutral-600">A1</div>
                <div className="flex-1 relative h-[32px] bg-white/5 rounded border border-white/10 flex items-center overflow-hidden">
                  <div className="absolute left-0 top-0 bottom-0 w-[200px] bg-secondary/20 border-l border-r border-secondary rounded-sm flex items-center px-2 overflow-hidden">
                    <div className="w-full flex items-end gap-[2px] h-full opacity-40 py-1">
                      {[40, 80, 30, 90, 50, 20, 60, 40, 70, 30, 80, 50].map((h, i) => (
                        <div key={i} className="w-1 bg-secondary" style={{ height: `${h}%` }}></div>
                      ))}
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </section>
      </div>

      {/* Right Column: Preview & AI */}
      <div className="flex flex-col gap-8 h-full">
        {/* Preview Player */}
        <section className="glass-panel rounded-xl flex flex-col overflow-hidden">
          <div className="flex items-center gap-2 p-3 border-b border-white/5 bg-white/[0.02]">
            <div className="flex gap-1.5">
              <div className="w-2.5 h-2.5 rounded-full bg-[#ff5f56]"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-[#ffbd2e]"></div>
              <div className="w-2.5 h-2.5 rounded-full bg-[#27c93f]"></div>
            </div>
            <span className="text-[10px] uppercase font-bold tracking-widest text-neutral-500 ml-2">Preview (16:9)</span>
          </div>
          <div className="aspect-video bg-black relative flex items-center justify-center group">
            <img className="w-full h-full object-cover opacity-60" src="https://lh3.googleusercontent.com/aida-public/AB6AXuDgnsAEKQ_XrklfNF0tcfyfceHjK5qdfc77rri8Tn2O95cLueJumcXYjx3lAutALVWax8oo1S9Ql9fhi9G3jg2T9-YvlWBUg3pxUvqorogrQEjVGJK1k7di6YcT7QibXtmk_xcbkoqB4HpzoR7GtAmdbnFPtx2dpm4W7NFDnHA-YlFvwHRjCLFjH98wEeOUuUFiC2MmriznBim8QOfaYvpZ2T9v-TvgcIPARpLPJlCOQ996vIb6ooJYprvg6qvaAOToHaLy8wOqKKFL" alt="Preview" />
            
            {/* Overlay Controls */}
            <div className="absolute bottom-0 left-0 right-0 p-6 bg-gradient-to-t from-black/90 to-transparent flex flex-col gap-4 translate-y-2 group-hover:translate-y-0 transition-transform">
              {/* Progress */}
              <div className="w-full h-1 bg-white/20 rounded-full overflow-hidden">
                <div className="w-1/3 h-full bg-gradient-to-r from-primary to-secondary shadow-[0_0_8px_rgba(192,193,255,0.4)]"></div>
              </div>
              {/* Buttons */}
              <div className="flex items-center justify-center gap-8 text-white">
                <button className="hover:text-primary transition-colors"><span className="material-symbols-outlined">skip_previous</span></button>
                <button className="w-12 h-12 rounded-full bg-white/10 hover:bg-white/20 backdrop-blur flex items-center justify-center transition-all border border-white/10 shadow-xl">
                  <span className="material-symbols-outlined" style={{ fontVariationSettings: "'FILL' 1" }}>play_arrow</span>
                </button>
                <button className="hover:text-primary transition-colors"><span className="material-symbols-outlined">skip_next</span></button>
              </div>
            </div>
          </div>
        </section>

        {/* AI Studio */}
        <section className="glass-panel rounded-xl flex-1 flex flex-col p-6 gap-4">
          <div className="flex items-center gap-2 mb-2">
            <span className="material-symbols-outlined text-secondary text-sm">auto_awesome</span>
            <span className="text-[10px] uppercase font-bold tracking-widest text-white ml-2">AI Studio</span>
          </div>
          <div className="flex-1 bg-black/30 border border-white/5 rounded-lg p-4 relative">
            <textarea 
              className="w-full h-full bg-transparent border-none resize-none focus:ring-0 text-sm text-neutral-300 placeholder-neutral-700" 
              placeholder="AI generated captions will appear here..."
            ></textarea>
          </div>
          <button className="w-full py-4 rounded-lg flex items-center justify-center gap-3 bg-gradient-to-r from-primary/80 to-secondary/80 text-white text-[10px] font-bold uppercase tracking-widest shadow-[0_0_20px_rgba(192,193,255,0.2)] hover:opacity-90 transition-opacity border border-white/10">
            <span className="material-symbols-outlined text-sm">magic_button</span>
            Generate Auto-Caption & Music
          </button>
        </section>
      </div>
    </div>
  );
}
