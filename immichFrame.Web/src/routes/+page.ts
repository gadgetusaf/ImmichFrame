import { redirect } from '@sveltejs/kit';

// The root no longer hosts a slideshow — content is served only via named /[slug] links.
// Anyone hitting the bare domain is sent to the admin sign-in instead.
// Client-only (like the /[slug] route) so the redirect runs in the browser via the SPA fallback.
export const prerender = false;
export const ssr = false;

export const load = () => {
  redirect(307, '/admin');
};
