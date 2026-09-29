import { api, API_BASE_URL, ApiError } from './apiClient';

export type IngestJobStatus =
  | 'queued'
  | 'running'
  | 'completed'
  | 'completed_with_errors'
  | 'failed'
  | 'canceled';

export interface IngestRecordErrorDto {
  recordId?: number | null;
  recordIndex?: number | null;
  reason?: string;
  error?: string;
  rawXmlSnippet?: string;
}

export interface IngestJobDto {
  id: string;
  fileName: string;
  fileSizeBytes: number;
  status: IngestJobStatus;
  totalRecords?: number;
  processedRecords?: number;
  failedRecords?: number;
  recordsTotal?: number;
  recordsParsed?: number;
  recordsFailed?: number;
  error?: string | null;
  createdBy: string;
  createdAtUtc: string;
  startedAtUtc?: string | null;
  finishedAtUtc?: string | null;
  firstErrors?: IngestRecordErrorDto[];
  recordErrors?: IngestRecordErrorDto[];
}

export interface UploadResponse {
  jobId: string;
  status: string;
  jobUrl: string;
}

export function humanizeIngestError(err: any): string {
  if (!err) return 'An unknown error occurred during ingestion.';

  const errorCode = err?.data?.error || err?.error || err?.response?.data?.error || '';
  const detail = err?.data?.detail || err?.detail || err?.response?.data?.detail || err?.message || '';

  if (errorCode === 'not_evtx' || detail.includes('not_evtx') || detail.includes('EVTX magic prefix')) {
    return "This doesn't look like a valid EVTX file.";
  }

  if (errorCode === 'file_too_large' || detail.includes('file_too_large') || err?.status === 413) {
    const maxBytes = err?.data?.maxBytes || err?.maxBytes || err?.response?.data?.maxBytes;
    if (maxBytes) {
      const maxMb = (maxBytes / (1024 * 1024)).toFixed(0);
      return `Upload exceeds the configured maximum limit of ${maxMb} MB.`;
    }
    return detail || 'Upload exceeds the configured maximum limit.';
  }

  if (errorCode === 'queue_full' || detail.includes('queue_full') || err?.status === 503) {
    return 'Ingest queue is full, try again shortly.';
  }

  if (detail) return detail;
  return 'Failed to process EVTX file.';
}

export const ingestApi = {
  uploadEvtx: async (
    file: File,
    onProgress?: (percent: number) => void
  ): Promise<UploadResponse> => {
    return new Promise((resolve, reject) => {
      const xhr = new XMLHttpRequest();
      xhr.open('POST', `${API_BASE_URL}/ingest/upload`, true);
      xhr.withCredentials = true; // Essential for ASP.NET Core cookie session auth

      xhr.setRequestHeader('Content-Type', 'application/octet-stream');
      xhr.setRequestHeader('X-Evtx-Filename', encodeURIComponent(file.name));

      if (xhr.upload && onProgress) {
        xhr.upload.onprogress = (event) => {
          if (event.lengthComputable) {
            const percent = Math.round((event.loaded / event.total) * 100);
            onProgress(percent);
          }
        };
      }

      xhr.onload = () => {
        if (xhr.status >= 200 && xhr.status < 300) {
          try {
            const data = JSON.parse(xhr.responseText);
            resolve(data);
          } catch {
            resolve({ jobId: '', status: 'queued', jobUrl: '' });
          }
        } else {
          let errorData: any = null;
          let errorMessage = `Upload failed with status ${xhr.status}`;
          try {
            errorData = JSON.parse(xhr.responseText);
            if (errorData?.detail) errorMessage = errorData.detail;
          } catch {}
          reject(new ApiError(xhr.status, errorMessage, errorData));
        }
      };

      xhr.onerror = () => {
        reject(new ApiError(500, 'Network connection error during EVTX upload'));
      };

      xhr.send(file);
    });
  },

  listJobs: async (limit: number = 50): Promise<IngestJobDto[]> => {
    return api.get<IngestJobDto[]>('/ingest/jobs', { params: { limit } });
  },

  getJob: async (id: string): Promise<IngestJobDto> => {
    return api.get<IngestJobDto>(`/ingest/jobs/${id}`);
  },
};
