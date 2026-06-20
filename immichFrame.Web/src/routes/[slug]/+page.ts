// Public per-link slideshow. Slugs are dynamic, so this route is not prerendered; it is served via
// the SPA fallback (index.html) and resolved client-side.
export const prerender = false;
export const ssr = false;

export const load = ({ params }: { params: { slug: string } }) => ({ slug: params.slug });
