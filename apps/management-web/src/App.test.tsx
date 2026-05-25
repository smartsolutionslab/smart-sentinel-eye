import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { App } from './App.js';
import { store } from './app/store.js';

describe('App shell', () => {
  it('Renders the management header in the scaffold layout', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('heading', { name: /smart sentinel eye/i })).toBeInTheDocument();
  });
});
