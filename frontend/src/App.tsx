import { Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import { RequireAuth } from './components/auth/RequireAuth';
import { RequireRole } from './components/auth/RequireRole';
import { AppShell } from './components/layout/AppShell';

import LoginPage from './pages/Login';
import FoundationShowcase from './pages/FoundationShowcase';
import EventsPage from './pages/Events';
import IngestPage from './pages/IngestPage';
import UsersPage from './pages/UsersPage';
import { PlaceholderPage } from './pages/PlaceholderPage';

import DetectionPage from './pages/Detection';
import RulesManagerPage from './pages/RulesManager';

import { LayoutDashboard } from 'lucide-react';

function App() {
  return (
    <AuthProvider>
      <Routes>
        {/* Public Login Route */}
        <Route path="/login" element={<LoginPage />} />

        {/* Protected App Routes wrapped inside Main App Shell */}
        <Route
          element={
            <RequireAuth>
              <AppShell />
            </RequireAuth>
          }
        >
          {/* Dashboard Placeholder (Phase 10) */}
          <Route
            path="/"
            element={
              <PlaceholderPage
                title="Dashboard Overview"
                phase="Phase 10 (Dashboards & Apache ECharts)"
                description="Available in a later phase. Real-time event timelines, severity distribution metrics, and host activity widgets will be wired up in Phase 10."
                icon={<LayoutDashboard className="w-5 h-5 text-kestrel-accent" />}
              />
            }
          />

          {/* Events Log Browser (Viewer+) */}
          <Route
            path="/events"
            element={
              <RequireRole role="Viewer">
                <EventsPage />
              </RequireRole>
            }
          />

          {/* Ingest Pipeline (Viewer+ can view job history, Upload is Analyst+ inside IngestPage) */}
          <Route
            path="/ingest"
            element={
              <RequireRole role="Viewer">
                <IngestPage />
              </RequireRole>
            }
          />

          {/* Detection & Sigma Rule Pack Browser (Viewer+) */}
          <Route
            path="/detection"
            element={
              <RequireRole role="Viewer">
                <DetectionPage />
              </RequireRole>
            }
          />

          {/* Sigma Rules CRUD & Editor (Viewer+) */}
          <Route
            path="/rules"
            element={
              <RequireRole role="Viewer">
                <RulesManagerPage />
              </RequireRole>
            }
          />

          {/* Alerts Inbox (Phase 5 Sigma Engine) */}
          <Route
            path="/alerts"
            element={
              <RequireRole role="Viewer">
                <DetectionPage />
              </RequireRole>
            }
          />

          {/* User Management (Admin Only — Fully Built) */}
          <Route
            path="/users"
            element={
              <RequireRole role="Admin">
                <UsersPage />
              </RequireRole>
            }
          />

          {/* Design Token Foundation Showcase */}
          <Route path="/showcase" element={<FoundationShowcase />} />
        </Route>

        {/* Catch-all redirect */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AuthProvider>
  );
}

export default App;
