import React from 'react'

const SettingsPage: React.FC = () => {
  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Settings</h1>
      <div className="glass rounded-xl p-4 space-y-4">
        <div>
          <label className="block text-sm font-medium mb-1">Role</label>
          <select className="w-full px-3 py-2 rounded bg-gray-800 border border-gray-700 text-white">
            <option>Viewer</option>
            <option>Analyst</option>
            <option>Admin</option>
          </select>
        </div>
        <div>
          <label className="block text-sm font-medium mb-1">Upload Size Limit (GB)</label>
          <input type="number" defaultValue="5" className="w-full px-3 py-2 rounded bg-gray-800 border border-gray-700 text-white" />
        </div>
      </div>
    </div>
  )
}

export default SettingsPage
