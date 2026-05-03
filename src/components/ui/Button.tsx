import React from 'react';

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost';
  children: React.ReactNode;
}

export const Button: React.FC<ButtonProps> = ({ variant = 'primary', children, className = "", ...props }) => {
  const baseStyle = "glass-btn";
  const styles = {
    primary: "",
    secondary: "background: transparent; border: 1px solid var(--primary);",
    ghost: "background: transparent; box-shadow: none;",
  };

  return (
    <button 
      className={`${baseStyle} ${className}`} 
      style={{ 
        padding: '12px 24px', 
        fontSize: '16px',
        ...(variant !== 'primary' ? { background: 'transparent' } : {}),
        ...(variant === 'secondary' ? { border: '1px solid var(--primary)' } : {}),
        ...(variant === 'ghost' ? { boxShadow: 'none' } : {})
      }} 
      {...props}
    >
      {children}
    </button>
  );
};
