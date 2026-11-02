import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { TextErrorAlert } from './TextErrorAlert.js';

describe('TextErrorAlert', () => {
  it('announces the selected label’s text error as an alert', () => {
    const errors = { elements: [undefined, { text: { type: 'too_small', message: 'Text is required' } }] };
    render(<TextErrorAlert errors={errors as never} selectedIndex={1} />);
    expect(screen.getByRole('alert')).toHaveTextContent('Text is required');
    expect(screen.getByTestId('overlay-editor-dialog-label-error')).toBe(screen.getByRole('alert'));
  });

  it('renders nothing when the selected label has no text error', () => {
    const errors = { elements: [{ text: { type: 'too_small', message: 'Text is required' } }] };
    const { container } = render(<TextErrorAlert errors={errors as never} selectedIndex={1} />);
    expect(container).toBeEmptyDOMElement();
  });
});
