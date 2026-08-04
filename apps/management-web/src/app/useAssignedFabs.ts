import { useMemo } from 'react';
import { useAuth } from 'react-oidc-context';

const FAB_GROUP_PREFIX = '/fabs/';

/**
 * The fabs the signed-in operator belongs to, read from the OIDC `groups`
 * claim — the same claim `FabClaims.AssignedFabs` reads server-side, so the UI
 * and the guard agree on what the caller holds.
 *
 * Read from the **ID token**, not the access token: the access token is the
 * server's to inspect, and a client picking it apart is a habit worth not
 * starting. The realm's `sse-groups` mapper emits into both.
 *
 * Entries that are not fab groups are ignored rather than treated as fabs, and
 * the result is sorted so a rendered list does not reorder between sign-ins.
 */
export function useAssignedFabs(): readonly string[] {
  const auth = useAuth();
  const claim = auth.user?.profile?.['groups'];

  return useMemo(() => {
    const groups = Array.isArray(claim) ? claim : typeof claim === 'string' ? claim.split(' ') : [];

    return [
      ...new Set(
        groups
          .filter((group): group is string => typeof group === 'string')
          .filter((group) => group.startsWith(FAB_GROUP_PREFIX))
          .map((group) => group.slice(FAB_GROUP_PREFIX.length))
          .filter((fab) => fab.length > 0),
      ),
    ].sort();
  }, [claim]);
}
