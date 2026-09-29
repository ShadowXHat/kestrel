import React from 'react'

export default function Layout({ children }: { children: React.ReactNode }) {
  return (
    <div className="min-h-screen bg-gray-950">
      {children}
    </div>
  )
}
