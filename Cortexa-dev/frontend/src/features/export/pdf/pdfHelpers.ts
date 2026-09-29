import { clamp } from '../../../shared/utils';
import type { AxisName, EvidenceSourceType, Recommendation } from '../../opportunity/opportunityTypes';
import type { CandidateCategory } from '../../results/resultsTypes';

export function truncate(text: string, maxLen: number): string {
  if (text.length <= maxLen) return text;
  return text.slice(0, maxLen) + '…';
}

export function clampScore(value: number): number {
  return clamp(Math.round(value), 0, 100);
}

export function humanizeAxis(axis: AxisName): string {
  const map: Partial<Record<AxisName, string>> = {
    novelty: 'Novelty',
    non_obviousness: 'Non-obviousness',
    utility: 'Utility',
    enablement: 'Enablement',
    claim_clarity: 'Claim Clarity',
    Novelty: 'Novelty',
    Inventiveness: 'Inventiveness',
    Commercial: 'Commercial',
    Strategic: 'Strategic',
    Patentability: 'Patentability',
  };
  return map[axis] ?? axis;
}

export function humanizeSourceType(type: EvidenceSourceType): string {
  const map: Record<EvidenceSourceType, string> = {
    patent_api: 'Patent API',
    vector_corpus: 'Vector Corpus',
    llm_deep_research: 'LLM Deep Research',
  };
  return map[type];
}

export function humanizeCategory(category: CandidateCategory): string {
  const map: Record<CandidateCategory, string> = {
    whitespace: 'Whitespace',
    defensive: 'Defensive',
    adjacent: 'Adjacent',
    continuation: 'Continuation',
  };
  return map[category];
}

export function recommendationColor(rec: Recommendation): string {
  const map: Record<Recommendation, string> = {
    pursue: '#0E700E',
    investigate: '#8A6D00',
    abandon: '#A4262C',
  };
  return map[rec];
}

export function humanizeRecommendation(rec: Recommendation): string {
  const map: Record<Recommendation, string> = {
    pursue: 'Pursue',
    investigate: 'Investigate',
    abandon: 'Abandon',
  };
  return map[rec];
}

export function formatExportDate(): string {
  return (
    new Intl.DateTimeFormat('en-GB', {
      dateStyle: 'medium',
      timeStyle: 'short',
      timeZone: 'UTC',
    }).format(new Date()) + ' UTC'
  );
}
