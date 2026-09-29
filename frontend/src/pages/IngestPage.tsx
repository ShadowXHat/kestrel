import React, { useState, useRef, useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import {
  ingestApi,
  IngestJobDto,
  IngestJobStatus,
  humanizeIngestError,
} from '../api/ingest';
import { useSignalR } from '../hooks/useSignalR';
import { useAuth } from '../hooks/useAuth';
import { hasRole } from '../utils/roleUtils';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '../components/ui/Card';
import { Button } from '../components/ui/Button';
import { Badge } from '../components/ui/Badge';
import { Table, TableHeader, TableBody, TableRow, TableHead, TableCell, TableEmptyState } from '../components/ui/Table';
import { useToast } from '../components/ui/Toast';
import {
  UploadCloud,
  FileCheck,
  AlertCircle,
  RefreshCw,
  HardDrive,
  ChevronDown,
  ChevronUp,
  AlertTriangle,
  Wifi,
  WifiOff,
  Lock,
  Loader2,
  Copy,
  Check,
} from 'lucide-react';

export const IngestPage: React.FC = () => {
  const { toast } = useToast();
  const { roles } = useAuth();
  const queryClient = useQueryClient();
  const fileInputRef = useRef<HTMLInputElement>(null);

  const canUpload = hasRole(roles, 'Analyst');

  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [isUploading, setIsUploading] = useState(false);
  const [uploadProgress, setUploadProgress] = useState<number>(0);
  const [isDragging, setIsDragging] = useState(false);
  const [humanErrorMsg, setHumanErrorMsg] = useState<string | null>(null);
  const [expandedJobId, setExpandedJobId] = useState<string | null>(null);
  const [copiedJobId, setCopiedJobId] = useState<string | null>(null);

  // SignalR socket connection to /hubs/events subscribing to group "ingest-jobs"
  const { status: socketStatus, isConnected, onEvent } = useSignalR({
    hubUrl: '/hubs/events',
    autoSubscribeGroup: 'ingest-jobs',
  });

  // Query recent jobs with fallback polling when socket is disconnected
  const {
    data: jobs = [],
    isLoading: isLoadingJobs,
    refetch: refetchJobs,
  } = useQuery<IngestJobDto[]>({
    queryKey: ['ingest', 'jobs'],
    queryFn: () => ingestApi.listJobs(50),
    // Disable polling when SignalR socket is live; fallback to 3s polling if socket drops
    refetchInterval: isConnected ? false : 3000,
  });

  // Query details for expanded job (includes retained per-record errors)
  const { data: expandedJobDetail, isLoading: isLoadingDetail } = useQuery<IngestJobDto>({
    queryKey: ['ingest', 'job-detail', expandedJobId],
    queryFn: () => ingestApi.getJob(expandedJobId!),
    enabled: !!expandedJobId,
  });

  // SignalR live update event listener
  useEffect(() => {
    const cleanup = onEvent<IngestJobDto>('IngestProgress', (updatedJob) => {
      queryClient.setQueryData<IngestJobDto[]>(['ingest', 'jobs'], (oldJobs) => {
        if (!oldJobs) return [updatedJob];
        const index = oldJobs.findIndex((j) => j.id === updatedJob.id);
        if (index >= 0) {
          const newJobs = [...oldJobs];
          newJobs[index] = updatedJob;
          return newJobs;
        }
        return [updatedJob, ...oldJobs];
      });

      // If currently expanded job receives progress update, invalidate detail cache too
      if (expandedJobId === updatedJob.id) {
        queryClient.invalidateQueries({ queryKey: ['ingest', 'job-detail', expandedJobId] });
      }
    });

    return cleanup;
  }, [onEvent, queryClient, expandedJobId]);

  const validateAndSetFile = (file: File) => {
    setHumanErrorMsg(null);

    // Client-side extension check
    if (!file.name.toLowerCase().endsWith('.evtx')) {
      setHumanErrorMsg("This doesn't look like a valid EVTX file (must have .evtx extension).");
      setSelectedFile(null);
      return;
    }

    setSelectedFile(file);
  };

  const handleFileDrop = (e: React.DragEvent<HTMLDivElement>) => {
    e.preventDefault();
    setIsDragging(false);
    if (!canUpload) return;

    if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
      validateAndSetFile(e.dataTransfer.files[0]);
    }
  };

  const handleFileSelect = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files.length > 0) {
      validateAndSetFile(e.target.files[0]);
    }
  };

  const handleUploadSubmit = async () => {
    if (!selectedFile || !canUpload) return;

    setIsUploading(true);
    setUploadProgress(0);
    setHumanErrorMsg(null);

    try {
      await ingestApi.uploadEvtx(selectedFile, (percent) => {
        setUploadProgress(percent);
      });

      toast({
        title: 'EVTX Upload Accepted',
        description: `File "${selectedFile.name}" enqueued for background parsing.`,
        variant: 'success',
      });

      setSelectedFile(null);
      if (fileInputRef.current) fileInputRef.current.value = '';

      refetchJobs();
    } catch (err: any) {
      const message = humanizeIngestError(err);
      setHumanErrorMsg(message);
      toast({
        title: 'Upload Rejected',
        description: message,
        variant: 'critical',
      });
    } finally {
      setIsUploading(false);
    }
  };

  const toggleExpandJob = (jobId: string) => {
    setExpandedJobId((prev) => (prev === jobId ? null : jobId));
  };

  const handleCopyJobId = (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    navigator.clipboard.writeText(id);
    setCopiedJobId(id);
    setTimeout(() => setCopiedJobId(null), 2000);
  };

  const formatBytes = (bytes: number): string => {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const formatTimestamp = (utcStr?: string | null): string => {
    if (!utcStr) return '—';
    try {
      return new Date(utcStr).toLocaleString();
    } catch {
      return utcStr;
    }
  };

  const renderStatusBadge = (status: IngestJobStatus) => {
    switch (status) {
      case 'queued':
        return (
          <Badge severity="medium" className="bg-amber-950/40 text-amber-300 border-amber-500/30">
            Queued
          </Badge>
        );
      case 'running':
        return (
          <Badge severity="info" className="bg-sky-950/60 text-sky-300 border-sky-500/40 flex items-center gap-1.5">
            <Loader2 className="w-3 h-3 animate-spin text-sky-400" />
            <span>Running</span>
          </Badge>
        );
      case 'completed':
        return (
          <Badge severity="low" className="bg-emerald-950/60 text-emerald-300 border-emerald-500/40">
            Completed
          </Badge>
        );
      case 'completed_with_errors':
        return (
          <Badge severity="medium" className="bg-amber-950/80 text-amber-300 border-amber-500/50">
            Completed w/ Errors
          </Badge>
        );
      case 'failed':
        return (
          <Badge severity="critical" className="bg-rose-950/80 text-rose-300 border-rose-500/50">
            Failed
          </Badge>
        );
      case 'canceled':
        return (
          <Badge variant="outline" className="bg-slate-900/60 text-slate-400 border-slate-700">
            Canceled
          </Badge>
        );
      default:
        return <Badge variant="outline">{status}</Badge>;
    }
  };

  return (
    <div className="p-6 md:p-10 max-w-7xl mx-auto space-y-8">
      {/* Header & Connection State Bar */}
      <div className="flex flex-col md:flex-row md:items-center justify-between gap-4 pb-2 border-b border-kestrel-border/60">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/30 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.25)]">
              <UploadCloud className="w-4.5 h-4.5" />
            </div>
            <span>EVTX Log Ingestion Engine</span>
          </h2>
          <p className="text-xs text-kestrel-muted mt-1">
            Streamed binary upload with server-side magic byte validation, bounded queue staging, and real-time SignalR status pushes.
          </p>
        </div>

        {/* Honest Connection State Display */}
        <div
          className={`flex items-center gap-2.5 px-4 py-2 rounded-full border text-xs font-mono select-none shadow-sm backdrop-blur-md ${
            socketStatus === 'connected'
              ? 'bg-emerald-950/40 border-emerald-500/40 text-emerald-300'
              : socketStatus === 'reconnecting'
              ? 'bg-amber-950/40 border-amber-500/40 text-amber-300 animate-pulse'
              : 'bg-rose-950/40 border-rose-500/40 text-rose-300'
          }`}
        >
          {socketStatus === 'connected' ? (
            <>
              <Wifi className="w-4 h-4 text-emerald-400 shrink-0" />
              <div className="flex flex-col">
                <span className="font-semibold text-emerald-300">SignalR Push Active</span>
                <span className="text-[10px] text-emerald-400/80">Group: ingest-jobs (No Polling)</span>
              </div>
            </>
          ) : socketStatus === 'reconnecting' ? (
            <>
              <WifiOff className="w-4 h-4 text-amber-400 shrink-0" />
              <div className="flex flex-col">
                <span className="font-semibold text-amber-300">SignalR Reconnecting...</span>
                <span className="text-[10px] text-amber-400/80">Retrying socket connection</span>
              </div>
            </>
          ) : (
            <>
              <WifiOff className="w-4 h-4 text-rose-400 shrink-0" />
              <div className="flex flex-col">
                <span className="font-semibold text-rose-300">SignalR Disconnected</span>
                <span className="text-[10px] text-rose-400/80">Fallback Polling Active (3s)</span>
              </div>
            </>
          )}
        </div>
      </div>

      {/* Upload Zone Section (Analyst+ Only) */}
      <Card variant="glass" className="rounded-xl border-kestrel-border">
        <CardHeader>
          <div className="flex items-center justify-between">
            <CardTitle className="text-base flex items-center gap-2">
              <UploadCloud className="w-5 h-5 text-sky-400" />
              <span>Upload EVTX File</span>
            </CardTitle>
            <Badge
              variant="outline"
              className={canUpload ? 'border-sky-500/40 text-sky-300' : 'border-amber-500/40 text-amber-300'}
            >
              {canUpload ? 'Analyst+ Permission Active' : 'Viewer Role (Upload Locked)'}
            </Badge>
          </div>
          <CardDescription>
            Streams raw EVTX bytes directly to staged storage. Server validates EVTX magic bytes (<code className="text-sky-300">ElfFile</code>) before parsing.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          {!canUpload ? (
            /* Restricted Upload Notice for Viewers */
            <div className="p-6 bg-amber-950/20 border border-amber-500/30 rounded-xl flex items-start gap-3 text-xs backdrop-blur-sm">
              <Lock className="w-5 h-5 text-amber-400 shrink-0 mt-0.5" />
              <div className="space-y-1">
                <h4 className="font-semibold text-amber-300">Analyst or Admin Role Required</h4>
                <p className="text-kestrel-muted leading-relaxed">
                  Your current user account has <span className="text-white font-mono">Viewer</span> privileges. Viewing job status history and live ingestion progress is available, but submitting raw EVTX files requires an <span className="text-sky-300 font-mono">Analyst</span> or <span className="text-purple-300 font-mono">Admin</span> role.
                </p>
              </div>
            </div>
          ) : (
            /* Drag and Drop Zone for Analyst+ */
            <>
              <div
                onDragOver={(e) => {
                  e.preventDefault();
                  setIsDragging(true);
                }}
                onDragLeave={() => setIsDragging(false)}
                onDrop={handleFileDrop}
                onClick={() => fileInputRef.current?.click()}
                className={`border-2 border-dashed rounded-xl p-8 text-center cursor-pointer transition-all duration-200 ${
                  isDragging
                    ? 'border-sky-400 bg-sky-950/40 shadow-[0_0_20px_rgba(14,165,233,0.2)]'
                    : 'border-kestrel-border hover:border-sky-500/50 bg-[#0b1019]/60 hover:bg-[#0b1019]'
                } ${isUploading ? 'pointer-events-none opacity-60' : ''}`}
              >
                <input
                  ref={fileInputRef}
                  type="file"
                  accept=".evtx"
                  onChange={handleFileSelect}
                  className="hidden"
                />

                <div className="flex flex-col items-center justify-center gap-3">
                  <div className="w-12 h-12 rounded-full bg-sky-950/60 border border-sky-500/30 flex items-center justify-center text-kestrel-accent">
                    <HardDrive className="w-6 h-6" />
                  </div>

                  <div>
                    <p className="text-sm font-semibold text-kestrel-text">
                      {selectedFile ? selectedFile.name : 'Click to browse or drop EVTX file here'}
                    </p>
                    <p className="text-xs text-kestrel-subtle mt-1 font-mono">
                      {selectedFile
                        ? `Selected File Size: ${formatBytes(selectedFile.size)}`
                        : 'Supports Security.evtx, Sysmon.evtx, System.evtx, Application.evtx'}
                    </p>
                  </div>
                </div>
              </div>

              {/* Plain Human Server / Client Error Alert */}
              {humanErrorMsg && (
                <div className="flex items-center gap-2.5 p-3.5 bg-rose-950/70 border border-rose-500/40 rounded-md text-xs text-rose-300 font-medium">
                  <AlertCircle className="w-4 h-4 text-rose-400 shrink-0" />
                  <span>{humanErrorMsg}</span>
                </div>
              )}

              {/* Active Upload Progress Bar */}
              {isUploading && (
                <div className="space-y-2 p-4 bg-kestrel-base border border-kestrel-border rounded-lg">
                  <div className="flex justify-between text-xs font-mono">
                    <span className="text-kestrel-muted">Streaming raw body to server...</span>
                    <span className="text-kestrel-accent font-bold">{uploadProgress}%</span>
                  </div>
                  <div className="w-full h-2 bg-kestrel-surface rounded-full overflow-hidden">
                    <div
                      className="h-full bg-kestrel-accent transition-all duration-150"
                      style={{ width: `${uploadProgress}%` }}
                    />
                  </div>
                </div>
              )}

              {/* Upload Actions */}
              <div className="flex justify-end gap-3 pt-2">
                {selectedFile && (
                  <Button
                    variant="secondary"
                    size="sm"
                    onClick={() => setSelectedFile(null)}
                    disabled={isUploading}
                  >
                    Clear Selection
                  </Button>
                )}

                <Button
                  variant="primary"
                  size="sm"
                  onClick={handleUploadSubmit}
                  disabled={!selectedFile || isUploading}
                  isLoading={isUploading}
                  leftIcon={<FileCheck className="w-4 h-4" />}
                >
                  {isUploading ? 'Streaming Upload...' : 'Submit EVTX for Parsing'}
                </Button>
              </div>
            </>
          )}
        </CardContent>
      </Card>

      {/* Ingest Job Queue Table */}
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <div>
            <h3 className="text-base font-semibold text-white font-sans flex items-center gap-2">
              Ingest Job History & Queue
              <span className="text-xs font-mono px-2 py-0.5 rounded bg-kestrel-surface text-kestrel-muted border border-kestrel-border">
                {jobs.length} jobs
              </span>
            </h3>
            <p className="text-xs text-kestrel-muted">
              Live updates via SignalR group <code className="text-kestrel-accent">ingest-jobs</code>. Click row to expand per-record errors.
            </p>
          </div>

          <Button
            variant="ghost"
            size="sm"
            onClick={() => refetchJobs()}
            isLoading={isLoadingJobs}
            leftIcon={<RefreshCw className="w-3.5 h-3.5" />}
          >
            Refresh Jobs
          </Button>
        </div>

        <Table>
          <TableHeader>
            <TableRow>
              <TableHead className="w-8"></TableHead>
              <TableHead>Job ID</TableHead>
              <TableHead>File Name</TableHead>
              <TableHead>File Size</TableHead>
              <TableHead>Status</TableHead>
              <TableHead>Records (Parsed / Total)</TableHead>
              <TableHead>Submitted</TableHead>
              <TableHead>Finished</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {jobs.length === 0 ? (
              <TableEmptyState
                colSpan={8}
                message="No EVTX files ingested yet. Submit a file above to begin."
                icon={<UploadCloud className="w-8 h-8 text-kestrel-subtle" />}
              />
            ) : (
              jobs.map((job) => {
                const isExpanded = expandedJobId === job.id;
                const totalRecs = job.totalRecords ?? job.recordsTotal ?? 0;
                const processedRecs = job.processedRecords ?? job.recordsParsed ?? 0;
                const failedRecs = job.failedRecords ?? job.recordsFailed ?? 0;

                return (
                  <React.Fragment key={job.id}>
                    <TableRow
                      isSelected={isExpanded}
                      className="cursor-pointer transition-colors"
                      onClick={() => toggleExpandJob(job.id)}
                    >
                      <TableCell className="text-kestrel-muted p-2">
                        {isExpanded ? (
                          <ChevronUp className="w-4 h-4 text-kestrel-accent" />
                        ) : (
                          <ChevronDown className="w-4 h-4" />
                        )}
                      </TableCell>
                      <TableCell isMono className="text-sky-400 font-bold">
                        {job.id.substring(0, 8)}...
                      </TableCell>
                      <TableCell className="font-mono text-white font-medium">
                        {job.fileName}
                      </TableCell>
                      <TableCell isMono>{formatBytes(job.fileSizeBytes)}</TableCell>
                      <TableCell>{renderStatusBadge(job.status)}</TableCell>
                      <TableCell isMono>
                        <div className="flex items-center gap-2">
                          <span className="text-emerald-400 font-bold">
                            {processedRecs.toLocaleString()}
                          </span>
                          {totalRecs > 0 && (
                            <span className="text-kestrel-subtle text-xs">
                              / {totalRecs.toLocaleString()}
                            </span>
                          )}
                          {failedRecs > 0 && (
                            <span className="text-amber-400 text-xs font-semibold">
                              ({failedRecs} failed)
                            </span>
                          )}
                        </div>
                      </TableCell>
                      <TableCell isMono className="text-kestrel-muted">
                        {formatTimestamp(job.createdAtUtc)}
                      </TableCell>
                      <TableCell isMono className="text-kestrel-muted">
                        {formatTimestamp(job.finishedAtUtc)}
                      </TableCell>
                    </TableRow>

                    {/* Expandable Per-Record Error Drawer */}
                    {isExpanded && (
                      <TableRow className="bg-kestrel-base/95 hover:bg-kestrel-base/95 border-b-2 border-kestrel-border">
                        <TableCell colSpan={8} className="p-4">
                          <div className="space-y-4 text-xs font-mono">
                            <div className="flex flex-wrap items-center justify-between gap-4 p-3 bg-kestrel-panel border border-kestrel-border rounded">
                              <div className="flex items-center gap-2">
                                <div>
                                  <span className="text-kestrel-subtle block text-[10px]">Full Job ID:</span>
                                  <span className="text-sky-300 font-semibold">{job.id}</span>
                                </div>
                                <button
                                  onClick={(e) => handleCopyJobId(job.id, e)}
                                  className="p-1 hover:bg-kestrel-surface rounded text-kestrel-muted hover:text-white transition-colors"
                                  title="Copy Job ID"
                                >
                                  {copiedJobId === job.id ? (
                                    <Check className="w-3.5 h-3.5 text-emerald-400" />
                                  ) : (
                                    <Copy className="w-3.5 h-3.5" />
                                  )}
                                </button>
                              </div>
                              <div>
                                <span className="text-kestrel-subtle block text-[10px]">Submitted By:</span>
                                <span className="text-white">{job.createdBy || 'SYSTEM'}</span>
                              </div>
                              <div>
                                <span className="text-kestrel-subtle block text-[10px]">Failed Record Count:</span>
                                <span className={failedRecs > 0 ? 'text-amber-400 font-bold' : 'text-emerald-400'}>
                                  {failedRecs}
                                </span>
                              </div>
                            </div>

                            {/* Job Terminal Error */}
                            {job.error && (
                              <div className="p-3 bg-rose-950/70 border border-rose-500/40 rounded text-rose-300">
                                <span className="font-semibold block mb-1 flex items-center gap-2">
                                  <AlertTriangle className="w-4 h-4 text-rose-400" />
                                  Terminal Job Failure:
                                </span>
                                <span>{job.error}</span>
                              </div>
                            )}

                            {/* Retained Per-Record Errors */}
                            <div className="space-y-2">
                              {(() => {
                                const recordErrors =
                                  expandedJobDetail?.firstErrors ||
                                  expandedJobDetail?.recordErrors ||
                                  job.firstErrors ||
                                  job.recordErrors ||
                                  [];

                                return (
                                  <>
                                    <span className="text-kestrel-subtle uppercase tracking-wider block font-semibold text-[10px]">
                                      Retained Per-Record Errors ({recordErrors.length})
                                    </span>

                                    {isLoadingDetail ? (
                                      <p className="text-kestrel-muted py-2">Loading detailed record errors...</p>
                                    ) : recordErrors.length > 0 ? (
                                      <div className="max-h-60 overflow-y-auto space-y-2 pr-1">
                                        {recordErrors.map((rec, idx) => {
                                          const recId = rec.recordId ?? rec.recordIndex ?? idx + 1;
                                          const reason = rec.reason || rec.error || 'Record parsing failure';

                                          return (
                                            <div
                                              key={idx}
                                              className="p-3 bg-kestrel-panel border border-amber-500/30 rounded space-y-1.5"
                                            >
                                              <div className="flex items-center justify-between text-amber-300 font-bold">
                                                <span>Record #{recId}</span>
                                              </div>
                                              <p className="text-kestrel-text font-sans">{reason}</p>
                                              {rec.rawXmlSnippet && (
                                                <pre className="p-2 bg-kestrel-base rounded text-[11px] text-emerald-400 overflow-x-auto font-mono border border-kestrel-border">
                                                  {rec.rawXmlSnippet}
                                                </pre>
                                              )}
                                            </div>
                                          );
                                        })}
                                      </div>
                                    ) : (
                                      <p className="text-kestrel-muted py-1 text-[11px]">
                                        No individual record parsing errors recorded for this job.
                                      </p>
                                    )}
                                  </>
                                );
                              })()}
                            </div>
                          </div>
                        </TableCell>
                      </TableRow>
                    )}
                  </React.Fragment>
                );
              })
            )}
          </TableBody>
        </Table>
      </section>
    </div>
  );
};

export default IngestPage;
