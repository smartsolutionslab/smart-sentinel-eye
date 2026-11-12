import { describe, it, expect } from 'vitest';
import { redirectUri, returnPath, routerBasename } from './signinPaths';

describe('The OIDC callback lands under the instance base', () => {
  it('Builds the kiosk callback from the origin and the base', () => {
    expect(redirectUri('http://localhost:5180', '/kiosk/')).toBe('http://localhost:5180/kiosk/oidc/callback');
  });

  it('Builds the wall callback from the origin and the base', () => {
    expect(redirectUri('http://localhost:5180', '/wall/')).toBe('http://localhost:5180/wall/oidc/callback');
  });
});

describe('Signing in returns to a path inside this instance only', () => {
  it('Returns the stored path when it starts with the base', () => {
    expect(returnPath('/kiosk/layouts/abc', '/kiosk/')).toBe('/kiosk/layouts/abc');
  });

  it('Keeps the query of the stored path', () => {
    expect(returnPath('/kiosk/layouts/abc?cell=2', '/kiosk/')).toBe('/kiosk/layouts/abc?cell=2');
  });

  it('Returns a wall path under the wall base', () => {
    expect(returnPath('/wall/walls/x', '/wall/')).toBe('/wall/walls/x');
  });

  it.each([
    ['undefined', undefined],
    ['a non-string', 42],
    ['an empty string', ''],
    ['the origin root', '/'],
    ['a protocol-relative URL', '//evil.example/x'],
    ['a protocol-relative URL under the base', '//kiosk/x'],
    ['the other instance', '/wall/x'],
    ['a sibling prefix of the base', '/kioskx/y'],
    ['an absolute URL', 'https://evil.example/kiosk/'],
  ])('Falls back to the base for %s', (_name, stored) => {
    expect(returnPath(stored, '/kiosk/')).toBe('/kiosk/');
  });

  it('Rejects a kiosk path under the wall base', () => {
    expect(returnPath('/kiosk/x', '/wall/')).toBe('/wall/');
  });
});

describe('The router basename is the base without its trailing slash', () => {
  it.each([
    ['/kiosk/', '/kiosk'],
    ['/wall/', '/wall'],
  ])('Turns %s into %s', (base, basename) => {
    expect(routerBasename(base)).toBe(basename);
  });
});
