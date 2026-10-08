import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { Provider } from 'react-redux';
import { App } from './App.js';
import { listenForWindowFocus, store } from './app/store.js';
import '@smart-sentinel-eye/shared/ui/fonts/fonts.css';
import './styles/index.css';

const rootElement = document.getElementById('root');
if (rootElement === null) {
  throw new Error('Root element with id "root" not found.');
}

// Spec 317 (#2751): inert without this call — refetchOnFocus on a
// subscription does nothing until RTK Query's window listeners are
// installed. Once, here, before the app renders.
listenForWindowFocus();

createRoot(rootElement).render(
  <StrictMode>
    <Provider store={store}>
      <App />
    </Provider>
  </StrictMode>,
);
