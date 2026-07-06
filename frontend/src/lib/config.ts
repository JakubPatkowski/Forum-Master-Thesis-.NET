/** Deployment configuration. Everything backend-related comes from the environment. */

const DEFAULT_API_URL = "http://localhost:5099";

export const apiUrl: string = (process.env.NEXT_PUBLIC_API_URL ?? DEFAULT_API_URL).replace(
  /\/+$/,
  "",
);

/** ws(s) endpoint of the realtime hub; derived from the API URL unless overridden. */
export const wsUrl: string =
  process.env.NEXT_PUBLIC_WS_URL ?? `${apiUrl.replace(/^http/, "ws")}/api/realtime/ws`;
