import React from 'react';

interface InputProps extends React.InputHTMLAttributes<HTMLInputElement> {
  label?: string;
}

export const Input: React.FC<InputProps> = ({ label, className = "", ...props }) => {
  return (
    <div style={{ width: '100%' }}>
      {label && (
        <label style={{ 
          display: 'block', 
          fontSize: '14px', 
          marginBottom: '8px', 
          fontWeight: '500',
          fontFamily: 'var(--font-space)',
          opacity: 0.8
        }}>
          {label}
        </label>
      )}
      <input 
        className={className}
        style={{ 
          width: '100%', 
          padding: '12px 16px', 
          background: 'rgba(0,0,0,0.2)', 
          border: '1px solid var(--glass-border)', 
          borderRadius: '8px',
          color: 'white',
          outline: 'none',
          fontFamily: 'var(--font-manrope)',
          transition: 'border-color 0.2s ease'
        }} 
        {...props}
      />
    </div>
  );
};
