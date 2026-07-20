import { describe, expect, it } from 'vitest';
import { resolveLayoutHubUrl } from './hubUrl.js';

describe('resolveLayoutHubUrl', () => {
  it('Falls back to the dev proxy path when unset outside production', () => {
    expect(resolveLayoutHubUrl({ PROD: false })).toBe('/hubs/layouts');
  });

  it('Uses the configured URL and strips trailing slashes', () => {
    expect(resolveLayoutHubUrl({ PROD: true, VITE_LAYOUT_HUB_URL: 'https://fab.example/hubs/layouts/' })).toBe(
      'https://fab.example/hubs/layouts',
    );
  });

  it('Throws loudly when unset in a production build', () => {
    expect(() => resolveLayoutHubUrl({ PROD: true })).toThrowError(/VITE_LAYOUT_HUB_URL/);
  });

  it('Treats an empty string as unset', () => {
    expect(() => resolveLayoutHubUrl({ PROD: true, VITE_LAYOUT_HUB_URL: '' })).toThrowError(/VITE_LAYOUT_HUB_URL/);
  });
});
