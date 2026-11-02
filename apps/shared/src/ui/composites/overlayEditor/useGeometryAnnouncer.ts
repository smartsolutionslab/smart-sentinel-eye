import { useCallback, useEffect, useRef, useState } from 'react';

// FR-015: the geometry announcement is debounced so a burst of keypresses makes one.
const ANNOUNCE_DELAY_MS = 500;

export function useGeometryAnnouncer() {
  // FR-015/016/017: one debounced live-region message. The timer is reset on
  // every handled keypress so a burst produces exactly one announcement.
  const [liveMessage, setLiveMessage] = useState('');
  const announceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Phase 6 should-fix 5: a held key that stays refused at the same edge
  // repeats the same `message` burst after burst. Writing an unchanged
  // string is a React no-op — the DOM text never changes, so the region
  // never fires again after the first burst. A trailing zero-width space,
  // toggled on each write, makes a repeat of the same announcement a
  // different string every other time, without changing what is read aloud.
  const lastAnnouncedRef = useRef('');
  const announceToggleRef = useRef(false);

  const queueAnnouncement = useCallback((message: string) => {
    if (announceTimerRef.current !== null) {
      clearTimeout(announceTimerRef.current);
    }
    announceTimerRef.current = setTimeout(() => {
      const repeated = message === lastAnnouncedRef.current;
      announceToggleRef.current = repeated ? !announceToggleRef.current : false;
      lastAnnouncedRef.current = message;
      setLiveMessage(announceToggleRef.current ? `${message}\u200B` : message);
      announceTimerRef.current = null;
    }, ANNOUNCE_DELAY_MS);
  }, []);

  useEffect(
    () => () => {
      if (announceTimerRef.current !== null) {
        clearTimeout(announceTimerRef.current);
      }
    },
    [],
  );

  return { liveMessage, queueAnnouncement };
}
