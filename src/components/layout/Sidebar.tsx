"use client";

import React from 'react';
import Link from 'next/link';
import { usePathname } from 'next/navigation';

const navItems = [
  { name: 'Overview', href: '/', icon: 'dashboard' },
  { name: 'Deployments', href: '/deployments', icon: 'rocket_launch' },
  { name: 'AI Workflows', href: '/studio', icon: 'auto_awesome' },
  { name: 'Queues', href: '/queue', icon: 'stacked_line_chart' },
  { name: 'Settings', href: '/settings', icon: 'settings' },
];

export const Sidebar = () => {
  const pathname = usePathname();

  return (
    <nav className="hidden md:flex h-screen w-64 glass-nav shadow-[20px_0_40px_rgba(0,0,0,0.3)] fixed left-0 top-0 bottom-0 flex flex-col pt-6 z-50 font-sans tracking-tight antialiased text-[13px]">
      <div className="px-6 mb-8">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 rounded bg-primary/20 border border-primary/30 flex items-center justify-center text-primary">
            <span className="material-symbols-outlined text-sm" style={{ fontVariationSettings: "'FILL' 1" }}>rocket_launch</span>
          </div>
          <div>
            <h1 className="text-white font-semibold tracking-tighter text-lg leading-tight">Post-Infra</h1>
            <p className="text-neutral-500 text-[10px] uppercase tracking-wider">Enterprise Tier</p>
          </div>
        </div>
      </div>

      <div className="flex flex-col space-y-1 mt-4 px-2">
        {navItems.map((item) => {
          const isActive = pathname === item.href;
          
          return (
            <Link 
              key={item.name} 
              href={item.href} 
              className={`flex items-center gap-3 px-4 py-2 rounded-lg transition-all duration-200 active:scale-[0.98] ${
                isActive 
                ? "text-white font-medium border-r-2 border-indigo-500 bg-white/5" 
                : "text-neutral-500 hover:text-neutral-300 hover:bg-white/5"
              }`}
            >
              <span className="material-symbols-outlined">{item.icon}</span>
              {item.name}
            </Link>
          );
        })}
      </div>

      <div className="mt-auto p-4 mb-4">
        <div className="flex items-center gap-3 px-4 py-2 border border-white/5 bg-white/5 rounded-lg hover:bg-white/10 transition-colors cursor-pointer">
          <div className="w-6 h-6 rounded-full bg-surface-container-highest border border-white/10 overflow-hidden">
            <img 
              alt="User Avatar" 
              className="w-full h-full object-cover" 
              src="https://lh3.googleusercontent.com/aida-public/AB6AXuDnn9I2zHGXXlMdio7188CkVMEAawGL-fBBCGasCVwvaxyoW8fOOrSQQuQ4zAou7ICTjHZFEtyiaBPbxNkpZk0EXQMTcH0-BEAaJs0RtOZy2569akntFt-Oomc_3ZbhQAZKYts4Z24MTOvIAmy1lcyOsSkLGoGGPA75Y71vxzJKu2fAbnOCBe-zjZGvey-cvSxXyVlSlk17JQXtcglAqniuZUhpTauzC70y29LeVlRa3mqGdkIxZrVx6YBT55dxBgsZ3NQx10FH5neg" 
            />
          </div>
          <div className="flex flex-col">
            <span className="text-white text-xs font-medium">J. Doe</span>
            <span className="text-neutral-500 text-[10px]">Admin</span>
          </div>
        </div>
      </div>
    </nav>
  );
};
