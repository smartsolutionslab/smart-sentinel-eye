import { z } from 'zod';

// Schema mirrors spec/001-register-camera FR-008.
// Reused for client-side form validation and as the input shape for the API client.
export const registerCameraSchema = z.object({
  name: z.string().trim().min(1, 'Name is required').max(200, 'Name must be 200 characters or fewer'),
  rtspUrl: z
    .string()
    .min(1, 'RTSP URL is required')
    .max(2048, 'RTSP URL must be 2048 characters or fewer')
    .regex(/^rtsp:\/\//i, 'Must start with rtsp://')
    .refine(
      (url) => !/^rtsp:\/\/[^@\s]+@/i.test(url),
      'Credentials in URL are not allowed; use a separate secret reference',
    ),
});

export type RegisterCameraInput = z.infer<typeof registerCameraSchema>;

/**
 * Spec 029 FR-009 — the address is validated before it is sent, so a rejection
 * the API would certainly make costs no round trip.
 *
 * Derived from `registerCameraSchema` rather than restated. The rule for what
 * counts as a usable RTSP address must not be able to differ between
 * registering a camera and correcting one; picking the field out keeps a single
 * definition instead of a second opinion that drifts.
 *
 * No `name` — but not because names are immutable. Spec 029 FR-012 scoped
 * renaming out of *that* feature and spec 033 delivered it (ADR-0120: a name may
 * be changed only where the aggregate is not addressed by it, which a camera is
 * not). The name has its own schema below, because the endpoint applies exactly
 * one field per request under its own version.
 */
export const changeCameraAddressSchema = registerCameraSchema.pick({ rtspUrl: true });

export type ChangeCameraAddressFormInput = z.infer<typeof changeCameraAddressSchema>;

/**
 * Spec 035 FR-010 — the corrected name is validated before it is sent.
 *
 * Derived from `registerCameraSchema` for the same reason
 * `changeCameraAddressSchema` above is: the rule for what counts as a usable
 * name must not be able to differ between registering a camera and correcting
 * one.
 *
 * `.trim()` comes with it and is deliberate — removing surrounding whitespace
 * is the only alteration permitted. **No case normalisation belongs here.**
 * `Line-4-Inlet` and `line-4-inlet` normalise identically, so a client that
 * lower-cased before sending would turn a real correction into a silent no-op;
 * spec 033 found that same trap in three server-side layers already.
 */
export const renameCameraSchema = registerCameraSchema.pick({ name: true });

export type RenameCameraFormInput = z.infer<typeof renameCameraSchema>;
