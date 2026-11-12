export function kioskBase(mode: string | undefined): string {
  return (mode ?? '').toLowerCase() === 'wall' ? '/wall/' : '/kiosk/';
}
