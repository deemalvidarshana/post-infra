"use client";
import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';

import Cookies from 'js-cookie';

export default function SignUpPage() {
  const [fullName, setFullName] = useState('');
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
      const response = await fetch('/api/identity/auth/register', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({ fullName, email, password }),
      });

      const data = await response.json();

      if (response.ok && data.success) {
        // Store token in cookie (expires in 7 days)
        Cookies.set('auth_token', data.token, { expires: 7, secure: true, sameSite: 'strict' });
        router.push('/'); // Redirect to dashboard
      } else {
        setError(data.message || 'Registration failed');
      }
    } catch (err) {
      setError('Could not connect to the server');
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="bg-[#0a0a0a] text-on-background min-h-screen flex flex-col font-sans antialiased selection:bg-primary-container selection:text-on-primary-container">
      {/* TopNavBar */}
      <nav className="fixed top-0 w-full z-50 bg-neutral-950/70 backdrop-blur-xl text-sm tracking-tight text-white border-b border-white/10 shadow-2xl shadow-black/50">
        <div className="flex justify-between items-center h-16 px-6 max-w-7xl mx-auto">
          <div className="text-xl font-bold tracking-tighter text-white">
            post-infra
          </div>
          <div className="hidden md:flex space-x-6">
            <a className="text-neutral-400 font-medium hover:text-white transition-colors duration-200" href="#">Solutions</a>
            <a className="text-neutral-400 font-medium hover:text-white transition-colors duration-200" href="#">Infrastructure</a>
            <a className="text-neutral-400 font-medium hover:text-white transition-colors duration-200" href="#">Docs</a>
            <a className="text-neutral-400 font-medium hover:text-white transition-colors duration-200" href="#">Pricing</a>
          </div>
          <div className="flex space-x-4">
            <Link href="/login" className="text-neutral-400 font-medium hover:text-white transition-colors duration-200">Sign In</Link>
            <button className="text-neutral-400 font-medium hover:text-white transition-colors duration-200">Get Started</button>
          </div>
        </div>
      </nav>

      {/* Main Content Area */}
      <main className="flex-grow flex items-center justify-center pt-24 pb-12 px-6 relative z-10">
        {/* Ambient Background Glow */}
        <div className="fixed inset-0 z-0 pointer-events-none flex justify-center items-center">
          <div className="absolute w-[800px] h-[800px] bg-primary/5 rounded-full blur-[120px] pointer-events-none z-[-1]"></div>
        </div>

        <div className="glass-panel w-full max-w-md rounded-xl p-10 shadow-2xl relative overflow-hidden">
          {/* Decorative accent line */}
          <div className="absolute top-0 left-0 w-full h-[2px] bg-gradient-to-r from-transparent via-primary-container to-transparent opacity-50"></div>
          
          <div className="text-center mb-8">
            <h1 className="text-3xl font-bold text-white mb-1 tracking-tight">Create your account</h1>
            <p className="text-sm text-neutral-400">Join post-infra to build precise infrastructure.</p>
          </div>

          {error && (
            <div className="bg-red-500/10 border border-red-500/20 text-red-400 text-xs py-2 px-3 rounded text-center mb-6">
              {error}
            </div>
          )}

          <button className="w-full flex items-center justify-center gap-4 py-3 px-6 rounded border border-white/10 bg-transparent text-white text-sm hover:bg-white/5 transition-all duration-300 mb-8 group">
            <svg fill="none" height="20" viewBox="0 0 24 24" width="20" xmlns="http://www.w3.org/2000/svg">
              <path d="M22.56 12.25c0-.78-.07-1.53-.2-2.25H12v4.26h5.92c-.26 1.37-1.04 2.53-2.21 3.31v2.77h3.57c2.08-1.92 3.28-4.74 3.28-8.09z" fill="#4285F4"></path>
              <path d="M12 23c2.97 0 5.46-.98 7.28-2.66l-3.57-2.77c-.98.66-2.23 1.06-3.71 1.06-2.86 0-5.29-1.93-6.16-4.53H2.18v2.84C3.99 20.53 7.7 23 12 23z" fill="#34A853"></path>
              <path d="M5.84 14.09c-.22-.66-.35-1.36-.35-2.09s.13-1.43.35-2.09V7.07H2.18C1.43 8.55 1 10.22 1 12s.43 3.45 1.18 4.93l2.85-2.22.81-.62z" fill="#FBBC05"></path>
              <path d="M12 5.38c1.62 0 3.06.56 4.21 1.64l3.15-3.15C17.45 2.09 14.97 1 12 1 7.7 1 3.99 3.47 2.18 7.07l3.66 2.84c.87-2.6 3.3-4.53 6.16-4.53z" fill="#EA4335"></path>
            </svg>
            <span className="group-hover:text-white transition-colors duration-300">Continue with Google</span>
          </button>

          <div className="flex items-center mb-8">
            <div className="flex-grow h-px bg-white/10"></div>
            <span className="px-4 text-[10px] uppercase tracking-widest text-neutral-500 font-bold">Or</span>
            <div className="flex-grow h-px bg-white/10"></div>
          </div>

          <form className="space-y-4" onSubmit={handleSubmit}>
            <div className="space-y-1">
              <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block" htmlFor="name">Full Name</label>
              <input 
                className="w-full bg-[#1c1b1b] border border-white/10 rounded px-4 py-3 text-sm text-white placeholder-neutral-600 focus:outline-none focus:border-primary/50 focus:ring-1 focus:ring-primary/50 transition-all duration-200" 
                id="name" 
                placeholder="Jane Doe" 
                type="text"
                value={fullName}
                onChange={(e) => setFullName(e.target.value)}
              />
            </div>

            <div className="space-y-1">
              <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block" htmlFor="email">Email Address</label>
              <input 
                className="w-full bg-[#1c1b1b] border border-white/10 rounded px-4 py-3 text-sm text-white placeholder-neutral-600 focus:outline-none focus:border-primary/50 focus:ring-1 focus:ring-primary/50 transition-all duration-200" 
                id="email" 
                placeholder="name@company.com" 
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
              />
            </div>

            <div className="space-y-1">
              <label className="text-[10px] uppercase tracking-widest font-bold text-neutral-400 block" htmlFor="password">Password</label>
              <input 
                className="w-full bg-[#1c1b1b] border border-white/10 rounded px-4 py-3 text-sm text-white placeholder-neutral-600 focus:outline-none focus:border-primary/50 focus:ring-1 focus:ring-primary/50 transition-all duration-200" 
                id="password" 
                placeholder="••••••••" 
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>

            <button 
              className="w-full h-12 mt-6 rounded bg-white text-black text-sm font-bold hover:bg-neutral-200 hover:shadow-[0_0_20px_rgba(192,193,255,0.3)] transition-all duration-300 flex justify-center items-center gap-2 group disabled:opacity-50 disabled:cursor-not-allowed" 
              type="submit"
              disabled={loading}
            >
              {loading ? 'Creating Account...' : 'Sign Up'}
              {!loading && <span className="material-symbols-outlined text-black group-hover:translate-x-1 transition-transform duration-300" style={{ fontSize: '18px' }}>arrow_forward</span>}
            </button>
          </form>

          <div className="mt-8 text-center text-[13px] text-neutral-400">
            Already have an account? <Link className="text-primary hover:text-white transition-colors font-bold" href="/login">Sign in</Link>
          </div>
        </div>
      </main>

      <footer className="bg-neutral-950 w-full py-12 border-t border-white/5 relative z-10 mt-auto">
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
