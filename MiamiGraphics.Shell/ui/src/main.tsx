import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import App from './App';
import { SettingsProvider } from './contexts/SettingsContext';
import { ErrorBoundary } from './components/ErrorBoundary';
import './styles/fonts.css';
import '@fontsource/inter/700.css';
import '@fontsource/inter/800.css';
import './styles/theme.css';
import './styles/globals.css';
import './i18n';
import { installImageFallback } from './lib/imageFallback';
import { installImageRecycler } from './lib/imageRecycler';

installImageFallback();

installImageRecycler();

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ErrorBoundary>
      <SettingsProvider>
        <App />
      </SettingsProvider>
    </ErrorBoundary>
  </StrictMode>,
);
