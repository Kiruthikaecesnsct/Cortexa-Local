import { type ReactNode } from 'react';

export function AuthLayout({ children }: { children: ReactNode }) {
  return (
    <div
      data-theme="dark"
      style={{
        minHeight: '100vh',
        background: 'var(--surface-canvas)',
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        padding: 24,
        overflowY: 'auto',
        fontFamily: 'var(--font-body)',
        color: 'var(--text-primary)',
      }}
    >
      <div
        style={{
          display: 'flex',
          width: 1100,
          maxWidth: '100%',
          borderRadius: 'var(--radius-xl)',
          overflow: 'hidden',
          boxShadow: 'var(--shadow-lg)',
          margin: 'auto 0',
        }}
      >
        {/* Brand hero */}
        <div
          style={{
            flex: 1,
            background: 'var(--surface-brand-strong)',
            padding: '48px 56px',
            position: 'relative',
            overflow: 'hidden',
            display: 'flex',
            flexDirection: 'column',
            justifyContent: 'space-between',
          }}
        >
          <div
            style={{
              position: 'absolute',
              width: 440,
              height: 440,
              borderRadius: '50%',
              background: 'radial-gradient(circle, rgba(164,107,255,0.5), transparent 65%)',
              right: -150,
              bottom: -180,
              filter: 'blur(12px)',
              animation: 'cortexa-drift 9s ease-in-out infinite',
            }}
          />
          <div
            style={{
              position: 'absolute',
              width: 400,
              height: 400,
              borderRadius: '50%',
              background: 'radial-gradient(circle, rgba(62,139,255,0.45), transparent 65%)',
              left: '18%',
              top: -210,
              filter: 'blur(12px)',
              animation: 'cortexa-drift 13s ease-in-out infinite reverse',
            }}
          />
          <div style={{ position: 'absolute', inset: 0, background: 'linear-gradient(180deg, transparent 55%, rgba(5,6,14,0.38))' }} />
          <div style={{ position: 'relative' }}>
            <div
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 8,
                padding: '7px 14px',
                borderRadius: 999,
                border: '1px solid rgba(255,255,255,0.14)',
                background: 'rgba(255,255,255,0.06)',
                backdropFilter: 'blur(8px)',
                color: 'var(--text-on-brand-muted)',
                fontSize: 11.5,
                fontWeight: 600,
                letterSpacing: '0.08em',
                marginBottom: 22,
              }}
            >
              PATENT SEEDING &amp; HARVESTING PLATFORM
            </div>
            <div
              style={{
                fontFamily: 'var(--font-display)',
                color: 'var(--text-on-brand)',
                fontWeight: 600,
                fontSize: 44,
                letterSpacing: '-0.03em',
                lineHeight: 1.1,
              }}
            >
              Find the patent hiding in your{' '}
              <span
                style={{
                  background: 'linear-gradient(100deg,#7FB2FF,#9D8CFF,#C79BFF)',
                  WebkitBackgroundClip: 'text',
                  backgroundClip: 'text',
                  WebkitTextFillColor: 'transparent',
                  color: 'transparent',
                }}
              >
                research
              </span>
              .
            </div>
            <div style={{ color: 'var(--text-on-brand-muted)', fontSize: 15, lineHeight: 1.65, marginTop: 16, maxWidth: 400 }}>
              Two engines, three independent evidence sources, full provenance — from raw papers and code to ranked,
              defensible opportunities.
            </div>
          </div>
          <div style={{ position: 'relative', display: 'flex', alignItems: 'center', gap: 8, color: 'var(--text-on-brand-muted)', fontSize: 13 }}>
            <span>Powered by</span>
            <img src="/brand/imaginext-logo-full.svg" alt="Imaginext" style={{ height: 16, width: 'auto', display: 'block' }} />
          </div>
        </div>

        {/* Form panel */}
        <div
          style={{
            flex: 1,
            background: 'var(--surface-card)',
            backdropFilter: 'blur(24px)',
            WebkitBackdropFilter: 'blur(24px)',
            padding: '40px 56px',
            display: 'flex',
            flexDirection: 'column',
            gap: 14,
            minWidth: 0,
          }}
        >
          {children}
        </div>
      </div>
    </div>
  );
}
