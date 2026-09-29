import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { initializeMsal } from './core/auth/msalClient';
import { initSentry, SentryErrorBoundary } from './core/monitoring/sentry';
import './shared/theme/tokens.css';
import { App } from './App';

const rootEl = document.getElementById('root');
if (!rootEl) {
  throw new Error('Root element #root not found in index.html');
}

initSentry();

void (async () => {
  try {
    await initializeMsal();
  } catch (err) {
    /* MSAL init is optional — Entra button will show an error on click */
    void err;
  }

  createRoot(rootEl).render(
    <StrictMode>
      <SentryErrorBoundary
        fallback={() => (
          <div
            role="alert"
            style={{
              minHeight: '100vh',
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
              justifyContent: 'center',
              gap: 12,
              fontFamily: 'var(--font-body)',
              background: 'var(--surface-page)',
              color: 'var(--text-primary)',
              padding: 24,
              textAlign: 'center',
            }}
          >
            <div style={{ fontFamily: 'var(--font-display)', fontSize: 22 }}>Something went wrong</div>
            <div style={{ color: 'var(--text-muted)', fontSize: 14 }}>An unexpected error occurred. Please reload the page.</div>
            <button
              type="button"
              onClick={() => window.location.reload()}
              style={{
                padding: '10px 20px',
                borderRadius: 8,
                border: 'none',
                background: 'var(--accent-grad)',
                color: '#fff',
                fontWeight: 600,
                cursor: 'pointer',
              }}
            >
              Reload
            </button>
          </div>
        )}
      >
        <App />
      </SentryErrorBoundary>
    </StrictMode>
  );
})();
