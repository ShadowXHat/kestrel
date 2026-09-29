import React from 'react';
import ECharts from 'echarts-for-react';
import { Card, CardHeader, CardTitle, CardContent } from '../components/ui/Card';
import { Badge } from '../components/ui/Badge';
import {
  LayoutDashboard,
  Activity,
  ShieldAlert,
  Server,
  Zap,
  Clock,
  TrendingUp,
  Filter,
} from 'lucide-react';

const DashboardPage: React.FC = () => {
  // Sample time series telemetry for the SOC overview
  const timelineData = [
    ['2026-09-19 08:00', 142],
    ['2026-09-19 08:15', 215],
    ['2026-09-19 08:30', 189],
    ['2026-09-19 08:45', 310],
    ['2026-09-19 09:00', 450],
  ];

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      {/* Header Bar */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-2 border-b border-kestrel-border/60">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/30 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.25)]">
              <LayoutDashboard className="w-4.5 h-4.5" />
            </div>
            <span>SOC Incident Operations Dashboard</span>
          </h2>
          <p className="text-xs text-kestrel-muted mt-1">
            Real-time Windows Event Log streaming telemetry, threat severity distribution, and host activity metrics.
          </p>
        </div>

        <div className="flex items-center gap-3">
          <Badge variant="accent" size="md" className="font-mono">
            Phase 10 Preview
          </Badge>
        </div>
      </div>

      {/* Overview Stat Cards */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
        <Card variant="glass" className="p-4 rounded-xl border-kestrel-border">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono uppercase tracking-wider text-kestrel-subtle font-semibold">
              Total Logs Processed
            </span>
            <div className="p-2 rounded-lg bg-sky-950/80 border border-sky-500/30 text-sky-400">
              <Activity className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-white font-mono">1,306,420</div>
            <p className="text-[11px] text-emerald-400 font-mono mt-1 flex items-center gap-1">
              <TrendingUp className="w-3 h-3" />
              <span>+14.2% from last hour</span>
            </p>
          </div>
        </Card>

        <Card variant="glass" className="p-4 rounded-xl border-kestrel-border">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono uppercase tracking-wider text-kestrel-subtle font-semibold">
              Threat Detection Matches
            </span>
            <div className="p-2 rounded-lg bg-rose-950/80 border border-rose-500/40 text-rose-400">
              <ShieldAlert className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-rose-400 font-mono">18</div>
            <p className="text-[11px] text-rose-300/80 font-mono mt-1">
              3 Critical Sigma Rule Matches
            </p>
          </div>
        </Card>

        <Card variant="glass" className="p-4 rounded-xl border-kestrel-border">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono uppercase tracking-wider text-kestrel-subtle font-semibold">
              Active Computer Hosts
            </span>
            <div className="p-2 rounded-lg bg-emerald-950/80 border border-emerald-500/30 text-emerald-400">
              <Server className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-white font-mono">42</div>
            <p className="text-[11px] text-emerald-400 font-mono mt-1">
              100% telemetry coverage
            </p>
          </div>
        </Card>

        <Card variant="glass" className="p-4 rounded-xl border-kestrel-border">
          <div className="flex items-center justify-between">
            <span className="text-xs font-mono uppercase tracking-wider text-kestrel-subtle font-semibold">
              Parser Stream Velocity
            </span>
            <div className="p-2 rounded-lg bg-amber-950/80 border border-amber-500/30 text-amber-400">
              <Zap className="w-4 h-4" />
            </div>
          </div>
          <div className="mt-3">
            <div className="text-2xl font-bold text-sky-400 font-mono">4,850 evt/s</div>
            <p className="text-[11px] text-kestrel-muted font-mono mt-1 flex items-center gap-1">
              <Clock className="w-3 h-3 text-sky-400" />
              <span>Zero drop rate</span>
            </p>
          </div>
        </Card>
      </div>

      {/* ECharts Telemetry Widgets */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        <Card variant="glass" className="rounded-xl border-kestrel-border">
          <CardHeader className="pb-2 flex flex-row items-center justify-between">
            <div>
              <CardTitle className="text-sm font-bold text-white font-sans flex items-center gap-2">
                <Activity className="w-4 h-4 text-sky-400" />
                <span>Ingestion Velocity & Timeline</span>
              </CardTitle>
              <span className="text-xs text-kestrel-muted font-mono">
                Events ingested over time (15-min intervals)
              </span>
            </div>
          </CardHeader>
          <CardContent className="h-72 pt-2">
            <ECharts
              option={{
                tooltip: { trigger: 'axis', backgroundColor: '#0d131f', borderColor: '#1e293b', textStyle: { color: '#f8fafc', fontSize: 11, fontFamily: 'JetBrains Mono' } },
                grid: { top: 20, bottom: 30, left: 45, right: 20 },
                xAxis: {
                  type: 'category',
                  data: timelineData.map((d) => d[0]),
                  axisLabel: { color: '#94a3b8', fontSize: 10, fontFamily: 'JetBrains Mono' },
                  axisLine: { lineStyle: { color: '#1e293b' } },
                },
                yAxis: {
                  type: 'value',
                  axisLabel: { color: '#94a3b8', fontSize: 10, fontFamily: 'JetBrains Mono' },
                  splitLine: { lineStyle: { color: 'rgba(30, 41, 59, 0.6)', type: 'dashed' } },
                },
                series: [
                  {
                    type: 'line',
                    smooth: true,
                    data: timelineData.map((d) => d[1]),
                    lineStyle: { color: '#38bdf8', width: 2 },
                    itemStyle: { color: '#38bdf8' },
                    areaStyle: {
                      color: {
                        type: 'linear',
                        x: 0,
                        y: 0,
                        x2: 0,
                        y2: 1,
                        colorStops: [
                          { offset: 0, color: 'rgba(56, 189, 248, 0.35)' },
                          { offset: 1, color: 'rgba(56, 189, 248, 0.0)' },
                        ],
                      },
                    },
                  },
                ],
                backgroundColor: 'transparent',
              }}
              style={{ height: '100%', width: '100%' }}
            />
          </CardContent>
        </Card>

        <Card variant="glass" className="rounded-xl border-kestrel-border">
          <CardHeader className="pb-2 flex flex-row items-center justify-between">
            <div>
              <CardTitle className="text-sm font-bold text-white font-sans flex items-center gap-2">
                <Filter className="w-4 h-4 text-amber-400" />
                <span>Severity Distribution Breakdown</span>
              </CardTitle>
              <span className="text-xs text-kestrel-muted font-mono">
                Categorized by Windows Event log severity levels
              </span>
            </div>
          </CardHeader>
          <CardContent className="h-72 pt-2">
            <ECharts
              option={{
                tooltip: { trigger: 'item', backgroundColor: '#0d131f', borderColor: '#1e293b', textStyle: { color: '#f8fafc', fontSize: 11, fontFamily: 'JetBrains Mono' } },
                legend: { bottom: '0%', left: 'center', textStyle: { color: '#94a3b8', fontSize: 11, fontFamily: 'JetBrains Mono' } },
                series: [
                  {
                    type: 'pie',
                    radius: ['45%', '70%'],
                    avoidLabelOverlap: false,
                    itemStyle: { borderRadius: 6, borderColor: '#0d131f', borderWidth: 2 },
                    label: { show: false },
                    data: [
                      { value: 18, name: 'Critical', itemStyle: { color: '#f43f5e' } },
                      { value: 85, name: 'Error', itemStyle: { color: '#f97316' } },
                      { value: 240, name: 'Warning', itemStyle: { color: '#fbbf24' } },
                      { value: 620, name: 'Audit Success', itemStyle: { color: '#2dd4bf' } },
                      { value: 1850, name: 'Information', itemStyle: { color: '#38bdf8' } },
                    ],
                  },
                ],
                backgroundColor: 'transparent',
              }}
              style={{ height: '100%', width: '100%' }}
            />
          </CardContent>
        </Card>
      </div>
    </div>
  );
};

export default DashboardPage;
