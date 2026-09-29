import React, { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  sigmaApi,
  SigmaRule,
  DetectionAlert,
  DetectionRunResult,
  runSigmaDetectionEngine,
  validateSigmaYaml,
} from '../api/sigma';
import { DetectionPanel } from '../components/DetectionPanel';
import { Badge } from '../components/ui/Badge';
import { Button } from '../components/ui/Button';
import { Modal } from '../components/ui/Modal';
import {
  ShieldAlert,
  Upload,
  Play,
  CheckCircle2,
  XCircle,
  RefreshCw,
  FileCode,
  Layers,
  Code,
  Sliders,
} from 'lucide-react';

export const DetectionPage: React.FC = () => {
  const navigate = useNavigate();

  const [rules, setRules] = useState<SigmaRule[]>([]);
  const [activeTab, setActiveTab] = useState<'rules' | 'alerts'>('rules');

  // Detection Run Execution State
  const [isScanning, setIsScanning] = useState(false);
  const [scanProgress, setScanProgress] = useState(0);
  const [scanResult, setScanResult] = useState<DetectionRunResult | null>(null);
  const [alerts, setAlerts] = useState<DetectionAlert[]>([]);

  // Upload Modal State
  const [isUploadModalOpen, setIsUploadModalOpen] = useState(false);
  const [uploadFiles, setUploadFiles] = useState<File[]>([]);
  const [uploadPreviews, setUploadPreviews] = useState<
    { file: File; isValid: boolean; title: string; errors: string[] }[]
  >([]);
  const [isProcessingUpload, setIsProcessingUpload] = useState(false);

  // View YAML Drawer State
  const [inspectRule, setInspectRule] = useState<SigmaRule | null>(null);

  // Load rules on mount
  const fetchRules = useCallback(async () => {
    try {
      const data = await sigmaApi.listRules();
      setRules(data);
    } catch (err) {
      console.error('Failed to fetch Sigma rules:', err);
    }
  }, []);

  useEffect(() => {
    fetchRules();
  }, [fetchRules]);

  // Handle Detection Execution
  const handleRunDetection = async () => {
    setIsScanning(true);
    setScanProgress(10);

    const interval = setInterval(() => {
      setScanProgress((prev) => {
        if (prev >= 90) {
          clearInterval(interval);
          return 90;
        }
        return prev + 25;
      });
    }, 120);

    try {
      const res = await runSigmaDetectionEngine(rules.filter((r) => r.enabled && r.status === 'valid'));
      setScanProgress(100);
      setTimeout(() => {
        setScanResult(res);
        setAlerts(res.alerts);
        setIsScanning(false);
        setActiveTab('alerts');
      }, 200);
    } catch (err) {
      console.error('Detection engine execution failed:', err);
      setIsScanning(false);
    }
  };

  // Toggle Rule Enabled State
  const handleToggleRule = async (id: string) => {
    const updated = await sigmaApi.toggleRuleEnabled(id);
    if (updated) {
      setRules((prev) => prev.map((r) => (r.id === id ? updated : r)));
    }
  };

  // Handle File Selection in Upload Modal
  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    if (!e.target.files) return;
    const fileList = Array.from(e.target.files);
    setUploadFiles(fileList);

    const previews = await Promise.all(
      fileList.map(async (file) => {
        const text = await file.text();
        const val = validateSigmaYaml(text);
        return {
          file,
          isValid: val.isValid,
          title: val.parsedRule?.title || file.name,
          errors: val.errors,
        };
      })
    );
    setUploadPreviews(previews);
  };

  // Process Batch Rule Upload
  const handleImportUploadedRules = async () => {
    if (uploadFiles.length === 0) return;
    setIsProcessingUpload(true);
    try {
      for (const file of uploadFiles) {
        await sigmaApi.uploadRuleFile(file);
      }
      await fetchRules();
      setIsUploadModalOpen(false);
      setUploadFiles([]);
      setUploadPreviews([]);
    } catch (err) {
      console.error('Upload failed:', err);
    } finally {
      setIsProcessingUpload(false);
    }
  };

  // Calculate Summary Metrics
  const totalRules = rules.length;
  const validRules = rules.filter((r) => r.status === 'valid').length;
  const invalidRules = rules.filter((r) => r.status === 'invalid').length;
  const enabledRules = rules.filter((r) => r.enabled).length;

  const totalTechniques = Array.from(
    new Set(rules.flatMap((r) => r.attckTechniques.map((t) => t.id)))
  ).length;

  return (
    <div className="p-6 md:p-8 max-w-[1600px] mx-auto space-y-6 flex flex-col min-h-[calc(100vh-4rem)]">
      {/* Top Title & Header Actions Bar */}
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-4 shrink-0">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/30 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.25)]">
              <ShieldAlert className="w-4.5 h-4.5" />
            </div>
            <span>Sigma Threat Detection Engine</span>
          </h2>
          <p className="text-xs text-kestrel-muted mt-1">
            Browser for compiled Sigma rule packs, MITRE ATT&CK enrichments, and live event log matching.
          </p>
        </div>

        {/* Action Controls */}
        <div className="flex flex-wrap items-center gap-3">
          <Button
            variant="secondary"
            size="sm"
            onClick={() => setIsUploadModalOpen(true)}
            leftIcon={<Upload className="w-3.5 h-3.5" />}
          >
            Upload .yml Rule
          </Button>

          <Button
            variant="secondary"
            size="sm"
            onClick={() => navigate('/rules')}
            leftIcon={<Sliders className="w-3.5 h-3.5" />}
          >
            Manage Rules in CRUD
          </Button>

          <Button
            variant="primary"
            size="sm"
            onClick={handleRunDetection}
            isLoading={isScanning}
            leftIcon={<Play className="w-3.5 h-3.5 fill-current" />}
            className="shadow-[0_0_15px_rgba(14,165,233,0.3)]"
          >
            Run Detection Engine
          </Button>
        </div>
      </div>

      {/* Progress Bar when Detection Engine is scanning */}
      {isScanning && (
        <div className="glass-panel p-4 rounded-xl border border-sky-500/40 space-y-2 animate-in fade-in duration-200">
          <div className="flex items-center justify-between text-xs font-mono">
            <span className="text-sky-300 flex items-center gap-2">
              <RefreshCw className="w-3.5 h-3.5 animate-spin text-sky-400" />
              Scanning EVTX event store against {enabledRules} active compiled Sigma rules...
            </span>
            <span className="text-white font-bold">{scanProgress}%</span>
          </div>
          <div className="w-full bg-[#070a0f] h-2 rounded-full overflow-hidden border border-kestrel-border">
            <div
              className="bg-gradient-to-r from-sky-500 to-emerald-400 h-full transition-all duration-150 rounded-full"
              style={{ width: `${scanProgress}%` }}
            />
          </div>
        </div>
      )}

      {/* Metrics Overview Strip */}
      <div className="grid grid-cols-2 sm:grid-cols-4 gap-4 shrink-0">
        <div className="glass-panel p-4 rounded-xl border border-kestrel-border/80 flex items-center justify-between">
          <div>
            <div className="text-[10px] font-mono uppercase text-kestrel-subtle tracking-wider">
              Total Sigma Rules
            </div>
            <div className="text-xl font-bold font-mono text-white mt-1">{totalRules}</div>
          </div>
          <div className="p-2.5 rounded-lg bg-sky-950/60 border border-sky-500/30 text-sky-400">
            <FileCode className="w-4 h-4" />
          </div>
        </div>

        <div className="glass-panel p-4 rounded-xl border border-kestrel-border/80 flex items-center justify-between">
          <div>
            <div className="text-[10px] font-mono uppercase text-kestrel-subtle tracking-wider">
              Compiled & Valid
            </div>
            <div className="text-xl font-bold font-mono text-emerald-400 mt-1">
              {validRules}{' '}
              <span className="text-xs font-normal text-kestrel-subtle">({enabledRules} active)</span>
            </div>
          </div>
          <div className="p-2.5 rounded-lg bg-emerald-950/60 border border-emerald-500/30 text-emerald-400">
            <CheckCircle2 className="w-4 h-4" />
          </div>
        </div>

        <div className="glass-panel p-4 rounded-xl border border-kestrel-border/80 flex items-center justify-between">
          <div>
            <div className="text-[10px] font-mono uppercase text-kestrel-subtle tracking-wider">
              Validation Errors
            </div>
            <div className="text-xl font-bold font-mono text-rose-400 mt-1">{invalidRules}</div>
          </div>
          <div className="p-2.5 rounded-lg bg-rose-950/60 border border-rose-500/30 text-rose-400">
            <XCircle className="w-4 h-4" />
          </div>
        </div>

        <div className="glass-panel p-4 rounded-xl border border-kestrel-border/80 flex items-center justify-between">
          <div>
            <div className="text-[10px] font-mono uppercase text-kestrel-subtle tracking-wider">
              ATT&CK Techniques
            </div>
            <div className="text-xl font-bold font-mono text-amber-300 mt-1">{totalTechniques}</div>
          </div>
          <div className="p-2.5 rounded-lg bg-amber-950/60 border border-amber-500/30 text-amber-400">
            <Layers className="w-4 h-4" />
          </div>
        </div>
      </div>

      {/* Main Tabs Navigation */}
      <div className="border-b border-kestrel-border flex items-center gap-6 shrink-0">
        <button
          onClick={() => setActiveTab('rules')}
          className={`pb-3 text-xs font-bold font-sans tracking-tight transition-colors relative flex items-center gap-2 ${
            activeTab === 'rules'
              ? 'text-sky-400 border-b-2 border-sky-400'
              : 'text-kestrel-muted hover:text-white'
          }`}
        >
          <FileCode className="w-4 h-4" />
          <span>Compiled Rule Pack Browser ({rules.length})</span>
        </button>

        <button
          onClick={() => setActiveTab('alerts')}
          className={`pb-3 text-xs font-bold font-sans tracking-tight transition-colors relative flex items-center gap-2 ${
            activeTab === 'alerts'
              ? 'text-sky-400 border-b-2 border-sky-400'
              : 'text-kestrel-muted hover:text-white'
          }`}
        >
          <ShieldAlert className="w-4 h-4" />
          <span>Detection Alerts Inbox ({alerts.length})</span>
          {scanResult && (
            <span className="text-[10px] font-mono px-1.5 py-0.5 rounded bg-sky-950 text-sky-400 border border-sky-500/30">
              {scanResult.executionTimeMs}ms
            </span>
          )}
        </button>
      </div>

      {/* Tab Content */}
      <div className="flex-1 min-h-0">
        {activeTab === 'rules' ? (
          /* Compiled Rule Pack Browser Table */
          <div className="glass-panel rounded-xl border border-kestrel-border overflow-hidden shadow-2xl">
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs font-mono">
                <thead className="bg-[#070a0f] text-kestrel-muted uppercase tracking-wider font-semibold text-[11px] border-b border-kestrel-border">
                  <tr>
                    <th className="py-3 px-4">Rule Title & Description</th>
                    <th className="py-3 px-4">Status</th>
                    <th className="py-3 px-4">Severity</th>
                    <th className="py-3 px-4">Category</th>
                    <th className="py-3 px-4">ATT&CK Techniques</th>
                    <th className="py-3 px-4 text-center">Enabled</th>
                    <th className="py-3 px-4 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-kestrel-border/60">
                  {rules.map((rule) => {
                    const isValid = rule.status === 'valid';
                    return (
                      <tr
                        key={rule.id}
                        className="hover:bg-kestrel-surface/60 transition-colors group"
                      >
                        {/* Title & Description */}
                        <td className="py-3 px-4 max-w-xs">
                          <div className="font-bold text-white font-sans text-xs group-hover:text-sky-300 transition-colors">
                            {rule.title}
                          </div>
                          <div className="text-[11px] text-kestrel-subtle truncate mt-0.5">
                            {rule.description}
                          </div>
                        </td>

                        {/* Status (Valid/Invalid) */}
                        <td className="py-3 px-4">
                          {isValid ? (
                            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-emerald-950/80 text-emerald-300 border border-emerald-500/30">
                              <CheckCircle2 className="w-3 h-3 text-emerald-400" />
                              VALID
                            </span>
                          ) : (
                            <span
                              className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-[10px] font-bold bg-rose-950/80 text-rose-300 border border-rose-500/30"
                              title={rule.validationErrors.join(', ')}
                            >
                              <XCircle className="w-3 h-3 text-rose-400" />
                              INVALID ({rule.validationErrors.length})
                            </span>
                          )}
                        </td>

                        {/* Severity */}
                        <td className="py-3 px-4 uppercase font-semibold">
                          <span
                            className={
                              rule.severity === 'critical'
                                ? 'text-rose-400 font-bold'
                                : rule.severity === 'high'
                                ? 'text-amber-400 font-bold'
                                : rule.severity === 'medium'
                                ? 'text-orange-300'
                                : 'text-sky-300'
                            }
                          >
                            {rule.severity}
                          </span>
                        </td>

                        {/* Category */}
                        <td className="py-3 px-4">
                          <span className="px-2 py-0.5 rounded bg-[#0b1019] text-sky-300 border border-kestrel-border text-[10px]">
                            {rule.category.replace('_', ' ')}
                          </span>
                        </td>

                        {/* ATT&CK Techniques */}
                        <td className="py-3 px-4">
                          <div className="flex flex-wrap gap-1">
                            {rule.attckTechniques.length === 0 ? (
                              <span className="text-kestrel-subtle text-[10px]">—</span>
                            ) : (
                              rule.attckTechniques.map((tech) => (
                                <span
                                  key={tech.id}
                                  className="px-1.5 py-0.5 rounded bg-amber-950/60 text-amber-300 border border-amber-500/30 text-[10px]"
                                  title={`${tech.tactic}: ${tech.name}`}
                                >
                                  {tech.id}
                                </span>
                              ))
                            )}
                          </div>
                        </td>

                        {/* Enabled Toggle */}
                        <td className="py-3 px-4 text-center">
                          <button
                            onClick={() => handleToggleRule(rule.id)}
                            disabled={!isValid}
                            className={`w-9 h-5 rounded-full transition-colors relative inline-flex items-center px-0.5 focus-ring ${
                              rule.enabled && isValid
                                ? 'bg-sky-500'
                                : 'bg-slate-700 opacity-60 cursor-not-allowed'
                            }`}
                            aria-label={`Toggle rule ${rule.title}`}
                          >
                            <span
                              className={`w-4 h-4 rounded-full bg-white transition-transform ${
                                rule.enabled && isValid ? 'translate-x-4' : 'translate-x-0'
                              }`}
                            />
                          </button>
                        </td>

                        {/* Actions */}
                        <td className="py-3 px-4 text-right">
                          <div className="flex items-center justify-end gap-2">
                            <button
                              onClick={() => setInspectRule(rule)}
                              className="text-xs text-kestrel-muted hover:text-white p-1 rounded hover:bg-kestrel-base"
                              title="View YAML Content"
                            >
                              <Code className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => navigate(`/rules?ruleId=${rule.id}`)}
                              className="text-xs text-sky-400 hover:text-sky-300 p-1 rounded hover:bg-kestrel-base"
                              title="Edit Rule in CRUD Manager"
                            >
                              <Sliders className="w-4 h-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
          </div>
        ) : (
          /* Detection Alerts Inbox Panel */
          <DetectionPanel
            alerts={alerts}
            isLoading={isScanning}
            onRefresh={handleRunDetection}
          />
        )}
      </div>

      {/* Upload Rules Modal */}
      <Modal
        isOpen={isUploadModalOpen}
        onClose={() => {
          setIsUploadModalOpen(false);
          setUploadFiles([]);
          setUploadPreviews([]);
        }}
        title="Upload Sigma Rule Pack (.yml / .yaml)"
      >
        <div className="space-y-4">
          <p className="text-xs text-kestrel-muted leading-relaxed">
            Upload custom Sigma rules in YAML format. The parser will automatically compile the rule, validate logsource and detection semantics, and tag MITRE ATT&CK techniques.
          </p>

          {/* Drag & Drop File Zone */}
          <div className="border-2 border-dashed border-sky-500/30 hover:border-sky-500/60 bg-sky-950/20 hover:bg-sky-950/30 rounded-xl p-6 text-center transition-all cursor-pointer relative">
            <input
              type="file"
              accept=".yml,.yaml"
              multiple
              onChange={handleFileChange}
              className="absolute inset-0 opacity-0 cursor-pointer w-full h-full"
            />
            <Upload className="w-8 h-8 text-sky-400 mx-auto mb-2" />
            <div className="text-xs font-semibold text-white font-sans">
              Click or drag .yml / .yaml files here
            </div>
            <div className="text-[11px] font-mono text-kestrel-subtle mt-1">
              Supports standard Sigma v2/v3 YAML rule packs
            </div>
          </div>

          {/* Selected Files Preview List */}
          {uploadPreviews.length > 0 && (
            <div className="space-y-2 max-h-48 overflow-y-auto pr-1">
              <div className="text-[10px] uppercase font-mono font-semibold text-kestrel-subtle">
                Parsed Rule Preview ({uploadPreviews.length} files)
              </div>
              {uploadPreviews.map((p, idx) => (
                <div
                  key={idx}
                  className={`p-2.5 rounded-lg border text-xs font-mono flex items-center justify-between ${
                    p.isValid
                      ? 'bg-emerald-950/30 border-emerald-500/40 text-emerald-200'
                      : 'bg-rose-950/30 border-rose-500/40 text-rose-200'
                  }`}
                >
                  <div className="space-y-0.5 truncate pr-2">
                    <div className="font-semibold font-sans truncate text-white">{p.title}</div>
                    <div className="text-[10px] text-kestrel-subtle">{p.file.name}</div>
                    {!p.isValid && (
                      <div className="text-[10px] text-rose-400">
                        {p.errors.join('; ')}
                      </div>
                    )}
                  </div>
                  {p.isValid ? (
                    <Badge severity="low" size="sm">Valid</Badge>
                  ) : (
                    <Badge severity="critical" size="sm">Invalid</Badge>
                  )}
                </div>
              ))}
            </div>
          )}

          {/* Modal Actions */}
          <div className="flex justify-end gap-3 pt-3 border-t border-kestrel-border/80">
            <Button
              variant="secondary"
              size="sm"
              onClick={() => {
                setIsUploadModalOpen(false);
                setUploadFiles([]);
                setUploadPreviews([]);
              }}
            >
              Cancel
            </Button>
            <Button
              variant="primary"
              size="sm"
              onClick={handleImportUploadedRules}
              disabled={uploadFiles.length === 0 || isProcessingUpload}
              isLoading={isProcessingUpload}
            >
              Import {uploadFiles.length} Rules
            </Button>
          </div>
        </div>
      </Modal>

      {/* Inspect Rule YAML Drawer Modal */}
      {inspectRule && (
        <Modal
          isOpen={!!inspectRule}
          onClose={() => setInspectRule(null)}
          title={`Sigma Rule: ${inspectRule.title}`}
        >
          <div className="space-y-3 font-mono text-xs">
            <div className="flex justify-between items-center bg-[#0b1019] p-2.5 rounded-lg border border-kestrel-border">
              <span className="text-kestrel-subtle">Rule ID:</span>
              <span className="text-sky-300 font-bold">{inspectRule.id}</span>
            </div>

            <pre className="p-4 bg-[#070a0f] rounded-xl border border-kestrel-border text-emerald-400 overflow-x-auto text-[11px] leading-relaxed max-h-96 whitespace-pre-wrap">
              {inspectRule.yaml}
            </pre>

            <div className="flex justify-end pt-2">
              <Button
                variant="secondary"
                size="sm"
                onClick={() => {
                  const ruleId = inspectRule.id;
                  setInspectRule(null);
                  navigate(`/rules?ruleId=${ruleId}`);
                }}
                leftIcon={<Sliders className="w-3.5 h-3.5" />}
              >
                Open in Editor & CRUD
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </div>
  );
};

export default DetectionPage;
