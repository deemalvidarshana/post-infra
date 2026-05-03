import React from 'react';

interface GlassCardProps {
  children: React.ReactNode;
  className?: string;
}

export const GlassCard: React.FC<GlassCardProps> = ({ children, className = "" }) => {
  return (
    <div className={`glass-card ${className}`} style={{ padding: '24px' }}>
      {children}
    </div>
  );
};
