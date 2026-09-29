import React, { useState } from 'react';
import { Button } from '../components/ui/Button';
import { Input } from '../components/ui/Input';
import { Select } from '../components/ui/Select';
import { Badge } from '../components/ui/Badge';
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell } from '../components/ui/Table';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '../components/ui/Card';
import { Modal } from '../components/ui/Modal';
import { useToast } from '../components/ui/Toast';
import {
  ShieldAlert,
  Search,
  Lock,
  Terminal,
  Filter,
  CheckCircle2,
  Download,
} from 'lucide-react';

export const FoundationShowcase: React.FC = () => {
  const { toast } = useToast();
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [isButtonLoading, setIsButtonLoading] = useState(false);
  const [searchValue, setSearchValue] = useState('4625');
  const [channelValue, setChannelValue] = useState('Security');

  const triggerLoading = () => {
    setIsButtonLoading(true);
    setTimeout(() => {
      setIsButtonLoading(false);
      toast({
        title: 'Query Executed',
        description: 'Sigma rule match query completed in 14ms.',
        variant: 'success',
      });
    }, 1500);
  };

  const sampleEvents = [
    {
      id: 'EVT-9042',
      time: '2026-09-18 22:45:12.802',
      eventId: 4625,
      channel: 'Security',
      user: 'SYSTEM\\Administrator',
      computer: 'DC01.corp.internal',
      severity: 'critical' as const,
    },
    {
      id: 'EVT-9041',
      time: '2026-09-18 22:44:59.110',
      eventId: 4688,
      channel: 'Sysmon',
      user: 'CORP\\jdoe',
      computer: 'WORKSTATION-88',
      severity: 'high' as const,
    },
    {
      id: 'EVT-9040',
      time: '2026-09-18 22:42:01.045',
      eventId: 4720,
      channel: 'Security',
      user: 'CORP\\secops',
      computer: 'DC01.corp.internal',
      severity: 'medium' as const,
    },
    {
      id: 'EVT-9039',
      time: '2026-09-18 22:38:15.520',
      eventId: 7045,
      channel: 'System',
      user: 'NT AUTHORITY\\SYSTEM',
      computer: 'SRV-SQL02',
      severity: 'low' as const,
    },
    {
      id: 'EVT-9038',
      time: '2026-09-18 22:30:00.000',
      eventId: 4624,
      channel: 'Security',
      user: 'CORP\\analyst1',
      computer: 'WORKSTATION-01',
      severity: 'info' as const,
    },
  ];

  return (
    <div className="min-h-screen bg-kestrel-base text-kestrel-text p-6 md:p-10 max-w-7xl mx-auto space-y-10">
      {/* Header Banner */}
      <header className="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-6 border-b border-kestrel-border">
        <div>
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 rounded-lg bg-sky-950/80 border border-sky-500/40 flex items-center justify-center text-kestrel-accent shadow-glow">
              <ShieldAlert className="w-6 h-6" />
            </div>
            <div>
              <h1 className="text-2xl font-bold tracking-tight text-white flex items-center gap-2">
                Kestrel <span className="text-xs font-mono px-2 py-0.5 rounded bg-sky-950 text-kestrel-accent border border-sky-500/30">Phase 3 Foundation</span>
              </h1>
              <p className="text-xs text-kestrel-muted mt-0.5">
                Design tokens, Google Fonts pairing, primitive components, typed API client & TanStack Query setup.
              </p>
            </div>
          </div>
        </div>

        <div className="flex items-center gap-3">
          <Badge role="analyst" size="md">Analyst Active</Badge>
          <Button variant="secondary" size="sm" onClick={() => setIsModalOpen(true)} leftIcon={<Terminal className="w-3.5 h-3.5" />}>
            Inspect System Spec
          </Button>
        </div>
      </header>

      {/* 1. Design Tokens Overview Grid */}
      <section className="space-y-4">
        <h2 className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-kestrel-accent inline-block" />
          1. Design Tokens & Palette Spec
        </h2>

        <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
          <div className="p-4 rounded-lg bg-kestrel-base border border-kestrel-border space-y-1">
            <span className="text-xs text-kestrel-subtle uppercase">Base Canvas</span>
            <div className="h-8 rounded bg-kestrel-base border border-kestrel-border flex items-center justify-between px-2 font-mono text-xs text-slate-300">
              <span>#0b0f17</span>
              <span className="w-3 h-3 rounded bg-kestrel-base border border-slate-700" />
            </div>
          </div>

          <div className="p-4 rounded-lg bg-kestrel-panel border border-kestrel-border space-y-1">
            <span className="text-xs text-kestrel-subtle uppercase">Panel Surface</span>
            <div className="h-8 rounded bg-kestrel-panel border border-kestrel-border flex items-center justify-between px-2 font-mono text-xs text-slate-300">
              <span>#111823</span>
              <span className="w-3 h-3 rounded bg-kestrel-panel border border-slate-700" />
            </div>
          </div>

          <div className="p-4 rounded-lg bg-kestrel-surface border border-kestrel-border space-y-1">
            <span className="text-xs text-kestrel-subtle uppercase">Border Tone</span>
            <div className="h-8 rounded bg-kestrel-surface border border-kestrel-border flex items-center justify-between px-2 font-mono text-xs text-slate-300">
              <span>#1e293b</span>
              <span className="w-3 h-3 rounded bg-kestrel-border" />
            </div>
          </div>

          <div className="p-4 rounded-lg bg-sky-950/40 border border-sky-500/30 space-y-1">
            <span className="text-xs text-sky-400 uppercase font-medium">Interactive Accent</span>
            <div className="h-8 rounded bg-kestrel-accent flex items-center justify-between px-2 font-mono text-xs text-slate-950 font-bold">
              <span>#0ea5e9</span>
              <span className="w-3 h-3 rounded bg-kestrel-accent" />
            </div>
          </div>
        </div>

        <div className="p-4 rounded-lg bg-kestrel-panel border border-kestrel-border flex flex-wrap items-center justify-between gap-4 text-xs">
          <div>
            <span className="text-kestrel-muted font-medium">UI / Body Typeface:</span>{' '}
            <span className="font-sans text-white font-semibold">Plus Jakarta Sans</span>
          </div>
          <div>
            <span className="text-kestrel-muted font-medium">Monospace Typeface:</span>{' '}
            <span className="font-mono text-kestrel-accent font-semibold">JetBrains Mono</span>
          </div>
          <div>
            <span className="text-kestrel-muted font-medium">Session Auth:</span>{' '}
            <span className="font-mono text-teal-400">credentials: 'include'</span>
          </div>
          <div>
            <span className="text-kestrel-muted font-medium">401 Handler:</span>{' '}
            <span className="font-mono text-rose-400">QueryClient Auto-Redirect /login</span>
          </div>
        </div>
      </section>

      {/* 2. Interactive Primitives Section */}
      <section className="space-y-6">
        <h2 className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-kestrel-accent inline-block" />
          2. Primitive Components (Button, Input, Select, Badge)
        </h2>

        {/* Component Workbench Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          
          {/* Button Variants Card */}
          <Card>
            <CardHeader>
              <CardTitle>Button Primitive</CardTitle>
              <CardDescription>
                Primary (Accent), Secondary, Danger, and Ghost variants with hover, active, loading, disabled & focus-visible states.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-5">
              {/* Variants row */}
              <div className="space-y-2">
                <span className="text-xs text-kestrel-subtle block uppercase font-mono">Variants</span>
                <div className="flex flex-wrap items-center gap-3">
                  <Button variant="primary">Primary Accent</Button>
                  <Button variant="secondary">Secondary</Button>
                  <Button variant="danger">Danger Action</Button>
                  <Button variant="ghost">Ghost Button</Button>
                </div>
              </div>

              {/* Sizes row */}
              <div className="space-y-2">
                <span className="text-xs text-kestrel-subtle block uppercase font-mono">Sizes & Icons</span>
                <div className="flex flex-wrap items-center gap-3">
                  <Button variant="primary" size="sm" leftIcon={<Filter className="w-3.5 h-3.5" />}>
                    Small Filter
                  </Button>
                  <Button variant="secondary" size="md" leftIcon={<Search className="w-4 h-4" />}>
                    Search Logs
                  </Button>
                  <Button variant="primary" size="lg" rightIcon={<Download className="w-4 h-4" />}>
                    Export Event CSV
                  </Button>
                </div>
              </div>

              {/* Interactive states row */}
              <div className="space-y-2">
                <span className="text-xs text-kestrel-subtle block uppercase font-mono">States (Loading & Disabled)</span>
                <div className="flex flex-wrap items-center gap-3">
                  <Button variant="primary" isLoading={isButtonLoading} onClick={triggerLoading}>
                    {isButtonLoading ? 'Parsing EVTX...' : 'Trigger Loading Test'}
                  </Button>
                  <Button variant="primary" disabled>
                    Disabled Primary
                  </Button>
                  <Button variant="secondary" disabled>
                    Disabled Secondary
                  </Button>
                </div>
              </div>
            </CardContent>
          </Card>

          {/* Input & Select Controls Card */}
          <Card>
            <CardHeader>
              <CardTitle>Input & Select Primitives</CardTitle>
              <CardDescription>
                Form controls with labels, icons, helper texts, error states, mono-font options, and keyboard ring outlines.
              </CardDescription>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <Input
                  label="Event ID Search"
                  value={searchValue}
                  onChange={(e) => setSearchValue(e.target.value)}
                  leftIcon={<Search className="w-4 h-4" />}
                  helperText="Enter 4-digit Windows Event ID"
                  isMono
                />

                <Select
                  label="Channel Filter"
                  value={channelValue}
                  onChange={(e) => setChannelValue(e.target.value)}
                  options={[
                    { value: 'Security', label: 'Security.evtx' },
                    { value: 'Sysmon', label: 'Microsoft-Windows-Sysmon' },
                    { value: 'System', label: 'System.evtx' },
                    { value: 'Application', label: 'Application.evtx' },
                  ]}
                />
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-2">
                <Input
                  label="Failed Target User (Error Demo)"
                  defaultValue="root; DROP TABLE"
                  error="Invalid character sequence detected"
                  leftIcon={<Lock className="w-4 h-4" />}
                  isMono
                />

                <Input
                  label="Disabled Input"
                  defaultValue="Ingest Daemon (Read-Only)"
                  disabled
                  leftIcon={<Terminal className="w-4 h-4" />}
                />
              </div>
            </CardContent>
          </Card>
        </div>

        {/* Badges Matrix Card */}
        <Card>
          <CardHeader>
            <CardTitle>Badge System (Severity & Roles)</CardTitle>
            <CardDescription>
              Explicit severity matrix distinguishable for colorblind users through combined hue, lightness, and dot indicators.
            </CardDescription>
          </CardHeader>
          <CardContent className="space-y-6">
            {/* Severity badges */}
            <div className="space-y-2">
              <span className="text-xs text-kestrel-subtle uppercase font-mono block">Severity Levels</span>
              <div className="flex flex-wrap items-center gap-3">
                <Badge severity="info">Info</Badge>
                <Badge severity="low">Low</Badge>
                <Badge severity="medium">Medium</Badge>
                <Badge severity="high">High</Badge>
                <Badge severity="critical">Critical</Badge>
              </div>
            </div>

            {/* Role badges */}
            <div className="space-y-2">
              <span className="text-xs text-kestrel-subtle uppercase font-mono block">RBAC User Roles</span>
              <div className="flex flex-wrap items-center gap-3">
                <Badge role="viewer">Role: Viewer</Badge>
                <Badge role="analyst">Role: Analyst</Badge>
                <Badge role="admin">Role: Admin</Badge>
              </div>
            </div>

            {/* Standard variants */}
            <div className="space-y-2">
              <span className="text-xs text-kestrel-subtle uppercase font-mono block">Standard Variants</span>
              <div className="flex flex-wrap items-center gap-3">
                <Badge variant="default">Default</Badge>
                <Badge variant="accent">Accent Cyber</Badge>
                <Badge variant="success">Rule Match</Badge>
                <Badge variant="danger" className="font-semibold">Flagged FP</Badge>
                <Badge variant="outline">Unparsed</Badge>
              </div>
            </div>
          </CardContent>
        </Card>
      </section>

      {/* 3. Table Shell & Toast / Modal Trigger Section */}
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="text-xs font-semibold uppercase tracking-wider text-kestrel-muted flex items-center gap-2">
            <span className="w-2 h-2 rounded-full bg-kestrel-accent inline-block" />
            3. Table Shell & Feedback (Toast / Modal)
          </h2>

          <div className="flex items-center gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={() =>
                toast({
                  title: 'Test Notification',
                  description: 'This is an informative toast message from the ToastProvider.',
                  variant: 'info',
                })
              }
            >
              Test Info Toast
            </Button>
            <Button
              variant="danger"
              size="sm"
              onClick={() =>
                toast({
                  title: 'Sigma Detection Triggered',
                  description: 'Rule "Mimikatz LSASS Dump" matched event 4688.',
                  variant: 'critical',
                })
              }
            >
              Test Alert Toast
            </Button>
          </div>
        </div>

        {/* Log Table Shell */}
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Event ID</TableHead>
              <TableHead>Timestamp</TableHead>
              <TableHead>Channel</TableHead>
              <TableHead>User Account</TableHead>
              <TableHead>Target Computer</TableHead>
              <TableHead>Severity</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {sampleEvents.map((evt, idx) => (
              <TableRow key={evt.id} isSelected={idx === 0}>
                <TableCell isMono className="font-bold text-sky-400">
                  {evt.eventId}
                </TableCell>
                <TableCell isMono>{evt.time}</TableCell>
                <TableCell>{evt.channel}</TableCell>
                <TableCell isMono>{evt.user}</TableCell>
                <TableCell isMono className="text-kestrel-muted">{evt.computer}</TableCell>
                <TableCell>
                  <Badge severity={evt.severity} size="sm" />
                </TableCell>
                <TableCell className="text-right">
                  <Button
                    variant="ghost"
                    size="sm"
                    className="h-7 text-xs"
                    onClick={() => setIsModalOpen(true)}
                  >
                    View Record
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </section>

      {/* Modal Dialog Component */}
      <Modal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        title={
          <div className="flex items-center gap-2">
            <ShieldAlert className="w-5 h-5 text-rose-400" />
            <span className="font-mono">Event Record #4625 — An Account Failed to Log On</span>
          </div>
        }
        subtitle="Provider: Microsoft-Windows-Security-Auditing | Keywords: Audit Failure"
        footer={
          <>
            <Button variant="secondary" size="sm" onClick={() => setIsModalOpen(false)}>
              Close
            </Button>
            <Button
              variant="primary"
              size="sm"
              leftIcon={<CheckCircle2 className="w-3.5 h-3.5" />}
              onClick={() => {
                setIsModalOpen(false);
                toast({
                  title: 'False Positive Flagged',
                  description: 'Event ID 4625 marked as FP for analyst review.',
                  variant: 'success',
                });
              }}
            >
              Flag as False Positive
            </Button>
          </>
        }
      >
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4 text-xs font-mono bg-kestrel-base p-3 rounded border border-kestrel-border">
            <div>
              <span className="text-kestrel-subtle block">Target UserName:</span>
              <span className="text-sky-300 font-semibold">Administrator</span>
            </div>
            <div>
              <span className="text-kestrel-subtle block">Logon Type:</span>
              <span className="text-white">3 (Network Logon)</span>
            </div>
            <div>
              <span className="text-kestrel-subtle block">IpAddress:</span>
              <span className="text-amber-300">192.168.1.105</span>
            </div>
            <div>
              <span className="text-kestrel-subtle block">Failure Reason:</span>
              <span className="text-rose-400 font-semibold">Unknown user name or bad password</span>
            </div>
          </div>

          <div className="space-y-1">
            <span className="text-xs text-kestrel-subtle uppercase font-mono block">Raw Event XML Snippet</span>
            <pre className="p-3 bg-kestrel-base border border-kestrel-border rounded text-[11px] font-mono text-emerald-400/90 overflow-x-auto">
{`<Event xmlns="http://schemas.microsoft.com/win/2004/08/events/event">
  <System>
    <EventID>4625</EventID>
    <Version>0</Version>
    <Level>0</Level>
    <Task>12544</Task>
    <Opcode>0</Opcode>
    <Keywords>0x8010000000000000</Keywords>
    <TimeCreated SystemTime="2026-09-18T22:45:12.8020000Z" />
    <Channel>Security</Channel>
    <Computer>DC01.corp.internal</Computer>
  </System>
</Event>`}
            </pre>
          </div>
        </div>
      </Modal>
    </div>
  );
};

export default FoundationShowcase;
