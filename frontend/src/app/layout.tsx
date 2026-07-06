import type { Metadata } from "next";
import { JetBrains_Mono, Space_Grotesk } from "next/font/google";

import { Providers } from "@/app/providers";

import "./globals.css";

// Self-hosted via next/font; exposed as the *-base variables the design tokens consume
// (see src/styles/tokens/typography.css).
const spaceGrotesk = Space_Grotesk({
  subsets: ["latin", "latin-ext"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-sans-base",
});

const jetBrainsMono = JetBrains_Mono({
  subsets: ["latin", "latin-ext"],
  weight: ["400", "500", "600", "700"],
  variable: "--font-mono-base",
});

export const metadata: Metadata = {
  title: {
    default: "FORUM://SIGNAL",
    template: "%s · FORUM://SIGNAL",
  },
  description: "A realtime community forum — React SPA over a .NET 10 modular monolith.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className={`${spaceGrotesk.variable} ${jetBrainsMono.variable}`}>
      <body>
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
