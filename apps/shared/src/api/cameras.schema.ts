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
