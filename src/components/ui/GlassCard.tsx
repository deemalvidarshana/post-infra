import React from 'react';

interface GlassCardProps {
  children: React.ReactNode;
  className?: string;
  glow?: boolean;
}

export const GlassCard: React.FC<GlassCardProps> = ({ children, className = "", glow = false }) => {
  return (
    <div className={`glass-panel rounded-xl relative overflow-hidden group ${className}`}>
      {glow && (
        <div className="absolute inset-0 bg-gradient-to-br from-primary/5 to-transparent opacity-0 group-hover:opacity-100 transition-opacity duration-700 pointer-events-none"></div>
      )}
      <div className="relative z-10">
        {children}
      </div>
    </div>
  );
};
