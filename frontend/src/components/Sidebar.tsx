import React from 'react'
import { NavLink } from 'react-router-dom'

const navItems = [
  { to: '/', label: 'Dashboard', icon: '📊' },
  { to: '/events', label: 'Events', icon: '📋' },
  { to: '/search', label: 'Search', icon: '🔍' },
  { to: '/alerts', label: 'Alerts', icon: '🚨' },
  { to: '/settings', label: 'Settings', icon: '⚙️' },
]

const Sidebar: React.FC = () => {
  return (
    <aside className="w-56 bg-gray-900 border-r border-gray-800 flex flex-col">
      <div className="p-4 border-b border-gray-800">
        <h2 className="text-xl font-bold text-blue-400">Kestrel</h2>
      </div>
      <nav className="flex-1 p-2 space-y-1">
        {navItems.map(item => (
          <NavLink
            key={item.to}
            to={item.to}
            end={item.to === '/'}
            className={({ isActive }) =>
              `flex items-center gap-2 px-3 py-2 rounded text-sm ${
                isActive ? 'bg-blue-600/20 text-blue-400' : 'text-gray-400 hover:bg-gray-800 hover:text-white'
              }`
            }
          >
            <span>{item.icon}</span>
            <span>{item.label}</span>
          </NavLink>
        ))}
      </nav>
      <div className="p-4 border-t border-gray-800">
        <p className="text-xs text-gray-500">Kestrel v1.0.0</p>
      </div>
    </aside>
  )
}

export default Sidebar
