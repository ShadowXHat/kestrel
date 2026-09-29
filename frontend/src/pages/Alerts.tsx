import React from 'react'

const AlertsPage: React.FC = () => {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Alerts</h1>
      <div className="space-y-3">
        <div className="glass rounded-lg p-4 border-l-4 border-red-500">
          <p className="font-medium">Failed Login Detected</p>
          <p className="text-sm text-gray-400">Event ID 4625 — 2024-01-15 03:22:11</p>
        </div>
        <div className="glass rounded-lg p-4 border-l-4 border-yellow-500">
          <p className="font-medium">Account Lockout</p>
          <p className="text-sm text-gray-400">Event ID 4740 — 2024-01-15 03:25:00</p>
        </div>
      </div>
    </div>
  )
}

export default AlertsPage
