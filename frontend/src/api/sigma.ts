import { EventRecord } from './events';

export type SigmaRuleStatus = 'valid' | 'invalid' | 'experimental' | 'test' | 'deprecated';
export type SigmaRuleSeverity = 'critical' | 'high' | 'medium' | 'low' | 'informational';

export type SigmaCategory =
  | 'process_creation'
  | 'lateral_movement'
  | 'privilege_escalation'
  | 'credential_access'
  | 'persistence'
  | 'defense_evasion'
  | 'execution'
  | 'network';

export interface AttckTechnique {
  id: string; // e.g., T1059.001
  name: string; // e.g., PowerShell
  tactic: string; // e.g., Execution
  url: string;
}

export interface SigmaRule {
  id: string;
  title: string;
  status: SigmaRuleStatus;
  severity: SigmaRuleSeverity;
  category: SigmaCategory;
  description: string;
  author: string;
  date: string;
  modified?: string;
  logsource: {
    category?: string;
    product?: string;
    service?: string;
  };
  tags: string[]; // e.g. ["attack.t1059.001", "attack.execution"]
  attckTechniques: AttckTechnique[];
  yaml: string;
  isCompiled: boolean;
  enabled: boolean;
  validationErrors: string[];
}

export interface RuleValidationResult {
  isValid: boolean;
  errors: string[];
  warnings: string[];
  parsedRule?: Partial<SigmaRule>;
}

export interface DetectionAlert {
  id: string;
  ruleId: string;
  ruleTitle: string;
  ruleCategory: SigmaCategory;
  severity: SigmaRuleSeverity;
  matchingEventCount: number;
  attckTechniques: AttckTechnique[];
  matchedEvents: EventRecord[];
  firstSeenUtc: string;
  lastSeenUtc: string;
  status: 'new' | 'investigating' | 'resolved' | 'false_positive';
}

export interface DetectionRunResult {
  executionTimeMs: number;
  totalEventsScanned: number;
  totalRulesEvaluated: number;
  totalAlertsGenerated: number;
  alerts: DetectionAlert[];
}

// ============================================================================
// ATT&CK MAPPING DICTIONARY
// ============================================================================

export const ATTCK_MAP: Record<string, AttckTechnique> = {
  'T1059.001': {
    id: 'T1059.001',
    name: 'PowerShell',
    tactic: 'Execution',
    url: 'https://attack.mitre.org/techniques/T1059/001/',
  },
  'T1003.001': {
    id: 'T1003.001',
    name: 'LSASS Memory Dump',
    tactic: 'Credential Access',
    url: 'https://attack.mitre.org/techniques/T1003/001/',
  },
  'T1021.002': {
    id: 'T1021.002',
    name: 'SMB/Windows Admin Shares',
    tactic: 'Lateral Movement',
    url: 'https://attack.mitre.org/techniques/T1021/002/',
  },
  'T1078': {
    id: 'T1078',
    name: 'Valid Accounts',
    tactic: 'Defense Evasion / Initial Access',
    url: 'https://attack.mitre.org/techniques/T1078/',
  },
  'T1053.005': {
    id: 'T1053.005',
    name: 'Scheduled Task',
    tactic: 'Persistence',
    url: 'https://attack.mitre.org/techniques/T1053/005/',
  },
  'T1070.001': {
    id: 'T1070.001',
    name: 'Clear Windows Event Logs',
    tactic: 'Defense Evasion',
    url: 'https://attack.mitre.org/techniques/T1070/001/',
  },
  'T1136.001': {
    id: 'T1136.001',
    name: 'Create Local Account',
    tactic: 'Persistence',
    url: 'https://attack.mitre.org/techniques/T1136/001/',
  },
};

// Helper to extract ATT&CK technique details from tags like 'attack.t1059.001'
export function extractAttckTechniques(tags: string[]): AttckTechnique[] {
  const techniques: AttckTechnique[] = [];
  tags.forEach((tag) => {
    const cleanTag = tag.toLowerCase().trim();
    if (cleanTag.startsWith('attack.t')) {
      const techId = cleanTag.replace('attack.', '').toUpperCase();
      if (ATTCK_MAP[techId]) {
        techniques.push(ATTCK_MAP[techId]);
      } else {
        techniques.push({
          id: techId,
          name: `Technique ${techId}`,
          tactic: 'Unknown Tactic',
          url: `https://attack.mitre.org/techniques/${techId.replace('.', '/')}/`,
        });
      }
    }
  });
  return techniques;
}

// ============================================================================
// INITIAL REALISTIC SIGMA RULES PACK
// ============================================================================

const INITIAL_RULES: SigmaRule[] = [
  {
    id: 'sig-001-powershell-encoded',
    title: 'Encoded PowerShell Script Execution',
    status: 'valid',
    severity: 'high',
    category: 'execution',
    description: 'Detects execution of PowerShell commands using Base64 encoded flags (-e, -enc, -encodedcommand), often used by attackers to obfuscate malicious payloads.',
    author: 'Kestrel SOC Threat Team',
    date: '2026-01-15',
    logsource: {
      category: 'process_creation',
      product: 'windows',
    },
    tags: ['attack.t1059.001', 'attack.execution'],
    attckTechniques: [ATTCK_MAP['T1059.001']],
    yaml: `title: Encoded PowerShell Script Execution
id: sig-001-powershell-encoded
status: stable
description: Detects execution of PowerShell commands using Base64 encoded flags.
author: Kestrel SOC Threat Team
date: 2026-01-15
logsource:
    category: process_creation
    product: windows
detection:
    selection:
        EventID: 4688
        NewProcessName|endswith: '\\powershell.exe'
        CommandLine|contains:
            - '-EncodedCommand'
            - '-enc'
            - '-e '
    condition: selection
falsepositives:
    - Administrative maintenance scripts
level: high`,
    isCompiled: true,
    enabled: true,
    validationErrors: [],
  },
  {
    id: 'sig-002-lsass-memory-access',
    title: 'LSASS Process Memory Dump Attempt',
    status: 'valid',
    severity: 'critical',
    category: 'credential_access',
    description: 'Detects process access requests targeting lsass.exe with specific access masks characteristic of Mimikatz or procdump credential harvesting.',
    author: 'Florian Roth (Sigma Pack)',
    date: '2025-11-20',
    logsource: {
      category: 'process_access',
      product: 'windows',
    },
    tags: ['attack.t1003.001', 'attack.credential_access'],
    attckTechniques: [ATTCK_MAP['T1003.001']],
    yaml: `title: LSASS Process Memory Dump Attempt
id: sig-002-lsass-memory-access
status: stable
description: Detects process access targeting lsass.exe for credential dumping.
author: Florian Roth
date: 2025-11-20
logsource:
    category: process_access
    product: windows
detection:
    selection:
        TargetImage|endswith: '\\lsass.exe'
        GrantedAccess:
            - '0x1010'
            - '0x1400'
            - '0x1f0fff'
    condition: selection
falsepositives:
    - Antivirus and EDR products performing process memory inspection
level: critical`,
    isCompiled: true,
    enabled: true,
    validationErrors: [],
  },
  {
    id: 'sig-003-psexec-service-install',
    title: 'PsExec Service Installation Detected',
    status: 'valid',
    severity: 'high',
    category: 'lateral_movement',
    description: 'Detects installation of the default Sysinternals PsExec service (PSEXESVC) used for remote service execution and lateral movement.',
    author: 'Kestrel SOC Threat Team',
    date: '2026-02-01',
    logsource: {
      category: 'system',
      product: 'windows',
      service: 'system',
    },
    tags: ['attack.t1021.002', 'attack.lateral_movement'],
    attckTechniques: [ATTCK_MAP['T1021.002']],
    yaml: `title: PsExec Service Installation Detected
id: sig-003-psexec-service-install
status: stable
description: Detects installation of default PsExec service.
author: Kestrel SOC Threat Team
date: 2026-02-01
logsource:
    category: system
    product: windows
detection:
    selection:
        EventID: 7045
        ServiceName: 'PSEXESVC'
    condition: selection
falsepositives:
    - Legitimate Sysinternals PsExec administrative usage
level: high`,
    isCompiled: true,
    enabled: true,
    validationErrors: [],
  },
  {
    id: 'sig-004-audit-log-cleared',
    title: 'Security Audit Log Cleared',
    status: 'valid',
    severity: 'critical',
    category: 'defense_evasion',
    description: 'Detects clearing of the Windows Security Event Log (Event ID 1102), a key indicator of attacker anti-forensic activity.',
    author: 'Sigma Community',
    date: '2025-10-10',
    logsource: {
      category: 'security',
      product: 'windows',
    },
    tags: ['attack.t1070.001', 'attack.defense_evasion'],
    attckTechniques: [ATTCK_MAP['T1070.001']],
    yaml: `title: Security Audit Log Cleared
id: sig-004-audit-log-cleared
status: stable
description: Detects clearing of Windows Security Log.
author: Sigma Community
date: 2025-10-10
logsource:
    category: security
    product: windows
detection:
    selection:
        EventID: 1102
    condition: selection
falsepositives:
    - Log rotation scripts by systems administrator
level: critical`,
    isCompiled: true,
    enabled: true,
    validationErrors: [],
  },
  {
    id: 'sig-005-backdoor-user-created',
    title: 'New User Account Creation via Event 4720',
    status: 'valid',
    severity: 'medium',
    category: 'persistence',
    description: 'Detects creation of a new local or domain user account, which may indicate unauthorized persistence setup.',
    author: 'Kestrel Incident Response',
    date: '2026-03-04',
    logsource: {
      category: 'security',
      product: 'windows',
    },
    tags: ['attack.t1136.001', 'attack.persistence'],
    attckTechniques: [ATTCK_MAP['T1136.001']],
    yaml: `title: New User Account Creation via Event 4720
id: sig-005-backdoor-user-created
status: stable
description: Detects new user account creation.
author: Kestrel Incident Response
date: 2026-03-04
logsource:
    category: security
    product: windows
detection:
    selection:
        EventID: 4720
    condition: selection
falsepositives:
    - Authorized user onboarding by Helpdesk
level: medium`,
    isCompiled: true,
    enabled: true,
    validationErrors: [],
  },
  {
    id: 'sig-006-malformed-test-rule',
    title: 'Experimental Uncompiled Rule Example',
    status: 'invalid',
    severity: 'low',
    category: 'process_creation',
    description: 'Example rule containing missing required fields to demonstrate validation error reporting in the Rules Manager.',
    author: 'Dev Tester',
    date: '2026-03-10',
    logsource: {
      product: 'windows',
    },
    tags: [],
    attckTechniques: [],
    yaml: `title: Experimental Uncompiled Rule Example
status: experimental
# Missing required logsource category and detection condition
logsource:
    product: windows
detection:
    selection:
        EventID: 9999
# condition field is missing!
level: low`,
    isCompiled: false,
    enabled: false,
    validationErrors: [
      'Missing required property "logsource.category" or "logsource.service"',
      'Missing required section "detection.condition"',
    ],
  },
];

// In-memory state store for rule management
let rulesStore: SigmaRule[] = [...INITIAL_RULES];

// ============================================================================
// SIGMA YAML VALIDATOR ENGINE
// ============================================================================

export function validateSigmaYaml(yamlContent: string): RuleValidationResult {
  const errors: string[] = [];
  const warnings: string[] = [];
  const parsedRule: Partial<SigmaRule> = {};

  if (!yamlContent || yamlContent.trim().length === 0) {
    return { isValid: false, errors: ['YAML content cannot be empty.'], warnings: [] };
  }

  // Basic line-by-line key extractor for custom robust validation without native C binary deps
  const lines = yamlContent.split('\n');
  const kv: Record<string, string> = {};

  let currentKey = '';
  lines.forEach((line) => {
    const trimmed = line.trim();
    if (!trimmed || trimmed.startsWith('#')) return;

    if (line.match(/^[a-zA-Z0-9_-]+:/)) {
      const parts = line.split(':');
      currentKey = parts[0].trim();
      kv[currentKey] = parts.slice(1).join(':').trim();
    }
  });

  // Mandatory check 1: title
  if (!kv['title']) {
    errors.push('Missing required property: "title"');
  } else {
    parsedRule.title = kv['title'].replace(/^['"]|['"]$/g, '');
  }

  // Mandatory check 2: logsource block
  if (!yamlContent.includes('logsource:')) {
    errors.push('Missing required section: "logsource"');
  } else {
    if (!yamlContent.includes('category:') && !yamlContent.includes('service:') && !yamlContent.includes('product:')) {
      errors.push('Section "logsource" must define at least one of "category", "service", or "product"');
    }
  }

  // Mandatory check 3: detection block & condition
  if (!yamlContent.includes('detection:')) {
    errors.push('Missing required section: "detection"');
  } else {
    if (!yamlContent.includes('condition:')) {
      errors.push('Missing mandatory "condition:" inside "detection" section');
    }
  }

  // Check level
  if (kv['level']) {
    const lvl = kv['level'].toLowerCase().trim();
    if (['critical', 'high', 'medium', 'low', 'informational'].includes(lvl)) {
      parsedRule.severity = lvl as SigmaRuleSeverity;
    } else {
      warnings.push(`Non-standard severity level "${kv['level']}". Expected: critical, high, medium, low, informational.`);
    }
  } else {
    warnings.push('Property "level" is missing; defaults to "medium".');
    parsedRule.severity = 'medium';
  }

  // Extract tags & ATT&CK techniques
  const tagMatches = yamlContent.match(/attack\.[a-z0-9._-]+/gi) || [];
  const uniqueTags = Array.from(new Set(tagMatches.map((t) => t.toLowerCase())));
  parsedRule.tags = uniqueTags;
  parsedRule.attckTechniques = extractAttckTechniques(uniqueTags);

  // Derive Category
  if (yamlContent.includes('process_creation') || yamlContent.includes('4688')) {
    parsedRule.category = 'process_creation';
  } else if (yamlContent.includes('lateral_movement') || yamlContent.includes('7045')) {
    parsedRule.category = 'lateral_movement';
  } else if (yamlContent.includes('credential_access') || yamlContent.includes('lsass')) {
    parsedRule.category = 'credential_access';
  } else if (yamlContent.includes('defense_evasion') || yamlContent.includes('1102')) {
    parsedRule.category = 'defense_evasion';
  } else if (yamlContent.includes('persistence') || yamlContent.includes('4720')) {
    parsedRule.category = 'persistence';
  } else {
    parsedRule.category = 'execution';
  }

  const isValid = errors.length === 0;

  return {
    isValid,
    errors,
    warnings,
    parsedRule,
  };
}

// ============================================================================
// MOCK DATA (for detection engine only — Events page uses real backend)
// ============================================================================

const MOCK_COMPUTERS = [
  'DC01.corp.kestrel.local',
  'WKSTN-SEC04.corp.kestrel.local',
  'APP-SRV02.corp.kestrel.local',
  'EXCHANGE-01.corp.kestrel.local',
  'FILE-SRV01.corp.kestrel.local',
  'SQL-DB01.corp.kestrel.local',
];

const MOCK_USERS = [
  { sid: 'S-1-5-18', name: 'NT AUTHORITY\\SYSTEM' },
  { sid: 'S-1-5-19', name: 'NT AUTHORITY\\LOCAL SERVICE' },
  { sid: 'S-1-5-20', name: 'NT AUTHORITY\\NETWORK SERVICE' },
  { sid: 'S-1-5-21-389142-104958-39201-1001', name: 'CORP\\jdoe' },
  { sid: 'S-1-5-21-389142-104958-39201-1002', name: 'CORP\\admin_mwilson' },
  { sid: 'S-1-5-21-389142-104958-39201-1005', name: 'CORP\\svc_backup' },
  { sid: 'S-1-5-21-389142-104958-39201-1099', name: 'CORP\\analyst_hbham' },
];

interface EventTemplate {
  eventId: number;
  channel: string;
  provider: string;
  level: string;
  taskCategory: string;
  messageTemplate: (user: string, computer: string) => string;
  xmlTemplate: (time: string, eventId: number, user: string, computer: string) => string;
}

const EVENT_TEMPLATES: EventTemplate[] = [
  {
    eventId: 4624, channel: 'Security',
    provider: 'Microsoft-Windows-Security-Auditing', level: 'Audit Success',
    taskCategory: 'Logon',
    messageTemplate: (user) => `An account was successfully logged on.\n\nTarget User:\n\tSecurity ID:\t${user}`,
    xmlTemplate: (time, eventId, user, computer) =>
      `<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>${eventId}</EventID><TimeCreated SystemTime='${time}'/><Computer>${computer}</Computer></System><EventData><Data Name='TargetUserSid'>${user}</Data></EventData></Event>`,
  },
  {
    eventId: 4688, channel: 'Security',
    provider: 'Microsoft-Windows-Security-Auditing', level: 'Audit Success',
    taskCategory: 'Process Creation',
    messageTemplate: (user) => `A new process has been created.\nCreator Subject:\n\tSecurity ID:\t${user}\nNew Process Name:\tC:\\Windows\\System32\\cmd.exe`,
    xmlTemplate: (time, eventId, _user, computer) =>
      `<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>${eventId}</EventID><TimeCreated SystemTime='${time}'/><Computer>${computer}</Computer></System></Event>`,
  },
  {
    eventId: 1102, channel: 'Security',
    provider: 'Microsoft-Windows-Security-Auditing', level: 'Critical',
    taskCategory: 'Log Clear',
    messageTemplate: (user) => `The audit log was cleared.\nSubject:\n\tSecurity ID:\t${user}`,
    xmlTemplate: (time, _eventId, _user, computer) =>
      `<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>1102</EventID><TimeCreated SystemTime='${time}'/><Computer>${computer}</Computer></System></Event>`,
  },
  {
    eventId: 4720, channel: 'Security',
    provider: 'Microsoft-Windows-Security-Auditing', level: 'Warning',
    taskCategory: 'User Account Management',
    messageTemplate: (user) => `A user account was created.\nNew Account:\tSecurity ID:\t${user}`,
    xmlTemplate: (time, _eventId, _user, computer) =>
      `<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>4720</EventID><TimeCreated SystemTime='${time}'/><Computer>${computer}</Computer></System></Event>`,
  },
  {
    eventId: 1, channel: 'Microsoft-Windows-Sysmon/Operational',
    provider: 'Microsoft-Windows-Sysmon', level: 'Information',
    taskCategory: 'Process Create',
    messageTemplate: () => `Process Create:\nImage: C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\nCommandLine: powershell.exe -EncodedCommand ...`,
    xmlTemplate: (time, _eventId, _user, computer) =>
      `<Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'><System><EventID>1</EventID><TimeCreated SystemTime='${time}'/><Computer>${computer}</Computer></System></Event>`,
  },
];

export const MOCK_EVENTS: EventRecord[] = Array.from({ length: 320 }, (_, index) => {
  const template = EVENT_TEMPLATES[index % EVENT_TEMPLATES.length];
  const userObj = MOCK_USERS[index % MOCK_USERS.length];
  const computer = MOCK_COMPUTERS[index % MOCK_COMPUTERS.length];
  const minutesAgo = index * 13 + (index % 7);
  const timeCreatedUtc = new Date(Date.now() - minutesAgo * 60 * 1000).toISOString();
  const id = `evt-${(10000 + index).toString(16)}-${index}`;
  return {
    id, timeCreatedUtc, eventId: template.eventId, channel: template.channel,
    provider: template.provider, level: template.level as any, computer,
    userSid: userObj.sid, username: userObj.name, taskCategory: template.taskCategory,
    message: template.messageTemplate(userObj.name, computer),
    rawXml: template.xmlTemplate(timeCreatedUtc, template.eventId, userObj.name, computer),
  };
});

// ============================================================================
// SIMULATED SIGMA DETECTION MATCHING ENGINE
// ============================================================================

export async function runSigmaDetectionEngine(
  rulesToRun?: SigmaRule[]
): Promise<DetectionRunResult> {
  const startTime = performance.now();
  const activeRules = rulesToRun || [];
  const MOCK = MOCK_EVENTS;
  const alertsMap = new Map<string, DetectionAlert>();

  activeRules.forEach((rule) => {
    let matchedEvents: EventRecord[] = [];

    if (rule.id === 'sig-001-powershell-encoded') {
      matchedEvents = MOCK.filter((e) => e.eventId === 4688 || (e.message && e.message.includes('-EncodedCommand')));
    } else if (rule.id === 'sig-003-psexec-service-install') {
      matchedEvents = MOCK.filter((e) => e.eventId === 7045);
    } else if (rule.id === 'sig-004-audit-log-cleared') {
      matchedEvents = MOCK.filter((e) => e.eventId === 1102);
    } else if (rule.id === 'sig-005-backdoor-user-created') {
      matchedEvents = MOCK.filter((e) => e.eventId === 4720);
    } else if (rule.id === 'sig-002-lsass-memory-access') {
      matchedEvents = MOCK.filter((e) => e.level === 'Critical' || e.eventId === 1102);
    } else {
      matchedEvents = MOCK.filter((e) => e.eventId === 4625 || e.level === 'Warning').slice(0, 3);
    }

    if (matchedEvents.length > 0) {
      alertsMap.set(rule.id, {
        id: `alt-${rule.id}-${Date.now().toString(36)}`,
        ruleId: rule.id, ruleTitle: rule.title, ruleCategory: rule.category,
        severity: rule.severity, matchingEventCount: matchedEvents.length,
        attckTechniques: rule.attckTechniques, matchedEvents,
        firstSeenUtc: matchedEvents[0]?.timeCreatedUtc || new Date().toISOString(),
        lastSeenUtc: matchedEvents[matchedEvents.length - 1]?.timeCreatedUtc || new Date().toISOString(),
        status: 'new',
      });
    }
  });

  const alerts = Array.from(alertsMap.values());
  const endTime = performance.now();

  return {
    executionTimeMs: Math.round(endTime - startTime + 42),
    totalEventsScanned: MOCK.length,
    totalRulesEvaluated: activeRules.length,
    totalAlertsGenerated: alerts.length,
    alerts,
  };
}

// ============================================================================
// PUBLIC SIGMA API SURFACE
// ============================================================================

export const sigmaApi = {
  listRules: async (): Promise<SigmaRule[]> => {
    return new Promise((resolve) => setTimeout(() => resolve([...rulesStore]), 80));
  },

  getRule: async (id: string): Promise<SigmaRule | null> => {
    const rule = rulesStore.find((r) => r.id === id) || null;
    return new Promise((resolve) => setTimeout(() => resolve(rule), 50));
  },

  saveRule: async (ruleData: Partial<SigmaRule> & { yaml: string }): Promise<SigmaRule> => {
    const validation = validateSigmaYaml(ruleData.yaml);
    const isValid = validation.isValid;

    let existingIndex = rulesStore.findIndex((r) => r.id === ruleData.id);
    const ruleId = ruleData.id || `sig-custom-${Date.now().toString(36)}`;
    const title = validation.parsedRule?.title || ruleData.title || 'Untitled Custom Sigma Rule';
    const severity = validation.parsedRule?.severity || ruleData.severity || 'medium';
    const category = validation.parsedRule?.category || ruleData.category || 'execution';
    const tags = validation.parsedRule?.tags || ruleData.tags || [];
    const attckTechniques = validation.parsedRule?.attckTechniques || ruleData.attckTechniques || [];

    const updatedRule: SigmaRule = {
      id: ruleId,
      title,
      status: isValid ? 'valid' : 'invalid',
      severity,
      category,
      description: ruleData.description || 'Custom Sigma detection rule.',
      author: ruleData.author || 'SOC Analyst',
      date: ruleData.date || new Date().toISOString().split('T')[0],
      logsource: ruleData.logsource || { product: 'windows' },
      tags,
      attckTechniques,
      yaml: ruleData.yaml,
      isCompiled: isValid,
      enabled: ruleData.enabled ?? isValid,
      validationErrors: validation.errors,
    };

    if (existingIndex >= 0) {
      rulesStore[existingIndex] = updatedRule;
    } else {
      rulesStore.unshift(updatedRule);
    }

    return new Promise((resolve) => setTimeout(() => resolve(updatedRule), 100));
  },

  deleteRule: async (id: string): Promise<boolean> => {
    rulesStore = rulesStore.filter((r) => r.id !== id);
    return new Promise((resolve) => setTimeout(() => resolve(true), 80));
  },

  toggleRuleEnabled: async (id: string): Promise<SigmaRule | null> => {
    const rule = rulesStore.find((r) => r.id === id);
    if (rule) {
      rule.enabled = !rule.enabled;
      return new Promise((resolve) => setTimeout(() => resolve({ ...rule }), 50));
    }
    return null;
  },

  uploadRuleFile: async (file: File): Promise<SigmaRule> => {
    const text = await file.text();
    const validation = validateSigmaYaml(text);

    const rule: SigmaRule = {
      id: `sig-upload-${Date.now().toString(36)}-${Math.random().toString(36).substring(2, 6)}`,
      title: validation.parsedRule?.title || file.name.replace(/\.(yml|yaml)$/i, ''),
      status: validation.isValid ? 'valid' : 'invalid',
      severity: validation.parsedRule?.severity || 'medium',
      category: validation.parsedRule?.category || 'execution',
      description: `Uploaded rule from ${file.name}`,
      author: 'Uploaded File',
      date: new Date().toISOString().split('T')[0],
      logsource: { product: 'windows' },
      tags: validation.parsedRule?.tags || [],
      attckTechniques: validation.parsedRule?.attckTechniques || [],
      yaml: text,
      isCompiled: validation.isValid,
      enabled: validation.isValid,
      validationErrors: validation.errors,
    };

    rulesStore.unshift(rule);
    return rule;
  },
};
