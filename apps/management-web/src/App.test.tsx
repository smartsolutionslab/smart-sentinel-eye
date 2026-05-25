import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import { Provider } from 'react-redux';
import { App } from './App.js';
import { store } from './app/store.js';

describe('App shell', () => {
  it('Renders the Cameras page heading and the Register button', () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );
    expect(screen.getByRole('heading', { name: /cameras/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /register camera/i })).toBeInTheDocument();
  });
});
