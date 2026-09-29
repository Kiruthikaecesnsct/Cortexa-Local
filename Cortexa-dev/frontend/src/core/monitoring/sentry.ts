import * as Sentry from '@sentry/react';
import { sentryDsn } from '../config/env';

const SENTRY_TRACES_SAMPLE_RATE = 0.1;

export function initSentry(): void {
  if (!sentryDsn) {
    return;
  }

  Sentry.init({
    dsn: sentryDsn,
    environment: 'dev',
    integrations: [
      Sentry.browserTracingIntegration(),
      // Forward console.log/warn/error calls to Sentry as logs.
      Sentry.consoleLoggingIntegration({ levels: ['log', 'warn', 'error'] }),
    ],
    tracesSampleRate: SENTRY_TRACES_SAMPLE_RATE,
    enableLogs: true,
  });

  // Metrics are auto-enabled once the SDK is initialised; emit a startup counter.
  Sentry.metrics.count('app.started', 1);
  Sentry.logger.info('app started', { log_source: 'app_bootstrap' });
}

export const SentryErrorBoundary = Sentry.ErrorBoundary;
