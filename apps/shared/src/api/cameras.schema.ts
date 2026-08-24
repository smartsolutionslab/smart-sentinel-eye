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
 * No `name`: it is not editable (spec 029 FR-012, tracked as #1850), so there is
 * nothing for a correction to carry.
 */
export const changeCameraAddressSchema = registerCameraSchema.pick({ rtspUrl: true });

export type ChangeCameraAddressFormInput = z.infer<typeof changeCameraAddressSchema>;
