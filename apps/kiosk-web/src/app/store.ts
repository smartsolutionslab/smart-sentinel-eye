import { configureStore } from '@reduxjs/toolkit';

// Single Redux store per app (ADR-0075). Variable subscriptions and overlay state added per feature.
export const store = configureStore({
  reducer: {},
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
