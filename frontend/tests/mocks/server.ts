import { setupServer } from 'msw/node';

// Tests must register every API response explicitly; no real backend is contacted.
export const server = setupServer();
export const apiUrl = (path: string) => `http://localhost:5000${path}`;
