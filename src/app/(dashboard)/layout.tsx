import React from 'react';
import { Sidebar } from '@/components/layout/Sidebar';

export default function DashboardLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <div className="flex min-h-screen bg-background">
      <Sidebar />
      <main className="md:ml-64 flex-grow flex flex-col">
        {/* TopNavBar */}
        <header className="h-14 w-full glass-header sticky top-0 z-40 flex items-center justify-between px-6 font-sans text-sm">
          <div className="flex items-center flex-1">
            <div className="hidden md:block text-primary font-bold tracking-tight mr-8">Post-Infra</div>
            <div className="relative max-w-md w-full ml-4 md:ml-0 group focus-within:ring-1 focus-within:ring-white/20 rounded-md">
              <span className="material-symbols-outlined absolute left-3 top-1/2 -translate-y-1/2 text-neutral-500 text-sm">search</span>
              <input 
                className="w-full bg-white/5 border border-white/10 rounded-md py-1.5 pl-9 pr-4 text-white placeholder-neutral-500 focus:outline-none focus:border-primary/50 focus:bg-white/10 transition-all text-sm h-8" 
                placeholder="Search resources..." 
                type="text"
              />
              <div className="absolute right-2 top-1/2 -translate-y-1/2 flex items-center gap-1">
                <kbd className="px-1.5 py-0.5 bg-white/10 rounded text-[10px] text-neutral-400 font-code border border-white/5">⌘</kbd>
                <kbd className="px-1.5 py-0.5 bg-white/10 rounded text-[10px] text-neutral-400 font-code border border-white/5">K</kbd>
              </div>
            </div>
          </div>
          <div className="flex items-center gap-4">
            <button className="text-neutral-400 hover:text-white transition-colors">
              <span className="material-symbols-outlined">notifications</span>
            </button>
            <button className="text-neutral-400 hover:text-white transition-colors">
              <span className="material-symbols-outlined">help_outline</span>
            </button>
          </div>
        </header>

        {/* Content Canvas */}
        <div className="flex-1 p-6 md:p-8 xl:p-10 max-w-7xl mx-auto w-full">
          {children}
        </div>
      </main>
    </div>
  );
}
