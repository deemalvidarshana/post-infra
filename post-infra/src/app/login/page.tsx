"use client";
import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';

import Cookies from 'js-cookie';

export default function LoginPage() {
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const router = useRouter();

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');

    try {
      const response = await fetch('/api/identity/auth/login', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ email, password }),
      });

      const data = await response.json();

      if (response.ok && data.success) {
        // Store token in cookie (expires in 7 days)
        Cookies.set('auth_token', data.token, { expires: 7 });
        router.push('/'); // Redirect to dashboard
      } else {
        setError(data.message || 'Login failed');
      }
    } catch {
      setError('Could not connect to the server');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="bg-[#0a0a0a] text-on-surface min-h-screen flex flex-col relative overflow-x-hidden antialiased">
      {/* Ambient Background Glow */}
      <div className="fixed inset-0 z-0 pointer-events-none flex justify-center items-center">
        <div className="absolute w-[800px] h-[800px] bg-primary/10 rounded-full blur-[120px] opacity-50 mix-blend-screen translate-x-1/4 -translate-y-1/4"></div>
        <div className="absolute w-[600px] h-[600px] bg-secondary-container/10 rounded-full blur-[100px] opacity-40 mix-blend-screen -translate-x-1/3 translate-y-1/3"></div>
      </div>

      <main className="flex-grow flex items-center justify-center relative z-10 px-6 py-16">
        {/* Login Card (Glassmorphism) */}
        <div className="w-full max-w-md p-10 rounded-xl bg-[#171717]/70 backdrop-blur-[40px] border border-white/5 shadow-2xl shadow-black/50 relative overflow-hidden group">
          <div className="absolute inset-0 bg-gradient-to-br from-primary/5 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-700 pointer-events-none"></div>
          
          <div className="relative z-10 space-y-6">
            <div className="text-center space-y-2">
              <h1 className="text-3xl font-bold text-white tracking-tight">Welcome back</h1>
              <p className="text-sm text-neutral-400">Sign in to precise infrastructure.</p>
            </div>

            {error && (
              <div className="bg-red-500/10 border border-red-500/20 text-red-400 text-xs py-2 px-3 rounded text-center">
                {error}
              </div>
            )}

            <button className="w-full h-12 flex items-center justify-center gap-3 rounded border border-white/10 bg-white/5 hover:bg-white/10 transition-colors duration-200 group">
              <span className="material-symbols-outlined text-[20px] text-on-surface group-hover:text-white transition-colors">login</span>
              <span className="text-sm font-medium text-on-surface group-hover:text-white transition-colors">Continue with Google</span>
            </button>

            <div className="flex items-center gap-4">
              <div className="h-px bg-white/10 flex-grow"></div>
              <span className="text-[10px] text-neutral-500 tracking-wider uppercase font-bold">Or</span>
              <div className="h-px bg-white/10 flex-grow"></div>
            </div>

            <form className="space-y-4" onSubmit={handleSubmit}>
              <div className="space-y-1">
                <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block" htmlFor="email">Email Address</label>
                <input 
                  className="w-full h-12 bg-black/30 border border-white/10 rounded px-4 text-sm text-white placeholder-neutral-600 focus:outline-none focus:border-primary/50 focus:ring-1 focus:ring-primary/50 transition-all duration-200" 
                  id="email" 
                  placeholder="name@company.com" 
                  type="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  required
                />
              </div>

              <div className="space-y-1">
                <div className="flex justify-between items-center">
                  <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400" htmlFor="password">Password</label>
                  <a className="text-[10px] uppercase tracking-widest font-bold text-primary hover:text-white transition-colors" href="#">Forgot?</a>
                </div>
                <input 
                  className="w-full h-12 bg-black/30 border border-white/10 rounded px-4 text-sm text-white placeholder-neutral-600 focus:outline-none focus:border-primary/50 focus:ring-1 focus:ring-primary/50 transition-all duration-200" 
                  id="password" 
                  placeholder="••••••••" 
                  type="password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  required
                />
              </div>

              <button 
                className="w-full h-12 mt-6 rounded bg-white text-black text-sm font-bold hover:bg-neutral-200 hover:shadow-[0_0_20px_rgba(192,193,255,0.3)] transition-all duration-300 disabled:opacity-50 disabled:cursor-not-allowed" 
                type="submit"
                disabled={loading}
              >
                {loading ? 'Signing In...' : 'Sign In'}
              </button>
            </form>

            <div className="text-center pt-4">
              <p className="text-sm text-neutral-400">
                Don&apos;t have an account? <Link className="text-primary hover:text-white transition-colors font-bold" href="/signup">Sign up</Link>
              </p>
            </div>
          </div>
        </div>
      </main>

      <footer className="bg-neutral-950 w-full py-8 border-t border-white/5 relative z-10 mt-auto">
        <div className="flex flex-col md:flex-row justify-between items-center px-8 max-w-7xl mx-auto space-y-4 md:space-y-0">
          <p className="text-[10px] uppercase tracking-[0.2em] text-neutral-500">© 2024 post-infra. Precise Infrastructure.</p>
          <nav className="flex gap-6">
            {['Status', 'Privacy', 'Terms', 'Contact'].map((item) => (
              <a key={item} className="text-[10px] uppercase tracking-[0.2em] text-neutral-500 hover:text-white transition-colors duration-300" href="#">{item}</a>
            ))}
          </nav>
        </div>
      </footer>
    </div>
  );
}
