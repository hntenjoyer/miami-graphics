import React from 'react';

interface ErrorBoundaryState {
  error: Error | null;
}

interface ErrorBoundaryProps {
  children: React.ReactNode;
}

export class ErrorBoundary extends React.Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }

  componentDidCatch(error: Error, info: React.ErrorInfo) {

    console.error('[ErrorBoundary]', error, info);
  }

  render() {
    if (this.state.error) {
      const e = this.state.error;
      return (
        <div style={{
          position: 'fixed', inset: 0, zIndex: 9999,
          display: 'flex', alignItems: 'center', justifyContent: 'center',
          background: 'rgba(0,0,0,0.85)', backdropFilter: 'blur(8px)',
          padding: '24px', fontFamily: 'monospace',
        }}>
          <div style={{
            maxWidth: 720, width: '100%',
            background: 'rgba(40,10,30,0.95)',
            border: '1px solid rgba(255,80,120,0.5)',
            borderRadius: 16, padding: 24,
            color: '#fff', fontSize: 13, lineHeight: 1.5,
          }}>
            <div style={{ fontSize: 16, fontWeight: 700, color: '#ff6b8b', marginBottom: 12 }}>
              Render error
            </div>
            <div style={{ marginBottom: 12 }}>
              <strong>{e.name}:</strong> {e.message}
            </div>
            {e.stack && (
              <pre style={{
                whiteSpace: 'pre-wrap', wordBreak: 'break-word',
                fontSize: 11, color: '#bbb', maxHeight: 360, overflow: 'auto',
                background: 'rgba(0,0,0,0.4)', padding: 12, borderRadius: 8,
              }}>{e.stack}</pre>
            )}
            <div style={{ marginTop: 12, fontSize: 11, color: '#888' }}>
              Перезапусти приложение, чтобы попробовать снова.
            </div>
          </div>
        </div>
      );
    }
    return this.props.children;
  }
}
