// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { OverlayLabel, OverlayShape } from '@smart-sentinel-eye/shared/api/overlays.api';
import { OverlayColorFields } from './OverlayColorFields.js';

/**
 * Spec 463 (issue #3108) T041, plan C11. New behaviour, RED (ADR-0139): a colour control for
 * the selected element. Assumed contract, stated for the engineer: `value` is the element,
 * `onCommit(color)` fires once per accepted colour with the canonical `#RRGGBBAA`, `readOnly`
 * makes every control non-editable. Swatches are `aria-pressed` buttons named by their swatch
 * name; the hex field is labelled "Colour"; the opacity range is labelled "Opacity" and its
 * numeric min/max/value are the alpha byte (0-255).
 */

function text(color: string): OverlayLabel {
  return {
    kind: 'Text',
    color,
    text: 'T1',
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.1,
    fontSizePx: 32,
  };
}

function box(color: string): OverlayShape {
  return {
    kind: 'Box',
    color,
    normalizedX: 0.1,
    normalizedY: 0.1,
    normalizedWidth: 0.3,
    normalizedHeight: 0.2,
  };
}

function hexField(): HTMLInputElement {
  return screen.getByLabelText('Colour', { exact: true }) as HTMLInputElement;
}

function opacity(): HTMLInputElement {
  return screen.getByLabelText('Opacity', { exact: true }) as HTMLInputElement;
}

function swatchButton(name: string): HTMLButtonElement {
  return screen.getByRole('button', { name }) as HTMLButtonElement;
}

function typeHex(value: string): void {
  fireEvent.change(hexField(), { target: { value } });
  fireEvent.keyDown(hexField(), { key: 'Enter' });
}

function alertText(): string {
  return screen.getByRole('alert').textContent ?? '';
}

afterEach(cleanup);

describe('OverlayColorFields', () => {
  describe('swatches', () => {
    it('Renders the thirteen swatches', () => {
      render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={vi.fn()} />);
      expect(screen.getAllByRole('button', { name: /^(gray-\d+|cyan-\d+|black)$/ })).toHaveLength(13);
    });

    it('Commits the swatch hex once when cyan-700 is chosen on a label', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={onCommit} />);

      fireEvent.click(swatchButton('cyan-700'));

      expect(onCommit).toHaveBeenCalledTimes(1);
      expect(onCommit).toHaveBeenCalledWith('#0F707DFF');
    });

    it('Commits gray-500 for a Box as #818A98FF', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={box('#D32F2F80')} onCommit={onCommit} />);

      fireEvent.click(swatchButton('gray-500'));

      expect(onCommit).toHaveBeenCalledTimes(1);
      expect(onCommit).toHaveBeenCalledWith('#818A98FF');
    });

    it('Shows the committed colour in the hex field', () => {
      render(<OverlayColorFields value={text('#0F707DFF')} onCommit={vi.fn()} />);
      expect(hexField().value).toBe('#0F707DFF');
    });

    it('Marks the swatch matching the current colour as pressed and the others as not', () => {
      render(<OverlayColorFields value={text('#0F707DFF')} onCommit={vi.fn()} />);
      expect(swatchButton('cyan-700').getAttribute('aria-pressed')).toBe('true');
      expect(swatchButton('cyan-600').getAttribute('aria-pressed')).toBe('false');
    });
  });

  describe('hex field', () => {
    it('Canonicalises a typed lower-case six-digit hex to upper-case with FF alpha', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={box('#D32F2F80')} onCommit={onCommit} />);

      typeHex('#d32f2f');

      expect(onCommit).toHaveBeenCalledTimes(1);
      expect(onCommit).toHaveBeenCalledWith('#D32F2FFF');
    });

    it('Accepts a typed eight-digit hex for a Box, upper-cased', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={box('#818A98FF')} onCommit={onCommit} />);

      typeHex('#d32f2f40');

      expect(onCommit).toHaveBeenCalledWith('#D32F2F40');
    });

    it('Refuses a too-bright #FFFFFF, commits nothing, and names the luminance cap', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={onCommit} />);

      typeHex('#FFFFFF');

      expect(onCommit).not.toHaveBeenCalled();
      expect(alertText()).toMatch(/brighter than the wall allows/i);
      expect(alertText()).toContain('0.35');
    });

    it('Refuses an illegible #D32F2F80 label colour, commits nothing, and names the F9 floor', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={onCommit} />);

      typeHex('#D32F2F80');

      expect(onCommit).not.toHaveBeenCalled();
      expect(alertText()).toMatch(/alpha of at least F9|opacity of at least F9/i);
    });

    it('Accepts the same translucent #D32F2F80 for a Box', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={box('#818A98FF')} onCommit={onCommit} />);

      typeHex('#D32F2F80');

      expect(onCommit).toHaveBeenCalledWith('#D32F2F80');
    });

    it.each(['red', '#FF00', '#GG0000'])(
      'Refuses the malformed "%s" and names the #RRGGBB or #RRGGBBAA form',
      (bad) => {
        const onCommit = vi.fn();
        render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={onCommit} />);

        typeHex(bad);

        expect(onCommit).not.toHaveBeenCalled();
        expect(alertText()).toMatch(/#RRGGBB/);
      },
    );

    it('Shows no alert while the field holds an accepted colour', () => {
      render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={vi.fn()} />);
      expect(screen.queryByRole('alert')).toBeNull();
    });
  });

  describe('opacity', () => {
    it('Has a minimum of F9 for a label coloured #D32F2F', () => {
      render(<OverlayColorFields value={text('#D32F2FFF')} onCommit={vi.fn()} />);
      expect(Number(opacity().min)).toBe(0xf9);
      expect(Number(opacity().max)).toBe(255);
    });

    it('Has a minimum of 00 for a Box', () => {
      render(<OverlayColorFields value={box('#D32F2FFF')} onCommit={vi.fn()} />);
      expect(Number(opacity().min)).toBe(0);
    });

    it('Commits the new alpha on the current RGB when the range moves', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={box('#D32F2FFF')} onCommit={onCommit} />);

      fireEvent.change(opacity(), { target: { value: String(0x80) } });

      expect(onCommit).toHaveBeenCalledWith('#D32F2F80');
    });
  });

  describe('read-only', () => {
    it('Makes the swatches, hex field and opacity control non-editable and commits nothing', () => {
      const onCommit = vi.fn();
      render(<OverlayColorFields value={text('#9F9F9FD9')} onCommit={onCommit} readOnly />);

      expect(hexField().readOnly || hexField().disabled).toBe(true);
      expect(opacity().disabled).toBe(true);
      const swatch = swatchButton('cyan-700');
      expect(swatch.disabled).toBe(true);
      fireEvent.click(swatch);
      expect(onCommit).not.toHaveBeenCalled();
    });
  });
});
