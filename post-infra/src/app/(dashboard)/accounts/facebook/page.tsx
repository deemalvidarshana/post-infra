"use client";

import React, { useState } from 'react';

interface FBPage {
  id: string;
  name: string;
  category: string;
  accessToken: string;
  avatar: string;
  status: 'connected' | 'disconnected';
}

export default function FacebookAccountsPage() {
  const [pages, setPages] = useState<FBPage[]>([
    {
      id: '123456789',
      name: 'Tech Solutions Sri Lanka',
      category: 'Information Technology',
      accessToken: 'EAAIb...',
      avatar: 'https://images.unsplash.com/photo-1531297484001-80022131f5a1?w=100&h=100&fit=crop',
      status: 'connected'
    },
    {
      id: '987654321',
      name: 'Digital Marketing Hub',
      category: 'Marketing Agency',
      accessToken: 'EAAJz...',
      avatar: 'https://images.unsplash.com/photo-1460925895917-afdab827c52f?w=100&h=100&fit=crop',
      status: 'connected'
    }
  ]);

  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isConnecting, setIsConnecting] = useState(false);
  
  // Fetch pages on mount
  React.useEffect(() => {
    const fetchPages = async () => {
      try {
        const response = await fetch('/api/smapi/Pages/facebook/user-123'); // Mock user ID
        const data = await response.json();
        if (Array.isArray(data)) {
          const mappedPages: FBPage[] = data.map((p: any) => ({
            id: p.pageId,
            name: p.pageName,
            category: p.category || 'Facebook Page',
            accessToken: p.accessToken,
            avatar: p.avatarUrl || '',
            status: 'connected'
          }));
          setPages(mappedPages);
        }
      } catch (err) {
        console.error('Failed to fetch pages', err);
      }
    };
    fetchPages();
  }, []);

  // New Page Form State
  const [formData, setFormData] = useState({
    name: '',
    id: '',
    apiKey: ''
  });

  const handleConnectPage = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsConnecting(true);
    
    try {
      const response = await fetch('/api/smapi/Pages/facebook/connect', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          pageId: formData.id,
          pageName: formData.name,
          accessToken: formData.apiKey,
          userId: 'user-123', // TODO: Get from auth token
          category: 'Manual Connection'
        }),
      });

      const data = await response.json();

      if (response.ok && data.success) {
        // Refresh local list (ideally fetch from backend again)
        const newPage: FBPage = {
          id: formData.id,
          name: formData.name,
          category: 'Manual Connection',
          accessToken: formData.apiKey,
          avatar: '',
          status: 'connected'
        };
        setPages([newPage, ...pages]);
        setIsModalOpen(false);
        setFormData({ name: '', id: '', apiKey: '' });
      } else {
        alert(data.message || 'Failed to connect page');
      }
    } catch (err) {
      alert('Could not connect to the backend server');
    } finally {
      setIsConnecting(false);
    }
  };

  return (
    <div className="p-8 space-y-8 animate-in fade-in duration-700">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-3xl font-bold text-white tracking-tight">Facebook Pages</h1>
          <p className="text-neutral-500 text-sm mt-1">Connect and manage multiple Facebook pages for auto-publishing.</p>
        </div>
        <button 
          onClick={() => setIsModalOpen(true)}
          className="flex items-center gap-2 px-6 py-3 bg-white text-black rounded-lg font-bold hover:bg-neutral-200 transition-all shadow-[0_0_20px_rgba(255,255,255,0.1)] active:scale-95"
        >
          <span className="material-symbols-outlined">add_circle</span>
          Connect New Page
        </button>
      </div>

      {/* Stats Summary */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <div className="glass-panel p-6 rounded-xl border border-white/5 space-y-2">
          <p className="text-neutral-500 text-[10px] uppercase tracking-widest font-bold">Total Pages</p>
          <p className="text-3xl font-bold text-white">{pages.length}</p>
        </div>
        <div className="glass-panel p-6 rounded-xl border border-white/5 space-y-2">
          <p className="text-neutral-500 text-[10px] uppercase tracking-widest font-bold">Total Tokens</p>
          <p className="text-3xl font-bold text-green-400">{pages.filter(p => p.status === 'connected').length} Active</p>
        </div>
        <div className="glass-panel p-6 rounded-xl border border-white/5 space-y-2">
          <p className="text-neutral-500 text-[10px] uppercase tracking-widest font-bold">System Status</p>
          <p className="text-3xl font-bold text-blue-400">Ready</p>
        </div>
      </div>

      {/* Pages Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 xl:grid-cols-3 gap-6">
        {pages.map((page) => (
          <div key={page.id} className="glass-panel p-6 rounded-xl border border-white/5 group hover:border-primary/30 transition-all duration-300 relative overflow-hidden">
            <div className="absolute top-0 right-0 p-4">
              <span className={`flex h-2 w-2 rounded-full ${page.status === 'connected' ? 'bg-green-500 shadow-[0_0_10px_rgba(34,197,94,0.5)]' : 'bg-red-500'}`}></span>
            </div>
            
            <div className="flex items-start gap-4">
              <div className="w-16 h-16 rounded-lg overflow-hidden border border-white/10 shrink-0 flex items-center justify-center bg-blue-600/10">
                {page.avatar ? (
                  <img src={page.avatar} alt={page.name} className="w-full h-full object-cover" />
                ) : (
                  <span className="material-symbols-outlined text-blue-500">facebook</span>
                )}
              </div>
              <div className="flex-grow min-w-0">
                <h3 className="text-lg font-bold text-white truncate">{page.name}</h3>
                <p className="text-neutral-500 text-[10px] truncate">ID: {page.id}</p>
                <div className="mt-3 flex items-center gap-2 text-[10px] text-neutral-400 bg-black/30 p-2 rounded border border-white/5">
                  <span className="material-symbols-outlined text-xs">key</span>
                  <span className="truncate">Token: {page.accessToken}</span>
                </div>
              </div>
            </div>

            <div className="mt-6 flex gap-3">
              <button className="flex-grow py-2 rounded bg-white/5 text-xs font-bold text-white hover:bg-white/10 transition-colors border border-white/5">
                View Logs
              </button>
              <button className="px-3 py-2 rounded bg-red-500/10 text-red-500 hover:bg-red-500/20 transition-colors border border-red-500/20">
                <span className="material-symbols-outlined text-sm">delete</span>
              </button>
            </div>
          </div>
        ))}
      </div>

      {/* Connection Modal */}
      {isModalOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-6">
          <div className="absolute inset-0 bg-black/80 backdrop-blur-sm" onClick={() => !isConnecting && setIsModalOpen(false)}></div>
          <div className="relative glass-panel w-full max-w-md p-8 rounded-2xl border border-white/10 shadow-2xl animate-in zoom-in-95 duration-200">
            <div className="space-y-6">
              <div className="text-center">
                <h2 className="text-2xl font-bold text-white">Connect Facebook Page</h2>
                <p className="text-neutral-400 text-sm mt-1">Enter your page details manually to connect.</p>
              </div>

              <form onSubmit={handleConnectPage} className="space-y-4">
                <div className="space-y-1">
                  <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Page Name</label>
                  <input 
                    required
                    type="text"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    placeholder="e.g. My Business Page"
                    value={formData.name}
                    onChange={(e) => setFormData({...formData, name: e.target.value})}
                  />
                </div>

                <div className="space-y-1">
                  <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Facebook Page ID</label>
                  <input 
                    required
                    type="text"
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all"
                    placeholder="e.g. 1029384756"
                    value={formData.id}
                    onChange={(e) => setFormData({...formData, id: e.target.value})}
                  />
                </div>

                <div className="space-y-1">
                  <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block">Facebook API Key (Access Token)</label>
                  <textarea 
                    required
                    rows={3}
                    className="w-full bg-black/50 border border-white/10 rounded-lg px-4 py-3 text-sm text-white focus:border-blue-500 focus:outline-none transition-all resize-none"
                    placeholder="Paste your Page Access Token here..."
                    value={formData.apiKey}
                    onChange={(e) => setFormData({...formData, apiKey: e.target.value})}
                  />
                </div>

                <div className="pt-4 flex flex-col gap-3">
                  <button 
                    type="submit"
                    disabled={isConnecting}
                    className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white rounded-xl font-bold transition-all flex items-center justify-center gap-3 disabled:opacity-50 shadow-lg shadow-blue-600/20"
                  >
                    {isConnecting ? (
                      <>
                        <div className="w-5 h-5 border-2 border-white/30 border-t-white rounded-full animate-spin"></div>
                        Connecting...
                      </>
                    ) : (
                      <>
                        Connect Page
                      </>
                    )}
                  </button>
                  <button 
                    type="button"
                    onClick={() => setIsModalOpen(false)}
                    disabled={isConnecting}
                    className="w-full py-2 bg-transparent text-neutral-500 hover:text-white transition-colors font-medium text-sm"
                  >
                    Cancel
                  </button>
                </div>
              </form>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
