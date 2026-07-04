<script lang="ts">
	import { onMount, onDestroy } from 'svelte';
	import HomePage from '$lib/components/home-page/home-page.svelte';
	import * as api from '$lib/immichFrameApi';
	import { setBaseUrl } from '$lib/index';
	import { configStore } from '$lib/stores/config.store';

	let { data }: { data: { slug: string } } = $props();
	const slug = data.slug;

	let phase = $state<'loading' | 'pin' | 'auth' | 'ready' | 'notfound'>('loading');
	let name = $state('');
	let pin = $state('');
	let pinError = $state('');
	let pinBusy = $state(false);

	let username = $state('');
	let password = $state('');
	let authError = $state('');
	let authBusy = $state(false);

	let retryTimer: ReturnType<typeof setTimeout> | undefined;
	let retryDelay = 2000;
	const MAX_RETRY_DELAY = 30000;

	onMount(resolve);

	// This is an SPA (ssr=false), so the API client's base URL persists across client-side
	// navigations. Reset it to the default root when leaving so a failed/abandoned gate or a
	// navigation to another slug or /admin can't strand this link's scoped /slideshow/{slug} base.
	onDestroy(() => {
		setBaseUrl('/');
		clearTimeout(retryTimer);
	});

	function scheduleRetry() {
		clearTimeout(retryTimer);
		retryTimer = setTimeout(resolve, retryDelay);
		retryDelay = Math.min(retryDelay * 2, MAX_RETRY_DELAY);
	}

	async function resolve() {
		try {
			const res = await fetch(`/api/slideshow/${encodeURIComponent(slug)}`, { credentials: 'include' });
			if (res.status === 404) {
				phase = 'notfound';
				return;
			}
			if (!res.ok) {
				// Transient backend failure (5xx, gateway error, rate limit); keep the loading
				// screen and retry with backoff instead of stranding a valid link on 'notfound'.
				phase = 'loading';
				scheduleRetry();
				return;
			}
			retryDelay = 2000;
			const body = await res.json();
			name = body.name ?? '';
			if (body.requiresAuth) phase = 'auth';
			else if (body.requiresPin) phase = 'pin';
			else await start();
		} catch {
			// Network error reaching the resolve endpoint; treat as transient and retry.
			phase = 'loading';
			scheduleRetry();
		}
	}

	async function submitLogin(event: SubmitEvent) {
		event.preventDefault();
		authError = '';
		authBusy = true;
		try {
			const res = await fetch('/api/viewer/login', {
				method: 'POST',
				credentials: 'include',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ username, password })
			});
			if (res.ok) {
				password = '';
				await resolve();
			} else {
				authError = 'Invalid username or password.';
			}
		} catch {
			authError = 'Could not reach the server.';
		} finally {
			authBusy = false;
		}
	}

	async function start() {
		// Point the slideshow's API client at this link's scoped endpoints, then load its config.
		setBaseUrl(`/slideshow/${slug}`);
		const cfg = await api.getConfig({ clientIdentifier: '' });
		if (cfg.status !== 200) {
			// The scoped config request failed (e.g. cookie rejected, link disabled or rotated
			// between resolve() and start()). Re-run the gate instead of entering 'ready' with a
			// poisoned config; resolve() self-heals transient races or re-shows the auth/pin gate.
			setBaseUrl('/');
			phase = 'loading';
			await resolve();
			return;
		}
		configStore.ps(cfg.data);
		phase = 'ready';
	}

	async function submitPin(event: SubmitEvent) {
		event.preventDefault();
		pinError = '';
		pinBusy = true;
		try {
			const res = await fetch(`/api/slideshow/${encodeURIComponent(slug)}/unlock`, {
				method: 'POST',
				credentials: 'include',
				headers: { 'Content-Type': 'application/json' },
				body: JSON.stringify({ pin })
			});
			if (res.ok) {
				pin = '';
				await start();
			} else {
				pinError = 'Incorrect PIN.';
			}
		} catch {
			pinError = 'Could not reach the server.';
		} finally {
			pinBusy = false;
		}
	}
</script>

<svelte:head><title>{name || 'ImmichFrame'}</title></svelte:head>

{#if phase === 'ready'}
	<HomePage />
{:else if phase === 'pin'}
	<div class="flex min-h-screen items-center justify-center bg-black p-4 text-slate-100">
		<form onsubmit={submitPin} class="w-full max-w-xs space-y-4 rounded-xl border border-slate-700 bg-slate-800 p-6 text-center shadow-xl">
			<h1 class="text-lg font-semibold">{name}</h1>
			<p class="text-sm text-slate-400">Enter the PIN to view this slideshow.</p>
			<input
				bind:value={pin}
				inputmode="numeric"
				autocomplete="off"
				class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 text-center text-lg tracking-widest outline-none focus:border-indigo-400"
			/>
			{#if pinError}<p class="text-sm text-red-400">{pinError}</p>{/if}
			<button type="submit" disabled={pinBusy} class="w-full rounded-md bg-indigo-600 px-3 py-2 font-medium hover:bg-indigo-500 disabled:opacity-50">
				{pinBusy ? 'Checking…' : 'View slideshow'}
			</button>
		</form>
	</div>
{:else if phase === 'auth'}
	<div class="flex min-h-screen items-center justify-center bg-black p-4 text-slate-100">
		<form onsubmit={submitLogin} class="w-full max-w-xs space-y-4 rounded-xl border border-slate-700 bg-slate-800 p-6 shadow-xl">
			<h1 class="text-center text-lg font-semibold">{name}</h1>
			<p class="text-center text-sm text-slate-400">Sign in to view this slideshow.</p>
			<input bind:value={username} autocomplete="username" placeholder="Username" class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400" />
			<input type="password" bind:value={password} autocomplete="current-password" placeholder="Password" class="w-full rounded-md border border-slate-600 bg-slate-700 px-3 py-2 outline-none focus:border-indigo-400" />
			{#if authError}<p class="text-sm text-red-400">{authError}</p>{/if}
			<button type="submit" disabled={authBusy} class="w-full rounded-md bg-indigo-600 px-3 py-2 font-medium hover:bg-indigo-500 disabled:opacity-50">
				{authBusy ? 'Signing in…' : 'Sign in'}
			</button>
		</form>
	</div>
{:else if phase === 'notfound'}
	<div class="flex min-h-screen items-center justify-center bg-black text-slate-400">This slideshow link doesn't exist.</div>
{:else}
	<div class="flex min-h-screen items-center justify-center bg-black text-slate-500">Loading…</div>
{/if}
