import { render, screen } from '@testing-library/react';
import { type ComponentRef, createRef } from 'react';
import { describe, expect, it } from 'vitest';
import { PageHeading } from './PageHeading.js';

describe('PageHeading', () => {
  it('Renders an h1 with the given text', () => {
    render(<PageHeading ref={createRef<ComponentRef<'h1'>>()}>Layouts</PageHeading>);

    expect(screen.getByRole('heading', { level: 1, name: 'Layouts' })).toBeInTheDocument();
  });

  it('Is focusable by script but not by Tab, and carries the page-heading classes', () => {
    render(<PageHeading ref={createRef<ComponentRef<'h1'>>()}>Rules</PageHeading>);

    const heading = screen.getByRole('heading', { level: 1 });
    expect(heading).toHaveAttribute('tabindex', '-1');
    expect(heading.className).toBe('text-2xl font-semibold focus:outline-none');
  });

  it('Hands its element to the given ref', () => {
    const ref = createRef<ComponentRef<'h1'>>();
    render(<PageHeading ref={ref}>System variables</PageHeading>);

    expect(ref.current).toBe(screen.getByRole('heading', { level: 1 }));
  });
});
