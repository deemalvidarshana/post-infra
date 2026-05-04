import React from 'react';

interface ButtonProps extends React.ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: 'primary' | 'secondary' | 'ghost' | 'outline';
  children: React.ReactNode;
}

export const Button: React.FC<ButtonProps> = ({ variant = 'primary', children, className = "", ...props }) => {
  const baseStyles = "px-6 py-3 rounded text-sm font-bold transition-all duration-300 active:scale-[0.98] flex items-center justify-center gap-2";
  
  const variants = {
    primary: "bg-white text-black hover:bg-neutral-200 hover:shadow-[0_0_20px_rgba(192,193,255,0.3)]",
    secondary: "bg-primary text-black hover:bg-primary/90 hover:shadow-[0_0_15px_rgba(192,193,255,0.2)]",
    outline: "bg-transparent border border-white/10 text-white hover:bg-white/5",
    ghost: "bg-transparent text-neutral-400 hover:text-white hover:bg-white/5",
  };

  return (
    <button 
      className={`${baseStyles} ${variants[variant]} ${className}`} 
      {...props}
    >
      {children}
    </button>
  );
};
