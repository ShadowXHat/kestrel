import React, { useState, useEffect, useMemo, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import {
  sigmaApi,
  SigmaRule,
  validateSigmaYaml,
  RuleValidationResult,
  extractAttckTechniques,
} from '../api/sigma';
import { Button } from '../components/ui/Button';
import {
  Sliders,
  Search,
  Plus,
  Save,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Copy,
  Trash2,
  Download,
  FileCode,
  Check,
} from 'lucide-react';

const CATEGORIES: { id: string; label: string }[] = [
  { id: 'ALL', label: 'All Categories' },
  { id: 'process_creation', label: 'Process Creation' },
  { id: 'lateral_movement', label: 'Lateral Movement' },
  { id: 'privilege_escalation', label: 'Privilege Escalation' },
  { id: 'credential_access', label: 'Credential Access' },
  { id: 'persistence', label: 'Persistence' },
  { id: 'defense_evasion', label: 'Defense Evasion' },
  { id: 'execution', label: 'Execution' },
];

const SAMPLE_YAML_TEMPLATE = `title: New Custom Sigma Rule
id: sig-custom-${Date.now().toString(36)}
status: experimental
description: Detects suspicious administrative tool usage.
author: SOC Analyst
date: ${new Date().toISOString().split('T')[0]}
logsource:
    category: process_creation
    product: windows
detection:
    selection:
        EventID: 4688
        NewProcessName|endswith: '\\cmd.exe'
    condition: selection
falsepositives:
    - System administrator actions
level: medium
tags:
    - attack.t1059
    - attack.execution`;

export const RulesManagerPage: React.FC = () => {
  const [searchParams] = useSearchParams();
  const targetRuleId = searchParams.get('ruleId');

  const [rules, setRules] = useState<SigmaRule[]>([]);
  const [selectedRuleId, setSelectedRuleId] = useState<string | null>(null);

  // Filter & Search State
  const [selectedCategory, setSelectedCategory] = useState<string>('ALL');
  const [searchQuery, setSearchQuery] = useState('');

  // Editor State
  const [yamlCode, setYamlCode] = useState<string>('');
  const [isSaving, setIsSaving] = useState(false);
  const [saveSuccess, setSaveSuccess] = useState(false);

  // Load Rules on mount
  const loadRules = useCallback(async () => {
    try {
      const data = await sigmaApi.listRules();
      setRules(data);
      if (data.length > 0) {
        if (targetRuleId && data.some((r) => r.id === targetRuleId)) {
          setSelectedRuleId(targetRuleId);
          const found = data.find((r) => r.id === targetRuleId);
          if (found) setYamlCode(found.yaml);
        } else {
          setSelectedRuleId(data[0].id);
          setYamlCode(data[0].yaml);
        }
      }
    } catch (err) {
      console.error('Failed to load rules:', err);
    }
  }, [targetRuleId]);

  useEffect(() => {
    loadRules();
  }, [loadRules]);

  // Selected Rule object
  const currentRule = useMemo(
    () => rules.find((r) => r.id === selectedRuleId) || null,
    [rules, selectedRuleId]
  );

  // Real-time live validation result
  const validationResult: RuleValidationResult = useMemo(() => {
    return validateSigmaYaml(yamlCode);
  }, [yamlCode]);

  // Filtered Rules List for Sidebar
  const filteredRules = useMemo(() => {
    return rules.filter((rule) => {
      if (selectedCategory !== 'ALL' && rule.category !== selectedCategory) {
        return false;
      }
      if (searchQuery.trim().length > 0) {
        const q = searchQuery.toLowerCase();
        const matchTitle = rule.title.toLowerCase().includes(q);
        const matchCategory = rule.category.toLowerCase().includes(q);
        const matchTags = rule.tags.some((t) => t.toLowerCase().includes(q));
        return matchTitle || matchCategory || matchTags;
      }
      return true;
    });
  }, [rules, selectedCategory, searchQuery]);

  // Select Rule in Sidebar
  const handleSelectRule = (rule: SigmaRule) => {
    setSelectedRuleId(rule.id);
    setYamlCode(rule.yaml);
    setSaveSuccess(false);
  };

  // Create New Rule Template
  const handleCreateNewRule = () => {
    const newId = `sig-custom-${Date.now().toString(36)}`;
    const newRuleYaml = SAMPLE_YAML_TEMPLATE.replace(/sig-custom-[a-z0-9]+/, newId);
    const newRule: SigmaRule = {
      id: newId,
      title: 'New Custom Sigma Rule',
      status: 'experimental',
      severity: 'medium',
      category: 'process_creation',
      description: 'Detects suspicious administrative tool usage.',
      author: 'SOC Analyst',
      date: new Date().toISOString().split('T')[0],
      logsource: { category: 'process_creation', product: 'windows' },
      tags: ['attack.t1059', 'attack.execution'],
      attckTechniques: extractAttckTechniques(['attack.t1059']),
      yaml: newRuleYaml,
      isCompiled: true,
      enabled: true,
      validationErrors: [],
    };

    setRules((prev) => [newRule, ...prev]);
    setSelectedRuleId(newRule.id);
    setYamlCode(newRuleYaml);
  };

  // Save Current Rule
  const handleSaveRule = async () => {
    if (!currentRule) return;
    setIsSaving(true);
    try {
      const updated = await sigmaApi.saveRule({
        ...currentRule,
        yaml: yamlCode,
      });

      setRules((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 2000);
    } catch (err) {
      console.error('Failed to save rule:', err);
    } finally {
      setIsSaving(false);
    }
  };

  // Duplicate Rule
  const handleDuplicateRule = () => {
    if (!currentRule) return;
    const dupId = `sig-dup-${Date.now().toString(36)}`;
    const dupTitle = `${currentRule.title} (Copy)`;
    const dupYaml = yamlCode
      .replace(`title: ${currentRule.title}`, `title: ${dupTitle}`)
      .replace(`id: ${currentRule.id}`, `id: ${dupId}`);

    const dupRule: SigmaRule = {
      ...currentRule,
      id: dupId,
      title: dupTitle,
      yaml: dupYaml,
    };

    setRules((prev) => [dupRule, ...prev]);
    setSelectedRuleId(dupId);
    setYamlCode(dupYaml);
  };

  // Delete Rule
  const handleDeleteRule = async () => {
    if (!currentRule) return;
    if (confirm(`Are you sure you want to delete "${currentRule.title}"?`)) {
      await sigmaApi.deleteRule(currentRule.id);
      const remaining = rules.filter((r) => r.id !== currentRule.id);
      setRules(remaining);
      if (remaining.length > 0) {
        setSelectedRuleId(remaining[0].id);
        setYamlCode(remaining[0].yaml);
      } else {
        setSelectedRuleId(null);
        setYamlCode('');
      }
    }
  };

  // Export YAML File
  const handleExportYaml = () => {
    if (!currentRule) return;
    const blob = new Blob([yamlCode], { type: 'text/yaml' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${currentRule.id || 'sigma-rule'}.yml`;
    a.click();
    URL.revokeObjectURL(url);
  };

  // Calculate line numbers for editor
  const yamlLines = yamlCode.split('\n');

  return (
    <div className="p-6 md:p-8 max-w-[1600px] mx-auto space-y-6 flex flex-col min-h-[calc(100vh-4rem)]">
      {/* Title & Header */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 shrink-0">
        <div>
          <h2 className="text-xl font-bold tracking-tight text-white font-sans flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-sky-950/80 border border-sky-500/30 flex items-center justify-center text-sky-400 shadow-[0_0_12px_rgba(14,165,233,0.25)]">
              <Sliders className="w-4.5 h-4.5" />
            </div>
            <span>Sigma Rule CRUD & YAML Editor</span>
          </h2>
          <p className="text-xs text-kestrel-muted mt-1">
            Author, edit, validate, and manage custom Sigma threat detection rules with real-time feedback.
          </p>
        </div>

        <Button
          variant="primary"
          size="sm"
          onClick={handleCreateNewRule}
          leftIcon={<Plus className="w-3.5 h-3.5" />}
        >
          New Sigma Rule
        </Button>
      </div>

      {/* Category Filter Pills */}
      <div className="flex items-center gap-2 overflow-x-auto pb-1 shrink-0 scrollbar-none">
        {CATEGORIES.map((cat) => {
          const isSelected = selectedCategory === cat.id;
          return (
            <button
              key={cat.id}
              onClick={() => setSelectedCategory(cat.id)}
              className={`px-3 py-1.5 rounded-lg text-xs font-mono font-medium transition-all shrink-0 focus-ring ${
                isSelected
                  ? 'bg-sky-500/20 text-sky-300 border border-sky-500/40 font-semibold shadow-[0_0_10px_rgba(14,165,233,0.15)]'
                  : 'bg-[#0b1019] text-kestrel-muted hover:text-white border border-kestrel-border/70 hover:bg-kestrel-surface'
              }`}
            >
              {cat.label}
            </button>
          );
        })}
      </div>

      {/* Main Split Layout: Left Catalog Sidebar vs Right Editor Panel */}
      <div className="flex-1 min-h-0 flex flex-col lg:flex-row gap-6">
        {/* Left: Rule Selection Catalog (320px width) */}
        <div className="w-full lg:w-80 glass-panel rounded-xl border border-kestrel-border flex flex-col shrink-0 overflow-hidden">
          {/* Search Box */}
          <div className="p-3 border-b border-kestrel-border/80 bg-kestrel-surface/50">
            <div className="relative">
              <Search className="w-4 h-4 text-kestrel-subtle absolute left-3 top-2.5" />
              <input
                type="text"
                placeholder="Filter rules by title, tag..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full bg-[#0b1019] text-xs text-white placeholder-kestrel-subtle/70 pl-9 pr-3 py-2 rounded-lg border border-kestrel-border focus-ring font-mono"
              />
            </div>
          </div>

          {/* Rules List */}
          <div className="flex-1 overflow-y-auto divide-y divide-kestrel-border/40 p-2 space-y-1">
            {filteredRules.length === 0 ? (
              <div className="p-6 text-center text-xs font-mono text-kestrel-subtle">
                No Sigma rules found matching current filter.
              </div>
            ) : (
              filteredRules.map((rule) => {
                const isSelected = selectedRuleId === rule.id;
                const isValid = rule.status === 'valid';
                return (
                  <button
                    key={rule.id}
                    onClick={() => handleSelectRule(rule)}
                    className={`w-full text-left p-3 rounded-lg transition-all flex flex-col gap-1.5 focus-ring ${
                      isSelected
                        ? 'bg-sky-950/70 border border-sky-500/40 text-white shadow-[0_0_12px_rgba(14,165,233,0.12)]'
                        : 'hover:bg-kestrel-surface/60 text-slate-300 border border-transparent'
                    }`}
                  >
                    <div className="flex items-center justify-between">
                      <span className="font-bold text-xs font-sans truncate tracking-tight text-white">
                        {rule.title}
                      </span>
                      {isValid ? (
                        <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400 shrink-0" />
                      ) : (
                        <XCircle className="w-3.5 h-3.5 text-rose-400 shrink-0" />
                      )}
                    </div>

                    <div className="flex items-center justify-between text-[10px] font-mono">
                      <span className="text-kestrel-subtle truncate max-w-[150px]">
                        {rule.category.replace('_', ' ')}
                      </span>
                      <span
                        className={
                          rule.severity === 'critical'
                            ? 'text-rose-400 font-bold'
                            : rule.severity === 'high'
                            ? 'text-amber-400 font-bold'
                            : 'text-sky-300 font-semibold'
                        }
                      >
                        {rule.severity.toUpperCase()}
                      </span>
                    </div>
                  </button>
                );
              })
            )}
          </div>
        </div>

        {/* Right: Custom YAML Editor & Live Validation Inspector */}
        <div className="flex-1 glass-panel rounded-xl border border-kestrel-border flex flex-col overflow-hidden min-h-[550px]">
          {currentRule ? (
            <>
              {/* Editor Header Bar */}
              <div className="p-4 border-b border-kestrel-border/80 bg-kestrel-surface/70 flex flex-col sm:flex-row sm:items-center justify-between gap-3 shrink-0">
                <div className="flex items-center gap-3">
                  <div className="p-2 rounded-lg bg-sky-950/80 border border-sky-500/30 text-sky-400">
                    <FileCode className="w-4 h-4" />
                  </div>
                  <div>
                    <h3 className="text-sm font-bold text-white font-sans flex items-center gap-2">
                      <span>{validationResult.parsedRule?.title || currentRule.title}</span>
                      {validationResult.isValid ? (
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-mono font-bold bg-emerald-950 text-emerald-300 border border-emerald-500/30">
                          Valid YAML
                        </span>
                      ) : (
                        <span className="px-2 py-0.5 rounded-full text-[10px] font-mono font-bold bg-rose-950 text-rose-300 border border-rose-500/30">
                          Syntax Errors
                        </span>
                      )}
                    </h3>
                    <div className="text-[11px] font-mono text-kestrel-subtle mt-0.5">
                      ID: {currentRule.id} • Author: {currentRule.author}
                    </div>
                  </div>
                </div>

                {/* Editor Action Buttons */}
                <div className="flex items-center gap-2">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={handleExportYaml}
                    leftIcon={<Download className="w-3.5 h-3.5" />}
                    title="Export .yml"
                  >
                    Export
                  </Button>

                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={handleDuplicateRule}
                    leftIcon={<Copy className="w-3.5 h-3.5" />}
                    title="Duplicate Rule"
                  >
                    Duplicate
                  </Button>

                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={handleDeleteRule}
                    leftIcon={<Trash2 className="w-3.5 h-3.5 text-rose-400" />}
                    className="hover:text-rose-300"
                    title="Delete Rule"
                  >
                    Delete
                  </Button>

                  <Button
                    variant="primary"
                    size="sm"
                    onClick={handleSaveRule}
                    isLoading={isSaving}
                    leftIcon={saveSuccess ? <Check className="w-3.5 h-3.5 text-emerald-400" /> : <Save className="w-3.5 h-3.5" />}
                  >
                    {saveSuccess ? 'Saved!' : 'Save Rule'}
                  </Button>
                </div>
              </div>

              {/* Validation Status Feedback Banner */}
              <div
                className={`p-3 text-xs font-mono border-b flex items-center justify-between ${
                  validationResult.isValid
                    ? 'bg-emerald-950/40 text-emerald-200 border-emerald-500/30'
                    : 'bg-rose-950/40 text-rose-200 border-rose-500/30'
                }`}
              >
                <div className="flex items-center gap-2">
                  {validationResult.isValid ? (
                    <CheckCircle2 className="w-4 h-4 text-emerald-400 shrink-0" />
                  ) : (
                    <AlertTriangle className="w-4 h-4 text-rose-400 shrink-0" />
                  )}
                  <span>
                    {validationResult.isValid
                      ? 'Sigma rule compiled successfully. Ready for detection engine execution.'
                      : `Validation failed: ${validationResult.errors.join('; ')}`}
                  </span>
                </div>

                {validationResult.parsedRule?.attckTechniques &&
                  validationResult.parsedRule.attckTechniques.length > 0 && (
                    <div className="flex items-center gap-1.5 shrink-0">
                      <span className="text-[10px] text-kestrel-subtle">ATT&CK Tags:</span>
                      {validationResult.parsedRule.attckTechniques.map((t) => (
                        <span
                          key={t.id}
                          className="px-1.5 py-0.5 rounded bg-amber-950/80 text-amber-300 border border-amber-500/30 text-[10px]"
                        >
                          {t.id}
                        </span>
                      ))}
                    </div>
                  )}
              </div>

              {/* Monospaced Dark SOC Code Editor */}
              <div className="flex-1 flex overflow-hidden bg-[#070a0f] relative font-mono text-xs">
                {/* Line Numbers Gutter */}
                <div className="w-12 bg-[#0b1019] text-kestrel-subtle/50 py-4 select-none text-right pr-3 border-r border-kestrel-border/60 shrink-0 font-mono text-[11px] leading-relaxed">
                  {yamlLines.map((_, i) => (
                    <div key={i}>{i + 1}</div>
                  ))}
                </div>

                {/* Code Textarea Area */}
                <textarea
                  value={yamlCode}
                  onChange={(e) => setYamlCode(e.target.value)}
                  spellCheck={false}
                  className="w-full h-full bg-transparent text-emerald-400 p-4 font-mono text-xs leading-relaxed focus:outline-none resize-none overflow-y-auto"
                />
              </div>

              {/* Editor Footer Info */}
              <div className="p-2.5 bg-[#0b1019] border-t border-kestrel-border/80 flex items-center justify-between text-[11px] font-mono text-kestrel-subtle">
                <div className="flex items-center gap-4">
                  <span>Lines: {yamlLines.length}</span>
                  <span>Characters: {yamlCode.length}</span>
                  <span>Format: YAML (Sigma v2/v3)</span>
                </div>
                <div>
                  Category: <span className="text-sky-300 font-semibold">{validationResult.parsedRule?.category || currentRule.category}</span>
                </div>
              </div>
            </>
          ) : (
            <div className="flex-1 flex items-center justify-center p-12 text-center text-kestrel-subtle font-mono text-xs">
              Select a Sigma rule from the left catalog or click "New Sigma Rule" to begin editing.
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default RulesManagerPage;
