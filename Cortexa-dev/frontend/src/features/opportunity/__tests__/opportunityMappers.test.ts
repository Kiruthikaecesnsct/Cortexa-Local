import { describe, expect, it } from 'vitest';
import type { CitationDto, ProvenanceLinkDto } from '../../../core/api/types';
import { candidateToDetail, opportunityToDetail } from '../opportunityMappers';
import type { CategorizedOpportunity, RankedCandidate } from '../../results/resultsTypes';

function makeRankedCandidate(overrides: Partial<RankedCandidate> = {}): RankedCandidate {
  return {
    id: 'cand-001',
    title: 'Adaptive Neural Compression',
    abstract: 'A method for compressing neural networks adaptively.',
    claimDraft: 'A method comprising: step A; step B.',
    patentability_score: 84,
    recommendation: 'pursue',
    rank: 1,
    maturity: 'mature',
    rationale: 'Strong patentability.',
    source_asset_id: 'asset-001',
    batch_id: 'batch-001',
    noveltyHypothesis: 'No prior art found.',
    axes: {
      Novelty: { axis: 'Novelty', score: 90, refs: ['ref-1', 'ref-2'] },
      Inventiveness: { axis: 'Inventiveness', score: 80, refs: ['ref-3'] },
      Commercial: { axis: 'Commercial', score: 85, refs: [] },
      Strategic: { axis: 'Strategic', score: 75, refs: ['ref-4'] },
      Patentability: { axis: 'Patentability', score: 70, refs: ['ref-5'] },
    },
    citations: [],
    provenanceLinks: [],
    sourceAvailability: {},
    ...overrides,
  };
}

function makeCategorizedOpportunity(overrides: Partial<CategorizedOpportunity> = {}): CategorizedOpportunity {
  return {
    id: 'opp-001',
    title: 'Seeding Opportunity',
    description: 'A new patent opportunity from seeding.',
    confidence_score: 78,
    roadmap_alignment: 'High',
    category: 'whitespace',
    ...overrides,
  };
}

describe('opportunityMappers', () => {
  describe('candidateToDetail', () => {
    it('maps basic candidate fields to detail', () => {
      const candidate = makeRankedCandidate();
      const result = candidateToDetail(candidate);

      expect(result.candidateId).toBe('cand-001');
      expect(result.title).toBe('Adaptive Neural Compression');
      expect(result.abstract).toBe('A method for compressing neural networks adaptively.');
      expect(result.claimDraft).toBe('A method comprising: step A; step B.');
      expect(result.overallScore).toBe(84);
      expect(result.recommendation).toBe('pursue');
      expect(result.source).toBe('harvesting');
    });

    it('maps axes in RESULTS_AXIS_ORDER with resolved citations', () => {
      const candidate = makeRankedCandidate({
        axes: {
          Novelty: { axis: 'Novelty', score: 90, refs: ['ref-1', 'ref-2'] },
          Inventiveness: { axis: 'Inventiveness', score: 80, refs: ['ref-3'] },
          Commercial: { axis: 'Commercial', score: 85, refs: [] },
        },
        citations: [
          { ref: 'ref-1', source_type: 'patent_api', title: 'Patent A', patent_id: 'US1234567', url: 'https://example.com/US1234567', similarity: 0.92 },
          { ref: 'ref-2', source_type: 'vector_corpus', title: 'Paper B', url: 'https://example.com/doc-b', similarity: 0.88 },
          { ref: 'ref-3', source_type: 'llm_deep_research', title: 'Finding C', url: '', similarity: 0.79 },
        ],
      });

      const result = candidateToDetail(candidate);

      expect(result.axisScores).toHaveLength(3);
      expect(result.axisScores[0]!.axis).toBe('Novelty');
      expect(result.axisScores[0]!.score).toBe(90);
      expect(result.axisScores[0]!.citations).toHaveLength(2);
      expect(result.axisScores[0]!.citations[0]!.ref).toBe('ref-1');
      expect(result.axisScores[0]!.citations[0]!.title).toBe('Patent A');
      expect(result.axisScores[0]!.citations[0]!.url).toBe('https://example.com/US1234567');
      expect(result.axisScores[0]!.citations[0]!.patentId).toBe('US1234567');
      expect(result.axisScores[0]!.citations[0]!.similarity).toBe(0.92);
      expect(result.axisScores[0]!.citations[0]!.sourceType).toBe('patent_api');

      expect(result.axisScores[0]!.citations[1]!.ref).toBe('ref-2');
      expect(result.axisScores[0]!.citations[1]!.title).toBe('Paper B');
      expect(result.axisScores[0]!.citations[1]!.url).toBe('https://example.com/doc-b');
      expect(result.axisScores[0]!.citations[1]!.patentId).toBeUndefined();
      expect(result.axisScores[0]!.citations[1]!.similarity).toBe(0.88);
      expect(result.axisScores[0]!.citations[1]!.sourceType).toBe('vector_corpus');

      expect(result.axisScores[1]!.axis).toBe('Inventiveness');
      expect(result.axisScores[1]!.citations).toHaveLength(1);
      expect(result.axisScores[1]!.citations[0]!.ref).toBe('ref-3');
      expect(result.axisScores[1]!.citations[0]!.title).toBe('Finding C');

      expect(result.axisScores[2]!.axis).toBe('Commercial');
      expect(result.axisScores[2]!.citations).toHaveLength(0);
    });

    it('handles unresolved refs by returning bare ref objects', () => {
      const candidate = makeRankedCandidate({
        axes: {
          Novelty: { axis: 'Novelty', score: 90, refs: ['ref-orphan'] },
        },
        citations: [],
      });

      const result = candidateToDetail(candidate);

      expect(result.axisScores[0]!.citations).toHaveLength(1);
      expect(result.axisScores[0]!.citations[0]).toEqual({ ref: 'ref-orphan' });
    });

    it('builds three evidence sources in fixed order with correct state derivation', () => {
      const candidate = makeRankedCandidate({
        citations: [
          { ref: 'ref-1', source_type: 'patent_api', title: 'Patent A', patent_id: 'US1234567', url: 'https://example.com/US1234567', similarity: 0.92 },
          { ref: 'ref-2', source_type: 'vector_corpus', title: 'Paper B', url: 'https://example.com/doc-b', similarity: 0.88 },
        ],
        sourceAvailability: { patent_api: true, vector_corpus: true, llm_deep_research: false },
      });

      const result = candidateToDetail(candidate);

      expect(result.evidenceSources).toHaveLength(3);

      const patentSource = result.evidenceSources[0]!;
      expect(patentSource.sourceType).toBe('patent_api');
      expect(patentSource.available).toBe(true);
      expect(patentSource.state).toBe('active_cited');
      expect(patentSource.citations).toHaveLength(1);
      expect(patentSource.citations[0]!.ref).toBe('ref-1');
      expect(patentSource.citations[0]!.title).toBe('Patent A');

      const vectorSource = result.evidenceSources[1]!;
      expect(vectorSource.sourceType).toBe('vector_corpus');
      expect(vectorSource.available).toBe(true);
      expect(vectorSource.state).toBe('active_cited');
      expect(vectorSource.citations).toHaveLength(1);
      expect(vectorSource.citations[0]!.ref).toBe('ref-2');

      const llmSource = result.evidenceSources[2]!;
      expect(llmSource.sourceType).toBe('llm_deep_research');
      expect(llmSource.available).toBe(false);
      expect(llmSource.state).toBe('unavailable');
      expect(llmSource.citations).toHaveLength(0);
    });

    it('derives active_uncited state when source contributed but no citations cited', () => {
      const candidate = makeRankedCandidate({
        citations: [],
        sourceAvailability: { patent_api: true, vector_corpus: false, llm_deep_research: false },
      });

      const result = candidateToDetail(candidate);

      const patentSource = result.evidenceSources[0]!;
      expect(patentSource.available).toBe(true);
      expect(patentSource.state).toBe('active_uncited');
      expect(patentSource.citations).toHaveLength(0);
    });

    it('derives unavailable state when source did not contribute', () => {
      const candidate = makeRankedCandidate({
        citations: [],
        sourceAvailability: { patent_api: false, vector_corpus: false, llm_deep_research: false },
      });

      const result = candidateToDetail(candidate);

      expect(result.evidenceSources[0]!.state).toBe('unavailable');
      expect(result.evidenceSources[1]!.state).toBe('unavailable');
      expect(result.evidenceSources[2]!.state).toBe('unavailable');
    });

    describe('evidence_sources per-source views (BUG183)', () => {
      it('surfaces the patent source\'s own hits and a real mean-similarity confidence, independent of scorer-cited axis refs', () => {
        const candidate = makeRankedCandidate({
          citations: [],
          axes: { Novelty: { axis: 'Novelty', score: 90, refs: [] } },
          evidenceSources: [
            {
              source_type: 'patent_api',
              available: true,
              status: 'active',
              hits: [
                { title: 'US Patent', patent_id: 'US1234567', url: 'https://patents.google.com/patent/US1234567', similarity: 0.9, jurisdiction: 'US' },
                { title: 'EP Patent', patent_id: 'EP7654321', url: 'https://patents.google.com/patent/EP7654321', similarity: 0.8, jurisdiction: 'EP' },
              ],
            },
            { source_type: 'vector_corpus', available: false, status: 'empty', hits: [] },
            { source_type: 'llm_deep_research', available: false, status: 'empty', reasoning: [] },
          ],
        });

        const result = candidateToDetail(candidate);
        const patentSource = result.evidenceSources[0]!;

        expect(patentSource.state).toBe('active_cited');
        expect(patentSource.hits).toHaveLength(2);
        expect(patentSource.hits![0]!.patentId).toBe('US1234567');
        expect(patentSource.hits![0]!.jurisdiction).toBe('US');
        expect(patentSource.confidence).toBeCloseTo(0.85);
      });

      it('surfaces LLM reasoning-only findings as Available with real confidence even though the backend sends status "empty" (no prior art found -> patentable case, Defect 2)', () => {
        // The backend LLM view derives status from hit count only — hits come from
        // finding.citations, not finding.findings. A finding with reasoning but no
        // citations legitimately arrives with status "empty" and hits: []. This must
        // still render as Available with the reasoning and real confidence, not as
        // "Unavailable — ran, returned 0 matches" with 0% confidence.
        const candidate = makeRankedCandidate({
          citations: [],
          evidenceSources: [
            { source_type: 'patent_api', available: false, status: 'empty' },
            { source_type: 'vector_corpus', available: false, status: 'empty' },
            {
              source_type: 'llm_deep_research',
              available: false,
              status: 'empty',
              hits: [],
              reasoning: ['The candidate is a non-obvious departure from the closest prior art.'],
              confidence: 0.69,
            },
          ],
        });

        const result = candidateToDetail(candidate);
        const llmSource = result.evidenceSources[2]!;

        expect(llmSource.state).toBe('active_cited');
        expect(llmSource.available).toBe(true);
        expect(llmSource.reasoning).toEqual(['The candidate is a non-obvious departure from the closest prior art.']);
        expect(llmSource.confidence).toBe(0.69);
      });

      it('surfaces patent hits-only as Available even when status is "empty" (hits should override a stale/mismatched status the same way reasoning does)', () => {
        const candidate = makeRankedCandidate({
          citations: [],
          evidenceSources: [
            {
              source_type: 'patent_api',
              available: false,
              status: 'empty',
              hits: [{ title: 'US Patent', patent_id: 'US9', url: 'https://x/US9', similarity: 0.77, jurisdiction: 'US' }],
            },
            { source_type: 'vector_corpus', available: false, status: 'empty' },
            { source_type: 'llm_deep_research', available: false, status: 'empty' },
          ],
        });

        const result = candidateToDetail(candidate);
        const patentSource = result.evidenceSources[0]!;

        expect(patentSource.state).toBe('active_cited');
        expect(patentSource.available).toBe(true);
        expect(patentSource.confidence).toBeCloseTo(0.77);
      });

      it('prefers dtoConfidence over mean similarity for the LLM source when both hits and reasoning are present', () => {
        const candidate = makeRankedCandidate({
          citations: [],
          evidenceSources: [
            { source_type: 'patent_api', available: false, status: 'empty' },
            { source_type: 'vector_corpus', available: false, status: 'empty' },
            {
              source_type: 'llm_deep_research',
              available: true,
              status: 'active',
              hits: [{ title: 'Cited prior art', similarity: 0.5 }],
              reasoning: ['Both a citation and a reasoning narrative are present.'],
              confidence: 0.69,
            },
          ],
        });

        const result = candidateToDetail(candidate);
        const llmSource = result.evidenceSources[2]!;

        expect(llmSource.state).toBe('active_cited');
        expect(llmSource.confidence).toBe(0.69);
      });

      it('reserves active_uncited (and a 0 confidence) for a source that ran and contributed but has no hits or reasoning of its own', () => {
        const candidate = makeRankedCandidate({
          citations: [],
          evidenceSources: [
            { source_type: 'patent_api', available: true, status: 'active', hits: [] },
            { source_type: 'vector_corpus', available: false, status: 'empty' },
            { source_type: 'llm_deep_research', available: false, status: 'empty' },
          ],
        });

        const result = candidateToDetail(candidate);
        const patentSource = result.evidenceSources[0]!;

        expect(patentSource.state).toBe('active_uncited');
        expect(patentSource.confidence).toBe(0);
        expect(patentSource.hits).toEqual([]);
      });

      it('carries degraded / degradedSources through only for the patent source', () => {
        const candidate = makeRankedCandidate({
          citations: [],
          evidenceSources: [
            {
              source_type: 'patent_api',
              available: true,
              status: 'active',
              hits: [{ title: 'US Patent', patent_id: 'US1', url: 'https://x/US1', similarity: 0.7, jurisdiction: 'US' }],
              degraded: true,
              degraded_sources: ['Lens'],
            },
            { source_type: 'vector_corpus', available: false, status: 'empty' },
            { source_type: 'llm_deep_research', available: false, status: 'empty' },
          ],
        });

        const result = candidateToDetail(candidate);

        expect(result.evidenceSources[0]!.degraded).toBe(true);
        expect(result.evidenceSources[0]!.degradedSources).toEqual(['Lens']);
        expect(result.evidenceSources[1]!.degraded).toBe(false);
      });

      it('still honors fixed statuses (filtered/error/timeout) unchanged when evidence_sources is present', () => {
        const candidate = makeRankedCandidate({
          citations: [],
          evidenceSources: [
            { source_type: 'patent_api', available: false, status: 'error' },
            { source_type: 'vector_corpus', available: true, status: 'filtered' },
            { source_type: 'llm_deep_research', available: false, status: 'timeout' },
          ],
        });

        const result = candidateToDetail(candidate);

        expect(result.evidenceSources[0]!.state).toBe('source_error');
        expect(result.evidenceSources[1]!.state).toBe('content_filtered');
        expect(result.evidenceSources[2]!.state).toBe('source_timeout');
      });

      it('falls back to the citation-derived cards when evidence_sources is absent (old reports)', () => {
        const candidate = makeRankedCandidate({
          citations: [
            { ref: 'ref-1', source_type: 'patent_api', title: 'Patent A', patent_id: 'US1234567', url: 'https://example.com/US1234567', similarity: 0.92 },
          ],
          sourceAvailability: { patent_api: true, vector_corpus: false, llm_deep_research: false },
          evidenceSources: undefined,
        });

        const result = candidateToDetail(candidate);
        const patentSource = result.evidenceSources[0]!;

        expect(patentSource.state).toBe('active_cited');
        expect(patentSource.citations).toHaveLength(1);
        expect(patentSource.hits).toBeUndefined();
      });
    });

    it('derives content_filtered state and a degrade note when a source is blocked by content filter (BUG169)', () => {
      const candidate = makeRankedCandidate({
        citations: [
          { ref: 'ref-1', source_type: 'patent_api', title: 'Patent A', patent_id: 'US1234567', url: 'https://example.com/US1234567', similarity: 0.92 },
          { ref: 'ref-2', source_type: 'vector_corpus', title: 'Paper B', url: 'https://example.com/doc-b', similarity: 0.88 },
        ],
        sourceAvailability: { patent_api: true, vector_corpus: true, llm_deep_research: false },
        sourceStatus: { patent_api: 'active', vector_corpus: 'active', llm_deep_research: 'filtered' },
      });

      const result = candidateToDetail(candidate);

      const llmSource = result.evidenceSources[2]!;
      expect(llmSource.sourceType).toBe('llm_deep_research');
      expect(llmSource.state).toBe('content_filtered');
      expect(llmSource.available).toBe(true);

      expect(result.evidenceSources[0]!.state).toBe('active_cited');
      expect(result.evidenceSources[1]!.state).toBe('active_cited');

      expect(result.degradeNote).toBe("Scored on 2 of 3 sources — LLM Deep Research was blocked by the provider's content policy.");
    });

    it('produces no degrade note when all sources are active or empty', () => {
      const candidate = makeRankedCandidate({
        citations: [],
        sourceAvailability: { patent_api: true, vector_corpus: true, llm_deep_research: true },
        sourceStatus: { patent_api: 'active', vector_corpus: 'empty', llm_deep_research: 'active' },
      });

      const result = candidateToDetail(candidate);

      expect(result.degradeNote).toBeNull();
    });

    it('produces no degrade note when source_status is absent (legacy payloads)', () => {
      const candidate = makeRankedCandidate({
        citations: [],
        sourceAvailability: { patent_api: false, vector_corpus: false, llm_deep_research: false },
        sourceStatus: undefined,
      });

      const result = candidateToDetail(candidate);

      expect(result.degradeNote).toBeNull();
    });

    it('joins multiple degraded reasons with "and"', () => {
      const candidate = makeRankedCandidate({
        citations: [],
        sourceAvailability: { patent_api: true, vector_corpus: false, llm_deep_research: false },
        sourceStatus: { patent_api: 'active', vector_corpus: 'timeout', llm_deep_research: 'filtered' },
      });

      const result = candidateToDetail(candidate);

      expect(result.degradeNote).toBe(
        "Scored on 1 of 3 sources — Corpus Vector Search timed out and LLM Deep Research was blocked by the provider's content policy."
      );
    });

    it('treats error/timeout statuses as their own honest, unavailable state — not active, not not_captured', () => {
      const candidate = makeRankedCandidate({
        citations: [],
        sourceAvailability: { patent_api: false, vector_corpus: false, llm_deep_research: false },
        sourceStatus: { patent_api: 'error', vector_corpus: 'timeout', llm_deep_research: 'active' },
      });

      const result = candidateToDetail(candidate);

      expect(result.evidenceSources[0]!.state).toBe('source_error');
      expect(result.evidenceSources[0]!.available).toBe(false);
      expect(result.evidenceSources[0]!.confidence).toBe(0);
      expect(result.evidenceSources[1]!.state).toBe('source_timeout');
      expect(result.evidenceSources[1]!.available).toBe(false);
      expect(result.evidenceSources[1]!.confidence).toBe(0);
    });

    it('maps provenance from first ProvenanceLinkDto with all fields', () => {
      const candidate = makeRankedCandidate({
        provenanceLinks: [
          {
            document_id: 'doc-abc-123',
            locator: 'chars:100-200',
            source_kind: 'Paper',
            hit_url: 'http://example.com#page=5',
            chunk_id: 'chunk-42',
            source_chunk_index: 12,
            page_number: 5,
            section_hint: '3.2 Results',
            span_start: 100,
            span_end: 200,
            excerpt: 'This is the excerpt text from the source.',
          } satisfies ProvenanceLinkDto,
        ],
      });

      const result = candidateToDetail(candidate);

      expect(result.provenance).toBeDefined();
      expect(result.provenance?.sourceDocumentId).toBe('doc-abc-123');
      expect(result.provenance?.sourceKind).toBe('Paper');
      expect(result.provenance?.hitUrl).toBe('http://example.com#page=5');
      expect(result.provenance?.spanStart).toBe(100);
      expect(result.provenance?.spanEnd).toBe(200);
      expect(result.provenance?.excerptText).toBe('This is the excerpt text from the source.');
      expect(result.provenance?.pageNumber).toBe(5);
      expect(result.provenance?.sectionHint).toBe('3.2 Results');
      expect(result.provenance?.chunkIndex).toBe(12);
      expect(result.provenance?.chunkId).toBe('chunk-42');
      expect(result.provenance?.locator).toBe('chars:100-200');
    });

    it('falls back to parsing locator when span_start/span_end absent', () => {
      const candidate = makeRankedCandidate({
        provenanceLinks: [
          {
            document_id: 'doc-legacy',
            locator: 'chars:512-980',
            source_kind: 'Code',
          } satisfies ProvenanceLinkDto,
        ],
      });

      const result = candidateToDetail(candidate);

      expect(result.provenance?.spanStart).toBe(512);
      expect(result.provenance?.spanEnd).toBe(980);
    });

    it('prefers explicit span fields over locator regex', () => {
      const candidate = makeRankedCandidate({
        provenanceLinks: [
          {
            document_id: 'doc-explicit',
            locator: 'chars:100-200',
            span_start: 1500,
            span_end: 2500,
          } satisfies ProvenanceLinkDto,
        ],
      });

      const result = candidateToDetail(candidate);

      expect(result.provenance?.spanStart).toBe(1500);
      expect(result.provenance?.spanEnd).toBe(2500);
    });

    it('returns undefined provenance when provenanceLinks is empty', () => {
      const candidate = makeRankedCandidate({ provenanceLinks: [] });

      const result = candidateToDetail(candidate);

      expect(result.provenance).toBeUndefined();
    });

    it('returns undefined provenance when provenanceLinks is not present', () => {
      const candidate = makeRankedCandidate({ provenanceLinks: undefined });

      const result = candidateToDetail(candidate);

      expect(result.provenance).toBeUndefined();
    });
  });

  describe('opportunityToDetail', () => {
    it('maps seeding opportunity to detail with no axes/evidence/provenance', () => {
      const opportunity = makeCategorizedOpportunity();
      const result = opportunityToDetail(opportunity);

      expect(result.candidateId).toBe('opp-001');
      expect(result.title).toBe('Seeding Opportunity');
      expect(result.abstract).toBe('A new patent opportunity from seeding.');
      expect(result.claimDraft).toBe('');
      expect(result.overallScore).toBe(78);
      expect(result.recommendation).toBe('pursue');
      expect(result.source).toBe('seeding');
      expect(result.axisScores).toEqual([]);
      expect(result.evidenceSources).toEqual([]);
      expect(result.provenance).toBeUndefined();
    });

    it('derives recommendation from confidence_score', () => {
      const highConfidence = makeCategorizedOpportunity({ confidence_score: 75 });
      expect(opportunityToDetail(highConfidence).recommendation).toBe('pursue');

      const mediumConfidence = makeCategorizedOpportunity({ confidence_score: 55 });
      expect(opportunityToDetail(mediumConfidence).recommendation).toBe('investigate');

      const lowConfidence = makeCategorizedOpportunity({ confidence_score: 35 });
      expect(opportunityToDetail(lowConfidence).recommendation).toBe('abandon');
    });

    it('maps v2 seeding fields: novelty delta, grounded-in, prior art, claim, axes, evidence', () => {
      const opportunity = makeCategorizedOpportunity({
        claimStatement: 'A method comprising step A.',
        noveltyDelta: 'A reinforcement signal is new.',
        mechanism: 'RL agent.',
        targetConcept: 'cache eviction',
        groundedIn: { chunk_ids: ['c1'], excerpts: [{ chunk_id: 'c1', section_label: 'S3', text: 'excerpt' }] },
        priorArtProximity: [
          { reference: 'US1', title: 'Patent One', url: 'https://x/US1', source: 'USPTO', relevance_score: 0.7, note: '' },
          { reference: 'CORP', title: 'Corpus Doc', url: '', source: 'corpus', relevance_score: 0.4, note: 'corpus match — no external link' },
        ],
        axes: { Novelty: { score: 90, refs: ['E1'] } },
        citations: [{ ref: 'E1', source_type: 'patent_api', title: 'Patent One', patent_id: 'US1', url: 'https://x/US1', similarity: 0.7 }],
        sourceAvailability: { patent_api: true, vector_corpus: true, llm_deep_research: true },
        sourceStatus: { patent_api: 'active', vector_corpus: 'active', llm_deep_research: 'active' },
      });

      const result = opportunityToDetail(opportunity);

      expect(result.claimDraft).toBe('A method comprising step A.');
      expect(result.noveltyDelta).toBe('A reinforcement signal is new.');
      expect(result.groundedIn?.excerpts[0]!.sectionLabel).toBe('S3');
      expect(result.priorArtProximity).toHaveLength(2);
      expect(result.priorArtProximity![0]!.isCorpus).toBe(false);
      expect(result.priorArtProximity![1]!.isCorpus).toBe(true);
      expect(result.axisScores).toHaveLength(1);
      expect(result.axisScores[0]!.axis).toBe('Novelty');
      expect(result.axisScores[0]!.citations[0]!.patentId).toBe('US1');
      expect(result.evidenceSources).toHaveLength(3);
    });

    it('surfaces evidence_sources hits on a seeding opportunity card (BUG183)', () => {
      const opportunity = makeCategorizedOpportunity({
        evidenceSources: [
          {
            source_type: 'patent_api',
            available: true,
            status: 'active',
            hits: [{ title: 'US Patent', patent_id: 'US1', url: 'https://x/US1', similarity: 0.75, jurisdiction: 'US' }],
          },
          { source_type: 'vector_corpus', available: false, status: 'empty' },
          { source_type: 'llm_deep_research', available: true, status: 'active', reasoning: ['Novel over the closest reference.'], confidence: 0.6 },
        ],
      });

      const result = opportunityToDetail(opportunity);

      expect(result.evidenceSources[0]!.state).toBe('active_cited');
      expect(result.evidenceSources[0]!.hits).toHaveLength(1);
      expect(result.evidenceSources[2]!.reasoning).toEqual(['Novel over the closest reference.']);
      expect(result.evidenceSources[2]!.confidence).toBe(0.6);
    });

    it('flags a prior-art item as corpus when it has an empty url even without a corpus source', () => {
      const opportunity = makeCategorizedOpportunity({
        priorArtProximity: [{ reference: 'X', title: 'No URL', url: '', source: 'EPO', relevance_score: 0.3, note: '' }],
      });
      const result = opportunityToDetail(opportunity);
      expect(result.priorArtProximity![0]!.isCorpus).toBe(true);
    });

    it('keeps legacy opportunities minimal: no evidence sources or degrade note', () => {
      const result = opportunityToDetail(makeCategorizedOpportunity());
      expect(result.evidenceSources).toEqual([]);
      expect(result.degradeNote).toBeNull();
      expect(result.noveltyDelta).toBeUndefined();
      expect(result.priorArtProximity).toBeUndefined();
    });
  });

  describe('convergence: both nav paths yield identical rich output', () => {
    it('store path and endpoint path resolve citations, axes, provenance, and evidence identically', () => {
      const sharedCitations: CitationDto[] = [
        { ref: 'ref-1', source_type: 'patent_api', title: 'US Patent 1234567', patent_id: 'US1234567', url: 'https://patents.google.com/patent/US1234567', similarity: 0.92 },
        { ref: 'ref-2', source_type: 'vector_corpus', title: 'Research Paper on Compression', url: 'https://arxiv.org/abs/1234.5678', similarity: 0.88 },
        { ref: 'ref-3', source_type: 'llm_deep_research', title: 'LLM Finding: No prior adaptive method', url: '', similarity: 0.79 },
        { ref: 'ref-4', source_type: 'patent_api', title: 'EP Patent 1000000', patent_id: 'EP1000000', url: 'https://patents.google.com/patent/EP1000000', similarity: 0.85 },
      ];

      const sharedSourceAvailability = {
        patent_api: true,
        vector_corpus: true,
        llm_deep_research: true,
      };

      const sharedProvenance: ProvenanceLinkDto = {
        document_id: 'doc-same-123',
        locator: 'chars:1000-1500',
        source_kind: 'Paper',
        hit_url: 'http://example.com#page=7',
        chunk_id: 'chunk-same',
        source_chunk_index: 8,
        page_number: 7,
        section_hint: '4.2 Adaptive Compression',
        span_start: 1000,
        span_end: 1500,
        excerpt: 'The adaptive compression ratio is determined by bandwidth and staleness.',
      };

      const storePath: RankedCandidate = {
        id: 'same-123',
        title: 'Adaptive Gradient Sparsification',
        abstract: 'A method that dynamically adjusts the gradient sparsification ratio per client.',
        claimDraft: '1. A computer-implemented method for federated model training...',
        patentability_score: 73,
        recommendation: 'pursue',
        rank: 5,
        maturity: 'mature',
        rationale: 'Strong patentability with clear novelty.',
        source_asset_id: 'asset-same',
        batch_id: 'batch-same',
        noveltyHypothesis: 'No prior art discloses bandwidth-and-staleness-conditioned per-client sparsification.',
        axes: {
          Novelty: { axis: 'Novelty', score: 68, refs: ['ref-1', 'ref-2'] },
          Inventiveness: { axis: 'Inventiveness', score: 71, refs: ['ref-4'] },
          Commercial: { axis: 'Commercial', score: 82, refs: ['ref-3'] },
        },
        citations: sharedCitations,
        provenanceLinks: [sharedProvenance],
        sourceAvailability: sharedSourceAvailability,
      };

      const storeResult = candidateToDetail(storePath);

      expect(storeResult.axisScores).toHaveLength(3);
      expect(storeResult.axisScores[0]!.axis).toBe('Novelty');
      expect(storeResult.axisScores[0]!.citations).toHaveLength(2);
      expect(storeResult.axisScores[0]!.citations[0]!.title).toBe('US Patent 1234567');
      expect(storeResult.axisScores[0]!.citations[0]!.url).toBe('https://patents.google.com/patent/US1234567');
      expect(storeResult.axisScores[0]!.citations[0]!.patentId).toBe('US1234567');
      expect(storeResult.axisScores[0]!.citations[0]!.similarity).toBe(0.92);
      expect(storeResult.axisScores[0]!.citations[0]!.sourceType).toBe('patent_api');
      expect(storeResult.axisScores[0]!.citations[1]!.title).toBe('Research Paper on Compression');
      expect(storeResult.axisScores[0]!.citations[1]!.url).toBe('https://arxiv.org/abs/1234.5678');
      expect(storeResult.axisScores[0]!.citations[1]!.sourceType).toBe('vector_corpus');

      expect(storeResult.axisScores[1]!.axis).toBe('Inventiveness');
      expect(storeResult.axisScores[1]!.citations).toHaveLength(1);
      expect(storeResult.axisScores[1]!.citations[0]!.ref).toBe('ref-4');
      expect(storeResult.axisScores[1]!.citations[0]!.patentId).toBe('EP1000000');

      expect(storeResult.evidenceSources).toHaveLength(3);
      expect(storeResult.evidenceSources[0]!.sourceType).toBe('patent_api');
      expect(storeResult.evidenceSources[0]!.state).toBe('active_cited');
      expect(storeResult.evidenceSources[0]!.citations).toHaveLength(2);
      expect(storeResult.evidenceSources[0]!.citations[0]!.ref).toBe('ref-1');
      expect(storeResult.evidenceSources[0]!.citations[0]!.title).toBe('US Patent 1234567');
      expect(storeResult.evidenceSources[0]!.citations[1]!.ref).toBe('ref-4');

      expect(storeResult.evidenceSources[1]!.sourceType).toBe('vector_corpus');
      expect(storeResult.evidenceSources[1]!.state).toBe('active_cited');
      expect(storeResult.evidenceSources[1]!.citations).toHaveLength(1);
      expect(storeResult.evidenceSources[1]!.citations[0]!.ref).toBe('ref-2');

      expect(storeResult.evidenceSources[2]!.sourceType).toBe('llm_deep_research');
      expect(storeResult.evidenceSources[2]!.state).toBe('active_cited');
      expect(storeResult.evidenceSources[2]!.citations).toHaveLength(1);
      expect(storeResult.evidenceSources[2]!.citations[0]!.ref).toBe('ref-3');

      expect(storeResult.provenance?.sourceDocumentId).toBe('doc-same-123');
      expect(storeResult.provenance?.spanStart).toBe(1000);
      expect(storeResult.provenance?.spanEnd).toBe(1500);
      expect(storeResult.provenance?.pageNumber).toBe(7);
      expect(storeResult.provenance?.sectionHint).toBe('4.2 Adaptive Compression');
      expect(storeResult.provenance?.chunkIndex).toBe(8);
      expect(storeResult.provenance?.sourceKind).toBe('Paper');
      expect(storeResult.provenance?.excerptText).toBe('The adaptive compression ratio is determined by bandwidth and staleness.');
    });
  });
});
